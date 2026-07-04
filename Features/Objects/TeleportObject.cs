using System.Collections.Generic;
using ProjectMER.Features.Serializable;
using UnityEngine;

namespace ProjectMER.Features.Objects;

public class TeleportObject : MonoBehaviour, ITeleporter
{
	public SerializableTeleport Base = null!;
	private MapEditorObject _mapEditorObject = null!;
	private BoxCollider? _trigger;
	private readonly HashSet<ReferenceHub> _ignoredPlayers = [];

	Transform ITeleporter.Transform => transform;
	BoxCollider? ITeleporter.Trigger => _trigger;
	float ITeleporter.TeleportCooldown => Base.Cooldown;
	HashSet<ReferenceHub> ITeleporter.IgnoredPlayers => _ignoredPlayers;
	ITeleporter? ITeleporter.GetTarget() => GetRandomTarget();

	private void Start()
	{
		TryInitialize();
	}

	private void FixedUpdate()
	{
		if (TryInitialize())
			TeleportHelper.Tick(this);
	}

	public TeleportObject? GetRandomTarget()
	{
		if (!TryInitialize() || Base.Targets is not { Count: > 0 } targets)
			return null;

		TeleportObject[] teleportObjects = FindObjectsByType<TeleportObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
		int startIndex = UnityEngine.Random.Range(0, targets.Count);

		for (int i = 0; i < targets.Count; i++)
		{
			string targetId = targets[(startIndex + i) % targets.Count];

			foreach (TeleportObject teleportObject in teleportObjects)
			{
				if (teleportObject == this || !teleportObject.TryInitialize() || teleportObject._mapEditorObject.Id != targetId)
					continue;

				return teleportObject;
			}
		}

		return null;
	}

	private void OnDestroy()
	{
		TeleportHelper.ClearArrivalReservation(transform);
	}

	private bool TryInitialize()
	{
		if (_mapEditorObject != null && Base != null)
			return true;

		if (!TryGetComponent(out _mapEditorObject) || _mapEditorObject.Base is not SerializableTeleport serializableTeleport)
			return false;

		Base = serializableTeleport;
		TryGetComponent(out _trigger);
		return true;
	}
}
