using UnityEngine;

namespace ProjectMER.Features.Serializable.Schematics;

/// <summary>
/// Teleport data stored in the separate {SchematicName}-Teleports.json file.
/// Compiled by the Unity editor's TeleportComponent.
/// </summary>
public class SchematicTeleportData
{
    public string Name { get; set; }

    public int ObjectId { get; set; }

    public int ParentId { get; set; }

    public Vector3 Position { get; set; }

    public Vector3 Rotation { get; set; }

    public Vector3 Scale { get; set; }

    public string Id { get; set; }

    public List<string> Targets { get; set; } = [];

    public float Cooldown { get; set; } = 5f;

    public Vector3 TriggerScale { get; set; } = Vector3.one;
}
