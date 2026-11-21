using CommandSystem;
using System.Globalization;
using LabApi.Features.Permissions;
using LabApi.Features.Wrappers;
using ProjectMER.Features;
using ProjectMER.Features.Objects;
using ProjectMER.Features.Serializable;
using ProjectMER.Features.ToolGun;

namespace ProjectMER.Commands.ToolGunLike;

/// <summary>
/// Command used for selecting the objects.
/// </summary>
public class Select : ICommand
{
	/// <inheritdoc/>
	public string Command => "select";

	/// <inheritdoc/>
	public string[] Aliases { get; } = ["sel", "choose"];

	/// <inheritdoc/>
	public string Description => "Select by aim, ID or radius. Usage: select [add|remove] [<id> ... | near <meters>], select list, select clear.";

	public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
	{
		if (!sender.HasAnyPermission($"mpr.{Command}"))
		{
			response = $"You don't have permission to execute this command. Required permission: mpr.{Command}";
			return false;
		}

		Player? player = Player.Get(sender);
		if (player is null)
		{
			response = "This command can't be run from the server console.";
			return false;
		}

		string mode = arguments.Count == 0 ? string.Empty : arguments.At(0).ToLowerInvariant();
		if (mode == "list" || mode == "clear")
		{
			if (arguments.Count != 1)
			{
				response = $"Usage: mer select {mode}";
				return false;
			}
			if (mode == "clear") ToolGunHandler.ClearSelection(player);
			response = DescribeSelection(player);
			return true;
		}

		bool append = mode == "add";
		bool remove = mode == "remove";
		int start = append || remove ? 1 : 0;
		IReadOnlyList<MapEditorObject> targets;
		if (arguments.Count > start && arguments.At(start).Equals("near", StringComparison.OrdinalIgnoreCase))
		{
			if (arguments.Count != start + 2 ||
				!float.TryParse(arguments.At(start + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float radius) ||
				!(radius > 0f) || float.IsInfinity(radius))
			{
				response = "Specify a positive radius in meters, e.g. mer select near 10.";
				return false;
			}
			targets = ToolGunHandler.FindNearbyObjects(player.Position, radius);
			if (targets.Count == 0)
			{
				response = "No editable objects in that radius.";
				return false;
			}
		}
		else if (arguments.Count > start)
		{
			List<MapEditorObject> objects = [];
			for (int i = start; i < arguments.Count; i++)
			{
				string id = arguments.At(i);
				if (!ToolGunHandler.TryGetObjectById(id, out MapEditorObject obj))
				{
					response = $"Unable to find object with ID '{id}'.";
					return false;
				}
				objects.Add(obj);
			}
			targets = objects;
		}
		else if (ToolGunHandler.TryGetMapObject(player, out MapEditorObject aimed))
		{
			targets = [aimed];
		}
		else
		{
			if (!append && !remove && ToolGunHandler.GetSelection(player).Count > 0)
				ToolGunHandler.ClearSelection(player);
			response = "You aren't looking at any object.";
			return false;
		}

		if (remove) ToolGunHandler.RemoveSelection(player, targets);
		else ToolGunHandler.SetSelection(player, targets, append);
		IReadOnlyList<MapEditorObject> selection = ToolGunHandler.GetSelection(player);
		response = selection.Count == 0 ? "No objects selected." :
			$"{selection.Count} selected. Primary: {selection[0].Id}.";
		return true;
	}

	private static string DescribeSelection(Player player)
	{
		IReadOnlyList<MapEditorObject> selection = ToolGunHandler.GetSelection(player);
		if (selection.Count == 0) return "No objects selected.";
		return $"{selection.Count} selected (first is the primary):\n" +
			string.Join("\n", selection.Select(obj => $"{obj.Id}  {obj.MapName}  {obj.Base.GetType().Name}"));
	}
}
