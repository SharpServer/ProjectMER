using ProjectMER.Features.Objects;
using ProjectMER.Features.Serializable;
using UnityEngine;

namespace ProjectMER.Features;

/// <summary>
/// Provides a public API for external plugins to access CustomTriggerPoints.
/// </summary>
public static class TriggerPointManager
{
    /// <summary>
    /// Gets all spawned CustomTriggerPoint objects across all loaded maps.
    /// </summary>
    public static List<MapEditorObject> GetAll()
    {
        List<MapEditorObject> results = [];
        foreach (MapSchematic map in MapUtils.LoadedMaps.Values)
        {
            foreach (MapEditorObject meo in map.SpawnedObjects)
            {
                if (meo.Base is SerializableCustomTriggerPoint)
                    results.Add(meo);
            }
        }

        return results;
    }

    /// <summary>
    /// Gets all schematic-local CustomTriggerPoint objects.
    /// </summary>
    public static List<SchematicTriggerPointObject> GetAllSchematic()
    {
        return [.. UnityEngine.Object.FindObjectsByType<SchematicTriggerPointObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)];
    }

    /// <summary>
    /// Tries to get all CustomTriggerPoints matching a specific tag.
    /// </summary>
    public static bool TryGetByTag(string tag, out List<MapEditorObject> results)
    {
        results = [];
        foreach (MapSchematic map in MapUtils.LoadedMaps.Values)
        {
            foreach (MapEditorObject meo in map.SpawnedObjects)
            {
                if (meo.Base is SerializableCustomTriggerPoint triggerPoint && triggerPoint.Tag == tag)
                    results.Add(meo);
            }
        }

        return results.Count > 0;
    }

    /// <summary>
    /// Tries to get all schematic-local CustomTriggerPoints matching a specific tag.
    /// </summary>
    public static bool TryGetSchematicByTag(string tag, out List<SchematicTriggerPointObject> results)
    {
        results = [];
        foreach (SchematicTriggerPointObject triggerPoint in UnityEngine.Object.FindObjectsByType<SchematicTriggerPointObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (triggerPoint.Tag == tag)
                results.Add(triggerPoint);
        }

        return results.Count > 0;
    }

    /// <summary>
    /// Tries to get a CustomTriggerPoint by its unique map editor ID.
    /// </summary>
    public static bool TryGetById(string id, out MapEditorObject result)
    {
        foreach (MapSchematic map in MapUtils.LoadedMaps.Values)
        {
            foreach (MapEditorObject meo in map.SpawnedObjects)
            {
                if (meo.Id == id && meo.Base is SerializableCustomTriggerPoint)
                {
                    result = meo;
                    return true;
                }
            }
        }

        result = null!;
        return false;
    }

    /// <summary>
    /// Tries to get a schematic-local CustomTriggerPoint by its unique ID.
    /// </summary>
    public static bool TryGetSchematicById(string id, out SchematicTriggerPointObject result)
    {
        foreach (SchematicTriggerPointObject triggerPoint in UnityEngine.Object.FindObjectsByType<SchematicTriggerPointObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (triggerPoint.Id == id)
            {
                result = triggerPoint;
                return true;
            }
        }

        result = null!;
        return false;
    }

    /// <summary>
    /// Gets the world position of a MapEditorObject.
    /// </summary>
    public static Vector3 GetWorldPosition(MapEditorObject mapEditorObject)
    {
        return mapEditorObject.transform.position;
    }

    /// <summary>
    /// Gets the world position of a schematic-local CustomTriggerPoint.
    /// </summary>
    public static Vector3 GetWorldPosition(SchematicTriggerPointObject triggerPoint)
    {
        return triggerPoint.transform.position;
    }
}

