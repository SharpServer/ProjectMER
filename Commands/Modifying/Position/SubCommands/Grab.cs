using CommandSystem;
using LabApi.Features.Permissions;
using LabApi.Features.Wrappers;
using MEC;
using ProjectMER.Features.Extensions;
using ProjectMER.Features.Objects;
using ProjectMER.Features.Serializable;
using ProjectMER.Features.ToolGun;
using UnityEngine;

namespace ProjectMER.Commands.Modifying.Position.SubCommands;

public class Grab : ICommand
{
    public string Command => "grab";
    public string[] Aliases => [];
    public string Description => "Grabs or releases the selected objects as a group.";

    private static readonly Dictionary<Player, GrabSession> Sessions = [];

    internal static IReadOnlyList<MapEditorObject> GetGrabbedObjects(Player player) =>
        Sessions.TryGetValue(player, out GrabSession? session) ? session.Objects : [];

    internal static bool IsGrabbing(SerializableObject data) =>
        Sessions.Values.Any(session => session.Before.Any(state => ReferenceEquals(state.Data, data)));

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.HasAnyPermission("mpr.position"))
        {
            response = "You don't have permission to execute this command. Required permission: mpr.position";
            return false;
        }

        Player? player = Player.Get(sender);
        if (player is null)
        {
            response = "This command can't be run from the server console.";
            return false;
        }

        if (Stop(player))
        {
            response = "Ungrabbed.";
            return true;
        }

        IReadOnlyList<MapEditorObject> selection = ToolGunHandler.GetSelection(player);
        if (selection.Count == 0)
        {
            response = "You need to select an object first!";
            return false;
        }

        if (Sessions.Values.Any(session => session.Objects.Any(existing =>
            selection.Any(obj => ReferenceEquals(obj.Base, existing.Base)))))
        {
            response = "Another player is already grabbing one of the selected objects.";
            return false;
        }

        GrabSession grab = new(selection);
        Sessions.Add(player, grab);
        grab.Handle = Timing.RunCoroutine(Move(player, grab));
        response = $"Grabbed {selection.Count} object(s). Run mer pos grab again to release.";
        return true;
    }

    internal static bool Stop(Player player, bool commit = true)
    {
        if (!Sessions.TryGetValue(player, out GrabSession? session))
            return false;
        Sessions.Remove(player);
        Timing.KillCoroutines(session.Handle);
        session.Finish(player, commit);
        return true;
    }

    internal static void StopAll()
    {
        foreach (Player player in Sessions.Keys.ToArray())
            Stop(player, commit: false);
    }

    private static IEnumerator<float> Move(Player player, GrabSession grab)
    {
        float distance = Vector3.Distance(player.Camera.position, grab.Origins[0]);
        Vector3 GetPointer() => player.Camera.position + player.Camera.forward * distance;
        Vector3 start = GetPointer();
        Vector3 previous = start;

        try
        {
            while (true)
            {
                yield return Timing.WaitForSeconds(0.02f);
                IReadOnlyList<MapEditorObject> selected = ToolGunHandler.GetSelection(player);
                if (!selected.SequenceEqual(grab.Objects) || grab.Objects.Any(obj => !SelectionPosition.IsLive(obj)))
                    break;

                Vector3 pointer = GetPointer();
                if ((previous - pointer).sqrMagnitude < 0.0001f)
                    continue;

                previous = pointer;
                Vector3 delta = pointer - start;
                for (int i = 0; i < grab.Objects.Length; i++)
                {
                    MapEditorObject obj = grab.Objects[i];
                    Vector3 position = grab.Origins[i] + delta;
                    obj.transform.position = position;
                    obj.Base.Position = obj.Room.GetRelativePosition(position);
                    obj.Map.IsDirty = true;
                    if (obj.Base is SerializableDoor)
                        obj.Base.SpawnOrUpdateObject(obj.Room, obj.gameObject);
                }
                Physics.SyncTransforms();
            }
        }
        finally
        {
            if (Sessions.TryGetValue(player, out GrabSession? current) && ReferenceEquals(current, grab))
            {
                Sessions.Remove(player);
                grab.Finish(player, commit: true);
            }
        }
    }

    private sealed class GrabSession
    {
        internal readonly MapEditorObject[] Objects;
        internal readonly Vector3[] Origins;
        internal readonly EditorHistory.State[] Before;
        internal CoroutineHandle Handle;
        private readonly (Collider Collider, bool Enabled)[] _colliders;
        private readonly (Rigidbody Body, bool IsKinematic, bool DetectCollisions)[] _bodies;

        internal GrabSession(IReadOnlyList<MapEditorObject> selection)
        {
            Objects = selection.ToArray();
            Origins = Objects.Select(obj => obj.transform.position).ToArray();
            Before = Objects.Select(EditorHistory.Capture).ToArray();
            _colliders = Objects.SelectMany(obj => obj.GetComponentsInChildren<Collider>(true))
                .Distinct().Select(collider => (collider, collider.enabled)).ToArray();
            _bodies = Objects.SelectMany(obj => obj.GetComponentsInChildren<Rigidbody>(true))
                .Distinct().Select(body => (body, body.isKinematic, body.detectCollisions)).ToArray();

            foreach (var (collider, _) in _colliders)
                collider.enabled = false;
            foreach (var (body, _, _) in _bodies)
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }
            Physics.SyncTransforms();
        }

        internal void Finish(Player player, bool commit)
        {
            foreach (var (collider, enabled) in _colliders)
                if (collider != null) collider.enabled = enabled;
            foreach (var (body, kinematic, collisions) in _bodies)
            {
                if (body == null) continue;
                body.isKinematic = kinematic;
                body.detectCollisions = collisions;
            }
            Physics.SyncTransforms();
            if (!commit) return;

            EditorHistory.RecordEdit(player, "grab", "mpr.position", Before.Where(state =>
                ReferenceEquals(state.Map.FindElement(state.Id), state.Data)));
            foreach (MapEditorObject obj in Objects)
            {
                if (SelectionPosition.IsLive(obj))
                    obj.UpdateObjectAndCopies();
            }
        }
    }
}
