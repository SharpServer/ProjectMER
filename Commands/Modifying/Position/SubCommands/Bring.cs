using CommandSystem;
using LabApi.Features.Permissions;
using LabApi.Features.Wrappers;
using ProjectMER.Features.Objects;
using ProjectMER.Features.ToolGun;

namespace ProjectMER.Commands.Modifying.Position.SubCommands;

/// <summary>
/// Modifies object's position by setting it to the sender's current position.
/// </summary>
public class Bring : ICommand
{
	/// <inheritdoc/>
	public string Command => "bring";

	/// <inheritdoc/>
	public string[] Aliases { get; } = [];

	/// <inheritdoc/>
	public string Description => "Brings the selected objects to the player using the primary object as the anchor.";

	/// <inheritdoc/>
	public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
	{
		if (!sender.HasAnyPermission($"mpr.position"))
		{
			response = $"You don't have permission to execute this command. Required permission: mpr.position";
			return false;
		}

		Player? player = Player.Get(sender);
		if (player is null)
		{
			response = "This command can't be run from the server console.";
			return false;
		}

		IReadOnlyList<MapEditorObject> selection = ToolGunHandler.GetSelection(player);
		if (selection.Any(obj => Grab.IsGrabbing(obj.Base)))
		{
			response = "Release the grabbed objects before moving them.";
			return false;
		}
		if (selection.Count == 0)
		{
			response = "You need to select an object first!";
			return false;
		}

		EditorHistory.State[] before = selection.Select(EditorHistory.Capture).ToArray();
		SelectionPosition.Translate(selection, player.Position - SelectionPosition.WorldPosition(selection[0]));
		EditorHistory.RecordEdit(player, "bring", "mpr.position", before);
		response = SelectionPosition.Describe(selection);
		return true;
	}
}
