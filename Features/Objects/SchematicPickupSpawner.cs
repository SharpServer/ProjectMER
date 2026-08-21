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
/// スキマティック内 Pickup ブロックのスポーナー。
/// 統一 Item 指定（CItem 優先 → ItemType）と TriggerSpawn（プラグインからの出現制御）に対応する。
/// スキマティック配置完了後（Start）に自動スポーンする（IsTriggerSpawn の場合は保留）。
/// </summary>
public class SchematicPickupSpawner : MonoBehaviour
{
	private readonly List<ItemPickupBase> _spawnedPickups = [];

	private SchematicObject? _schematic;
	private ItemSpawnSpec _spec;
	private ItemType _fallbackItemType = ItemType.None;
	private string _rawItem = string.Empty;
	private string _attachmentsCode = "-1";

	/// <summary>使用回数（CustomItemPickupUses / PickupUsesLeft へ登録される）。</summary>
	public int Uses { get; set; } = 1;

	/// <summary>Locked ボタン Pickup として登録するか。</summary>
	public bool Locked { get; set; }

	/// <summary>true なら自動スポーンせず、SpawnItems() 呼び出しでのみ出現する。</summary>
	public bool IsTriggerSpawn { get; set; }

	/// <summary>
	/// 統一書式のアイテム指定（bare 名 = CItem 優先 → ItemType、"(ItemType)X" / "(CItem)X"）。
	/// 変更すると次回 SpawnItems から反映される。
	/// </summary>
	public string Item
	{
		get => _rawItem;
		set
		{
			_rawItem = value ?? string.Empty;
			_spec = ItemSpawnSpec.Parse(_rawItem, null);
		}
	}

	/// <summary>このスポーナーが現在出現させている Pickup。</summary>
	public IReadOnlyList<ItemPickupBase> SpawnedPickups
	{
		get
		{
			_spawnedPickups.RemoveAll(pickup => pickup == null);
			return _spawnedPickups;
		}
	}

	public bool HasSpawnedItems => SpawnedPickups.Count > 0;

	internal void Init(
		SchematicObject schematic,
		string rawItem,
		string legacyCustomItem,
		ItemType fallbackItemType,
		int uses,
		bool locked,
		string attachmentsCode,
		bool isTriggerSpawn)
	{
		_schematic = schematic;
		_rawItem = !string.IsNullOrWhiteSpace(rawItem) ? rawItem : legacyCustomItem;
		_spec = ItemSpawnSpec.Parse(rawItem, legacyCustomItem);
		_fallbackItemType = fallbackItemType;
		Uses = uses;
		Locked = locked;
		_attachmentsCode = attachmentsCode;
		IsTriggerSpawn = isTriggerSpawn;
	}

	private void Start()
	{
		if (!IsTriggerSpawn)
			SpawnItems(clearExisting: false);
	}

	/// <summary>
	/// アイテムをスポーンする。CItem 優先 → ItemType の順で解決する。
	/// </summary>
	/// <param name="clearExisting">true なら既存のスポーン済みアイテムを先に破棄する。</param>
	/// <returns>スポーンできたアイテム数。</returns>
	public int SpawnItems(bool clearExisting = true)
	{
		if (clearExisting)
			ClearItems();

		Vector3 position = transform.position;
		Quaternion rotation = transform.rotation;

		if (_spec.AllowsCustom)
		{
			// ItemSpawnpoint と同じプロバイダ経由で CItem を解決する
			var syntheticSpawnpoint = new SerializableItemSpawnpoint();
			if (ItemSpawnpointCustomItemRegistry.TrySpawn(_spec.Name, syntheticSpawnpoint, position, rotation, transform, out ItemPickupBase? customPickup) &&
			    customPickup != null)
			{
				_spawnedPickups.Add(customPickup);
				PickupEventsHandler.CustomItemPickupUses[customPickup.Info.Serial] = new(_spec.Name, Uses);
				RegisterButtonPickup(customPickup);
				return 1;
			}
		}

		if (!_spec.AllowsVanilla)
		{
			Logger.Warn($"Schematic pickup \"{_spec.Name}\" has no registered custom item provider. " +
			            "Remove the \"(CItem)\" prefix to fall back to a vanilla ItemType.");
			return 0;
		}

		if (!_spec.TryGetItemType(_fallbackItemType, out ItemType itemType) || itemType == ItemType.None)
		{
			Logger.Warn($"Schematic pickup {ItemSpawnSpec.DescribeUnknownItem(_spec.Name)}");
			return 0;
		}

		Pickup pickup = Pickup.Create(itemType, position, rotation)!;
		if (pickup.Base == null)
			return 0;

		_spawnedPickups.Add(pickup.Base);
		pickup.Base.transform.parent = transform;
		PickupEventsHandler.PickupUsesLeft[pickup.Serial] = Uses;
		RegisterButtonPickup(pickup.Base);
		pickup.Spawn();

		if (pickup is FirearmPickup firearmPickup)
		{
			Timing.CallDelayed(0.01f, () =>
			{
				firearmPickup.Base.OnDistributed();
				firearmPickup.AttachmentCode = uint.TryParse(_attachmentsCode, out uint attachmentsCode)
					? attachmentsCode
					: AttachmentsUtils.GetRandomAttachmentsCode(firearmPickup.Type);
				if (firearmPickup.Base.Template.TryGetModule(out MagazineModule magazineModule))
					magazineModule.ServerResyncData();
			});
		}

		return 1;
	}

	/// <summary>
	/// このスポーナーが出現させた Pickup をすべて破棄する。
	/// </summary>
	public void ClearItems()
	{
		foreach (ItemPickupBase pickup in _spawnedPickups)
		{
			if (pickup == null)
				continue;

			PickupEventsHandler.PickupUsesLeft.Remove(pickup.Info.Serial);
			PickupEventsHandler.CustomItemPickupUses.Remove(pickup.Info.Serial);
			PickupEventsHandler.ButtonPickups.Remove(pickup.Info.Serial);
			pickup.DestroySelf();
		}

		_spawnedPickups.Clear();
	}

	private void RegisterButtonPickup(ItemPickupBase pickup)
	{
		if (Locked && _schematic != null)
			PickupEventsHandler.ButtonPickups[pickup.Info.Serial] = _schematic;
	}
}
