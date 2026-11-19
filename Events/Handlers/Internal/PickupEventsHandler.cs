using System.Collections.Generic;
using InventorySystem.Items;
using InventorySystem.Items.Firearms.Attachments;
using InventorySystem.Items.Firearms.Modules;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.CustomHandlers;
using LabApi.Features.Wrappers;
using ProjectMER.Features;
using ProjectMER.Features.Objects;
using FirearmPickup = LabApi.Features.Wrappers.FirearmPickup;

namespace ProjectMER.Events.Handlers.Internal;

public class PickupEventsHandler : CustomEventsHandler
{
	internal static readonly Dictionary<ushort, SchematicObject> ButtonPickups = [];
	internal static readonly Dictionary<ushort, int> PickupUsesLeft = [];
	internal static readonly Dictionary<ushort, CustomItemPickupUseInfo> CustomItemPickupUses = [];

	public override void OnPlayerSearchingPickup(PlayerSearchingPickupEventArgs ev)
	{
		Logger.Debug($"PickupDiag Searching serial={ev.Pickup.Serial} type={ev.Pickup.Type} " +
		             $"button={ButtonPickups.ContainsKey(ev.Pickup.Serial)} allowed={ev.IsAllowed}");
		if (!ButtonPickups.TryGetValue(ev.Pickup.Serial, out SchematicObject schematic))
			return;

		ev.IsAllowed = false;
		Schematic.OnButtonInteracted(new(ev.Pickup, ev.Player, schematic));
	}

	public override void OnPlayerPickingUpItem(PlayerPickingUpItemEventArgs ev)
	{
		Logger.Debug($"PickupDiag PickingUp serial={ev.Pickup.Serial} type={ev.Pickup.Type} " +
		             $"mapped={ev.Pickup.Transform.TryGetComponentInParent(out MapEditorObject _)} " +
		             $"customUse={CustomItemPickupUses.ContainsKey(ev.Pickup.Serial)} " +
		             $"usesLeft={PickupUsesLeft.ContainsKey(ev.Pickup.Serial)} allowed={ev.IsAllowed}");
		if (!ev.Pickup.Transform.TryGetComponentInParent(out MapEditorObject _))
			return;

		if (TryHandleCustomItemPickupUses(ev))
			return;

		if (!PickupUsesLeft.ContainsKey(ev.Pickup.Serial))
			return;

		if (--PickupUsesLeft[ev.Pickup.Serial] == 0)
		{
			PickupUsesLeft.Remove(ev.Pickup.Serial);
			return;
		}

		ev.IsAllowed = false;
		ev.Pickup.IsInUse = false;

		Item item = ev.Player.AddItem(ev.Pickup.Type, ItemAddReason.PickedUp)!;
		if (ev.Pickup is not FirearmPickup firearmPickup || item is not FirearmItem firearmItem)
			return;

		firearmItem.Base.ApplyAttachmentsCode(firearmPickup.AttachmentCode, false);
		if (firearmItem.Base.TryGetModule(out MagazineModule magazineModule))
		{
			magazineModule.MagazineInserted = true;
			magazineModule.AmmoStored = magazineModule.AmmoMax;
			magazineModule.ServerResyncData();
		}
		else if (firearmItem.Base.TryGetModule(out CylinderAmmoModule cylinderAmmoModule))
		{
			cylinderAmmoModule.ServerModifyAmmo(cylinderAmmoModule.AmmoMax);
			cylinderAmmoModule.ServerResync();
		}
	}

	public override void OnPlayerPickingUpAmmo(PlayerPickingUpAmmoEventArgs ev)
	{
		if (!ev.Pickup.Transform.TryGetComponentInParent(out MapEditorObject _))
			return;

		if (!PickupUsesLeft.ContainsKey(ev.Pickup.Serial))
			return;

		if (--PickupUsesLeft[ev.Pickup.Serial] == 0)
		{
			PickupUsesLeft.Remove(ev.Pickup.Serial);
			return;
		}

		ev.IsAllowed = false;
		ev.Pickup.IsInUse = false;
		ev.Player.AddAmmo(ev.AmmoType, ev.AmmoAmount);
	}

	private static bool TryHandleCustomItemPickupUses(PlayerPickingUpItemEventArgs ev)
	{
		if (!CustomItemPickupUses.TryGetValue(ev.Pickup.Serial, out CustomItemPickupUseInfo useInfo))
			return false;

		// A single-use custom pickup is already tracked by the custom item system under
		// this pickup's serial. Let the game's normal pickup flow transfer that serial to
		// inventory so its PickedUp callback (including the pickup hint) runs normally.
		if (useInfo.UsesLeft <= 1)
		{
			CustomItemPickupUses.Remove(ev.Pickup.Serial);
			PickupUsesLeft.Remove(ev.Pickup.Serial);
			ButtonPickups.Remove(ev.Pickup.Serial);
			return false;
		}

		// Game side keeps the pickup serial when it creates the inventory item
		// (ItemSearchCompletor.Complete -> ServerAddItem(..., Info.Serial, ...)).
		// Only cancel the vanilla pickup once the custom grant actually succeeded;
		// otherwise nothing would be added and the pickup would stay in place,
		// which looks like "can interact but cannot pick up".
		if (!ItemSpawnpointCustomItemRegistry.TryGive(useInfo.CustomItemKey, ev.Pickup.Base, ev.Player))
		{
			CustomItemPickupUses.Remove(ev.Pickup.Serial);
			ButtonPickups.Remove(ev.Pickup.Serial);
			Logger.Warn($"Custom pickup '{useInfo.CustomItemKey}' (serial {ev.Pickup.Serial}) could not be granted; " +
			            "falling back to vanilla pickup so the serial-based owner can adopt it.");
			return false;
		}

		ev.IsAllowed = false;
		ev.Pickup.IsInUse = false;

		if (--useInfo.UsesLeft > 0)
			return true;

		CustomItemPickupUses.Remove(ev.Pickup.Serial);
		PickupUsesLeft.Remove(ev.Pickup.Serial);
		ButtonPickups.Remove(ev.Pickup.Serial);
		ev.Pickup.Base.DestroySelf();

		return true;
	}

	internal sealed class CustomItemPickupUseInfo(string customItemKey, int usesLeft)
	{
		public string CustomItemKey { get; } = customItemKey;

		public int UsesLeft { get; set; } = usesLeft;
	}
}
