using UnityEngine;

namespace ProjectMER.Features.Serializable.Schematics;

public class SchematicEventInvokeMarkerData
{
    public string Name { get; set; } = string.Empty;
    public int ObjectId { get; set; }
    public int ParentId { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Rotation { get; set; }
    public Vector3 Scale { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public float Distance { get; set; } = 1f;
}
