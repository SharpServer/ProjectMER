#nullable disable
using System.Text;
using ProjectMER.Features.Enums;
using UnityEngine;
using Utf8Json;

namespace ProjectMER.Features.Serializable.Schematics;

/// <summary>
/// スキマティック JSON 専用の高速パーサー。成功した場合の結果は Utf8Json と同一。
/// JSON 標準外の入力や判断に迷う入力は例外を投げるので、呼び出し側は Utf8Json へフォールバックすること。
/// </summary>
public static class SchematicDataParser
{
	public static SchematicObjectDataList Parse(byte[] bytes)
	{
		if (bytes == null)
			throw new ArgumentNullException(nameof(bytes));

		return new Reader(bytes).ParseRoot();
	}

	private static readonly UTF8Encoding StrictUtf8 = new(false, true);

	private static readonly byte[] KeyRootObjectId = Encoding.ASCII.GetBytes("RootObjectId");
	private static readonly byte[] KeyBlocks = Encoding.ASCII.GetBytes("Blocks");
	private static readonly byte[] KeyPath = Encoding.ASCII.GetBytes("Path");
	private static readonly byte[] KeyName = Encoding.ASCII.GetBytes("Name");
	private static readonly byte[] KeyObjectId = Encoding.ASCII.GetBytes("ObjectId");
	private static readonly byte[] KeyParentId = Encoding.ASCII.GetBytes("ParentId");
	private static readonly byte[] KeyAnimatorName = Encoding.ASCII.GetBytes("AnimatorName");
	private static readonly byte[] KeyPosition = Encoding.ASCII.GetBytes("Position");
	private static readonly byte[] KeyRotation = Encoding.ASCII.GetBytes("Rotation");
	private static readonly byte[] KeyScale = Encoding.ASCII.GetBytes("Scale");
	private static readonly byte[] KeyBlockType = Encoding.ASCII.GetBytes("BlockType");
	private static readonly byte[] KeyProperties = Encoding.ASCII.GetBytes("Properties");
	private static readonly byte[] KeyX = { (byte)'x' };
	private static readonly byte[] KeyY = { (byte)'y' };
	private static readonly byte[] KeyZ = { (byte)'z' };

	private static readonly double[] Pow10 =
	{
		1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11,
		1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
	};

	private static readonly object BoxedZero = 0d;
	private static readonly object BoxedOne = 1d;
	private static readonly object BoxedTrue = true;
	private static readonly object BoxedFalse = false;

	private const int MaxDepth = 256;
	private const int MaxInternLength = 48;

	private sealed class Reader
	{
		private readonly byte[] _b;
		private readonly int _n;
		private int _p;
		private int _depth;
		private StringBuilder _sb;

		// 文字列 intern 用のオープンアドレス表（ASCII のみ）。
		private string[] _intern = new string[256];
		private int _internCount;

		public Reader(byte[] bytes)
		{
			_b = bytes;
			_n = bytes.Length;
		}

		private static Exception Error(string message, int pos) => new FormatException($"{message} (offset {pos})");

		// ---- 基本 ----

		private void SkipWs()
		{
			byte[] b = _b;
			int p = _p;
			while (p < _n)
			{
				byte c = b[p];
				if (c == ' ' || c == '\n' || c == '\r' || c == '\t')
					p++;
				else
					break;
			}

			_p = p;
		}

		private void Expect(byte c)
		{
			SkipWs();
			if (_p >= _n || _b[_p] != c)
				throw Error($"expected '{(char)c}'", _p);
			_p++;
		}

		/// <summary>null リテラルなら読み進めて true。</summary>
		private bool TryReadNull()
		{
			SkipWs();
			if (_p < _n && _b[_p] == 'n')
			{
				ExpectLiteral("null");
				return true;
			}

			return false;
		}

		private void ExpectLiteral(string literal)
		{
			if (_p + literal.Length > _n)
				throw Error("unexpected end", _p);
			for (int i = 0; i < literal.Length; i++)
			{
				if (_b[_p + i] != literal[i])
					throw Error("invalid literal", _p);
			}

			_p += literal.Length;
			// リテラルの直後に識別子文字が続くのは不正。
			if (_p < _n)
			{
				byte c = _b[_p];
				if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_')
					throw Error("invalid literal", _p);
			}
		}

		// ---- ルート ----

		public SchematicObjectDataList ParseRoot()
		{
			_p = 0;
			if (_n >= 3 && _b[0] == 0xEF && _b[1] == 0xBB && _b[2] == 0xBF)
				_p = 3;

			SkipWs();
			if (_p >= _n || _b[_p] != '{')
				throw Error("root must be an object", _p);
			_p++;

			var data = new SchematicObjectDataList();
			bool first = true;
			while (true)
			{
				SkipWs();
				if (_p >= _n)
					throw Error("unexpected end", _p);
				if (_b[_p] == '}')
				{
					_p++;
					break;
				}

				if (!first)
				{
					if (_b[_p] != ',')
						throw Error("expected ','", _p);
					_p++;
					SkipWs();
				}

				first = false;
				ReadKeySpan(out int ks, out int kl);
				Expect((byte)':');

				if (KeyEq(ks, kl, KeyRootObjectId))
					data.RootObjectId = ReadInt();
				else if (KeyEq(ks, kl, KeyBlocks))
					data.Blocks = ReadBlocks();
				else if (KeyEq(ks, kl, KeyPath))
					data.Path = ReadStringOrNull();
				else
					SkipValue();
			}

			return data;
		}

		private List<SchematicBlockData> ReadBlocks()
		{
			if (TryReadNull())
				return null;

			Expect((byte)'[');
			// 4040 ブロック級でも再確保が少なくなるよう、残りバイト数から控えめに見積もる。
			var list = new List<SchematicBlockData>(Math.Min(Math.Max((_n - _p) / 400, 4), 1 << 16));
			bool first = true;
			while (true)
			{
				SkipWs();
				if (_p >= _n)
					throw Error("unexpected end", _p);
				if (_b[_p] == ']')
				{
					_p++;
					break;
				}

				if (!first)
				{
					if (_b[_p] != ',')
						throw Error("expected ','", _p);
					_p++;
				}

				first = false;
				list.Add(ReadBlock());
			}

			return list;
		}

		private SchematicBlockData ReadBlock()
		{
			if (TryReadNull())
				return null;

			Expect((byte)'{');
			var block = new SchematicBlockData();
			bool first = true;
			while (true)
			{
				SkipWs();
				if (_p >= _n)
					throw Error("unexpected end", _p);
				if (_b[_p] == '}')
				{
					_p++;
					break;
				}

				if (!first)
				{
					if (_b[_p] != ',')
						throw Error("expected ','", _p);
					_p++;
					SkipWs();
				}

				first = false;
				ReadKeySpan(out int ks, out int kl);
				Expect((byte)':');

				switch (kl)
				{
					case 4 when KeyEq(ks, kl, KeyName):
						block.Name = ReadStringOrNull();
						break;
					case 5 when KeyEq(ks, kl, KeyScale):
						block.Scale = ReadVector3();
						break;
					case 8 when KeyEq(ks, kl, KeyObjectId):
						block.ObjectId = ReadInt();
						break;
					case 8 when KeyEq(ks, kl, KeyParentId):
						block.ParentId = ReadInt();
						break;
					case 8 when KeyEq(ks, kl, KeyPosition):
						block.Position = ReadVector3();
						break;
					case 8 when KeyEq(ks, kl, KeyRotation):
						block.Rotation = ReadVector3();
						break;
					case 9 when KeyEq(ks, kl, KeyBlockType):
						block.BlockType = (BlockType)ReadInt();
						break;
					case 10 when KeyEq(ks, kl, KeyProperties):
						block.Properties = ReadPropertiesOrNull();
						break;
					case 12 when KeyEq(ks, kl, KeyAnimatorName):
						block.AnimatorName = ReadStringOrNull();
						break;
					default:
						SkipValue();
						break;
				}
			}

			return block;
		}

		private Vector3 ReadVector3()
		{
			// null は Utf8Json 側で例外になるので、こちらも例外にしてフォールバックさせる。
			Expect((byte)'{');
			float x = 0, y = 0, z = 0;
			bool first = true;
			while (true)
			{
				SkipWs();
				if (_p >= _n)
					throw Error("unexpected end", _p);
				if (_b[_p] == '}')
				{
					_p++;
					break;
				}

				if (!first)
				{
					if (_b[_p] != ',')
						throw Error("expected ','", _p);
					_p++;
					SkipWs();
				}

				first = false;
				ReadKeySpan(out int ks, out int kl);
				Expect((byte)':');

				if (kl == 1 && KeyEq(ks, kl, KeyX))
					x = ReadFloat();
				else if (kl == 1 && KeyEq(ks, kl, KeyY))
					y = ReadFloat();
				else if (kl == 1 && KeyEq(ks, kl, KeyZ))
					z = ReadFloat();
				else
					SkipValue();
			}

			return new Vector3(x, y, z);
		}

		// ---- Properties（object 型の値） ----

		private Dictionary<string, object> ReadPropertiesOrNull()
		{
			if (TryReadNull())
				return null;
			return ReadObjectDict();
		}

		private Dictionary<string, object> ReadObjectDict()
		{
			Expect((byte)'{');
			if (++_depth > MaxDepth)
				throw Error("nesting too deep", _p);

			var dict = new Dictionary<string, object>();
			bool first = true;
			while (true)
			{
				SkipWs();
				if (_p >= _n)
					throw Error("unexpected end", _p);
				if (_b[_p] == '}')
				{
					_p++;
					break;
				}

				if (!first)
				{
					if (_b[_p] != ',')
						throw Error("expected ','", _p);
					_p++;
					SkipWs();
				}

				first = false;
				string key = ReadString(true);
				Expect((byte)':');
				dict.Add(key, ReadObjectValue());
			}

			_depth--;
			return dict;
		}

		private object ReadObjectValue()
		{
			SkipWs();
			if (_p >= _n)
				throw Error("unexpected end", _p);

			byte c = _b[_p];
			switch (c)
			{
				case (byte)'"':
					return ReadString(true);
				case (byte)'{':
					return ReadObjectDict();
				case (byte)'[':
					return ReadObjectList();
				case (byte)'t':
					ExpectLiteral("true");
					return BoxedTrue;
				case (byte)'f':
					ExpectLiteral("false");
					return BoxedFalse;
				case (byte)'n':
					ExpectLiteral("null");
					return null;
				default:
					if (c == '-' || (c >= '0' && c <= '9'))
					{
						double d = ReadDouble();
						long bits = BitConverter.DoubleToInt64Bits(d);
						if (bits == 0)
							return BoxedZero;
						if (d == 1d)
							return BoxedOne;
						return d;
					}

					throw Error("unexpected token", _p);
			}
		}

		private List<object> ReadObjectList()
		{
			Expect((byte)'[');
			if (++_depth > MaxDepth)
				throw Error("nesting too deep", _p);

			var list = new List<object>();
			bool first = true;
			while (true)
			{
				SkipWs();
				if (_p >= _n)
					throw Error("unexpected end", _p);
				if (_b[_p] == ']')
				{
					_p++;
					break;
				}

				if (!first)
				{
					if (_b[_p] != ',')
						throw Error("expected ','", _p);
					_p++;
				}

				first = false;
				list.Add(ReadObjectValue());
			}

			_depth--;
			return list;
		}

		// ---- 読み飛ばし（厳格に検証） ----

		private void SkipValue()
		{
			SkipWs();
			if (_p >= _n)
				throw Error("unexpected end", _p);

			byte c = _b[_p];
			switch (c)
			{
				case (byte)'"':
					SkipString();
					break;
				case (byte)'{':
					SkipContainer((byte)'{', (byte)'}');
					break;
				case (byte)'[':
					SkipContainer((byte)'[', (byte)']');
					break;
				case (byte)'t':
					ExpectLiteral("true");
					break;
				case (byte)'f':
					ExpectLiteral("false");
					break;
				case (byte)'n':
					ExpectLiteral("null");
					break;
				default:
					if (c == '-' || (c >= '0' && c <= '9'))
					{
						LexNumber(out _, out _, out _, out _, out _);
						break;
					}

					throw Error("unexpected token", _p);
			}
		}

		private void SkipContainer(byte open, byte close)
		{
			_p++; // open
			if (++_depth > MaxDepth)
				throw Error("nesting too deep", _p);

			bool first = true;
			while (true)
			{
				SkipWs();
				if (_p >= _n)
					throw Error("unexpected end", _p);
				if (_b[_p] == close)
				{
					_p++;
					break;
				}

				if (!first)
				{
					if (_b[_p] != ',')
						throw Error("expected ','", _p);
					_p++;
					SkipWs();
				}

				first = false;
				if (open == '{')
				{
					if (_p >= _n || _b[_p] != '"')
						throw Error("expected string key", _p);
					SkipString();
					Expect((byte)':');
				}

				SkipValue();
			}

			_depth--;
		}

		// ---- キー ----

		/// <summary>キー文字列の生バイト範囲を返す。エスケープや制御文字を含むキーは例外（Utf8Json へ任せる）。</summary>
		private void ReadKeySpan(out int start, out int length)
		{
			SkipWs();
			if (_p >= _n || _b[_p] != '"')
				throw Error("expected string key", _p);

			int p = ++_p;
			byte[] b = _b;
			while (true)
			{
				if (p >= _n)
					throw Error("unexpected end", p);
				byte c = b[p];
				if (c == '"')
					break;
				if (c == '\\' || c < 0x20)
					throw Error("unsupported key", p);
				p++;
			}

			start = _p;
			length = p - _p;
			_p = p + 1;
		}

		private bool KeyEq(int start, int length, byte[] name)
		{
			if (length != name.Length)
				return false;
			byte[] b = _b;
			for (int i = 0; i < length; i++)
			{
				if (b[start + i] != name[i])
					return false;
			}

			return true;
		}

		// ---- 文字列 ----

		private char[] _chars = new char[256];

		private string ReadStringOrNull() => TryReadNull() ? null : ReadString(false);

		private string ReadString(bool intern)
		{
			SkipWs();
			if (_p >= _n || _b[_p] != '"')
				throw Error("expected string", _p);

			byte[] b = _b;
			int start = ++_p;
			int p = start;
			bool high = false;
			while (true)
			{
				if (p >= _n)
					throw Error("unexpected end", p);
				byte c = b[p];
				if (c == '"')
					break;
				if (c == '\\')
					return ReadStringEscaped(start, p);
				if (c < 0x20)
					throw Error("control character in string", p);
				if (c >= 0x80)
					high = true;
				p++;
			}

			_p = p + 1;
			int len = p - start;
			if (len == 0)
				return string.Empty;
			if (high)
				return StrictUtf8.GetString(b, start, len);
			if (intern && len <= MaxInternLength)
				return Intern(start, len);
			return AsciiString(start, len);
		}

		private string AsciiString(int start, int len)
		{
			if (_chars.Length < len)
				_chars = new char[Math.Max(len, _chars.Length * 2)];
			char[] chars = _chars;
			byte[] b = _b;
			for (int i = 0; i < len; i++)
				chars[i] = (char)b[start + i];
			return new string(chars, 0, len);
		}

		private static uint HashAscii(byte[] b, int start, int len)
		{
			uint h = 2166136261;
			for (int i = 0; i < len; i++)
				h = (h ^ b[start + i]) * 16777619;
			return h;
		}

		private static uint HashAscii(string s)
		{
			uint h = 2166136261;
			for (int i = 0; i < s.Length; i++)
				h = (h ^ s[i]) * 16777619;
			return h;
		}

		private string Intern(int start, int len)
		{
			string[] table = _intern;
			int mask = table.Length - 1;
			int idx = (int)(HashAscii(_b, start, len) & (uint)mask);
			byte[] b = _b;
			while (true)
			{
				string s = table[idx];
				if (s == null)
					break;
				if (s.Length == len)
				{
					int i = 0;
					while (i < len && s[i] == b[start + i])
						i++;
					if (i == len)
						return s;
				}

				idx = (idx + 1) & mask;
			}

			string created = AsciiString(start, len);
			table[idx] = created;
			if (++_internCount * 2 > table.Length)
				GrowIntern();
			return created;
		}

		private void GrowIntern()
		{
			string[] old = _intern;
			var table = new string[old.Length * 2];
			int mask = table.Length - 1;
			foreach (string s in old)
			{
				if (s == null)
					continue;
				int idx = (int)(HashAscii(s) & (uint)mask);
				while (table[idx] != null)
					idx = (idx + 1) & mask;
				table[idx] = s;
			}

			_intern = table;
		}

		private string ReadStringEscaped(int start, int firstEscape)
		{
			byte[] b = _b;
			StringBuilder sb = _sb ??= new StringBuilder();
			sb.Length = 0;
			int segStart = start;
			int p = firstEscape;
			while (true)
			{
				if (p >= _n)
					throw Error("unexpected end", p);
				byte c = b[p];
				if (c == '"')
				{
					if (p > segStart)
						sb.Append(StrictUtf8.GetString(b, segStart, p - segStart));
					_p = p + 1;
					return sb.ToString();
				}

				if (c < 0x20)
					throw Error("control character in string", p);
				if (c != '\\')
				{
					p++;
					continue;
				}

				if (p > segStart)
					sb.Append(StrictUtf8.GetString(b, segStart, p - segStart));
				p++;
				if (p >= _n)
					throw Error("unexpected end", p);
				switch (b[p])
				{
					case (byte)'"': sb.Append('"'); p++; break;
					case (byte)'\\': sb.Append('\\'); p++; break;
					case (byte)'/': sb.Append('/'); p++; break;
					case (byte)'b': sb.Append('\b'); p++; break;
					case (byte)'f': sb.Append('\f'); p++; break;
					case (byte)'n': sb.Append('\n'); p++; break;
					case (byte)'r': sb.Append('\r'); p++; break;
					case (byte)'t': sb.Append('\t'); p++; break;
					case (byte)'u':
						int code = ReadHex4(p + 1);
						// サロゲートは Utf8Json の挙動に任せる。
						if (code >= 0xD800 && code <= 0xDFFF)
							throw Error("surrogate escape", p);
						sb.Append((char)code);
						p += 5;
						break;
					default:
						throw Error("invalid escape", p);
				}

				segStart = p;
			}
		}

		private int ReadHex4(int pos)
		{
			if (pos + 4 > _n)
				throw Error("unexpected end", pos);
			int v = 0;
			for (int i = 0; i < 4; i++)
			{
				byte c = _b[pos + i];
				int d;
				if (c >= '0' && c <= '9') d = c - '0';
				else if (c >= 'a' && c <= 'f') d = c - 'a' + 10;
				else if (c >= 'A' && c <= 'F') d = c - 'A' + 10;
				else throw Error("invalid \\u escape", pos + i);
				v = (v << 4) | d;
			}

			return v;
		}

		private void SkipString()
		{
			byte[] b = _b;
			int p = ++_p;
			while (true)
			{
				if (p >= _n)
					throw Error("unexpected end", p);
				byte c = b[p];
				if (c == '"')
					break;
				if (c < 0x20)
					throw Error("control character in string", p);
				if (c == '\\')
				{
					p++;
					if (p >= _n)
						throw Error("unexpected end", p);
					switch (b[p])
					{
						case (byte)'"': case (byte)'\\': case (byte)'/': case (byte)'b':
						case (byte)'f': case (byte)'n': case (byte)'r': case (byte)'t':
							break;
						case (byte)'u':
							ReadHex4(p + 1);
							p += 4;
							break;
						default:
							throw Error("invalid escape", p);
					}
				}

				p++;
			}

			// Utf8Json は読み飛ばしの際、直前が逆スラッシュの引用符を終端とみなさず、"..\\" で失敗する。
			// 結果が食い違い得るので、その形は Utf8Json に任せる。
			if (p > _p && b[p - 1] == '\\')
				throw Error("string ends with backslash", p);

			_p = p + 1;
		}

		// ---- 数値 ----

		private static bool IsDigit(byte c) => c >= '0' && c <= '9';

		/// <summary>-?[0-9]+ のみ。範囲外・小数・指数は例外。</summary>
		private int ReadInt()
		{
			SkipWs();
			byte[] b = _b;
			int p = _p;
			bool neg = false;
			if (p < _n && b[p] == '-')
			{
				neg = true;
				p++;
			}

			if (p >= _n || !IsDigit(b[p]))
				throw Error("expected integer", p);

			long v = 0;
			int digits = 0;
			if (b[p] == '0')
			{
				p++;
				if (p < _n && IsDigit(b[p]))
					throw Error("leading zero", p);
			}
			else
			{
				while (p < _n && IsDigit(b[p]))
				{
					if (++digits > 10)
						throw Error("integer too long", p);
					v = v * 10 + (b[p] - '0');
					p++;
				}
			}

			if (p < _n && (b[p] == '.' || b[p] == 'e' || b[p] == 'E'))
				throw Error("not an integer", p);

			if (neg)
				v = -v;
			if (v < int.MinValue || v > int.MaxValue)
				throw Error("integer out of range", p);

			_p = p;
			return (int)v;
		}

		private void LexNumber(out int start, out bool neg, out ulong m, out int e10, out bool tooMany)
		{
			byte[] b = _b;
			start = _p;
			neg = false;
			m = 0;
			e10 = 0;
			tooMany = false;
			int sig = 0;
			int p = _p;

			if (b[p] == '-')
			{
				neg = true;
				p++;
			}

			if (p >= _n)
				throw Error("unexpected end", p);

			byte c = b[p];
			if (c == '0')
			{
				p++;
				if (p < _n && IsDigit(b[p]))
					throw Error("leading zero", p);
			}
			else if (c >= '1' && c <= '9')
			{
				while (p < _n && IsDigit(c = b[p]))
				{
					if (sig < 19)
					{
						m = m * 10 + (ulong)(c - '0');
						sig++;
					}
					else
					{
						tooMany = true;
					}

					p++;
				}
			}
			else
			{
				throw Error("invalid number", p);
			}

			if (p < _n && b[p] == '.')
			{
				p++;
				if (p >= _n || !IsDigit(b[p]))
					throw Error("invalid number", p);
				while (p < _n && IsDigit(c = b[p]))
				{
					if (sig < 19)
					{
						m = m * 10 + (ulong)(c - '0');
						e10--;
						if (m != 0)
							sig++;
					}
					else
					{
						tooMany = true;
					}

					p++;
				}
			}

			if (p < _n && (b[p] == 'e' || b[p] == 'E'))
			{
				p++;
				bool eneg = false;
				if (p < _n && (b[p] == '+' || b[p] == '-'))
				{
					eneg = b[p] == '-';
					p++;
				}

				if (p >= _n || !IsDigit(b[p]))
					throw Error("invalid exponent", p);
				int ex = 0;
				while (p < _n && IsDigit(b[p]))
				{
					if (ex < 100000)
						ex = ex * 10 + (b[p] - '0');
					p++;
				}

				e10 += eneg ? -ex : ex;
			}

			_p = p;
		}

		private static readonly double NegativeZero = BitConverter.Int64BitsToDouble(long.MinValue);

		/// <summary>
		/// Clinger の高速パスで厳密に丸まる場合のみ true。有効桁は 15 桁以下に限る
		/// （16 桁以上では Utf8Json の結果が 1ulp ずれる入力があり、同一結果を保証するため JsonReader に任せる）。
		/// </summary>
		private static bool TryFast(ulong m, int e10, bool tooMany, out double d)
		{
			d = 0;
			if (tooMany || m >= 1_000_000_000_000_000UL || e10 < -22 || e10 > 22)
				return false;
			d = e10 >= 0 ? m * Pow10[e10] : m / Pow10[-e10];
			return true;
		}

		/// <summary>JsonReader は数値の後ろの空白も読み進めるので、自前の終端との差が空白だけなら一致とみなす。</summary>
		private void CheckReaderOffset(int offset, int start)
		{
			if (offset < _p || offset > _n)
				throw Error("number length mismatch", start);
			for (int i = _p; i < offset; i++)
			{
				byte c = _b[i];
				if (c != ' ' && c != '\n' && c != '\r' && c != '\t')
					throw Error("number length mismatch", start);
			}
		}

		private double ReadDouble()
		{
			SkipWs();
			LexNumber(out int start, out bool neg, out ulong m, out int e10, out bool tooMany);
			if (m == 0 && !tooMany)
				return neg ? NegativeZero : 0d;
			if (TryFast(m, e10, tooMany, out double d))
				return neg ? -d : d;

			var reader = new JsonReader(_b, start);
			double v = reader.ReadDouble();
			CheckReaderOffset(reader.GetCurrentOffsetUnsafe(), start);
			return v;
		}

		private float ReadFloat()
		{
			SkipWs();
			LexNumber(out int start, out bool neg, out ulong m, out int e10, out bool tooMany);
			if (m == 0 && !tooMany)
				return neg ? -0f : 0f;
			if (TryFast(m, e10, tooMany, out double d)
			    && d >= 1.1754943508222875e-38 && d <= 3.4e38
			    && (BitConverter.DoubleToInt64Bits(d) & 0x1FFFFFFF) != 0x10000000)
			{
				float f = (float)d;
				return neg ? -f : f;
			}

			var reader = new JsonReader(_b, start);
			float v = reader.ReadSingle();
			CheckReaderOffset(reader.GetCurrentOffsetUnsafe(), start);
			return v;
		}
	}
}
