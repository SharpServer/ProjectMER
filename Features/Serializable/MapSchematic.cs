using System.Diagnostics;
using LabApi.Features.Wrappers;
using MEC;
using NorthwoodLib.Pools;
using ProjectMER.Features.Extensions;
using ProjectMER.Features.Objects;
using ProjectMER.Features.Serializable.Lockers;
using ProjectMER.Features.Serializable.Schematics;
using UnityEngine;
using Utils.NonAllocLINQ;

namespace ProjectMER.Features.Serializable;

public class MapSchematic
{
	public MapSchematic() { }

	public MapSchematic(string mapName)
	{
		Name = mapName;
	}

	public string Name;

	public bool IsDirty;

	public Dictionary<string, SerializablePrimitive> Primitives { get; set; } = [];

	public Dictionary<string, SerializableLight> Lights { get; set; } = [];

	public Dictionary<string, SerializableDoor> Doors { get; set; } = [];

	public Dictionary<string, SerializableWorkstation> Workstations { get; set; } = [];

	public Dictionary<string, SerializableItemSpawnpoint> ItemSpawnpoints { get; set; } = [];

	public Dictionary<string, SerializablePlayerSpawnpoint> PlayerSpawnpoints { get; set; } = [];

	public Dictionary<string, SerializableCapybara> Capybaras { get; set; } = [];

	public Dictionary<string, SerializableText> Texts { get; set; } = [];

	public Dictionary<string, SerializableInteractable> Interactables { get; set; } = [];

	public Dictionary<string, SerializableScp079Camera> Scp079Cameras { get; set; } = [];

	public Dictionary<string, SerializableShootingTarget> ShootingTargets { get; set; } = [];

	public Dictionary<string, SerializableSchematic> Schematics { get; set; } = [];

	public Dictionary<string, SerializableTeleport> Teleports { get; set; } = [];

	public Dictionary<string, SerializableLocker> Lockers { get; set; } = [];

	public Dictionary<string, SerializableWaypoint> Waypoints { get; set; } = [];

	public Dictionary<string, SerializableCustomTriggerPoint> CustomTriggerPoints { get; set; } = [];

	public Dictionary<string, SerializableObjectPrefabMarker> ObjectPrefabMarkers { get; set; } = [];

	public Dictionary<string, SerializablePrefabMarker> PrefabMarkers { get; set; } = [];

	public Dictionary<string, SerializableEventInvokeMarker> EventInvokeMarkers { get; set; } = [];

	public List<MapEditorObject> SpawnedObjects = [];

	public MapSchematic Merge(MapSchematic other)
	{
		Primitives.AddRange(other.Primitives);
		Lights.AddRange(other.Lights);
		Doors.AddRange(other.Doors);
		Workstations.AddRange(other.Workstations);
		ItemSpawnpoints.AddRange(other.ItemSpawnpoints);
		PlayerSpawnpoints.AddRange(other.PlayerSpawnpoints);
		Capybaras.AddRange(other.Capybaras);
		Texts.AddRange(other.Texts);
		Interactables.AddRange(other.Interactables);
		Schematics.AddRange(other.Schematics);
		Scp079Cameras.AddRange(other.Scp079Cameras);
		ShootingTargets.AddRange(other.ShootingTargets);
		Teleports.AddRange(other.Teleports);
		Lockers.AddRange(other.Lockers);
		Waypoints.AddRange(other.Waypoints);
		CustomTriggerPoints.AddRange(other.CustomTriggerPoints);
		ObjectPrefabMarkers.AddRange(other.ObjectPrefabMarkers);
		PrefabMarkers.AddRange(other.PrefabMarkers);
		EventInvokeMarkers.AddRange(other.EventInvokeMarkers);

		return this;
	}

	public void Reload(IReadOnlyList<string>? prioritySchematicNames = null)
	{
		DestroySpawnedObjects();

		foreach (Action spawnAction in EnumeratePreSchematicSpawnActions())
			RunSpawnAction(spawnAction);
		foreach (Action spawnAction in EnumerateEarlyInteractiveSpawnActions())
			RunSpawnAction(spawnAction);

		(List<KeyValuePair<string, SerializableSchematic>> prioritySchematics, List<KeyValuePair<string, SerializableSchematic>> remainingSchematics)
			= SplitSchematicsByPriority(prioritySchematicNames);

		foreach (KeyValuePair<string, SerializableSchematic> kVP in prioritySchematics)
			RunSpawnAction(() => SpawnObject(kVP.Key, kVP.Value));
		foreach (KeyValuePair<string, SerializableCustomTriggerPoint> kVP in CustomTriggerPoints)
			RunSpawnAction(() => SpawnObject(kVP.Key, kVP.Value));
		foreach (KeyValuePair<string, SerializableSchematic> kVP in remainingSchematics)
			RunSpawnAction(() => SpawnObject(kVP.Key, kVP.Value));

		foreach (Action spawnAction in EnumeratePostSchematicSpawnActions())
			RunSpawnAction(spawnAction);
		foreach (Action spawnAction in EnumerateFinalSpawnActions())
			RunSpawnAction(spawnAction);
	}

	/// <summary>
	/// Reload の分散実行版。1フレームの処理時間が <paramref name="frameBudgetMs"/> を超えたら
	/// 次のフレームへ持ち越す。スポーン順は <see cref="Reload"/> と同一。
	/// Schematics はブロック生成自体も複数フレームへ分散する
	/// （PersonnelOfficeZone など数万ブロック規模のスキマティックが1回のスポーンで
	/// メインスレッドを長時間占有するのを避けるため）。
	/// <paramref name="prioritySchematicNames"/> に指定した SchematicName は他の Schematics より先に処理される
	/// （待機場など WaitingForPlayers 中にプレイヤーへ見える場所を早く仕上げるため）。
	/// </summary>
	public IEnumerator<float> ReloadStaggered(float frameBudgetMs, IReadOnlyList<string>? prioritySchematicNames = null)
	{
		DestroySpawnedObjects();

		Stopwatch stopwatch = Stopwatch.StartNew();

		foreach (Action spawnAction in EnumeratePreSchematicSpawnActions())
		{
			RunSpawnAction(spawnAction);

			if (stopwatch.Elapsed.TotalMilliseconds >= frameBudgetMs)
			{
				yield return Timing.WaitForOneFrame;
				stopwatch.Restart();
			}
		}

		// Teleports only depend on room transforms and on other teleport IDs. Spawn them before
		// potentially huge schematics so gameplay does not wait for the visual map pipeline.
		foreach (Action spawnAction in EnumerateEarlyInteractiveSpawnActions())
		{
			RunSpawnAction(spawnAction);

			if (stopwatch.Elapsed.TotalMilliseconds >= frameBudgetMs)
			{
				yield return Timing.WaitForOneFrame;
				stopwatch.Restart();
			}
		}

		if (Teleports.Count > 0)
			Logger.Debug($"[{Name}] Early interactive objects ready ({Teleports.Count} teleports).");

		(List<KeyValuePair<string, SerializableSchematic>> prioritySchematics, List<KeyValuePair<string, SerializableSchematic>> remainingSchematics)
			= SplitSchematicsByPriority(prioritySchematicNames);

		foreach (KeyValuePair<string, SerializableSchematic> kVP in prioritySchematics)
		{
			IEnumerator<float> spawn = SpawnSchematicStaggered(kVP.Key, kVP.Value, frameBudgetMs);
			try
			{
			while (spawn.MoveNext())
				yield return spawn.Current;
			}
			finally
			{
				spawn.Dispose();
			}

			if (stopwatch.Elapsed.TotalMilliseconds >= frameBudgetMs)
			{
				yield return Timing.WaitForOneFrame;
				stopwatch.Restart();
			}
		}

		foreach (KeyValuePair<string, SerializableCustomTriggerPoint> kVP in CustomTriggerPoints)
		{
			RunSpawnAction(() => SpawnObject(kVP.Key, kVP.Value));

			if (stopwatch.Elapsed.TotalMilliseconds >= frameBudgetMs)
			{
				yield return Timing.WaitForOneFrame;
				stopwatch.Restart();
			}
		}

		foreach (KeyValuePair<string, SerializableSchematic> kVP in remainingSchematics)
		{
			IEnumerator<float> spawn = SpawnSchematicStaggered(kVP.Key, kVP.Value, frameBudgetMs);
			try
			{
			while (spawn.MoveNext())
				yield return spawn.Current;
			}
			finally
			{
				spawn.Dispose();
			}

			if (stopwatch.Elapsed.TotalMilliseconds >= frameBudgetMs)
			{
				yield return Timing.WaitForOneFrame;
				stopwatch.Restart();
			}
		}

		foreach (Action spawnAction in EnumeratePostSchematicSpawnActions())
		{
			RunSpawnAction(spawnAction);

			if (stopwatch.Elapsed.TotalMilliseconds >= frameBudgetMs)
			{
				yield return Timing.WaitForOneFrame;
				stopwatch.Restart();
			}
		}

		foreach (Action spawnAction in EnumerateFinalSpawnActions())
		{
			RunSpawnAction(spawnAction);

			if (stopwatch.Elapsed.TotalMilliseconds >= frameBudgetMs)
			{
				yield return Timing.WaitForOneFrame;
				stopwatch.Restart();
			}
		}
	}

	// 1オブジェクトの失敗でマップ全体のロードが中断しないようにする（RunSpawnAction のコルーチン版）
	private IEnumerator<float> SpawnSchematicStaggered(string id, SerializableSchematic serializableObject, float frameBudgetMs)
	{
		List<Room> rooms = serializableObject.GetRooms();
		try
		{
		foreach (Room room in rooms)
		{
			if (serializableObject.Index >= 0 && serializableObject.Index != room.GetRoomIndex())
				continue;

			GameObject? resultGameObject = null;
			IEnumerator<float> spawn = serializableObject.SpawnOrUpdateObjectStaggered(room, frameBudgetMs, go => resultGameObject = go);
				try
				{
			while (true)
			{
				bool moved;
				try
				{
					moved = spawn.MoveNext();
				}
				catch (Exception e)
				{
					Logger.Error($"[{Name}] Failed to spawn a schematic map object '{id}': {e}");
					break;
				}

				if (!moved)
					break;

				yield return spawn.Current;
					}
				}
				finally
				{
					spawn.Dispose();
			}

			if (resultGameObject == null)
				continue;

			MapEditorObject mapEditorObject = resultGameObject.AddComponent<MapEditorObject>().Init(serializableObject, Name, id, room);
			SpawnedObjects.Add(mapEditorObject);
		}
		}
		finally
		{
		ListPool<Room>.Shared.Return(rooms);
		}
	}

	private void DestroySpawnedObjects()
	{
		foreach (MapEditorObject mapEditorObject in SpawnedObjects)
			mapEditorObject.Destroy();

		SpawnedObjects.Clear();
	}

	// 1オブジェクトの失敗でマップ全体のロードが中断しないようにする
	private void RunSpawnAction(Action spawnAction)
	{
		try
		{
			spawnAction();
		}
		catch (Exception e)
		{
			Logger.Error($"[{Name}] Failed to spawn a map object: {e}");
		}
	}

	/// <summary>
	/// <paramref name="priorityNames"/> に含まれる SchematicName（先勝ち、リスト順）を Priority 側へ、
	/// 残りを元の登録順のまま Remaining 側へ振り分ける。
	/// CustomTriggerPoints は Priority の Schematics 直後・Remaining の Schematics より前に
	/// スポーンするため、呼び出し側で両者を分けて処理できるようにしている。
	/// </summary>
	private (List<KeyValuePair<string, SerializableSchematic>> Priority, List<KeyValuePair<string, SerializableSchematic>> Remaining) SplitSchematicsByPriority(
		IReadOnlyList<string>? priorityNames)
	{
		List<KeyValuePair<string, SerializableSchematic>> remaining = new(Schematics);

		if (priorityNames == null || priorityNames.Count == 0)
			return ([], remaining);

		List<KeyValuePair<string, SerializableSchematic>> priority = [];

		foreach (string priorityName in priorityNames)
		{
			for (int i = 0; i < remaining.Count; i++)
			{
				if (!string.Equals(remaining[i].Value.SchematicName, priorityName, StringComparison.OrdinalIgnoreCase))
					continue;

				priority.Add(remaining[i]);
				remaining.RemoveAt(i);
				i--;
			}
		}

		return (priority, remaining);
	}

	private IEnumerable<Action> EnumeratePreSchematicSpawnActions()
	{
		foreach (KeyValuePair<string, SerializablePrimitive> kVP in Primitives)
			yield return () => SpawnObject(kVP.Key, kVP.Value);
		foreach (KeyValuePair<string, SerializableLight> kVP in Lights)
			yield return () => SpawnObject(kVP.Key, kVP.Value);
		foreach (KeyValuePair<string, SerializableDoor> kVP in Doors)
			yield return () =>
			{
				Door? vanillaDoor = Door.Get(kVP.Key);
				if (vanillaDoor != null)
				{
					kVP.Value.SetupDoor(vanillaDoor.Base);
					return;
				}

				SpawnObject(kVP.Key, kVP.Value);
			};
		foreach (KeyValuePair<string, SerializableWorkstation> kVP in Workstations)
			yield return () => SpawnObject(kVP.Key, kVP.Value);
		foreach (KeyValuePair<string, SerializablePlayerSpawnpoint> kVP in PlayerSpawnpoints)
			yield return () => SpawnObject(kVP.Key, kVP.Value);
		foreach (KeyValuePair<string, SerializableCapybara> kVP in Capybaras)
			yield return () => SpawnObject(kVP.Key, kVP.Value);
		foreach (KeyValuePair<string, SerializableText> kVP in Texts)
			yield return () => SpawnObject(kVP.Key, kVP.Value);
		foreach (KeyValuePair<string, SerializableInteractable> kVP in Interactables)
			yield return () => SpawnObject(kVP.Key, kVP.Value);
	}

	private IEnumerable<Action> EnumerateEarlyInteractiveSpawnActions()
	{
		foreach (KeyValuePair<string, SerializableTeleport> kVP in Teleports)
			yield return () => SpawnObject(kVP.Key, kVP.Value);
	}

	private IEnumerable<Action> EnumeratePostSchematicSpawnActions()
	{
		foreach (KeyValuePair<string, SerializableScp079Camera> kVP in Scp079Cameras)
			yield return () => SpawnObject(kVP.Key, kVP.Value);
		foreach (KeyValuePair<string, SerializableShootingTarget> kVP in ShootingTargets)
			yield return () => SpawnObject(kVP.Key, kVP.Value);
		foreach (KeyValuePair<string, SerializableLocker> kVP in Lockers)
			yield return () =>
			{
				kVP.Value._prevType = kVP.Value.LockerType;
				SpawnObject(kVP.Key, kVP.Value);
			};
		foreach (KeyValuePair<string, SerializableWaypoint> kVP in Waypoints)
			yield return () => SpawnObject(kVP.Key, kVP.Value);
	}

	/// <summary>
	/// ItemSpawnpoint（Pickup 生成、CustomItem 解決を伴う）と各種 Marker 類
	/// （ObjectPrefabMarkers 等、Slafight 側ブリッジで追加のスキマティック生成を誘発しうる）は、
	/// 部屋・スキマティックなど見た目の骨格が整ってから最後に処理する。
	/// CustomTriggerPoints は優先 Schematics の直後（本メソッドより前）でスポーン済みのため対象外。
	/// </summary>
	private IEnumerable<Action> EnumerateFinalSpawnActions()
	{
		foreach (KeyValuePair<string, SerializableItemSpawnpoint> kVP in ItemSpawnpoints)
			yield return () => SpawnObject(kVP.Key, kVP.Value);
		foreach (KeyValuePair<string, SerializableObjectPrefabMarker> kVP in ObjectPrefabMarkers)
			yield return () => SpawnObject(kVP.Key, kVP.Value);
		foreach (KeyValuePair<string, SerializablePrefabMarker> kVP in PrefabMarkers)
			yield return () => SpawnObject(kVP.Key, kVP.Value);
		foreach (KeyValuePair<string, SerializableEventInvokeMarker> kVP in EventInvokeMarkers)
			yield return () => SpawnObject(kVP.Key, kVP.Value);
	}

	public void SpawnObject<T>(string id, T serializableObject) where T : SerializableObject
	{
		List<Room> rooms = serializableObject.GetRooms();
		foreach (Room room in rooms)
		{
			if (serializableObject.Index < 0 || serializableObject.Index == room.GetRoomIndex())
			{
				GameObject? gameObject = serializableObject.SpawnOrUpdateObject(room);
				if (gameObject == null)
					continue;

				MapEditorObject mapEditorObject = gameObject.AddComponent<MapEditorObject>().Init(serializableObject, Name, id, room);
				SpawnedObjects.Add(mapEditorObject);
			}
		}

		ListPool<Room>.Shared.Return(rooms);
	}

	public void DestroyObject(string id)
	{
		foreach (MapEditorObject mapEditorObject in SpawnedObjects.ToList())
		{
			if (mapEditorObject.Id != id)
				continue;

			SpawnedObjects.Remove(mapEditorObject);
			mapEditorObject.Destroy();
		}
	}

	public bool TryAddElement<T>(string id, T serializableObject) where T : SerializableObject
	{
		bool dirtyPrevValue = IsDirty;
		IsDirty = true;

		if (Primitives.TryAdd(id, serializableObject))
			return true;

		if (Lights.TryAdd(id, serializableObject))
			return true;

		if (Doors.TryAdd(id, serializableObject))
			return true;

		if (Workstations.TryAdd(id, serializableObject))
			return true;

		if (ItemSpawnpoints.TryAdd(id, serializableObject))
			return true;

		if (PlayerSpawnpoints.TryAdd(id, serializableObject))
			return true;

		if (Capybaras.TryAdd(id, serializableObject))
			return true;

		if (Texts.TryAdd(id, serializableObject))
			return true;

		if (Interactables.TryAdd(id, serializableObject))
			return true;

		if (Schematics.TryAdd(id, serializableObject))
			return true;

		if (Scp079Cameras.TryAdd(id, serializableObject))
			return true;

		if (ShootingTargets.TryAdd(id, serializableObject))
			return true;

		if (Teleports.TryAdd(id, serializableObject))
			return true;

		if (Lockers.TryAdd(id, serializableObject))
			return true;

		if (Waypoints.TryAdd(id, serializableObject))
			return true;

		if (CustomTriggerPoints.TryAdd(id, serializableObject))
			return true;

		if (ObjectPrefabMarkers.TryAdd(id, serializableObject))
			return true;

		if (PrefabMarkers.TryAdd(id, serializableObject))
			return true;
		if (EventInvokeMarkers.TryAdd(id, serializableObject))
			return true;

		IsDirty = dirtyPrevValue;
		return false;
	}

	public bool TryRemoveElement(string id)
	{
		bool dirtyPrevValue = IsDirty;
		IsDirty = true;

		if (Primitives.Remove(id))
			return true;

		if (Lights.Remove(id))
			return true;

		if (Doors.Remove(id))
			return true;

		if (Workstations.Remove(id))
			return true;

		if (ItemSpawnpoints.Remove(id))
			return true;

		if (PlayerSpawnpoints.Remove(id))
			return true;

		if (Capybaras.Remove(id))
			return true;

		if (Texts.Remove(id))
			return true;

		if (Interactables.Remove(id))
			return true;

		if (Schematics.Remove(id))
			return true;

		if (Scp079Cameras.Remove(id))
			return true;

		if (ShootingTargets.Remove(id))
			return true;

		if (Teleports.Remove(id))
			return true;

		if (Lockers.Remove(id))
			return true;

		if (Waypoints.Remove(id))
			return true;

		if (CustomTriggerPoints.Remove(id))
			return true;

		if (ObjectPrefabMarkers.Remove(id))
			return true;

		if (PrefabMarkers.Remove(id))
			return true;
		if (EventInvokeMarkers.Remove(id))
			return true;

		IsDirty = dirtyPrevValue;
		return false;
	}
}
