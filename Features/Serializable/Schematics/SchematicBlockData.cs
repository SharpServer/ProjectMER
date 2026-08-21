using AdminToys;
using Interactables.Interobjects.DoorUtils;
using InventorySystem.Items.Firearms.Attachments;
using LabApi.Features.Wrappers;
using MapGeneration.Distributors;
using MEC;
using Mirror;
using ProjectMER.Events.Handlers.Internal;
using ProjectMER.Features.Enums;
using ProjectMER.Features.Extensions;
using ProjectMER.Features.Objects;
using UnityEngine;
using LightSourceToy = AdminToys.LightSourceToy;
using PrimitiveObjectToy = AdminToys.PrimitiveObjectToy;
using TextToy = AdminToys.TextToy;
using WaypointToy = AdminToys.WaypointToy;
using LabApiLocker = LabApi.Features.Wrappers.Locker;
using LabApiLockerChamber = LabApi.Features.Wrappers.LockerChamber;

namespace ProjectMER.Features.Serializable.Schematics;

public class SchematicBlockData
{
	public virtual string Name { get; set; }

	public virtual int ObjectId { get; set; }

	public virtual int ParentId { get; set; }

	public virtual string AnimatorName { get; set; }

	public virtual Vector3 Position { get; set; }

	public virtual Vector3 Rotation { get; set; }

	public virtual Vector3 Scale { get; set; }

	public virtual BlockType BlockType { get; set; }

	public virtual Dictionary<string, object> Properties { get; set; }

	public GameObject Create(SchematicObject schematicObject, Transform parentTransform, bool isLeaf)
	{
		Vector3 localScale = Scale;
		Quaternion localRotation = Quaternion.Euler(Rotation);
		PrimitiveType primitiveType = default;

		if (BlockType == BlockType.Primitive)
		{
			primitiveType = (PrimitiveType)Convert.ToInt32(Properties["PrimitiveType"]);
			if (isLeaf)
				NormalizeLeafPrimitive(ref primitiveType, ref localRotation, ref localScale);
		}

		GameObject gameObject = BlockType switch
		{
			BlockType.Empty => CreateEmpty(),
			BlockType.Primitive => CreatePrimitive(primitiveType),
			BlockType.Light => CreateLight(),
			BlockType.Pickup => CreatePickup(schematicObject),
			BlockType.Workstation => CreateWorkstation(),
			BlockType.Text => CreateText(),
			BlockType.Interactable => CreateInteractable(),
			BlockType.Waypoint => CreateWaypoint(),
			BlockType.Locker => CreateLocker(),
			// Teleport ブロックの実体は <Name>-Teleports.json 側で生成されるため、
			// ここでは階層を保つための空 GO だけを作る（警告は出さない）。
			BlockType.Teleport => CreateEmpty(),
			BlockType.Schematic => CreateEmpty(),
			_ => CreateEmpty(true)
		};

		gameObject.name = Name;

		Transform transform = gameObject.transform;
		transform.SetParent(parentTransform);
		transform.SetLocalPositionAndRotation(Position, localRotation);
		
		if (BlockType == BlockType.Waypoint)
			gameObject.GetComponent<WaypointToy>().NetworkBoundsSize = Scale;
		else
			transform.localScale = BlockType switch
			{
				BlockType.Empty when Scale == Vector3.zero => Vector3.one,
				_ => localScale
			};

		// StructurePositionSync（ロッカー等の構造物）はクライアント側の設置位置を決めるため、
		// ブロックの座標を適用したあとに書き込む必要がある。
		// マップ配置ロッカー（SerializableLocker）も同じ順序で処理している。
		if (gameObject.TryGetComponent(out StructurePositionSync structurePositionSync))
		{
			structurePositionSync.Network_position = transform.position;
			structurePositionSync.Network_rotationY = (sbyte)Mathf.RoundToInt(transform.rotation.eulerAngles.y / 5.625f);
		}

		if (gameObject.TryGetComponent(out AdminToyBase adminToyBase))
		{
			if (Properties != null && Properties.TryGetValue("Static", out object isStatic) && Convert.ToBoolean(isStatic))
			{
				adminToyBase.NetworkIsStatic = true;
			}
			else
			{
				adminToyBase.NetworkMovementSmoothing = 60;
			}
		}

		return gameObject;
	}

	private GameObject CreateEmpty(bool fallback = false)
	{
		if (fallback)
			Logger.Warn($"{BlockType} is not yet implemented. Object will be an empty GameObject instead.");

		PrimitiveObjectToy primitive = GameObject.Instantiate(PrefabManager.PrimitiveObject);
		primitive.NetworkPrimitiveFlags = PrimitiveFlags.None;

		return primitive.gameObject;
	}

	private GameObject CreatePrimitive(PrimitiveType primitiveType)
	{
		PrimitiveObjectToy primitive = GameObject.Instantiate(PrefabManager.PrimitiveObject);

		primitive.NetworkPrimitiveType = primitiveType;
		primitive.NetworkMaterialColor = Properties["Color"].ToString().GetColorFromString();

		PrimitiveFlags primitiveFlags;
		if (Properties.TryGetValue("PrimitiveFlags", out object flags))
		{
			primitiveFlags = (PrimitiveFlags)Convert.ToByte(flags);
		}
		else
		{
			// Backward compatibility
			primitiveFlags = PrimitiveFlags.Visible;
			if (Scale.x >= 0f)
				primitiveFlags |= PrimitiveFlags.Collidable;
		}

		primitive.NetworkPrimitiveFlags = primitiveFlags;

		return primitive.gameObject;
	}

	private static void NormalizeLeafPrimitive(
		ref PrimitiveType primitiveType,
		ref Quaternion rotation,
		ref Vector3 scale)
	{
		if (primitiveType == PrimitiveType.Plane)
		{
			primitiveType = PrimitiveType.Quad;
			rotation *= Quaternion.Euler(90f, 0f, 0f);
			scale = new Vector3(scale.x * 10f, scale.z * 10f, scale.y);
		}

		if (scale.x >= 0f && scale.y >= 0f && scale.z >= 0f)
			return;

		if (primitiveType == PrimitiveType.Quad)
		{
			// Quad is symmetric in its XY plane. Preserve the original normal when
			// removing a negative thickness scale.
			if (scale.z < 0f)
				rotation *= Quaternion.Euler(180f, 0f, 0f);
		}
		else
		{
			// Two negative axes form an exact 180-degree rotation.
			if (scale.x < 0f && scale.y < 0f && scale.z >= 0f)
				rotation *= Quaternion.Euler(0f, 0f, 180f);
			else if (scale.x < 0f && scale.y >= 0f && scale.z < 0f)
				rotation *= Quaternion.Euler(0f, 180f, 0f);
			else if (scale.x >= 0f && scale.y < 0f && scale.z < 0f)
				rotation *= Quaternion.Euler(180f, 0f, 0f);
		}

		scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
	}

	private GameObject CreateLight()
	{
		LightSourceToy light = GameObject.Instantiate(PrefabManager.LightSource);

		light.NetworkLightType = Properties.TryGetValue("LightType", out object lightType) ? (LightType)Convert.ToInt32(lightType) : LightType.Point;
		light.NetworkLightColor = Properties["Color"].ToString().GetColorFromString();
		light.NetworkLightIntensity = Convert.ToSingle(Properties["Intensity"]);
		light.NetworkLightRange = Convert.ToSingle(Properties["Range"]);

		if (Properties.TryGetValue("Shadows", out object shadows))
		{
			// Backward compatibility
			light.NetworkShadowType = Convert.ToBoolean(shadows) ? LightShadows.Soft : LightShadows.None;
		}
		else
		{
			light.NetworkShadowType = (LightShadows)Convert.ToInt32(Properties["ShadowType"]);
			light.NetworkLightShape = (LightShape)Convert.ToInt32(Properties["Shape"]);
			light.NetworkSpotAngle = Convert.ToSingle(Properties["SpotAngle"]);
			light.NetworkInnerSpotAngle = Convert.ToSingle(Properties["InnerSpotAngle"]);
			light.NetworkShadowStrength = Convert.ToSingle(Properties["ShadowStrength"]);
		}

		return light.gameObject;
	}

	private GameObject CreatePickup(SchematicObject schematicObject)
	{
		if (Properties.TryGetValue("Chance", out object property) && UnityEngine.Random.Range(0, 101) > Convert.ToSingle(property))
			return new("Empty Pickup");

		string item = Properties.TryGetValue("Item", out object itemObj) ? itemObj?.ToString() ?? string.Empty : string.Empty;
		string legacyCustomItem = Properties.TryGetValue("CustomItem", out object customObj) ? customObj?.ToString() ?? string.Empty : string.Empty;
		bool triggerSpawn = Properties.TryGetValue("TriggerSpawn", out object triggerObj) && Convert.ToBoolean(triggerObj);

		// 統一 Item 指定（または旧 CustomItem / TriggerSpawn）は、スキマティック配置完了後に
		// CItem 優先で解決する遅延スポーナーへ委譲する。
		if (!string.IsNullOrWhiteSpace(item) || !string.IsNullOrWhiteSpace(legacyCustomItem) || triggerSpawn)
		{
			ItemType fallbackItemType = Properties.TryGetValue("ItemType", out object legacyType)
				? (ItemType)Convert.ToInt32(legacyType)
				: ItemType.None;
			int uses = Properties.TryGetValue("Uses", out object usesObj) ? Convert.ToInt32(usesObj) : 1;
			string attachmentsCode = Properties.TryGetValue("AttachmentsCode", out object attachments)
				? attachments?.ToString() ?? "-1"
				: "-1";

			GameObject placeholder = new("Pickup Spawner");
			SchematicPickupSpawner spawner = placeholder.AddComponent<SchematicPickupSpawner>();
			spawner.Init(
				schematicObject,
				item,
				legacyCustomItem,
				fallbackItemType,
				uses,
				locked: Properties.GetLegacyFlag("Locked"),
				attachmentsCode,
				triggerSpawn);

			return placeholder;
		}

		ItemType pickupType = (ItemType)Properties.GetInt("ItemType", (int)ItemType.None);
		if (pickupType == ItemType.None)
		{
			Logger.Warn($"Pickup block \"{Name}\" has no item assigned. Object will be an empty GameObject instead.");
			return new("Empty Pickup");
		}

		Pickup pickup = Pickup.Create(pickupType, Vector3.zero)!;
		if (Properties.GetLegacyFlag("Locked"))
			PickupEventsHandler.ButtonPickups[pickup.Serial] = schematicObject;

		return pickup.GameObject;
	}

	private GameObject CreateWorkstation()
	{
		WorkstationController workstation = GameObject.Instantiate(PrefabManager.Workstation);
		workstation.NetworkStatus = (byte)(Properties.TryGetValue("IsInteractable", out object isInteractable) && Convert.ToBoolean(isInteractable) ? 0 : 4);

		return workstation.gameObject;
	}

	private GameObject CreateText()
	{
		TextToy text = GameObject.Instantiate(PrefabManager.Text);

		text.TextFormat = Convert.ToString(Properties["Text"]);
		text.DisplaySize = Properties["DisplaySize"].ToVector2() * 20f;

		return text.gameObject;
	}

	private GameObject CreateInteractable()
	{
		InvisibleInteractableToy interactable = GameObject.Instantiate(PrefabManager.Interactable);
		interactable.NetworkShape = (InvisibleInteractableToy.ColliderShape)Convert.ToInt32(Properties["Shape"]);
		interactable.NetworkInteractionDuration = Convert.ToSingle(Properties["InteractionDuration"]);
		interactable.NetworkIsLocked = Properties.TryGetValue("IsLocked", out object isLocked) && Convert.ToBoolean(isLocked);

		return interactable.gameObject;
	}

	private GameObject CreateWaypoint()
	{
		WaypointToy waypoint = GameObject.Instantiate(PrefabManager.Waypoint);
		waypoint.NetworkPriority = byte.MaxValue;

		return waypoint.gameObject;
	}

	private GameObject CreateLocker()
	{
		if (!Properties.TryGetValueSafe("LockerType", out object lockerTypeObj))
			return CreateEmpty(true);

		float chance = Properties.GetFloat("Chance", 100f);
		if (chance < 100f && UnityEngine.Random.Range(0f, 100f) > chance)
			return new("Empty Locker");

		LockerType lockerType = (LockerType)Convert.ToInt32(lockerTypeObj);

		MapGeneration.Distributors.Locker prefab = lockerType switch
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
			_ => PrefabManager.LockerMisc,
		};

		MapGeneration.Distributors.Locker locker = GameObject.Instantiate(prefab);

		// StructurePositionSync の同期はブロック座標の適用後（Create 側）に行う
		ConfigureLockerContents(locker);

		return locker.gameObject;
	}

	/// <summary>
	/// Unity 側 LockerComponent が書き出した Loot / Chambers / Items をロッカーへ適用する。
	/// マップ配置ロッカー（<see cref="Lockers.SerializableLocker"/>）と同じ共通処理を使う。
	/// </summary>
	private void ConfigureLockerContents(MapGeneration.Distributors.Locker locker)
	{
		if (Properties.GetBool("InteractLock"))
			LockerEventsHandler.RegisterInteractLock(locker);

		List<string> items = Properties.GetStringList("Items");
		IReadOnlyList<object> lootData = Properties.GetList("Loot");
		IReadOnlyList<object> chamberData = Properties.GetList("Chambers");

		if (items.Count == 0 && lootData.Count == 0 && chamberData.Count == 0)
			return;

		LabApiLocker labApiLocker = LabApiLocker.Get(locker);
		bool useSimpleItems = items.Count > 0;

		if (useSimpleItems)
		{
			// 簡易 Items 指定時はネイティブの初回抽選そのものを止める
			labApiLocker.ClearLockerLoot();
			locker._serverChambersFilled = true;
		}
		else if (lootData.Count > 0)
		{
			LockerConfigurator.ApplyLoot(labApiLocker, ParseLoot(lootData));
		}

		List<Lockers.SerializableLockerChamber> chambers = ParseChambers(chamberData);
		if (chambers.Count > 0 || useSimpleItems)
		{
			labApiLocker.ClearAllChambers();

			int index = 0;
			foreach (LabApiLockerChamber chamber in labApiLocker.Chambers)
			{
				if (index < chambers.Count)
				{
					// エディタ側で受け入れアイテムが未設定なら、プレハブ既定値を残す
					// （空配列で上書きすると何も入れられないロッカーになってしまう）
					if (chambers[index].AcceptableItems.Count > 0)
						chamber.AcceptableItems = chambers[index].AcceptableItems.ToArray();

					chamber.RequiredPermissions = chambers[index].RequiredPermissions;
				}

				index++;
			}
		}

		bool shuffleChambers = Properties.GetBool("ShuffleChambers", true);
		int openedChambers = Properties.GetInt("OpenedChambers");

		Timing.CallDelayed(0.25f, () =>
		{
			if (locker == null)
				return;

			if (useSimpleItems)
				LockerConfigurator.FillItems(labApiLocker, items, shuffleChambers);

			LockerConfigurator.UnfreezeContents(labApiLocker);

			int index = 0;
			foreach (LabApiLockerChamber chamber in labApiLocker.Chambers)
			{
				if (index < chambers.Count && chambers[index].IsOpen)
					chamber.IsOpen = true;

				index++;
			}

			LockerConfigurator.OpenRandomChambers(labApiLocker, openedChambers);
		});
	}

	private static List<Lockers.SerializableLockerLoot> ParseLoot(IReadOnlyList<object> lootData)
	{
		List<Lockers.SerializableLockerLoot> loot = new(lootData.Count);

		foreach (object element in lootData)
		{
			Dictionary<string, object>? entry = element.AsDictionary();
			if (entry == null)
				continue;

			ItemType targetItem = (ItemType)entry.GetInt("TargetItem", (int)ItemType.None);
			if (targetItem == ItemType.None)
				continue;

			loot.Add(new Lockers.SerializableLockerLoot(
				targetItem,
				Math.Max(1, entry.GetInt("RemainingUses", 1)),
				Math.Max(1, entry.GetInt("MaxPerChamber", 1)),
				Math.Max(0, entry.GetInt("ProbabilityPoints", 100)),
				Math.Max(0, entry.GetInt("MinPerChamber", 1))));
		}

		return loot;
	}

	private static List<Lockers.SerializableLockerChamber> ParseChambers(IReadOnlyList<object> chamberData)
	{
		List<Lockers.SerializableLockerChamber> chambers = new(chamberData.Count);

		foreach (object element in chamberData)
		{
			Dictionary<string, object>? entry = element.AsDictionary();
			if (entry == null)
				continue;

			List<ItemType> acceptableItems = [];
			foreach (object acceptableItem in entry.GetList("AcceptableItems"))
			{
				try
				{
					acceptableItems.Add((ItemType)Convert.ToInt32(acceptableItem));
				}
				catch
				{
					// 不正な要素は無視する
				}
			}

			chambers.Add(new Lockers.SerializableLockerChamber(
				acceptableItems.ToArray(),
				entry.GetBool("IsOpen"),
				(DoorPermissionFlags)entry.GetInt("RequiredPermissions")));
		}

		return chambers;
	}
}
