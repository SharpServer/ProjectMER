using System.Collections.Generic;
using AdminToys;
using LabApi.Features.Wrappers;
using MEC;
using Mirror;
using ProjectMER.Events.Arguments;
using ProjectMER.Events.Handlers;
using ProjectMER.Features.Extensions;
using ProjectMER.Features.Objects;
using UnityEngine;
using Utf8Json;
using PrimitiveObjectToy = AdminToys.PrimitiveObjectToy;

namespace ProjectMER.Features.Serializable.Schematics;

public class SerializableSchematic : SerializableObject
{
	public string SchematicName { get; set; } = "None";

	public override GameObject? SpawnOrUpdateObject(Room? room = null, GameObject? instance = null)
	{
		PrimitiveObjectToy schematic = instance == null ? UnityEngine.Object.Instantiate(PrefabManager.PrimitiveObject) : instance.GetComponent<PrimitiveObjectToy>();
		schematic.NetworkPrimitiveFlags = PrimitiveFlags.None;
		schematic.NetworkMovementSmoothing = 60;

		Vector3 position = room.GetAbsolutePosition(Position);
		Quaternion rotation = room.GetAbsoluteRotation(Rotation);
		_prevIndex = Index;

		schematic.name = $"CustomSchematic-{SchematicName}";
		schematic.transform.SetPositionAndRotation(position, rotation);
		schematic.transform.localScale = Scale;

		if (instance == null)
		{
			_ = MapUtils.TryGetSchematicDataByName(SchematicName, out SchematicObjectDataList? data) ? data : null;

			if (data == null)
			{
				GameObject.Destroy(schematic.gameObject);
				return null;
			}

			SchematicSpawningEventArgs ev = new(data, SchematicName);
			Schematic.OnSchematicSpawning(ev);
			data = ev.Data;

			if (!ev.IsAllowed)
			{
				GameObject.Destroy(schematic.gameObject);
				return null;
			}

			NetworkServer.Spawn(schematic.gameObject);
			schematic.gameObject.AddComponent<SchematicObject>().Init(data);
		}

		return schematic.gameObject;
	}

	/// <summary>
	/// <see cref="SpawnOrUpdateObject"/> の分散実行版（新規スポーン専用、instance 更新経路は対象外）。
	/// スキマティックのブロック生成を複数フレームへ分散し、大規模スキマティック
	/// （数千～数万ブロック）による長時間のメインスレッド占有を避ける。
	/// </summary>
	public IEnumerator<float> SpawnOrUpdateObjectStaggered(Room? room, float frameBudgetMs, Action<GameObject?>? onComplete = null)
	{
		PrimitiveObjectToy schematic = UnityEngine.Object.Instantiate(PrefabManager.PrimitiveObject);
		bool completed = false;
		try
		{
		schematic.NetworkPrimitiveFlags = PrimitiveFlags.None;
		schematic.NetworkMovementSmoothing = 60;

		Vector3 position = room.GetAbsolutePosition(Position);
		Quaternion rotation = room.GetAbsoluteRotation(Rotation);
		_prevIndex = Index;

		schematic.name = $"CustomSchematic-{SchematicName}";
		schematic.transform.SetPositionAndRotation(position, rotation);
		schematic.transform.localScale = Scale;

			Task<MapUtils.SchematicDataLoadResult> loadTask = MapUtils.LoadSchematicDataForStaggeredAsync(SchematicName);
			while (!loadTask.IsCompleted)
				yield return Timing.WaitForOneFrame;

			MapUtils.SchematicDataLoadResult loadResult;
			try
		{
				loadResult = loadTask.GetAwaiter().GetResult();
			}
			catch (Exception)
			{
				// Preserve TryGetSchematicDataByName's failure behavior if the worker itself cannot return a result.
				onComplete?.Invoke(null);
				yield break;
			}

			if (loadResult.Error is JsonParsingException error)
			{
				string message = $"Failed to load schematic data: File {SchematicName}.json has JSON errors!\n{error.ToString().Split('\n')[0]}";
				Logger.Error(message);
			}

			SchematicObjectDataList? data = loadResult.Data;
			if (data == null)
			{
				onComplete?.Invoke(null);
				yield break;
			}

			if (loadResult.DirectoryPath == null)
			{
			onComplete?.Invoke(null);
			yield break;
		}

			data.Path = loadResult.DirectoryPath;

		SchematicSpawningEventArgs ev = new(data, SchematicName);
		Schematic.OnSchematicSpawning(ev);
		data = ev.Data;

		if (!ev.IsAllowed)
		{
			onComplete?.Invoke(null);
			yield break;
		}

		NetworkServer.Spawn(schematic.gameObject);
		SchematicObject schematicObject = schematic.gameObject.AddComponent<SchematicObject>();

		IEnumerator<float> init = schematicObject.InitStaggered(data, frameBudgetMs);
			try
			{
		while (init.MoveNext())
			yield return init.Current;
			}
			finally
			{
				init.Dispose();
			}

			completed = true;
		onComplete?.Invoke(schematic.gameObject);
		}
		finally
		{
			if (!completed && schematic != null && schematic.gameObject != null)
			{
				NetworkIdentity identity = schematic.netIdentity;
				if (NetworkServer.active && identity != null && identity.isServer)
					NetworkServer.Destroy(schematic.gameObject);
				else
					GameObject.Destroy(schematic.gameObject);
			}
		}
	}
}
