using System.Collections.Generic;
using AdminToys;
using LabApi.Features.Wrappers;
using Mirror;
using ProjectMER.Events.Arguments;
using ProjectMER.Events.Handlers;
using ProjectMER.Features.Extensions;
using ProjectMER.Features.Objects;
using UnityEngine;
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
		schematic.NetworkPrimitiveFlags = PrimitiveFlags.None;
		schematic.NetworkMovementSmoothing = 60;

		Vector3 position = room.GetAbsolutePosition(Position);
		Quaternion rotation = room.GetAbsoluteRotation(Rotation);
		_prevIndex = Index;

		schematic.name = $"CustomSchematic-{SchematicName}";
		schematic.transform.SetPositionAndRotation(position, rotation);
		schematic.transform.localScale = Scale;

		if (!MapUtils.TryGetSchematicDataByName(SchematicName, out SchematicObjectDataList? data) || data == null)
		{
			GameObject.Destroy(schematic.gameObject);
			onComplete?.Invoke(null);
			yield break;
		}

		SchematicSpawningEventArgs ev = new(data, SchematicName);
		Schematic.OnSchematicSpawning(ev);
		data = ev.Data;

		if (!ev.IsAllowed)
		{
			GameObject.Destroy(schematic.gameObject);
			onComplete?.Invoke(null);
			yield break;
		}

		NetworkServer.Spawn(schematic.gameObject);
		SchematicObject schematicObject = schematic.gameObject.AddComponent<SchematicObject>();

		IEnumerator<float> init = schematicObject.InitStaggered(data, frameBudgetMs);
		while (init.MoveNext())
			yield return init.Current;

		onComplete?.Invoke(schematic.gameObject);
	}
}
