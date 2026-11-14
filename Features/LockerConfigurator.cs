using Interactables.Interobjects.DoorUtils;
using InventorySystem.Items.Pickups;
using LabApiAmmoPickup = LabApi.Features.Wrappers.AmmoPickup;
using LabApiPickup = LabApi.Features.Wrappers.Pickup;
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
	/// bare 名はカスタムアイテム（プロバイダ経由）優先 → ItemType。
	/// </summary>
	/// <param name="locker">対象ロッカー。</param>
	/// <param name="items">統一書式のアイテム指定リスト。</param>
	/// <param name="shuffleChambers">true なら配置先チャンバーの順序をシャッフルする。</param>
	public static void FillItems(LabApiLocker locker, IReadOnlyList<string> items, bool shuffleChambers = false, IReadOnlyList<int>? ammoPerItem = null)
	{
		List<LabApiLockerChamber> chambers = locker.Chambers.ToList();
		if (chambers.Count == 0)
			return;

		foreach (LabApiLockerChamber chamber in chambers)
			ClearChamber(chamber);

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
				            $"Remove the \"{ItemSpawnSpec.CustomPrefix}\" prefix to fall back to a vanilla ItemType.");
				continue;
			}

			if (!spec.TryGetItemType(ItemType.None, out ItemType itemType) || itemType == ItemType.None)
			{
				Logger.Warn($"Locker item {ItemSpawnSpec.DescribeUnknownItem(spec.Name)}");
				continue;
			}

			chamber.Base.SpawnItem(itemType, 1);
			if (ammoPerItem != null && index < ammoPerItem.Count)
				ApplyAmmoToNewest(chamber, 1, ammoPerItem[index]);
		}
	}

	/// <summary>
	/// カスタムアイテムをチャンバーへ配置する。プロバイダが解決できなければ false。
	/// </summary>
	public static bool TrySpawnCustomItemInChamber(LabApiLockerChamber chamber, string customItemName)
	{
		string key = ItemSpawnSpec.Parse(customItemName, customItemName).Name;
		if (chamber.Base.SpawnOnFirstChamberOpening && !chamber.Base.WasEverOpened && !chamber.Base.IsOpen)
		{
			LockerCustomItemQueue queue = chamber.Base.GetComponent<LockerCustomItemQueue>() ??
				chamber.Base.gameObject.AddComponent<LockerCustomItemQueue>();
			queue.Add(chamber, key);
			return true;
		}
		ItemSpawnpointCustomItemRegistry.TryGetBaseType(key, out ItemType baseType);
		chamber.Base.GetSpawnpoint(baseType, chamber.Base.Content.Count, out Vector3 position, out Quaternion rotation, out Transform spawnpoint);
		var syntheticSpawnpoint = new SerializableItemSpawnpoint();

		if (!ItemSpawnpointCustomItemRegistry.TrySpawn(
			    key,
			    syntheticSpawnpoint,
			    position,
			    rotation,
			    spawnpoint,
			    out ItemPickupBase? pickup) ||
		    pickup == null)
		{
			return false;
		}

		// A provider without metadata may still choose an item-specific spawnpoint.
		chamber.Base.GetSpawnpoint(pickup.Info.ItemId, chamber.Base.Content.Count, out position, out rotation, out spawnpoint);
		pickup.transform.SetParent(spawnpoint, true);
		pickup.transform.SetPositionAndRotation(position, rotation);
		PickupSyncInfo info = pickup.Info;
		info.Locked = !chamber.Base.WasEverOpened && !chamber.Base.IsOpen;
		pickup.NetworkInfo = info;

		chamber.Base.Content.Add(pickup);
		return true;
	}

	private static void ClearChamber(LabApiLockerChamber chamber)
	{
		chamber.Base.GetComponent<LockerCustomItemQueue>()?.Clear();
		chamber.RemoveAllItems();
		chamber.Base.ToBeSpawned.Clear();
	}

	/// <summary>Use a mixed native/custom weighted table, preserving native chamber limits.</summary>
	public static void FillMixedLoot(LabApiLocker locker, IReadOnlyList<SerializableLockerLoot> loot)
	{
		List<LabApiLockerChamber> chambers = locker.Chambers.ToList();
		foreach (LabApiLockerChamber chamber in chambers) ClearChamber(chamber);
		if (locker.Base.MinChambersToFill != 0 && locker.Base.MaxChambersToFill >= locker.Base.MinChambersToFill)
		{
			Shuffle(chambers);
			int count = UnityEngine.Random.Range(locker.Base.MinChambersToFill, locker.Base.MaxChambersToFill + 1);
			if (chambers.Count > count) chambers.RemoveRange(Math.Max(0, count), chambers.Count - Math.Max(0, count));
		}
		int[] remaining = loot.Select(entry => Math.Max(0, entry.RemainingUses)).ToArray();
		foreach (LabApiLockerChamber chamber in chambers)
		{
			List<int> candidates = Enumerable.Range(0, loot.Count).Where(i => remaining[i] > 0 &&
				(!string.IsNullOrWhiteSpace(loot[i].CustomItem) ||
				 (loot[i].TargetItem != ItemType.None && (chamber.AcceptableItems.Length == 0 ||
				  chamber.AcceptableItems.Contains(loot[i].TargetItem))))).ToList();
			if (candidates.Count == 0) continue;
			// Vanilla FillChamber includes ProbabilityPoints+1 tickets per candidate.
			double roll = UnityEngine.Random.value * candidates.Sum(i => Math.Max(0, loot[i].ProbabilityPoints) + 1d);
			int selected = candidates[candidates.Count - 1];
			foreach (int candidate in candidates)
			{
				roll -= Math.Max(0, loot[candidate].ProbabilityPoints) + 1d;
				if (roll < 0) { selected = candidate; break; }
			}
			SerializableLockerLoot entry = loot[selected];
			int min = Math.Max(0, entry.MinPerChamber);
			int max = Math.Max(min, entry.MaxPerChamber);
			int amount = UnityEngine.Random.Range(min, max + 1);
			if (!string.IsNullOrWhiteSpace(entry.CustomItem))
			{
				for (int i = 0; i < amount; i++)
					if (!TrySpawnCustomItemInChamber(chamber, entry.CustomItem))
						Logger.Warn($"Cannot spawn custom locker loot '{entry.CustomItem}'. Check the Slafight class name.");
			}
			else
			{
				chamber.Base.SpawnItem(entry.TargetItem, amount);
				ApplyAmmoToNewest(chamber, amount, entry.AmmoPerPickup);
			}
			remaining[selected]--;
		}
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

	/// <summary>
	/// チャンバーへ最後に追加された count 個のうち弾薬ピックアップの弾数を ammo にする（LabAPI の AmmoPickup ラッパー経由）。
	/// ammo が 0 以下なら何もしない（ゲーム既定）。初回開封まで未スポーンのピックアップにも、スポーン前に値が入る。
	/// </summary>
	public static void ApplyAmmoToNewest(LabApiLockerChamber chamber, int count, int ammo)
	{
		if (ammo <= 0 || count <= 0)
			return;
		List<ItemPickupBase> content = chamber.Base.Content.ToList();
		for (int i = Math.Max(0, content.Count - count); i < content.Count; i++)
			SetAmmo(content[i], ammo);
	}

	/// <summary>
	/// ネイティブ抽選（ApplyLoot）で出た弾薬に、Loot の AmmoPerPickup を種類ごとに当てる。
	/// 同じ弾薬の枠が複数あるときは、0 より大きい最初の枠の値を使う（ネイティブ抽選はどの枠が当たったかを残さないため）。
	/// </summary>
	public static void ApplyLootAmmo(LabApiLocker locker, IReadOnlyList<SerializableLockerLoot> loot)
	{
		Dictionary<ItemType, int> perType = new();
		foreach (SerializableLockerLoot entry in loot)
		{
			if (entry.AmmoPerPickup > 0 && string.IsNullOrWhiteSpace(entry.CustomItem) && !perType.ContainsKey(entry.TargetItem))
				perType[entry.TargetItem] = entry.AmmoPerPickup;
		}
		if (perType.Count == 0)
			return;

		foreach (LabApiLockerChamber chamber in locker.Chambers)
		foreach (ItemPickupBase pickup in chamber.Base.Content.ToList())
		{
			if (pickup != null && perType.TryGetValue(pickup.Info.ItemId, out int ammo))
				SetAmmo(pickup, ammo);
		}
	}

	private static void SetAmmo(ItemPickupBase? pickupBase, int ammo)
	{
		if (pickupBase == null)
			return;
		if (LabApiPickup.Get(pickupBase) is LabApiAmmoPickup ammoPickup)
			ammoPickup.Ammo = (ushort)Math.Min(ushort.MaxValue, Math.Max(1, ammo));
	}

	/// <summary>ロッカー内の Pickup の Rigidbody を物理有効に戻す。</summary>
	public static void UnfreezeContents(LabApiLocker locker)
	{
		foreach (LabApiLockerChamber chamber in locker.Chambers)
		foreach (ItemPickupBase itemPickupBase in chamber.Base.Content)
		{
			if (itemPickupBase == null) continue;
			if (chamber.Base.IsOpen || chamber.Base.WasEverOpened)
			{
				PickupSyncInfo info = itemPickupBase.Info;
				info.Locked = false;
				itemPickupBase.NetworkInfo = info;
			}
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
