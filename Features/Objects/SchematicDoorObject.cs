using MEC;
using Mirror;
using ProjectMER.Features.Serializable;
using ProjectMER.Features.Serializable.Schematics;
using UnityEngine;

namespace ProjectMER.Features.Objects;

public class SchematicDoorObject : MonoBehaviour
{
    public SchematicObject? Schematic { get; internal set; }

    public GameObject? DoorGameObject { get; private set; }

    private SerializableDoor? _door;
    private CoroutineHandle _syncCoroutine;
    private Vector3 _lastPosition;
    private Quaternion _lastRotation;
    private Vector3 _lastScale;

    internal void Init(SchematicDoorData data, SchematicObject schematic)
    {
        Schematic = schematic;
        _door = new SerializableDoor
        {
            Position = transform.position,
            Rotation = transform.rotation.eulerAngles,
            Scale = transform.lossyScale,
            DoorType = data.DoorType,
            IsOpen = data.IsOpen,
            IsLocked = data.IsLocked,
            RequiredPermissions = data.RequiredPermissions,
            RequireAll = data.RequireAll,
        };

        DoorGameObject = _door.SpawnOrUpdateObject();
        DoorGameObject.name = data.Name;
        _lastPosition = transform.position;
        _lastRotation = transform.rotation;
        _lastScale = transform.lossyScale;
        _syncCoroutine = Timing.RunCoroutine(SyncCoroutine());
    }

    private IEnumerator<float> SyncCoroutine()
    {
        while (this != null && DoorGameObject != null)
        {
            SyncDoorTransform(force: false);
            yield return Timing.WaitForSeconds(0.05f);
        }
    }

    private void SyncDoorTransform(bool force)
    {
        if (_door == null || DoorGameObject == null)
            return;

        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;
        Vector3 scale = transform.lossyScale;

        if (!force &&
            (position - _lastPosition).sqrMagnitude < 0.0001f &&
            Quaternion.Angle(rotation, _lastRotation) < 0.1f &&
            (scale - _lastScale).sqrMagnitude < 0.0001f)
        {
            return;
        }

        _lastPosition = position;
        _lastRotation = rotation;
        _lastScale = scale;

        _door.Position = position;
        _door.Rotation = rotation.eulerAngles;
        _door.Scale = scale;
        _door.SpawnOrUpdateObject(null, DoorGameObject);
    }

    private void OnDestroy()
    {
        if (_syncCoroutine.IsRunning)
            Timing.KillCoroutines(_syncCoroutine);

        if (DoorGameObject != null)
            NetworkServer.Destroy(DoorGameObject);
    }
}
