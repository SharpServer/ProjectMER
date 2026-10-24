using HarmonyLib;
using InventorySystem.Items.Autosync;
using UnityEngine;

namespace ProjectMER.Patches;

/// <summary>
/// Supplements the stock melee scan for non-player IDestructible implementations whose own
/// collider would otherwise be treated as line-of-sight obstruction.
/// </summary>
[HarmonyPatch(typeof(MeleeAutoSync), nameof(MeleeAutoSync.DetectDestructibles))]
internal static class MeleeAutoSyncCustomHitboxPatch
{
    [HarmonyPostfix]
    private static void Postfix(MeleeAutoSync __instance)
    {
        if (__instance == null || __instance.Owner == null || __instance.Owner.PlayerCameraReference == null)
            return;

        ReferenceHub owner = __instance.Owner;
        Transform camera = owner.PlayerCameraReference;

        Vector3 origin = camera.position;
        int count = Physics.OverlapSphereNonAlloc(
            origin + camera.forward * __instance.HitregOffset,
            __instance.HitregRadius,
            MeleeAutoSync.DetectedColliders,
            __instance.DetectionMask,
            QueryTriggerInteraction.UseGlobal);

        for (int i = 0; i < count; i++)
        {
            Collider collider = MeleeAutoSync.DetectedColliders[i];
            if (collider == null ||
                !collider.TryGetComponent<IDestructible>(out var target) ||
                target is HitboxIdentity ||
                target.NetworkId == owner.netId ||
                MeleeAutoSync.DetectedNetIds.Contains(target.NetworkId))
                continue;

            Vector3 toTarget = target.CenterOfMass - origin;
            float sqrDistance = toTarget.sqrMagnitude;
            if (sqrDistance <= 0.0001f ||
                Vector3.Dot(toTarget / Mathf.Sqrt(sqrDistance), camera.forward) <= __instance.MaxDotProduct ||
                !HasClearLineOfSight(__instance, origin, target))
                continue;

            if (MeleeAutoSync._detectionsLen >= MeleeAutoSync.DetectedDestructibles.Length)
                break;

            MeleeAutoSync.DetectedNetIds.Add(target.NetworkId);
            MeleeAutoSync.DetectedDestructibles[MeleeAutoSync._detectionsLen++] = target;
        }
    }

    private static bool HasClearLineOfSight(MeleeAutoSync melee, Vector3 origin, IDestructible target)
    {
        if (!Physics.Linecast(origin, target.CenterOfMass, out RaycastHit hit, melee.LinecastMask, QueryTriggerInteraction.Ignore))
            return true;

        return hit.collider != null &&
               hit.collider.TryGetComponent<IDestructible>(out var blocker) &&
               blocker.NetworkId == target.NetworkId;
    }
}
