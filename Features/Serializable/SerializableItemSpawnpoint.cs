using InventorySystem.Items.Firearms.Attachments;
using InventorySystem.Items.Firearms.Modules;
using InventorySystem.Items.Pickups;
using LabApi.Features.Wrappers;
using MEC;
using ProjectMER.Events.Handlers.Internal;
using ProjectMER.Features.Extensions;
using ProjectMER.Features.Interfaces;
using ProjectMER.Features.Objects;
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

	/// <summary>
	/// 統一アイテム指定。カスタムアイテム優先で解決し、無ければ ItemType として解釈する。
	/// "(ItemType)Medkit" / "(CItem)MyItem" で種類を強制できる。
	/// 空の場合は旧来の CustomItemKey / ItemType プロパティが使われる。
	/// </summary>
	public string Item { get; set; } = string.Empty;

	/// <summary>プラグイン側から検索するためのタグ。</summary>
	public string Tag { get; set; } = string.Empty;

	/// <summary>
	/// true の場合、生成時にはアイテムをスポーンしない。
	/// プラグイン側から ItemSpawnpointObject.SpawnItems() を呼んだときのみ出現する。
	/// </summary>
	public bool IsTriggerSpawn { get; set; } = false;

	public override GameObject? SpawnOrUpdateObject(Room? room = null, GameObject? instance = null)
	{
		GameObject itemSpawnPoint = instance ?? new GameObject("ItemSpawnpoint");
		Vector3 position = room.GetAbsolutePosition(Position);
		Quaternion rotation = room.GetAbsoluteRotation(Rotation);
		_prevIndex = Index;

		itemSpawnPoint.transform.SetPositionAndRotation(position, rotation);

		ItemSpawnpointObject spawnpointObject = itemSpawnPoint.GetComponent<ItemSpawnpointObject>() ??
		                                        itemSpawnPoint.AddComponent<ItemSpawnpointObject>();
		spawnpointObject.Init(this);

		spawnpointObject.ClearItems();
		if (!IsTriggerSpawn)
			spawnpointObject.SpawnItems(clearExisting: false);

		return itemSpawnPoint.gameObject;
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
