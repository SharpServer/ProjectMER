using System;
using Interactables.Interobjects.DoorUtils;
using LabApi.Features.Wrappers;
using MapGeneration.Distributors;
using MapGeneration.RoomConnectors;
using Mirror;
using ProjectMER.Features.Objects;
using ProjectMER.Patches;
using ProjectMER.Features.Enums;
using ProjectMER.Features.Extensions;
using UnityEngine;

namespace ProjectMER.Features.Serializable;

public class SerializableDoor : SerializableObject
{
	public DoorType DoorType { get; set; } = DoorType.Lcz;
	public bool IsOpen { get; set; } = false;
	public bool IsLocked { get; set; } = false;
	public DoorPermissionFlags RequiredPermissions { get; set; } = DoorPermissionFlags.None;
	public bool RequireAll { get; set; } = true;
	public string AccessDeniedMessage { get; set; } = string.Empty;
	public string SecretCode { get; set; } = string.Empty;

	public override GameObject SpawnOrUpdateObject(Room? room = null, GameObject? instance = null)
	{
		Vector3 position = room.GetAbsolutePosition(Position);
		Quaternion rotation = room.GetAbsoluteRotation(Rotation);
		_prevIndex = Index;

		if (instance == null)
			return SpawnAt(position, rotation);

		DoorVariant doorVariant = instance.GetComponent<DoorVariant>();
		doorVariant.transform.SetPositionAndRotation(position, rotation);
		doorVariant.transform.localScale = Scale;
		_prevType = DoorType;
		SetupDoor(doorVariant);
		if (doorVariant.TryGetComponent(out StructurePositionSync structure))
		{
			StructureTransformSync.Attach(structure);
			NetworkServer.UnSpawn(instance);
			NetworkServer.Spawn(instance);
		}
		else
		{
			SendTransformUpdate(doorVariant);
		}

		return instance;
	}

	internal GameObject SpawnAt(Vector3 position, Quaternion rotation, string? name = null)
	{
		DoorVariant doorVariant = GameObject.Instantiate(DoorPrefab, position, rotation);
		if (doorVariant.TryGetComponent(out DoorRandomInitialStateExtension randomInitialState))
			GameObject.Destroy(randomInitialState);
		if (name != null)
			doorVariant.gameObject.name = name;

		doorVariant.transform.localScale = Scale;
		if (doorVariant.TryGetComponent(out StructurePositionSync structure))
			StructureTransformSync.Attach(structure);
		_prevType = DoorType;
		SetupDoor(doorVariant);
		NetworkServer.Spawn(doorVariant.gameObject);
		return doorVariant.gameObject;
	}

	public void SetupDoor(DoorVariant doorVariant)
	{
		doorVariant.NetworkTargetState = IsOpen;
		doorVariant.ServerChangeLock(DoorLockReason.SpecialDoorFeature, IsLocked);
		doorVariant.RequiredPermissions = new DoorPermissionsPolicy(RequiredPermissions, RequireAll);
		DoorAccessFeedback? feedback = doorVariant.GetComponent<DoorAccessFeedback>();
		if (feedback == null && (!string.IsNullOrWhiteSpace(AccessDeniedMessage) || !string.IsNullOrEmpty(SecretCode)))
			feedback = doorVariant.gameObject.AddComponent<DoorAccessFeedback>();
		if (feedback != null)
		{
			feedback.DenialMessage = AccessDeniedMessage;
			feedback.SecretCode = SecretCode;
		}
		byte? mask = DoorType switch
		{
			DoorType.Hcz => 3,
			DoorType.HczOneSided => 1,
			DoorType.HczTwoSided => 0,
			_ => null,
		};
		if (mask.HasValue && doorVariant.TryGetComponent(out WallableSmallNodeRoomConnector connector))
			SpawnedDoorWallConfiguration.Apply(connector, mask.Value);
	}

	private DoorVariant DoorPrefab
	{
		get
		{
			DoorVariant prefab = DoorType switch
			{
				DoorType.Lcz => PrefabManager.DoorLcz,
				DoorType.Hcz or DoorType.HczOneSided or DoorType.HczTwoSided => PrefabManager.DoorHcz,
				DoorType.Ez => PrefabManager.DoorEz,
				DoorType.Bulkdoor => PrefabManager.DoorHeavyBulk,
				DoorType.Gate => PrefabManager.DoorGate,
				_ => throw new InvalidOperationException(),
			};

			return prefab;
		}
	}

	private static void SendTransformUpdate(DoorVariant doorVariant)
	{
		if (!doorVariant.TryGetComponent(out NetworkIdentity identity) || identity.netId == 0)
			return;

		Transform transform = doorVariant.transform;
		NetworkServer.SendToReadyObservers(identity, new SpawnMessage
		{
			netId = identity.netId,
			isLocalPlayer = false,
			isOwner = false,
			sceneId = identity.sceneId,
			assetId = identity.assetId,
			position = transform.localPosition,
			rotation = transform.localRotation,
			scale = transform.localScale,
			payload = default,
		});
	}

	public override bool RequiresReloading => DoorType != _prevType || base.RequiresReloading;

	internal DoorType _prevType;
}
