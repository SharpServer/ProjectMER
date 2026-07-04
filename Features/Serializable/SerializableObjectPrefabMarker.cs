using AdminToys;
using LabApi.Features.Wrappers;
using ProjectMER.Features.Extensions;
using ProjectMER.Features.Interfaces;
using ProjectMER.Features.Objects;
using UnityEngine;
using PrimitiveObjectToy = AdminToys.PrimitiveObjectToy;

namespace ProjectMER.Features.Serializable;

public class SerializableObjectPrefabMarker : SerializableObject, IIndicatorDefinition
{
    public string PrefabType { get; set; } = string.Empty;

    public string Tag { get; set; } = string.Empty;

    public int MaxRooms { get; set; } = 1;

    public bool AutoDestroyEnabled { get; set; }

    public float AutoDestroyTime { get; set; } = -1f;

    public Dictionary<string, string> Options { get; set; } = [];

    public override GameObject? SpawnOrUpdateObject(Room? room = null, GameObject? instance = null)
    {
        GameObject marker = instance ?? new GameObject("ObjectPrefabMarker");
        Vector3 position = room.GetAbsolutePosition(Position);
        Quaternion rotation = room.GetAbsoluteRotation(Rotation);
        _prevIndex = Index;

        marker.transform.SetPositionAndRotation(position, rotation);
        marker.transform.localScale = Scale;

        SchematicObjectPrefabObject component = marker.GetComponent<SchematicObjectPrefabObject>() ??
                                                marker.AddComponent<SchematicObjectPrefabObject>();
        component.PrefabType = PrefabType;
        component.Tag = Tag;
        component.MaxRooms = MaxRooms <= 0 ? 1 : MaxRooms;
        component.AutoDestroyEnabled = AutoDestroyEnabled;
        component.AutoDestroyTime = AutoDestroyTime;
        component.Options = Options ?? [];
        component.Schematic = null;
        component.NotifySpawned();

        return marker;
    }

    public GameObject SpawnOrUpdateIndicator(Room room, GameObject? instance = null)
    {
        PrimitiveObjectToy primitive = instance == null
            ? UnityEngine.Object.Instantiate(PrefabManager.PrimitiveObject)
            : instance.GetComponent<PrimitiveObjectToy>();
        Vector3 position = room.GetAbsolutePosition(Position);
        Quaternion rotation = room.GetAbsoluteRotation(Rotation);

        primitive.NetworkPrimitiveType = PrimitiveType.Cube;
        primitive.NetworkPrimitiveFlags = PrimitiveFlags.Visible;
        primitive.NetworkMaterialColor = new Color(0f, 0.75f, 1f, 0.65f);
        primitive.transform.localScale = Vector3.one * 0.4f;
        primitive.transform.SetPositionAndRotation(position, rotation);

        return primitive.gameObject;
    }
}
