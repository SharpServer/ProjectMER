namespace ProjectMER.Features;

/// <summary>
/// ItemSpawnpoint の統一アイテム指定文字列の解釈結果。
/// <list type="bullet">
/// <item>"Medkit" — カスタムアイテム優先で解決し、無ければ ItemType として解釈</item>
/// <item>"(ItemType)Medkit" / "(Item)Medkit" / "(Vanilla)Medkit" — ItemType のみ</item>
/// <item>"(CItem)MyItem" / "(CustomItem)MyItem" / "(Custom)MyItem" — カスタムアイテムのみ</item>
/// <item>空文字 — 旧来の CustomItemKey / ItemType プロパティへフォールバック</item>
/// </list>
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

		if (Enum.TryParse(Name, true, out itemType))
			return true;

		if (int.TryParse(Name, out int numeric) && Enum.IsDefined(typeof(ItemType), numeric))
		{
			itemType = (ItemType)numeric;
			return true;
		}

		itemType = ItemType.None;
		return false;
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
				}
			}
		}

		return new ItemSpawnSpec(Kind.Auto, trimmed);
	}
}
