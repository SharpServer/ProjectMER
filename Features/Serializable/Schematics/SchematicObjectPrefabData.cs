using UnityEngine;

namespace ProjectMER.Features.Serializable.Schematics;

public class SchematicObjectPrefabData
{
    public string Name { get; set; } = string.Empty;

    public int ObjectId { get; set; }

    public int ParentId { get; set; }

    public Vector3 Position { get; set; }

    public Vector3 Rotation { get; set; }

    public Vector3 Scale { get; set; } = Vector3.one;

    public string PrefabType { get; set; } = string.Empty;

    public string Tag { get; set; } = string.Empty;

    public int MaxRooms { get; set; } = 1;

    public bool AutoDestroyEnabled { get; set; }

    public float AutoDestroyTime { get; set; } = -1f;

    public Dictionary<string, string> Options { get; set; } = [];
}
