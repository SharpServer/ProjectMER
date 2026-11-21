using ProjectMER.Features.Extensions;
using ProjectMER.Features.Objects;
using UnityEngine;

namespace ProjectMER.Features.ToolGun;

internal static class SelectionPosition
{
    internal static Vector3 WorldPosition(MapEditorObject obj) => obj.Room.GetAbsolutePosition(obj.Base.Position);

    internal static bool IsLive(MapEditorObject obj) =>
        obj != null && MapUtils.LoadedMaps.TryGetValue(obj.MapName, out var map) && map.SpawnedObjects.Contains(obj);

    internal static void Translate(IReadOnlyList<MapEditorObject> selection, Vector3 worldDelta)
    {
        foreach (MapEditorObject obj in selection)
            obj.Base.Position = obj.Room.GetRelativePosition(WorldPosition(obj) + worldDelta);
        foreach (MapEditorObject obj in selection)
        {
            if (IsLive(obj))
                obj.UpdateObjectAndCopies();
        }
    }

    internal static string Describe(IReadOnlyList<MapEditorObject> selection) =>
        selection.Count == 1 ? selection[0].Base.Position.ToString("F3") :
            $"Moved {selection.Count} objects. Primary position: {selection[0].Base.Position:F3}.";
}
