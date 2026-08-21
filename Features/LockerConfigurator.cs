using Interactables.Interobjects.DoorUtils;
using InventorySystem.Items.Pickups;
using ProjectMER.Features.Serializable;
using ProjectMER.Features.Serializable.Lockers;
using UnityEngine;
using LabApiLocker = LabApi.Features.Wrappers.Locker;
using LabApiLockerChamber = LabApi.Features.Wrappers.LockerChamber;

namespace ProjectMER.Features;

/// <summary>
/// ロッカーの中身・チャンバー設定を適用する共通処理。
/// マップ配置ロッカー（<see cref="SerializableLocker"/>）とスキマティック内 Locker ブロックの
/// 双方から使われ、両者の挙動が食い違わないようにする。
/// </summary>
internal static class LockerConfigurator
{
	/// <summary>ネイティブの抽選テーブルを指定の Loot で置き換える。</summary>
	public static void ApplyLoot(LabApiLocker locker, IReadOnlyList<SerializableLockerLoot> loot)
	{
		locker.ClearLockerLoot();

		foreach (SerializableLockerLoot entry in loot)
			locker.AddLockerLoot(entry.TargetItem, entry.RemainingUses, entry.ProbabilityPoints, entry.MinPerChamber, entry.MaxPerChamber);
	}

	/// <summary>
	/// 統一書式の Items をチャンバーへ順番に振り分けて配置する。
	/// bare 名は CItem（プロバイダ経由）優先 → ItemType。
	/// </summary>
	/// <param name="locker">対象ロッカー。</param>
	/// <param name="items">統一書式のアイテム指定リスト。</param>
	/// <param name="shuffleChambers">true なら配置先チャンバーの順序をシャッフルする。</param>
	public static void FillItems(LabApiLocker locker, IReadOnlyList<string> items, bool shuffleChambers = false)
	{
		List<LabApiLockerChamber> chambers = locker.Chambers.ToList();
		if (chambers.Count == 0)
			return;

		foreach (LabApiLockerChamber chamber in chambers)
			chamber.RemoveAllItems();

		if (shuffleChambers)
			Shuffle(chambers);

		for (int index = 0; index < items.Count; index++)
		{
			LabApiLockerChamber chamber = chambers[index % chambers.Count];
			ItemSpawnSpec spec = ItemSpawnSpec.Parse(items[index], null);

			if (spec.AllowsCustom && TrySpawnCustomItemInChamber(chamber, spec.Name))
				continue;

			if (!spec.AllowsVanilla)
			{
				Logger.Warn($"Locker item \"{spec.Name}\" has no registered custom item provider. " +
				            "Remove the \"(CItem)\" prefix to fall back to a vanilla ItemType.");
				continue;
			}

			if (!spec.TryGetItemType(ItemType.None, out ItemType itemType) || itemType == ItemType.None)
			{
				Logger.Warn($"Locker item {ItemSpawnSpec.DescribeUnknownItem(spec.Name)}");
				continue;
			}

			chamber.Base.SpawnItem(itemType, 1);
		}
	}

	/// <summary>
	/// カスタムアイテムをチャンバーへ配置する。プロバイダが解決できなければ false。
	/// </summary>
	public static bool TrySpawnCustomItemInChamber(LabApiLockerChamber chamber, string customItemName)
	{
		Transform spawnpoint = chamber.Base.Spawnpoint != null ? chamber.Base.Spawnpoint : chamber.Base.transform;
		var syntheticSpawnpoint = new SerializableItemSpawnpoint();

		if (!ItemSpawnpointCustomItemRegistry.TrySpawn(
			    customItemName,
			    syntheticSpawnpoint,
			    spawnpoint.position,
			    spawnpoint.rotation,
			    spawnpoint,
			    out ItemPickupBase? pickup) ||
		    pickup == null)
		{
			return false;
		}

		// ネイティブと同じ「開けるまでロック」挙動に合わせる
		if (!chamber.Base.WasEverOpened)
		{
			PickupSyncInfo info = pickup.Info;
			info.Locked = true;
			pickup.NetworkInfo = info;
		}

		chamber.Base.Content.Add(pickup);
		return true;
	}

	/// <summary>
	/// キーカード権限文字列を解釈する。"ContainmentLevelTwo" のような名前、
	/// "Checkpoints, ExitGates" のような複数指定、数値のいずれも受け付ける。
	/// </summary>
	public static bool TryParsePermissions(string? permissions, out DoorPermissionFlags flags)
	{
		flags = DoorPermissionFlags.None;
		if (string.IsNullOrWhiteSpace(permissions))
			return false;

		string trimmed = permissions!.Trim();

		if (Enum.TryParse(trimmed, true, out flags))
			return true;

		if (int.TryParse(trimmed, out int numeric))
		{
			flags = (DoorPermissionFlags)numeric;
			return true;
		}

		Logger.Warn($"\"{trimmed}\" is not a valid keycard permission. " +
		            $"Valid values: {string.Join(", ", Enum.GetNames(typeof(DoorPermissionFlags)))}.");
		return false;
	}

	/// <summary>指定数のチャンバーをランダムに開く。</summary>
	public static void OpenRandomChambers(LabApiLocker locker, int amount)
	{
		if (amount <= 0)
			return;

		List<LabApiLockerChamber> chambers = locker.Chambers.ToList();
		Shuffle(chambers);

		for (int i = 0; i < Math.Min(amount, chambers.Count); i++)
			chambers[i].IsOpen = true;
	}

	/// <summary>ロッカー内の Pickup の Rigidbody を物理有効に戻す。</summary>
	public static void UnfreezeContents(LabApiLocker locker)
	{
		foreach (ItemPickupBase itemPickupBase in locker.Base.GetComponentsInChildren<ItemPickupBase>())
		{
			if (itemPickupBase.TryGetComponent(out Rigidbody rigidbody))
				rigidbody.isKinematic = false;
		}
	}

	private static void Shuffle<T>(IList<T> list)
	{
		for (int i = list.Count - 1; i > 0; i--)
		{
			int j = UnityEngine.Random.Range(0, i + 1);
			(list[i], list[j]) = (list[j], list[i]);
		}
	}
}
