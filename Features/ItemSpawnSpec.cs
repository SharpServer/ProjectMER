namespace ProjectMER.Features;

/// <summary>
/// ItemSpawnpoint / Pickup / Locker の統一アイテム指定文字列の解釈結果。
/// <list type="bullet">
/// <item>"Medkit" — カスタムアイテム優先で解決し、無ければ ItemType として解釈</item>
/// <item>"(ItemType)Medkit" / "(Item)Medkit" / "(Vanilla)Medkit" — ItemType のみ</item>
/// <item>"(CItem)MyItem" / "(CustomItem)MyItem" / "(Custom)MyItem" — カスタムアイテムのみ</item>
/// <item>空文字 — 旧来の CustomItemKey / ItemType プロパティへフォールバック</item>
/// </list>
/// ItemType 名の照合は寛容で、大文字小文字・空白・アンダースコア・ハイフン・ドットの違いを無視する
/// （"gun com15" / "Gun_COM15" / "ItemType.GunCOM15" / "SCP-500" などはすべて解決できる）。
/// </summary>
public readonly struct ItemSpawnSpec
{
	private enum Kind
	{
		/// <summary>旧来動作: ItemType プロパティでスポーン。</summary>
		LegacyVanilla,

		/// <summary>カスタム優先 → ItemType フォールバック。</summary>
		Auto,

		/// <summary>カスタムアイテムのみ。</summary>
		CustomOnly,

		/// <summary>ItemType のみ。</summary>
		VanillaOnly,
	}

	/// <summary>正規化済み ItemType 名 → ItemType。</summary>
	private static readonly Dictionary<string, ItemType> NormalizedItemTypes = BuildItemTypeLookup();

	private readonly Kind _kind;

	private ItemSpawnSpec(Kind kind, string name)
	{
		_kind = kind;
		Name = name;
	}

	/// <summary>指定名（カスタムキーまたは ItemType 名）。LegacyVanilla では空。</summary>
	public string Name { get; }

	/// <summary>カスタムアイテムとしての解決を試みるか。</summary>
	public bool AllowsCustom => _kind is Kind.Auto or Kind.CustomOnly;

	/// <summary>ItemType としての解決を試みるか。</summary>
	public bool AllowsVanilla => _kind is Kind.Auto or Kind.VanillaOnly or Kind.LegacyVanilla;

	/// <summary>
	/// ItemType を解決する。LegacyVanilla は <paramref name="fallback"/>（ItemType プロパティ）を返す。
	/// </summary>
	public bool TryGetItemType(ItemType fallback, out ItemType itemType)
	{
		if (_kind == Kind.LegacyVanilla)
		{
			itemType = fallback;
			return true;
		}

		return TryResolveItemType(Name, out itemType);
	}

	/// <summary>
	/// 統一指定文字列と旧来プロパティから解釈する。
	/// </summary>
	public static ItemSpawnSpec Parse(string? item, string? legacyCustomItemKey)
	{
		string trimmed = item?.Trim() ?? string.Empty;

		if (trimmed.Length == 0)
		{
			// 旧来動作: CustomItemKey があればカスタムのみ、無ければ ItemType プロパティ。
			return !string.IsNullOrWhiteSpace(legacyCustomItemKey)
				? new ItemSpawnSpec(Kind.CustomOnly, legacyCustomItemKey!.Trim())
				: new ItemSpawnSpec(Kind.LegacyVanilla, string.Empty);
		}

		if (trimmed[0] == '(')
		{
			int close = trimmed.IndexOf(')');
			if (close > 1)
			{
				string prefix = trimmed.Substring(1, close - 1).Trim().ToLowerInvariant();
				string name = trimmed.Substring(close + 1).Trim();

				switch (prefix)
				{
					case "itemtype":
					case "item":
					case "vanilla":
						return new ItemSpawnSpec(Kind.VanillaOnly, name);
					case "citem":
					case "customitem":
					case "custom":
						return new ItemSpawnSpec(Kind.CustomOnly, name);
					default:
						// 未知のプレフィックスは括弧ごと名前の一部として扱う（旧挙動）が、
						// 綴り間違いを黙って握り潰さないよう警告する。
						Logger.Warn($"Unknown item specification prefix \"({prefix})\" in \"{trimmed}\". " +
						            "Expected \"(ItemType)\" or \"(CItem)\". Treating the whole string as an item name.");
						break;
				}
			}
		}

		return new ItemSpawnSpec(Kind.Auto, trimmed);
	}

	/// <summary>
	/// 表記ゆれを許容して ItemType を解決する。
	/// 大文字小文字・空白・"_"・"-"・"."・先頭の "ItemType" を無視し、数値 ID も受け付ける。
	/// </summary>
	public static bool TryResolveItemType(string? name, out ItemType itemType)
	{
		itemType = ItemType.None;
		if (string.IsNullOrWhiteSpace(name))
			return false;

		string trimmed = name!.Trim();

		if (NormalizedItemTypes.TryGetValue(Normalize(trimmed), out itemType))
			return true;

		if (int.TryParse(trimmed, out int numeric) && TryGetNumericItemType(numeric, out itemType))
		{
			return true;
		}

		itemType = ItemType.None;
		return false;
	}

	/// <summary>
	/// 数値 ID から ItemType を解決する（ItemType の基底型に依存しない）。
	/// </summary>
	private static bool TryGetNumericItemType(int numeric, out ItemType itemType)
	{
		foreach (ItemType candidate in (ItemType[])Enum.GetValues(typeof(ItemType)))
		{
			if ((int)candidate != numeric)
				continue;

			itemType = candidate;
			return true;
		}

		itemType = ItemType.None;
		return false;
	}

	/// <summary>
	/// 解決できなかったアイテム名に対する説明文を作る（近い ItemType 名があれば提案する）。
	/// </summary>
	public static string DescribeUnknownItem(string? name)
	{
		string trimmed = name?.Trim() ?? string.Empty;
		string? suggestion = GetClosestItemTypeName(trimmed);

		return suggestion == null
			? $"\"{trimmed}\" is not a valid ItemType and no custom item provider recognized it."
			: $"\"{trimmed}\" is not a valid ItemType and no custom item provider recognized it. Did you mean \"{suggestion}\"?";
	}

	/// <summary>
	/// 綴り間違いに一番近い ItemType 名を返す。近いものが無ければ null。
	/// </summary>
	public static string? GetClosestItemTypeName(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
			return null;

		string normalized = Normalize(name!);
		if (normalized.Length == 0)
			return null;

		string? best = null;
		int bestDistance = int.MaxValue;

		foreach (KeyValuePair<string, ItemType> pair in NormalizedItemTypes)
		{
			// 部分一致は最優先で提案する（"com15" → "GunCOM15" など）。
			// 極端に短い入力は誤提案になりやすいので部分一致の対象から外す。
			if (normalized.Length >= 3 && (pair.Key.Contains(normalized) || normalized.Contains(pair.Key)))
			{
				int lengthDelta = Math.Abs(pair.Key.Length - normalized.Length);
				if (lengthDelta < bestDistance)
				{
					bestDistance = lengthDelta;
					best = pair.Value.ToString();
				}

				continue;
			}

			if (bestDistance == int.MaxValue || bestDistance > 0)
			{
				int distance = LevenshteinDistance(normalized, pair.Key);
				int threshold = Math.Max(2, normalized.Length / 3);
				if (distance <= threshold && distance < bestDistance)
				{
					bestDistance = distance;
					best = pair.Value.ToString();
				}
			}
		}

		return best;
	}

	private static Dictionary<string, ItemType> BuildItemTypeLookup()
	{
		Dictionary<string, ItemType> lookup = new(StringComparer.Ordinal);

		foreach (ItemType itemType in (ItemType[])Enum.GetValues(typeof(ItemType)))
		{
			string normalized = Normalize(itemType.ToString());
			if (normalized.Length > 0)
				lookup[normalized] = itemType;
		}

		return lookup;
	}

	/// <summary>
	/// 英数字だけを残して小文字化し、先頭の "itemtype" 修飾を取り除く。
	/// </summary>
	private static string Normalize(string value)
	{
		char[] buffer = new char[value.Length];
		int length = 0;

		foreach (char c in value)
		{
			if (char.IsLetterOrDigit(c))
				buffer[length++] = char.ToLowerInvariant(c);
		}

		// "ItemType.Medkit" / "itemtype medkit" のような修飾付きも受け付ける。
		const string itemTypePrefix = "itemtype";
		int start = 0;
		if (length > itemTypePrefix.Length && StartsWith(buffer, length, itemTypePrefix))
			start = itemTypePrefix.Length;

		return new string(buffer, start, length - start);
	}

	private static bool StartsWith(char[] buffer, int length, string prefix)
	{
		if (length < prefix.Length)
			return false;

		for (int i = 0; i < prefix.Length; i++)
		{
			if (buffer[i] != prefix[i])
				return false;
		}

		return true;
	}

	private static int LevenshteinDistance(string left, string right)
	{
		if (left.Length == 0)
			return right.Length;

		if (right.Length == 0)
			return left.Length;

		int[] previous = new int[right.Length + 1];
		int[] current = new int[right.Length + 1];

		for (int j = 0; j <= right.Length; j++)
			previous[j] = j;

		for (int i = 1; i <= left.Length; i++)
		{
			current[0] = i;

			for (int j = 1; j <= right.Length; j++)
			{
				int cost = left[i - 1] == right[j - 1] ? 0 : 1;
				current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
			}

			(previous, current) = (current, previous);
		}

		return previous[right.Length];
	}
}
