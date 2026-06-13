using ProjectMER.Features.Serializable.Schematics;
using UnityEngine;

namespace ProjectMER.Features.Objects;

public class SchematicObjectPrefabObject : MonoBehaviour
{
    public string PrefabType = string.Empty;

    public int MaxRooms = 1;

    public bool AutoDestroyEnabled;

    public float AutoDestroyTime = -1f;

    public Dictionary<string, string> Options = [];

    public SchematicObject? Schematic { get; set; }

    internal void Init(SchematicObjectPrefabData data, SchematicObject schematic)
    {
        PrefabType = data.PrefabType;
        MaxRooms = data.MaxRooms <= 0 ? 1 : data.MaxRooms;
        AutoDestroyEnabled = data.AutoDestroyEnabled;
        AutoDestroyTime = data.AutoDestroyTime;
        Options = data.Options ?? [];
        Schematic = schematic;
    }
}
