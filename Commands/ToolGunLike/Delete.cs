using CommandSystem;
using LabApi.Features.Permissions;
using LabApi.Features.Wrappers;
using ProjectMER.Features.Objects;
using ProjectMER.Features.ToolGun;

namespace ProjectMER.Commands.ToolGunLike;

public class Delete : ICommand
{
	/// <inheritdoc/>
	public string Command => "delete";

	/// <inheritdoc/>
	public string[] Aliases { get; } = ["del", "remove", "rm"];

	/// <inheritdoc/>
	public string Description => "Deletes the aimed object, an ID, or the entire selection (remove selected).";

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

		if (arguments.Count > 0)
		{
			string id = arguments.At(0);
			if (id.Equals("selected", StringComparison.OrdinalIgnoreCase) && arguments.Count == 1)
				return Remove(ToolGunHandler.GetSelection(player), player, out response);
			if (ToolGunHandler.TryGetObjectById(id, out MapEditorObject idObject))
				return Remove([idObject], player, out response);

			response = $"Unable to find object with ID of {id}!";
			return false;
		}

		if (ToolGunHandler.TryGetMapObject(player, out MapEditorObject mapEditorObject))
			return Remove([mapEditorObject], player, out response);

		response = "You aren't looking at any Map Editor object!";
		return false;
	}

	private static bool Remove(IReadOnlyList<MapEditorObject> objects, Player player, out string response)
	{
		if (objects.Count == 0)
		{
			response = "No objects selected.";
			return false;
		}
		if (!ToolGunHandler.DeleteObjects(objects, player))
		{
			response = "One of these objects is being grabbed or is no longer available.";
			return false;
		}
		response = $"Removed {objects.Count} object(s). Use mp undo to restore them.";
		return true;
	}
}
