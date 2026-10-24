using MapGeneration.Distributors;
using Mirror;
using UnityEngine;

namespace ProjectMER.Features;

/// <summary>Keep MER structures at the exact pose the vanilla client can represent.</summary>
public sealed class StructureTransformSync : MonoBehaviour
{
    private StructurePositionSync sync = null!;
    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private Vector3 lastScale;

    public static void Attach(StructurePositionSync target)
    {
        StructureTransformSync component = target.GetComponent<StructureTransformSync>() ??
            target.gameObject.AddComponent<StructureTransformSync>();
        component.sync = target;
        component.ApplyPose();
        component.RememberPose();
    }

    public static Quaternion ClientRotation(Quaternion rotation)
    {
        float yaw = Mathf.Round(rotation.eulerAngles.y / StructurePositionSync.ConversionRate) * StructurePositionSync.ConversionRate;
        return Quaternion.Euler(0f, yaw, 0f);
    }

    private void ApplyPose()
    {
        // StructurePositionSync.Start discards pitch/roll and quantizes yaw on clients.
        // Apply that same pose to server colliders, rather than letting them diverge.
        transform.rotation = ClientRotation(transform.rotation);
        sync.Network_position = transform.position;
        sync.Network_rotationY = (sbyte)Mathf.RoundToInt(transform.rotation.eulerAngles.y / StructurePositionSync.ConversionRate);
    }

    private void RememberPose()
    {
        lastPosition = transform.position;
        lastRotation = transform.rotation;
        lastScale = transform.lossyScale;
    }

    private void LateUpdate()
    {
        if (!NetworkServer.active || sync == null) return;
        if (transform.position == lastPosition && transform.rotation == lastRotation && transform.lossyScale == lastScale) return;
        ApplyPose();
        RememberPose();
        // Vanilla structure SyncVars have no transform hooks: send a fresh spawn
        // when an already spawned schematic is moved/scaled so clients apply the pose.
        if (sync.netId != 0)
        {
            NetworkServer.UnSpawn(gameObject);
            NetworkServer.Spawn(gameObject);
        }
    }
}
