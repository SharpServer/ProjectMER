using System;
using System.Collections.Generic;
using Interactables.Interobjects.DoorUtils;
using InventorySystem.Items.Pickups;
using MapGeneration.Distributors;
using MEC;
using Mirror;
using ProjectMER.Features.Enums;
using ProjectMER.Features.Extensions;
using UnityEngine;

using Room = LabApi.Features.Wrappers.Room;
using LabApiLocker = LabApi.Features.Wrappers.Locker;
using LapApiLockerChamber = LabApi.Features.Wrappers.LockerChamber;

namespace ProjectMER.Features.Serializable.Lockers;

public class SerializableLocker : SerializableObject
{
	public LockerType LockerType { get; set; } = LockerType.PedestalScp500;

	public List<SerializableLockerLoot> Loot { get; set; } = [];

	public List<SerializableLockerChamber> Chambers { get; set; } = [];

	/// <summary>
	/// 簡易アイテム設定（統一書式）。指定するとネイティブの Loot 抽選は無効になり、
	/// リストの順にチャンバーへ振り分けて配置される。
	/// 例: ["Medkit", "(CItem)MasterCard", "(ItemType)Adrenaline"]
	/// </summary>
	public List<string> Items { get; set; } = [];

	/// <summary>
	/// 全チャンバー一括のキーカード権限（例: "ContainmentLevelTwo" / "Checkpoints, ExitGates"）。
	/// 空なら Chambers の個別設定を使う。
	/// </summary>
	public string Permissions { get; set; } = string.Empty;

	/// <summary>
	/// 全チャンバー一括の開閉状態。null なら Chambers の個別設定を使う。
	/// </summary>
	public bool? Open { get; set; }

	public override GameObject? SpawnOrUpdateObject(Room? room = null, GameObject? instance = null)
	{
		Locker locker = instance == null ? UnityEngine.Object.Instantiate(LockerPrefab) : instance.GetComponent<Locker>();
		Vector3 position = room.GetAbsolutePosition(Position);
		Quaternion rotation = room.GetAbsoluteRotation(Rotation);
		_prevIndex = Index;

		locker.transform.SetPositionAndRotation(position, rotation);
		locker.transform.localScale = Scale;

		if (locker.TryGetComponent(out StructurePositionSync structurePositionSync))
		{
			structurePositionSync.Network_position = locker.transform.position;
			structurePositionSync.Network_rotationY = (sbyte)Mathf.RoundToInt(locker.transform.rotation.eulerAngles.y / 5.625f);
		}

		LabApiLocker labApiLocker = LabApiLocker.Get(locker);
		if (LockerType != _prevType)
			SetDefaultSettings(labApiLocker);

		bool useSimpleItems = Items.Count > 0;

		labApiLocker.ClearLockerLoot();
		if (!useSimpleItems)
		{
			foreach (SerializableLockerLoot loot in Loot)
			{
				labApiLocker.AddLockerLoot(loot.TargetItem, loot.RemainingUses, loot.ProbabilityPoints, loot.MinPerChamber, loot.MaxPerChamber);
			}
		}
		else
		{
			// 簡易 Items 指定時はネイティブの初回抽選そのものを止める
			locker._serverChambersFilled = true;
		}

		bool hasBulkPermissions = TryParsePermissions(Permissions, out DoorPermissionFlags bulkPermissions);

		int i = 0;
		labApiLocker.ClearAllChambers();
		foreach (LapApiLockerChamber chamber in labApiLocker.Chambers)
		{
			if (i <= Chambers.Count - 1)
			{
				chamber.AcceptableItems = Chambers[i].AcceptableItems.ToArray();
				chamber.RequiredPermissions = Chambers[i].RequiredPermissions;
			}

			if (hasBulkPermissions)
				chamber.RequiredPermissions = bulkPermissions;

			i++;
		}

		_prevType = LockerType;
		NetworkServer.UnSpawn(locker.gameObject);
		NetworkServer.Spawn(locker.gameObject);

		Timing.CallDelayed(0.25f, () =>
		{
			if (locker == null)
				return;

			if (useSimpleItems)
				FillSimpleItems(labApiLocker);

			foreach (ItemPickupBase itemPickupBase in locker.GetComponentsInChildren<ItemPickupBase>())
			{
				if (itemPickupBase.TryGetComponent(out Rigidbody rigidbody))
					rigidbody.isKinematic = false;
			}

			int i = 0;
			foreach (LapApiLockerChamber chamber in labApiLocker.Chambers)
			{
				bool isOpen = Open ?? (i <= Chambers.Count - 1 && Chambers[i].IsOpen);
				chamber.IsOpen = isOpen;
				i++;
			}
		});

		return locker.gameObject;
	}

	/// <summary>
	/// 統一書式の Items をチャンバーへ順番に振り分けて配置する。
	/// bare 名は CItem（プロバイダ経由）優先 → ItemType。
	/// </summary>
	private void FillSimpleItems(LabApiLocker labApiLocker)
	{
		List<LapApiLockerChamber> chambers = labApiLocker.Chambers.ToList();
		if (chambers.Count == 0)
			return;

		foreach (LapApiLockerChamber chamber in chambers)
			chamber.RemoveAllItems();

		for (int index = 0; index < Items.Count; index++)
		{
			LapApiLockerChamber chamber = chambers[index % chambers.Count];
			ItemSpawnSpec spec = ItemSpawnSpec.Parse(Items[index], null);

			if (spec.AllowsCustom && TrySpawnCustomItemInChamber(chamber, spec.Name))
				continue;

			if (!spec.AllowsVanilla)
			{
				Logger.Warn($"Locker item '{spec.Name}' has no custom item provider.");
				continue;
			}

			if (!spec.TryGetItemType(ItemType.None, out ItemType itemType) || itemType == ItemType.None)
			{
				Logger.Warn($"Locker item '{Items[index]}' matched no custom item and is not a valid ItemType.");
				continue;
			}

			chamber.Base.SpawnItem(itemType, 1);
		}
	}

	private static bool TrySpawnCustomItemInChamber(LapApiLockerChamber chamber, string customItemName)
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

	private static bool TryParsePermissions(string permissions, out DoorPermissionFlags flags)
	{
		flags = DoorPermissionFlags.None;
		return !string.IsNullOrWhiteSpace(permissions) && Enum.TryParse(permissions, true, out flags);
	}

	private void SetDefaultSettings(LabApiLocker labApiLocker)
	{
		Loot.Clear();
		Chambers.Clear();

		foreach (LockerLoot loot in labApiLocker.Loot)
		{
			Loot.Add(new SerializableLockerLoot(loot.TargetItem, loot.RemainingUses, loot.MaxPerChamber, loot.ProbabilityPoints, loot.MinPerChamber));
		}

		foreach (LapApiLockerChamber chamber in labApiLocker.Chambers)
		{
			Chambers.Add(new SerializableLockerChamber(chamber.AcceptableItems, chamber.IsOpen, chamber.RequiredPermissions));
		}
	}

	private Locker LockerPrefab
	{
		get
		{
			Locker prefab = LockerType switch
			{
				LockerType.PedestalScp500 => PrefabManager.PedestalScp500,
				LockerType.LargeGun => PrefabManager.LockerLargeGun,
				LockerType.RifleRack => PrefabManager.LockerRifleRack,
				LockerType.Misc => PrefabManager.LockerMisc,
				LockerType.Medkit => PrefabManager.LockerRegularMedkit,
				LockerType.Adrenaline => PrefabManager.LockerAdrenalineMedkit,
				LockerType.PedestalScp018 => PrefabManager.PedestalScp018,
				LockerType.PedestalScp207 => PrefabManager.PedstalScp207,
				LockerType.PedestalScp244 => PrefabManager.PedestalScp244,
				LockerType.PedestalScp268 => PrefabManager.PedestalScp268,
				LockerType.PedestalScp1853 => PrefabManager.PedstalScp1853,
				LockerType.PedestalScp2176 => PrefabManager.PedestalScp2176,
				LockerType.PedestalScpScp1576 => PrefabManager.PedestalScp1576,
				LockerType.PedestalAntiScp207 => PrefabManager.PedestalAntiScp207,
				LockerType.PedestalScp1344 => PrefabManager.PedestalScp1344,
				LockerType.ExperimentalWeapon => PrefabManager.LockerExperimentalWeapon,
				_ => throw new InvalidOperationException(),
			};

			return prefab;
		}
	}

	public override bool RequiresReloading => true;

	internal LockerType _prevType = LockerType.None;
}
