using System;
using System.Collections.Generic;
using LabApi.Features.Wrappers;
using UnityEngine;

namespace ProjectMER.Features.Objects;

/// <summary>
/// Teleport object used within schematics.
/// Unlike <see cref="TeleportObject"/> which depends on <see cref="MapEditorObject"/>,
/// this stores teleport data directly for use in compiled schematics.
/// </summary>
public class SchematicTeleportObject : MonoBehaviour
{
    public string Id;

    public List<string> Targets = [];

    public float Cooldown = 5f;

    public DateTime NextTimeUse;

    public SchematicTeleportObject? GetRandomTarget()
    {
        if (Targets.Count == 0)
            return null;

        string targetId = Targets[UnityEngine.Random.Range(0, Targets.Count)];

        foreach (SchematicTeleportObject teleportObject in FindObjectsByType<SchematicTeleportObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (teleportObject.Id == targetId)
                return teleportObject;
        }

        return null;
    }

    public void OnTriggerEnter(Collider other)
    {
        Player? player = Player.Get(other.gameObject);
        if (player is null)
            return;

        if (NextTimeUse > DateTime.Now)
            return;

        SchematicTeleportObject? target = GetRandomTarget();
        if (target == null)
            return;

        DateTime dateTime = DateTime.Now.AddSeconds(Cooldown);
        NextTimeUse = dateTime;
        target.NextTimeUse = dateTime;

        player.Position = target.gameObject.transform.position;
        player.LookRotation = target.gameObject.transform.eulerAngles;
    }
}
