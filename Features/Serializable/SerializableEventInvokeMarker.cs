using AdminToys;
using LabApi.Features.Wrappers;
using ProjectMER.Features.Extensions;
using ProjectMER.Features.Interfaces;
using ProjectMER.Features.Objects;
using UnityEngine;
using PrimitiveObjectToy = AdminToys.PrimitiveObjectToy;

namespace ProjectMER.Features.Serializable;

public class SerializableEventInvokeMarker : SerializableObject, IIndicatorDefinition
{
    public string Tag { get; set; } = string.Empty;
    public float Distance { get; set; } = 1f;

    public override GameObject? SpawnOrUpdateObject(Room? room = null, GameObject? instance = null)
    {
        GameObject marker = instance ?? new GameObject("EventInvokeMarker");
        Vector3 position = room.GetAbsolutePosition(Position);
        Quaternion rotation = room.GetAbsoluteRotation(Rotation);
        _prevIndex = Index;

        marker.transform.SetPositionAndRotation(position, rotation);
        marker.transform.localScale = Scale;

        EventInvokeMarkerObject markerObject = marker.GetComponent<EventInvokeMarkerObject>() ?? marker.AddComponent<EventInvokeMarkerObject>();
        markerObject.Tag = Tag;
        markerObject.Distance = Mathf.Max(0.01f, Distance);
        return marker;
    }

    public GameObject SpawnOrUpdateIndicator(Room room, GameObject? instance = null)
    {
        PrimitiveObjectToy primitive = instance == null ? UnityEngine.Object.Instantiate(PrefabManager.PrimitiveObject) : instance.GetComponent<PrimitiveObjectToy>();
        primitive.NetworkPrimitiveType = UnityEngine.PrimitiveType.Sphere;
        primitive.NetworkPrimitiveFlags = PrimitiveFlags.Visible;
        primitive.NetworkMaterialColor = new Color(1f, 0.55f, 0f, 0.75f);
        primitive.transform.localScale = Vector3.one * Mathf.Max(0.1f, Distance * 2f);
        primitive.transform.SetPositionAndRotation(room.GetAbsolutePosition(Position), room.GetAbsoluteRotation(Rotation));
        return primitive.gameObject;
    }
}
