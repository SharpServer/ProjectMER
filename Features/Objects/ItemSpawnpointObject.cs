using InventorySystem.Items.Firearms.Attachments;
using InventorySystem.Items.Firearms.Modules;
using InventorySystem.Items.Pickups;
using LabApi.Features.Wrappers;
using MEC;
using ProjectMER.Events.Handlers.Internal;
using ProjectMER.Features.Extensions;
using ProjectMER.Features.Serializable;
using UnityEngine;

namespace ProjectMER.Features.Objects;

/// <summary>
/// ItemSpawnpoint の GO に付与されるランタイムコンポーネント。
/// IsTriggerSpawn なスポーンポイントのアイテム出現をプラグイン側から制御できる。
/// </summary>
public class ItemSpawnpointObject : MonoBehaviour
{
	/// <summary>スポーンポイントが生成・更新されたときに発火する。</summary>
	public static event Action<ItemSpawnpointObject>? Spawned;

	/// <summary>スポーンポイントの GO が破棄されたときに発火する。</summary>
	public static event Action<ItemSpawnpointObject>? Destroyed;

	private static readonly List<ItemSpawnpointObject> ActiveInstances = [];

	private readonly List<ItemPickupBase> _spawnedPickups = [];

	/// <summary>現在アクティブな全スポーンポイント。</summary>
	public static IReadOnlyList<ItemSpawnpointObject> List
	{
		get
		{
			ActiveInstances.RemoveAll(instance => instance == null);
			return ActiveInstances;
		}
	}

	/// <summary>Tag が一致するスポーンポイントを列挙する（大文字小文字無視）。</summary>
	public static IEnumerable<ItemSpawnpointObject> GetByTag(string tag)
		=> List.Where(instance => string.Equals(instance.Tag, tag, StringComparison.OrdinalIgnoreCase));

	/// <summary>シリアライズ元データ。</summary>
	public SerializableItemSpawnpoint Base { get; private set; } = null!;

	public string Tag => Base?.Tag ?? string.Empty;

	public bool IsTriggerSpawn => Base != null && Base.IsTriggerSpawn;

	/// <summary>このスポーンポイントが現在出現させている Pickup。</summary>
	public IReadOnlyList<ItemPickupBase> SpawnedPickups
	{
		get
		{
			_spawnedPickups.RemoveAll(pickup => pickup == null);
			return _spawnedPickups;
		}
	}

	public bool HasSpawnedItems => SpawnedPickups.Count > 0;

	internal void Init(SerializableItemSpawnpoint data)
	{
		Base = data;
		Spawned?.Invoke(this);
	}

	/// <summary>
	/// データに従ってアイテムをスポーンする。
	/// 統一指定（Item）はカスタムアイテム優先で解決し、無ければ ItemType として解釈する。
	/// </summary>
	/// <param name="clearExisting">true なら既存のスポーン済みアイテムを先に破棄する。</param>
	/// <returns>スポーンできたアイテム数。</returns>
	public int SpawnItems(bool clearExisting = true)
	{
		if (Base == null)
			return 0;

		if (clearExisting)
			ClearItems();

		Vector3 position = transform.position;
		Quaternion rotation = transform.rotation;
		ItemSpawnSpec spec = ItemSpawnSpec.Parse(Base.Item, Base.CustomItemKey);
		int spawned = 0;

		for (int i = 0; i < Base.NumberOfItems; i++)
		{
			if (spec.AllowsCustom &&
			    ItemSpawnpointCustomItemRegistry.TrySpawn(spec.Name, Base, position, rotation, transform, out ItemPickupBase? customPickup) &&
			    customPickup != null)
			{
				ConfigurePickup(customPickup);
				PickupEventsHandler.CustomItemPickupUses[customPickup.Info.Serial] = new(spec.Name, Base.NumberOfUses);
				_spawnedPickups.Add(customPickup);
				spawned++;
				continue;
			}

			if (!spec.AllowsVanilla)
			{
				Logger.Warn($"ItemSpawnpoint custom item \"{spec.Name}\" has no registered custom item provider. " +
				            "Remove the \"(CItem)\" prefix to fall back to a vanilla ItemType.");
				break;
			}

			if (!spec.TryGetItemType(Base.ItemType, out ItemType itemType) || itemType == ItemType.None)
			{
				Logger.Warn($"ItemSpawnpoint item {ItemSpawnSpec.DescribeUnknownItem(spec.Name)}");
				break;
			}

			Pickup pickup = Pickup.Create(itemType, position, rotation, Base.Scale)!;
			if (pickup.Base != null)
			{
				ConfigurePickup(pickup.Base);
				_spawnedPickups.Add(pickup.Base);
			}

			PickupEventsHandler.PickupUsesLeft[pickup.Serial] = Base.NumberOfUses;
			pickup.Spawn();
			spawned++;

			if (pickup is FirearmPickup firearmPickup)
			{
				Timing.CallDelayed(0.01f, () =>
				{
					firearmPickup.Base.OnDistributed();
					firearmPickup.AttachmentCode = uint.TryParse(Base.AttachmentsCode, out uint attachmentsCode)
						? attachmentsCode
						: AttachmentsUtils.GetRandomAttachmentsCode(firearmPickup.Type);
					if (firearmPickup.Base.Template.TryGetModule(out MagazineModule magazineModule))
						magazineModule.ServerResyncData();
				});
			}
		}

		return spawned;
	}

	/// <summary>
	/// このスポーンポイント配下の Pickup をすべて破棄する。
	/// </summary>
	public void ClearItems()
	{
		foreach (ItemPickupBase pickup in GetComponentsInChildren<ItemPickupBase>())
		{
			PickupEventsHandler.PickupUsesLeft.Remove(pickup.Info.Serial);
			PickupEventsHandler.CustomItemPickupUses.Remove(pickup.Info.Serial);
			pickup.DestroySelf();
		}

		_spawnedPickups.Clear();
	}

	private void ConfigurePickup(ItemPickupBase pickup)
	{
		pickup.transform.parent = transform;
		if (Base.Weight != -1)
			pickup.Info.WeightKg = Base.Weight;

		if (pickup.TryGetComponent(out Rigidbody rigidbody))
			rigidbody.isKinematic = !Base.UseGravity;

		pickup.Info.Locked = !Base.CanBePickedUp;
	}

	private void Awake() => ActiveInstances.Add(this);

	private void OnDestroy()
	{
		ActiveInstances.Remove(this);
		Destroyed?.Invoke(this);
	}
}
