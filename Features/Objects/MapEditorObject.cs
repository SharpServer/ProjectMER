using LabApi.Features.Wrappers;
using MEC;
using Mirror;
using ProjectMER.Features.Serializable;
using ProjectMER.Features.ToolGun;
using UnityEngine;

namespace ProjectMER.Features.Objects;

public class MapEditorObject : MonoBehaviour
{
	public SerializableObject Base;

	public string MapName { get; protected set; }

	public string Id { get; protected set; }

	public Room Room { get; protected set; }

	public MapSchematic Map => MapUtils.LoadedMaps[MapName];

	public MapEditorObject Init(SerializableObject serializableObject, string mapName, string id, Room room)
	{
		Base = serializableObject;
		MapName = mapName;
		Id = id;
		Room = room;

		return this;
	}

	internal void MoveToMap(string mapName) => MapName = mapName;

	public virtual void UpdateObjectAndCopies()
	{
		Map.IsDirty = true;

		if (Base.RequiresReloading)
		{
			Base._prevIndex = Base.Index;
			MapSchematic map = Map;
			string id = Id;
			SerializableObject data = Base;
			map.DestroyObject(id);

			Timing.CallDelayed(0.1f, () =>
			{
				if (!MapUtils.LoadedMaps.TryGetValue(map.Name, out MapSchematic? current) || !ReferenceEquals(current, map) ||
					!ReferenceEquals(map.FindElement(id), data) || map.SpawnedObjects.Any(obj => obj != null && obj.Id == id))
					return;
				map.SpawnObject(id, data);
				ToolGunHandler.RefreshSelections();
			});

			return;
		}

		foreach (MapEditorObject copy in Map.SpawnedObjects.ToList())
		{
			if (copy == null || copy.Id != Id)
				continue;

			copy.UpdateCopy();
		}
	}

	private void UpdateCopy()
	{
		IndicatorObject.TrySpawnOrUpdateIndicator(this);
		Base.SpawnOrUpdateObject(Room, gameObject);
	}

	/// <summary>
	/// Destroys the object.
	/// </summary>
	public void Destroy()
	{
		// Unity objects can remain in SpawnedObjects after their native component has
		// already been destroyed by another owner. Accessing Component.gameObject in
		// that state throws before UnityEngine.Object.Destroy can be reached.
		if (this == null)
			return;

		if (TryGetComponent(out NetworkIdentity identity))
			CullingManager.UnregisterCullable(identity);

		IndicatorObject.TryDestroyIndicator(this);
		Destroy(gameObject);
	}
}
