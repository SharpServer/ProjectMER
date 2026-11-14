using System.Text;
using Utf8Json;

namespace ProjectMER.Features.Serializable.Schematics;

/// <summary>
/// スキマティック JSON の読み込み。解析結果をファイルの更新時刻・サイズで検証してキャッシュし、
/// 呼び出し側には毎回ディープコピーを返す。専用パーサーが失敗したら Utf8Json に任せる。
/// </summary>
internal static class SchematicDataLoader
{
	private sealed class CacheEntry
	{
		public DateTime LastWriteUtc;
		public long Length;
		public SchematicObjectDataList Template = null!;
	}

	private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);
	private static readonly object CacheLock = new();

	/// <summary>ファイルを読み込み（キャッシュ命中ならファイル本体は読まない）、呼び出し側専用のコピーを返す。</summary>
	public static SchematicObjectDataList Load(string path)
	{
		string fullPath = Path.GetFullPath(path);
		var info = new FileInfo(fullPath);
		DateTime lastWrite = info.LastWriteTimeUtc;
		long length = info.Length;

		SchematicObjectDataList? template = null;
		lock (CacheLock)
		{
			if (Cache.TryGetValue(fullPath, out CacheEntry? entry) && entry.LastWriteUtc == lastWrite && entry.Length == length)
				template = entry.Template;
		}

		if (template == null)
		{
			template = ParseWithFallback(ReadUtf8Bytes(fullPath));
			lock (CacheLock)
				Cache[fullPath] = new CacheEntry { LastWriteUtc = lastWrite, Length = length, Template = template };
		}

		return DeepCopy(template);
	}

	public static void Clear()
	{
		lock (CacheLock)
			Cache.Clear();
	}

	/// <summary>専用パーサー、失敗時は Utf8Json（異常系の例外型・メッセージを従来と同一に保つ）。</summary>
	public static SchematicObjectDataList ParseWithFallback(byte[] bytes)
	{
		try
		{
			return SchematicDataParser.Parse(bytes);
		}
		catch (Exception e)
		{
			Logger.Debug($"Fast schematic parser fell back to Utf8Json: {e.Message}");
			return JsonSerializer.Deserialize<SchematicObjectDataList>(bytes);
		}
	}

	private static byte[] ReadUtf8Bytes(string path)
	{
		byte[] bytes = File.ReadAllBytes(path);
		// UTF-16 の JSON は従来の File.ReadAllText が読めていたので、UTF-8 に直す。
		if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
			return Encoding.UTF8.GetBytes(File.ReadAllText(path));
		return bytes;
	}

	public static SchematicObjectDataList DeepCopy(SchematicObjectDataList source)
	{
		var copy = new SchematicObjectDataList
		{
			Path = source.Path,
			RootObjectId = source.RootObjectId,
		};

		if (source.Blocks == null)
		{
			copy.Blocks = null!;
			return copy;
		}

		var blocks = new List<SchematicBlockData>(source.Blocks.Count);
		foreach (SchematicBlockData block in source.Blocks)
		{
			blocks.Add(block == null ? null! : new SchematicBlockData
			{
				Name = block.Name,
				ObjectId = block.ObjectId,
				ParentId = block.ParentId,
				AnimatorName = block.AnimatorName,
				Position = block.Position,
				Rotation = block.Rotation,
				Scale = block.Scale,
				BlockType = block.BlockType,
				Properties = block.Properties == null ? null! : CopyDictionary(block.Properties),
			});
		}

		copy.Blocks = blocks;
		return copy;
	}

	private static Dictionary<string, object> CopyDictionary(Dictionary<string, object> source)
	{
		var copy = new Dictionary<string, object>(source.Count);
		foreach (KeyValuePair<string, object> pair in source)
			copy.Add(pair.Key, CopyValue(pair.Value));
		return copy;
	}

	private static object CopyValue(object value)
	{
		switch (value)
		{
			case Dictionary<string, object> dictionary:
				return CopyDictionary(dictionary);
			case List<object> list:
				var listCopy = new List<object>(list.Count);
				foreach (object item in list)
					listCopy.Add(CopyValue(item));
				return listCopy;
			default:
				// double / string / bool / null は不変なので共有してよい。
				return value;
		}
	}
}
