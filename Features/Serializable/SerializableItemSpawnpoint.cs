using InventorySystem.Items.Firearms.Attachments;
using InventorySystem.Items.Firearms.Modules;
using InventorySystem.Items.Pickups;
using LabApi.Features.Wrappers;
using MEC;
using ProjectMER.Events.Handlers.Internal;
using ProjectMER.Features.Extensions;
using ProjectMER.Features.Interfaces;
using UnityEngine;
using PrimitiveObjectToy = AdminToys.PrimitiveObjectToy;

namespace ProjectMER.Features.Serializable;

public class SerializableItemSpawnpoint : SerializableObject, IIndicatorDefinition
{
	public ItemType ItemType { get; set; } = ItemType.Lantern;
	public float Weight { get; set; } = -1;
	public string AttachmentsCode { get; set; } = "-1";
	public string CustomItemKey { get; set; } = string.Empty;
	public uint NumberOfItems { get; set; } = 1;
	public int NumberOfUses { get; set; } = 1;
	public bool UseGravity { get; set; } = true;
	public bool CanBePickedUp { get; set; } = true;

	public override GameObject? SpawnOrUpdateObject(Room? room = null, GameObject? instance = null)
	{
		GameObject itemSpawnPoint = instance ?? new GameObject("ItemSpawnpoint");
		Vector3 position = room.GetAbsolutePosition(Position);
		Quaternion rotation = room.GetAbsoluteRotation(Rotation);
		_prevIndex = Index;

		itemSpawnPoint.transform.SetPositionAndRotation(position, rotation);

		if (instance != null)
		{
			foreach (ItemPickupBase pickup in instance.GetComponentsInChildren<ItemPickupBase>())
			{
				PickupEventsHandler.PickupUsesLeft.Remove(pickup.Info.Serial);
				PickupEventsHandler.CustomItemPickupUses.Remove(pickup.Info.Serial);
				pickup.DestroySelf();
			}
		}

		for (int i = 0; i < NumberOfItems; i++)
		{
			if (!string.IsNullOrWhiteSpace(CustomItemKey))
			{
				if (!ItemSpawnpointCustomItemRegistry.TrySpawn(CustomItemKey, this, position, rotation, itemSpawnPoint.transform, out ItemPickupBase? customPickup) ||
				    customPickup == null)
				{
					Logger.Warn($"ItemSpawnpoint custom item '{CustomItemKey}' has no provider.");
					continue;
				}

				ConfigurePickup(customPickup, itemSpawnPoint.transform);
				PickupEventsHandler.CustomItemPickupUses[customPickup.Info.Serial] = new(CustomItemKey, NumberOfUses);
				continue;
			}

			Pickup pickup = Pickup.Create(ItemType, position, rotation, Scale)!;
			if (pickup.Base != null)
				ConfigurePickup(pickup.Base, itemSpawnPoint.transform);
			PickupEventsHandler.PickupUsesLeft.Add(pickup.Serial, NumberOfUses);

			pickup.Spawn();

			if (pickup is FirearmPickup firearmPickup)
			{
				Timing.CallDelayed(0.01f, () =>
				{
					firearmPickup.Base.OnDistributed();
					firearmPickup.AttachmentCode = uint.TryParse(AttachmentsCode, out uint attachmentsCode) ? attachmentsCode : AttachmentsUtils.GetRandomAttachmentsCode(firearmPickup.Type);
					if (firearmPickup.Base.Template.TryGetModule(out MagazineModule magazineModule))
						magazineModule.ServerResyncData();
				});
			}
		}

		return itemSpawnPoint.gameObject;
	}

	private void ConfigurePickup(ItemPickupBase pickup, Transform parent)
	{
		pickup.transform.parent = parent;
		if (Weight != -1)
			pickup.Info.WeightKg = Weight;

		if (pickup.TryGetComponent(out Rigidbody rigidbody))
			rigidbody.isKinematic = !UseGravity;

		pickup.Info.Locked = !CanBePickedUp;
	}

	public GameObject SpawnOrUpdateIndicator(Room room, GameObject? instance = null)
	{
		PrimitiveObjectToy cube;

		Vector3 position = room.GetAbsolutePosition(Position);
		Quaternion rotation = room.GetAbsoluteRotation(Rotation);

		if (instance == null)
		{
			cube = UnityEngine.Object.Instantiate(PrefabManager.PrimitiveObject);
			cube.NetworkPrimitiveType = PrimitiveType.Cube;
			cube.NetworkPrimitiveFlags = AdminToys.PrimitiveFlags.Visible;
			cube.NetworkMaterialColor = new Color(0f, 1f, 0f, 0.9f);
			cube.transform.localScale = Vector3.one * 0.25f;
		}
		else
		{
			cube = instance.GetComponent<PrimitiveObjectToy>();
		}

		cube.transform.SetPositionAndRotation(position, rotation);

		return cube.gameObject;
	}
}
