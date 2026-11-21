using MEC;
using MapGeneration.Distributors;
using Mirror;
using ProjectMER.Features.Serializable;
using ProjectMER.Features.Serializable.Schematics;
using UnityEngine;

namespace ProjectMER.Features.Objects;

public class SchematicDoorObject : MonoBehaviour
{
    public SchematicObject? Schematic { get; internal set; }

    public GameObject? DoorGameObject { get; private set; }

    private CoroutineHandle _syncCoroutine;
    private Vector3 _lastPosition;
    private Quaternion _lastRotation;

    internal void Init(SchematicDoorData data, SchematicObject schematic)
    {
        Schematic = schematic;
        SerializableDoor door = new()
        {
            Position = Vector3.zero,
            Rotation = Vector3.zero,
            Scale = Vector3.one,
            DoorType = data.DoorType,
            IsOpen = data.IsOpen,
            IsLocked = data.IsLocked,
            RequiredPermissions = data.RequiredPermissions,
            RequireAll = data.RequireAll,
            AccessDeniedMessage = data.AccessDeniedMessage,
            SecretCode = data.SecretCode,
        };

        DoorGameObject = door.SpawnAt(transform.position, transform.rotation, data.Name);
        _lastPosition = transform.position;
        _lastRotation = transform.rotation;
        _syncCoroutine = Timing.RunCoroutine(SyncCoroutine());
    }

    private IEnumerator<float> SyncCoroutine()
    {
        while (this != null && DoorGameObject != null)
        {
            SyncDoorTransform();
            yield return Timing.WaitForSeconds(0.05f);
        }
    }

    private void SyncDoorTransform()
    {
        if (DoorGameObject == null)
            return;

        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;

        if ((position - _lastPosition).sqrMagnitude < 0.0001f &&
            Quaternion.Angle(rotation, _lastRotation) < 0.1f)
        {
            return;
        }

        _lastPosition = position;
        _lastRotation = rotation;

        RespawnDoorAtMarker();
    }

    private void RespawnDoorAtMarker()
    {
        if (DoorGameObject == null)
            return;

        NetworkServer.UnSpawn(DoorGameObject);
        DoorGameObject.transform.SetPositionAndRotation(transform.position, transform.rotation);
        if (DoorGameObject.TryGetComponent(out StructurePositionSync structure))
            StructureTransformSync.Attach(structure);
        NetworkServer.Spawn(DoorGameObject);
    }

    private void OnDestroy()
    {
        if (_syncCoroutine.IsRunning)
            Timing.KillCoroutines(_syncCoroutine);

        if (DoorGameObject != null)
            NetworkServer.Destroy(DoorGameObject);
    }
}
