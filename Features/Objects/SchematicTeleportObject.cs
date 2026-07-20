using System.Collections.Generic;
using UnityEngine;

namespace ProjectMER.Features.Objects;

/// <summary>
/// Teleport object used within schematics.
/// Unlike <see cref="TeleportObject"/> which depends on <see cref="MapEditorObject"/>,
/// this stores teleport data directly for use in compiled schematics.
/// </summary>
public class SchematicTeleportObject : MonoBehaviour, ITeleporter
{
    private static readonly HashSet<SchematicTeleportObject> Instances = [];

    public string Id;

    public List<string> Targets = [];

    public float Cooldown = 5f;

    private BoxCollider? _trigger;
    private readonly HashSet<ReferenceHub> _ignoredPlayers = [];

    Transform ITeleporter.Transform => transform;
    BoxCollider? ITeleporter.Trigger => _trigger;
    float ITeleporter.TeleportCooldown => Cooldown;
    HashSet<ReferenceHub> ITeleporter.IgnoredPlayers => _ignoredPlayers;
    ITeleporter? ITeleporter.GetTarget() => GetRandomTarget();

    private void Awake()
    {
        TryGetComponent(out _trigger);
    }

    private void OnEnable()
    {
        Instances.Add(this);
    }

    private void OnDisable()
    {
        Instances.Remove(this);
    }

    private void FixedUpdate()
    {
        TeleportHelper.Tick(this);
    }

    public SchematicTeleportObject? GetRandomTarget()
    {
        if (Targets is not { Count: > 0 } targets)
            return null;

        int startIndex = UnityEngine.Random.Range(0, targets.Count);

        for (int i = 0; i < targets.Count; i++)
        {
            string targetId = targets[(startIndex + i) % targets.Count];

            foreach (SchematicTeleportObject teleportObject in Instances)
            {
                if (teleportObject == this || teleportObject.Id != targetId)
                    continue;

                return teleportObject;
            }
        }

        return null;
    }

    private void OnDestroy()
    {
        Instances.Remove(this);
        TeleportHelper.ClearArrivalReservation(transform);
    }
}
