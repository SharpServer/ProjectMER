using CommandSystem;
using LabApi.Features.Permissions;
using LabApi.Features.Wrappers;
using ProjectMER.Features.ToolGun;

namespace ProjectMER.Commands.Utility;

internal sealed class ReplayEdit(bool redo) : ICommand
{
    public string Command => redo ? "redo" : "undo";

    public string[] Aliases => [];

    public string Description => redo ? "Reapplies your last undone map edit." : "Reverts your last map edit.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        Player? player = Player.Get(sender);
        if (player is null)
        {
            response = "This command can't be run from the server console.";
            return false;
        }
        if (arguments.Count != 0)
        {
            response = $"Usage: mp {Command}";
            return false;
        }

        return EditorHistory.Replay(player, redo, permission => sender.HasAnyPermission(permission), out response);
    }
}
