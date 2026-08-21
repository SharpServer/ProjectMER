using System.Collections;

namespace ProjectMER.Features.Extensions;

/// <summary>
/// <see cref="Serializable.Schematics.SchematicBlockData.Properties"/> のような
/// JSON 由来の弱い型付け辞書を安全に読むためのヘルパー。
/// キーが無い・型が違う・null といったケースでも例外を投げずにフォールバックを返す。
/// </summary>
public static class SchematicPropertyExtensions
{
	public static bool TryGetValueSafe(this Dictionary<string, object>? properties, string key, out object value)
	{
		value = null!;
		return properties != null && properties.TryGetValue(key, out value!) && value != null;
	}

	public static string GetString(this Dictionary<string, object>? properties, string key, string fallback = "")
		=> properties.TryGetValueSafe(key, out object value) ? value.ToString() ?? fallback : fallback;

	public static int GetInt(this Dictionary<string, object>? properties, string key, int fallback = 0)
	{
		if (!properties.TryGetValueSafe(key, out object value))
			return fallback;

		try
		{
			return Convert.ToInt32(value);
		}
		catch
		{
			return fallback;
		}
	}

	public static float GetFloat(this Dictionary<string, object>? properties, string key, float fallback = 0f)
	{
		if (!properties.TryGetValueSafe(key, out object value))
			return fallback;

		try
		{
			return Convert.ToSingle(value);
		}
		catch
		{
			return fallback;
		}
	}

	public static bool GetBool(this Dictionary<string, object>? properties, string key, bool fallback = false)
	{
		if (!properties.TryGetValueSafe(key, out object value))
			return fallback;

		try
		{
			return Convert.ToBoolean(value);
		}
		catch
		{
			return fallback;
		}
	}

	/// <summary>
	/// 値が bool として読めればその値、キーだけが存在して値が読めない場合は
	/// <paramref name="keyOnlyValue"/> を返す。
	/// 旧形式（キーの存在だけで真を意味していたプロパティ）との互換用。
	/// </summary>
	public static bool GetLegacyFlag(this Dictionary<string, object>? properties, string key, bool keyOnlyValue = true)
	{
		if (properties == null || !properties.ContainsKey(key))
			return false;

		object? value = properties[key];
		if (value == null)
			return keyOnlyValue;

		try
		{
			return Convert.ToBoolean(value);
		}
		catch
		{
			return keyOnlyValue;
		}
	}

	/// <summary>
	/// 配列プロパティを列挙する。配列でなければ空を返す。
	/// </summary>
	public static IReadOnlyList<object> GetList(this Dictionary<string, object>? properties, string key)
	{
		if (!properties.TryGetValueSafe(key, out object value))
			return Array.Empty<object>();

		return value.AsList();
	}

	/// <summary>
	/// 文字列配列プロパティを読む。null や空要素は除外される。
	/// </summary>
	public static List<string> GetStringList(this Dictionary<string, object>? properties, string key)
	{
		List<string> result = [];

		foreach (object element in properties.GetList(key))
		{
			string? text = element?.ToString();
			if (!string.IsNullOrWhiteSpace(text))
				result.Add(text!);
		}

		return result;
	}

	/// <summary>
	/// JSON 配列（<see cref="object"/>[] / <see cref="IEnumerable"/>）を列挙可能なリストへ変換する。
	/// </summary>
	public static IReadOnlyList<object> AsList(this object? value)
	{
		switch (value)
		{
			case null:
			case string:
				return Array.Empty<object>();
			case IReadOnlyList<object> list:
				return list;
			case IEnumerable enumerable:
				List<object> result = [];
				foreach (object element in enumerable)
					result.Add(element);

				return result;
			default:
				return Array.Empty<object>();
		}
	}

	/// <summary>
	/// JSON オブジェクトを <see cref="Dictionary{TKey, TValue}"/> として読む。オブジェクトでなければ null。
	/// </summary>
	public static Dictionary<string, object>? AsDictionary(this object? value)
	{
		switch (value)
		{
			case null:
				return null;
			case Dictionary<string, object> dictionary:
				return dictionary;
			case IDictionary<string, object> genericDictionary:
				return new Dictionary<string, object>(genericDictionary);
			case IDictionary dictionary:
				Dictionary<string, object> result = [];
				foreach (DictionaryEntry entry in dictionary)
				{
					string? key = entry.Key?.ToString();
					if (key != null)
						result[key] = entry.Value!;
				}

				return result;
			default:
				return null;
		}
	}
}
