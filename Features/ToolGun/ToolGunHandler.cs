using LabApi.Features.Wrappers;
using MapGeneration;
using ProjectMER.Commands.Modifying.Position.SubCommands;
using ProjectMER.Configs;
using ProjectMER.Features.Enums;
using ProjectMER.Features.Extensions;
using ProjectMER.Features.Objects;
using ProjectMER.Features.Serializable;
using ProjectMER.Features.Serializable.Schematics;
using UnityEngine;

namespace ProjectMER.Features.ToolGun;

public static class ToolGunHandler
{
	public static Dictionary<Player, MapEditorObject> PlayerSelectedObjectDict { get; private set; } = [];
	private static readonly Dictionary<Player, List<SelectionTarget>> Selections = [];

	public static void CreateObject(Player player, ToolGunObjectType objectType, string schematicName = "")
	{
		if (!Raycast(player, out RaycastHit hit))
			return;

		MapEditorObject? mapEditorObject = CreateObjectAndGet(hit.point, objectType, schematicName);
		if (mapEditorObject != null)
			RecordCreation(player, mapEditorObject);
		if (Config.AutoSelect && mapEditorObject is not null)
			SelectObject(player, mapEditorObject);
	}

	public static void CreateObject(Vector3 position, ToolGunObjectType objectType, string schematicName = "") =>
		CreateObjectAndGet(position, objectType, schematicName);

	public static MapEditorObject? CreateObjectAndGet(Vector3 position, ToolGunObjectType objectType, string schematicName = "")
	{
		Room room = RoomExtensions.GetRoomAtPosition(position);

		position = room.Name == RoomName.Outside ? position : room.Transform.InverseTransformPoint(position);
		string roomId = room.GetRoomStringId();

		MapSchematic map = MapUtils.UntitledMap;
		string id = Guid.NewGuid().ToString("N").Substring(0, 8);

		SerializableObject serializableObject = (SerializableObject)Activator.CreateInstance(ToolGunItem.TypesDictionary[objectType]);
		serializableObject.Room = roomId;
		serializableObject.Index = room.GetRoomIndex();

		switch (serializableObject)
		{
			case SerializablePlayerSpawnpoint _:
				{
					serializableObject.Position = position + Vector3.up * 0.01f;
					break;
				}

			case SerializableTeleport _:
				{
					serializableObject.Position = position + Vector3.up;
					break;
				}

			case SerializableSchematic serializableSchematic:
				{
					serializableObject.Position = position;
					serializableSchematic.SchematicName = schematicName!;
					break;
				}

			default:
				serializableObject.Position = position;
				break;
		}

		bool added = map.TryAddElement(id, serializableObject);
		if (added)
			map.SpawnObject(id, serializableObject);

		MapEditorObject? createdObject = null;
		foreach (MapEditorObject mapEditorObject in map.SpawnedObjects)
		{
			if (mapEditorObject.Id != id)
				continue;

			IndicatorObject.TrySpawnOrUpdateIndicator(mapEditorObject);
			createdObject ??= mapEditorObject;
		}

		if (added && createdObject is null)
			map.TryRemoveElement(id);

		return createdObject;
	}

	public static void DeleteObject(MapEditorObject mapEditorObject) => DeleteObjects([mapEditorObject], null);

	internal static void RecordCreation(Player? player, MapEditorObject obj)
	{
		if (player == null) return;
		EditorHistory.State state = EditorHistory.Capture(obj);
		EditorHistory.Record(player, "create", "mpr.create", [new(state.Absent(), state)]);
	}

	internal static bool DeleteObjects(IReadOnlyList<MapEditorObject> objects, Player? player)
	{
		if (objects.Count == 0 || objects.Any(obj => !SelectionPosition.IsLive(obj) ||
			!ReferenceEquals(obj.Map.FindElement(obj.Id), obj.Base) ||
			Grab.IsGrabbing(obj.Base)))
			return false;

		EditorHistory.Change[] changes = player == null ? [] : objects.Select(obj =>
		{
			EditorHistory.State state = EditorHistory.Capture(obj);
			return new EditorHistory.Change(state, state.Absent());
		}).ToArray();

		foreach (MapEditorObject obj in objects)
		{
			MapSchematic map = obj.Map;
			foreach (Player selectedBy in Selections.Keys.ToArray())
				RemoveSelection(selectedBy, [obj]);
			map.TryRemoveElement(obj.Id);
			map.DestroyObject(obj.Id);
		}
		EditorHistory.Record(player, "remove", "mpr.delete", changes);
		return true;
	}

	public static bool TryGetMapObject(Player player, out MapEditorObject mapEditorObject)
	{
		mapEditorObject = null!;
		if (!Raycast(player, out RaycastHit hit))
			return false;

		if (!hit.transform.TryGetComponentInParent(out mapEditorObject))
			return false;

		if (mapEditorObject is IndicatorObject indicatorObject)
			mapEditorObject = IndicatorObject.Dictionary[indicatorObject];

		return true;
	}

	public static bool TryGetSelectedMapObject(Player player, out MapEditorObject mapEditorObject)
	{
		mapEditorObject = GetSelection(player).FirstOrDefault()!;
		return mapEditorObject != null;
	}

	public static void SelectObject(Player player, MapEditorObject? mapEditorObject) =>
		SetSelection(player, mapEditorObject != null ? [mapEditorObject] : []);

	internal static void SetSelection(Player player, IEnumerable<MapEditorObject> objects, bool append = false)
	{
		if (!append)
			ClearSelection(player);

		List<SelectionTarget> selection = GetSelectionTargets(player);
		foreach (MapEditorObject obj in objects)
		{
			if (obj != null && !selection.Any(target => target.Matches(obj)))
				selection.Add(new SelectionTarget(obj));
		}
		GetSelection(player);
	}

	internal static void RemoveSelection(Player player, IEnumerable<MapEditorObject> objects)
	{
		if (!Selections.TryGetValue(player, out List<SelectionTarget>? selection))
			return;
		foreach (MapEditorObject obj in objects)
			selection.RemoveAll(target => target.Matches(obj));
		GetSelection(player);
	}

	internal static IReadOnlyList<MapEditorObject> GetSelection(Player player)
	{
		if (!Selections.TryGetValue(player, out List<SelectionTarget>? selection))
			return PlayerSelectedObjectDict.TryGetValue(player, out MapEditorObject? legacy) && legacy != null ? [legacy] : [];

		List<MapEditorObject> live = [];
		selection.RemoveAll(target => !target.MapIsLoaded);
		foreach (SelectionTarget target in selection)
		{
			MapEditorObject? obj = target.Resolve();
			if (obj != null)
				live.Add(obj);
		}

		if (live.Count > 0)
			PlayerSelectedObjectDict[player] = live[0];
		else
			PlayerSelectedObjectDict.Remove(player);
		if (selection.Count == 0)
			Selections.Remove(player);
		return live;
	}

	internal static IReadOnlyList<MapEditorObject> FindNearbyObjects(Vector3 center, float radius)
	{
		float radiusSquared = radius * radius;
		return MapUtils.LoadedMaps.Values.SelectMany(map => map.SpawnedObjects)
			.Where(obj => obj != null)
			.Select(obj => (Object: obj, Distance: (obj.transform.position - center).sqrMagnitude))
			.Where(entry => entry.Distance <= radiusSquared)
			.OrderBy(entry => entry.Distance)
			.ThenBy(entry => entry.Object.MapName, StringComparer.Ordinal)
			.ThenBy(entry => entry.Object.Id, StringComparer.Ordinal)
			.Select(entry => entry.Object)
			.ToArray();
	}

	internal static void ClearSelection(Player player)
	{
		Selections.Remove(player);
		PlayerSelectedObjectDict.Remove(player);
	}

	internal static void ClearSelections()
	{
		Selections.Clear();
		PlayerSelectedObjectDict.Clear();
	}

	internal static void RefreshSelections()
	{
		foreach (Player player in Selections.Keys.ToArray())
			GetSelection(player);
	}

	internal static void RelocateSelections(EditorHistory.State source, EditorHistory.State target)
	{
		foreach (List<SelectionTarget> selection in Selections.Values)
			foreach (SelectionTarget entry in selection)
				entry.Relocate(source, target);
	}

	internal static void ReplaceSelectedInstance(Player player, MapEditorObject replacement)
	{
		List<SelectionTarget> targets = GetSelectionTargets(player);
		int index = targets.FindIndex(target => target.Matches(replacement));
		if (index < 0)
			targets.Insert(0, new SelectionTarget(replacement));
		else
			targets[index] = new SelectionTarget(replacement);
		GetSelection(player);
	}

	private static List<SelectionTarget> GetSelectionTargets(Player player)
	{
		if (!Selections.TryGetValue(player, out List<SelectionTarget>? selection))
		{
			selection = [];
			if (PlayerSelectedObjectDict.TryGetValue(player, out MapEditorObject? legacy) && legacy != null)
				selection.Add(new SelectionTarget(legacy));
			Selections.Add(player, selection);
		}
		return selection;
	}

	private sealed class SelectionTarget(MapEditorObject obj)
	{
		private MapEditorObject _instance = obj;
		private string _id = obj.Id;
		private readonly SerializableObject _data = obj.Base;
		private readonly Room _room = obj.Room;
		private MapSchematic _map = obj.Map;

		public bool MapIsLoaded
		{
			get
			{
				if (_instance != null && _instance.MapName != _map.Name &&
					MapUtils.LoadedMaps.TryGetValue(_instance.MapName, out MapSchematic? movedMap) &&
					movedMap.SpawnedObjects.Contains(_instance))
					_map = movedMap;
				return MapUtils.LoadedMaps.TryGetValue(_map.Name, out MapSchematic? current) && ReferenceEquals(current, _map);
			}
		}

		public bool Matches(MapEditorObject other) => other.Id == _id && ReferenceEquals(other.Base, _data) && ReferenceEquals(other.Map, _map);

		public void Relocate(EditorHistory.State source, EditorHistory.State target)
		{
			if (source.Map != _map || source.Id != _id || !ReferenceEquals(source.Data, _data)) return;
			_map = target.Map;
			_id = target.Id;
			_instance = null!;
		}

		public MapEditorObject? Resolve()
		{
			if (_instance != null && _map.SpawnedObjects.Contains(_instance))
				return _instance;
			MapEditorObject? replacement = null;
			foreach (MapEditorObject candidate in _map.SpawnedObjects)
			{
				if (candidate == null || candidate.Id != _id || !ReferenceEquals(candidate.Base, _data)) continue;
				if (candidate.Room == _room)
					return _instance = candidate;
				replacement ??= candidate;
			}
			return _instance = replacement!;
		}
	}

	public static bool TryGetObjectById(string id, out MapEditorObject mapEditorObject)
	{
		foreach (MapSchematic map in MapUtils.LoadedMaps.Values)
		{
			foreach (MapEditorObject meo in map.SpawnedObjects)
			{
				if (meo != null && meo.Id == id)
				{
					mapEditorObject = meo;
					return true;
				}
			}
		}

		mapEditorObject = null!;
		return false;
	}

	public static bool Raycast(Player player, out RaycastHit hit) => Raycast(player.Camera.position, player.Camera.forward, out hit);

	public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit) => Physics.Raycast(origin, direction, out hit, 100f, ToolGunMask.Mask);

	private static readonly CachedLayerMask ToolGunMask = new("Default", "Door", "CCTV");

	private static Config Config => ProjectMER.Singleton.Config!;
}
