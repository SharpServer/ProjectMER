using AdminToys;
using InventorySystem.Items.Firearms.Attachments;
using LabApi.Features.Wrappers;
using MapGeneration.Distributors;
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

	public GameObject Create(SchematicObject schematicObject, Transform parentTransform)
	{
		GameObject gameObject = BlockType switch
		{
			BlockType.Empty => CreateEmpty(),
			BlockType.Primitive => CreatePrimitive(),
			BlockType.Light => CreateLight(),
			BlockType.Pickup => CreatePickup(schematicObject),
			BlockType.Workstation => CreateWorkstation(),
			BlockType.Text => CreateText(),
			BlockType.Interactable => CreateInteractable(),
			BlockType.Waypoint => CreateWaypoint(),
			BlockType.Locker => CreateLocker(),
			_ => CreateEmpty(true)
		};

		gameObject.name = Name;

		Transform transform = gameObject.transform;
		transform.SetParent(parentTransform);
		transform.SetLocalPositionAndRotation(Position, Quaternion.Euler(Rotation));

		transform.localScale = BlockType switch
		{
			BlockType.Empty when Scale == Vector3.zero => Vector3.one,
			BlockType.Waypoint => Scale * SerializableWaypoint.ScaleMultiplier,
			_ => Scale,
		};

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

	private GameObject CreatePrimitive()
	{
		PrimitiveObjectToy primitive = GameObject.Instantiate(PrefabManager.PrimitiveObject);

		primitive.NetworkPrimitiveType = (PrimitiveType)Convert.ToInt32(Properties["PrimitiveType"]);
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
				locked: Properties.ContainsKey("Locked"),
				attachmentsCode,
				triggerSpawn);

			return placeholder;
		}

		Pickup pickup = Pickup.Create((ItemType)Convert.ToInt32(Properties["ItemType"]), Vector3.zero)!;
		if (Properties.ContainsKey("Locked"))
			PickupEventsHandler.ButtonPickups.Add(pickup.Serial, schematicObject);

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
		if (!Properties.TryGetValue("LockerType", out object lockerTypeObj))
			return CreateEmpty(true);

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

		if (locker.TryGetComponent(out StructurePositionSync structurePositionSync))
		{
			structurePositionSync.Network_position = locker.transform.position;
			structurePositionSync.Network_rotationY = (sbyte)Mathf.RoundToInt(locker.transform.rotation.eulerAngles.y / 5.625f);
		}

		return locker.gameObject;
	}
}
