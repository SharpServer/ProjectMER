using AdminToys;
using LabApi.Features.Wrappers;
using ProjectMER.Features.Extensions;
using ProjectMER.Features.Interfaces;
using UnityEngine;
using YamlDotNet.Serialization;
using ExiledPrefabType = Exiled.API.Enums.PrefabType;
using PrefabHelper = Exiled.API.Features.PrefabHelper;
using PrimitiveObjectToy = AdminToys.PrimitiveObjectToy;

namespace ProjectMER.Features.Serializable;

/// <summary>
/// A marker that spawns a single <see cref="ExiledPrefabType"/> at its position using EXILED's <see cref="PrefabHelper"/>.
/// </summary>
public class SerializablePrefabMarker : SerializableObject, IIndicatorDefinition
{
    /// <summary>
    /// Gets or sets the name of the <see cref="ExiledPrefabType"/> to spawn (parsed at runtime).
    /// </summary>
    public string PrefabType { get; set; } = string.Empty;

    public override GameObject? SpawnOrUpdateObject(Room? room = null, GameObject? instance = null)
    {
        Vector3 position = room.GetAbsolutePosition(Position);
        Quaternion rotation = room.GetAbsoluteRotation(Rotation);
        _prevIndex = Index;
        _prevPrefabType = PrefabType;

        // Existing instance: just move it. A PrefabType change triggers a full reload (see RequiresReloading).
        if (instance != null)
        {
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.transform.localScale = Scale;
            return instance;
        }

        if (!Enum.TryParse(PrefabType, true, out ExiledPrefabType prefabType))
            return null;

        // PrefabHelper.Spawn instantiates the prefab and calls NetworkServer.Spawn itself,
        // mirroring how the other SerializableObjects own their networking.
        GameObject? spawned = PrefabHelper.Spawn(prefabType, position, rotation);
        if (spawned == null)
            return null;

        spawned.transform.localScale = Scale;
        return spawned;
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
        primitive.NetworkMaterialColor = new Color(1f, 0.5f, 0f, 0.65f);
        primitive.transform.localScale = Vector3.one * 0.4f;
        primitive.transform.SetPositionAndRotation(position, rotation);

        return primitive.gameObject;
    }

    [YamlIgnore]
    public override bool RequiresReloading => Index != _prevIndex || PrefabType != _prevPrefabType;

    private string _prevPrefabType = string.Empty;
}
