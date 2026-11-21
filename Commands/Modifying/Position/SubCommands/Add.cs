using System;
using CommandSystem;
using LabApi.Features.Permissions;
using LabApi.Features.Wrappers;
using ProjectMER.Features.Objects;
using ProjectMER.Features.ToolGun;
using UnityEngine;
using static ProjectMER.Features.Extensions.StructExtensions;

namespace ProjectMER.Commands.Modifying.Position.SubCommands;

/// <summary>
/// Modifies object's position by adding a certain value to the current one.
/// </summary>
public class Add : ICommand
{
	/// <inheritdoc/>
	public string Command => "add";

	/// <inheritdoc/>
	public string[] Aliases { get; } = [];

	/// <inheritdoc/>
	public string Description => string.Empty;

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

		if (arguments.Count >= 3 && TryGetVector(arguments.At(0), arguments.At(1), arguments.At(2), out Vector3 newPosition))
		{
			EditorHistory.State[] before = selection.Select(EditorHistory.Capture).ToArray();
			if (selection.Count == 1)
			{
				selection[0].Base.Position += newPosition;
				selection[0].UpdateObjectAndCopies();
			}
			else SelectionPosition.Translate(selection, newPosition);

			EditorHistory.RecordEdit(player, "move", "mpr.position", before);
			response = SelectionPosition.Describe(selection);
			return true;
		}

		response = "Invalid values.";
		return false;
	}
}
