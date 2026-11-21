using System.Text;
using LabApi.Features.Wrappers;
using NorthwoodLib.Pools;
using ProjectMER.Commands.Modifying.Position.SubCommands;
using ProjectMER.Features.Enums;
using ProjectMER.Features.Extensions;
using ProjectMER.Features.Objects;
using ProjectMER.Features.Serializable;
using ProjectMER.Features.Serializable.Schematics;
using UnityEngine;
using UserSettings.ServerSpecific;
using YamlDotNet.Serialization;

namespace ProjectMER.Features.ToolGun;

public static class ToolGunUI
{
	public static string GetHintHUD(Player player) => GetHintHUD(player, ToolGunHandler.GetSelection(player));

	internal static string GetHintHUD(Player player, IReadOnlyList<MapEditorObject> selected)
	{
		StringBuilder sb = StringBuilderPool.Shared.Rent();

		sb.Append("<font=\"LiberationSans SDF\">");

		IReadOnlyList<MapEditorObject> grabbed = Grab.GetGrabbedObjects(player);
		if (grabbed.Count > 1)
		{
			AppendGroup(sb, grabbed, grabbing: true);
		}
		else if (selected.Count > 1)
		{
			AppendGroup(sb, selected, grabbing: false);
		}
		else if (selected.Count == 1)
		{
			MapEditorObject mapEditorObject = selected[0];
			sb.Append($"<size=50%>MapName: {MapUtils.GetColoredMapName(mapEditorObject.MapName)}</size>");
			sb.AppendLine();
			sb.Append($"<size=50%>ID: {MapUtils.GetColoredString(mapEditorObject.Id)}</size>");
			sb.AppendLine();

			object instance = mapEditorObject.Base;
			foreach (string property in instance.GetType().GetProperties().ToList().GetColoredProperties(instance))
			{
				sb.Append($"<size=50%>");
				sb.Append(property);
				sb.Append("</size>");
				sb.AppendLine();
			}
		}

		if (!player.CurrentItem.IsToolGun(out ToolGunItem toolGun))
		{
			sb.Append("</font>");
			return StringBuilderPool.Shared.ToStringReturn(sb);
		}

		sb.Append($"<size=50%>");
		sb.Append(GetToolGunModeString(player, toolGun));
		sb.Append("</size>");

		sb.AppendLine();


		sb.Append($"<size=50%>");
		sb.Append($"{player.Position:F3}");
		sb.Append("</size>");

		sb.AppendLine();

		sb.Append($"<size=50%>");
		sb.Append(GetRoomString(player));
		sb.Append("</size>");
		sb.Append("</font>");

		return StringBuilderPool.Shared.ToStringReturn(sb);
	}

	private static void AppendGroup(StringBuilder sb, IReadOnlyList<MapEditorObject> objects, bool grabbing)
	{
		const int previewCount = 3;
		sb.Append("<size=50%><color=#66D9FF><b>").Append(grabbing ? "GRABBING" : "SELECTED")
			.Append(" · ").Append(objects.Count).Append(" OBJECTS</b></color>\n");
		sb.Append(string.Join(", ", objects.Take(previewCount).Select(GetShortObjectName)));
		if (objects.Count > previewCount)
			sb.Append("  <color=#B3B3B3>+").Append(objects.Count - previewCount).Append(" more</color>");
		sb.Append("\n<color=#B3B3B3>").Append(grabbing ? "mer pos grab · release" : "mer select list · show all")
			.Append("</color></size>\n");
	}

	private static string GetShortObjectName(MapEditorObject obj)
	{
		if (obj == null) return "Removed";

		string name = obj.Base switch
		{
			SerializableSchematic schematic when !string.IsNullOrWhiteSpace(schematic.SchematicName) => schematic.SchematicName,
			SerializableItemSpawnpoint pickup when !string.IsNullOrWhiteSpace(pickup.Item) => pickup.Item,
			SerializableDoor door => $"{door.DoorType} Door",
			SerializableTeleport => $"Teleport {obj.Id}",
			_ => obj.Base.GetType().Name.Replace("Serializable", string.Empty),
		};
		name = name.Replace('<', '‹').Replace('>', '›').Replace('\n', ' ').Replace('\r', ' ');
		const int nameLimit = 20;
		return name.Length > nameLimit ? name.Substring(0, nameLimit - 1) + "…" : name;
	}

	private static string GetToolGunModeString(Player player, ToolGunItem toolGun)
	{
		if (toolGun.CreateMode)
		{
			string output;
			if (toolGun.SelectedObjectToSpawn == ToolGunObjectType.Schematic)
			{
				if (ServerSpecificSettingsSync.TryGetSettingOfUser(player.ReferenceHub, 0, out SSDropdownSetting dropdownSetting) && dropdownSetting.TryGetSyncSelectionText(out string schematicName))
					output = schematicName.ToUpper();
				else
					output = "Please select schematic in options";
			}
			else
			{
				output = toolGun.SelectedObjectToSpawn.ToString().ToUpper();
			}

			return $"<color=green>CREATE</color>\n<color=yellow>{output}</color>";
		}

		string name = " ";
		if (ToolGunHandler.Raycast(player, out RaycastHit hit))
		{
			if (hit.transform.TryGetComponentInParent(out MapEditorObject mapEditorObject))
			{
				if (mapEditorObject is IndicatorObject indicatorObject)
					mapEditorObject = IndicatorObject.Dictionary[indicatorObject];

				if (mapEditorObject.gameObject.TryGetComponent(out SchematicObject schematicObject))
				{
					name = schematicObject.Name.ToUpper();
				}
				else
				{
					name = mapEditorObject.Base.ToString().Split('.').Last().Replace("Serializable", "").ToUpper();
				}
			}
		}

		if (toolGun.DeleteMode)
			return $"<color=red>DELETE</color>\n<color=yellow>{name}</color>";

		if (toolGun.SelectMode)
			return $"<color=yellow>SELECT</color>\n<color=yellow>{name}</color>";

		return "\n ";
	}

	private static string GetRoomString(Player player)
	{
		Room room = RoomExtensions.GetRoomAtPosition(player.Camera.transform.position);
		var roomType = room.FindRoomType();
		List<Room> list = ListPool<Room>.Shared.Rent(Room.List.Where(x => x.Base != null && x.FindRoomType() == roomType));

		string roomString;
		if (list.Count == 1)
		{
			roomString = room.GetRoomStringId();
		}
		else
		{
			roomString = $"{room.GetRoomStringId()} ({list.IndexOf(room)}) ({list.Count})";
		}

		ListPool<Room>.Shared.Return(list);
		return roomString;
	}
}
