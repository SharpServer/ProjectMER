using HarmonyLib;
using MapGeneration.RoomConnectors;
using UnityEngine;

namespace ProjectMER.Patches;

// The native connector detects walls in Start. Keep the requested variant on MER's doors.
[HarmonyPatch(typeof(WallableSmallNodeRoomConnector), nameof(WallableSmallNodeRoomConnector.ServerDetectWalls))]
internal sealed class SpawnedDoorWallConfiguration : MonoBehaviour
{
    private byte _mask;

    internal static void Apply(WallableSmallNodeRoomConnector connector, byte mask)
    {
        SpawnedDoorWallConfiguration configuration = connector.GetComponent<SpawnedDoorWallConfiguration>() ??
            connector.gameObject.AddComponent<SpawnedDoorWallConfiguration>();
        configuration._mask = mask;
        SetWalls(connector, mask);
    }

    private static bool Prefix(WallableSmallNodeRoomConnector __instance)
    {
        if (!__instance.TryGetComponent(out SpawnedDoorWallConfiguration configuration))
            return true;

        SetWalls(__instance, configuration._mask);
        return false;
    }

    private static void SetWalls(WallableSmallNodeRoomConnector connector, byte mask)
    {
        connector.Network_syncBitmask = mask;
        connector.UpdateMask(0, mask);
    }
}
