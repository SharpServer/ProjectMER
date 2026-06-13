using System.Linq;
using CommandSystem;
using LabApi.Features.Permissions;
using LabApi.Features.Wrappers;
using MEC;
using ProjectMER.Features.Objects;
using ProjectMER.Features.Serializable;
using ProjectMER.Features.ToolGun;
using UnityEngine;

namespace ProjectMER.Commands.Modifying.Position.SubCommands;

/// <summary>
/// Grabs a specific <see cref="MapEditorObject"/>.
/// </summary>
public class Grab : ICommand
{
	public string Command => "grab";

	public string[] Aliases => [];

	public string Description => "Grabs an object.";

	public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
	{
		if (!sender.HasAnyPermission($"mpr.position"))
		{
			response = $"You don't have permission to execute this command. Required permission: mpr.position";
			return false;
		}

		Player? player = Player.Get(sender);
		if (player is null)
		{
			response = "This command can't be run from the server console.";
			return false;
		}

		if (!ToolGunHandler.TryGetSelectedMapObject(player, out MapEditorObject mapEditorObject))
		{
			response = "You need to select an object first!";
			return false;
		}

		if (GrabbingPlayers.ContainsKey(player))
		{
			Timing.KillCoroutines(GrabbingPlayers[player]);
			GrabbingPlayers.Remove(player);
			RestorePhysicsState(player);

			UpdateSerializedPosition(mapEditorObject, mapEditorObject.transform.position);
			mapEditorObject.UpdateObjectAndCopies();

			response = "Ungrabbed";
			return true;
		}

		DisablePhysicsState(player, mapEditorObject);
		GrabbingPlayers.Add(player, Timing.RunCoroutine(GrabbingCoroutine(player, mapEditorObject)));

		response = "Grabbed";
		return true;
	}

	private IEnumerator<float> GrabbingCoroutine(Player player, MapEditorObject mapEditorObject)
	{
		Vector3 position = player.Camera.position;
		float multiplier = Vector3.Distance(position, mapEditorObject.transform.position);
		Vector3 prevPos = position + (player.Camera.forward * multiplier);

		try
		{
			while (true)
			{
				yield return Timing.WaitForSeconds(0.02f);

				if (mapEditorObject == null || !ToolGunHandler.TryGetSelectedMapObject(player, out _))
					break;

				Vector3 newPos = player.Camera.position + (player.Camera.forward * multiplier);

				if ((prevPos - newPos).sqrMagnitude < 0.0001f)
					continue;

				prevPos = newPos;
				MoveGrabbedObject(mapEditorObject, prevPos);
			}
		}
		finally
		{
			GrabbingPlayers.Remove(player);
			RestorePhysicsState(player);
			if (mapEditorObject != null)
			{
				UpdateSerializedPosition(mapEditorObject, mapEditorObject.transform.position);
				mapEditorObject.UpdateObjectAndCopies();
			}
		}
	}

	private static void MoveGrabbedObject(MapEditorObject mapEditorObject, Vector3 worldPosition)
	{
		mapEditorObject.transform.position = worldPosition;
		UpdateSerializedPosition(mapEditorObject, worldPosition);
		if (mapEditorObject.Base is SerializableDoor)
			mapEditorObject.Base.SpawnOrUpdateObject(mapEditorObject.Room, mapEditorObject.gameObject);

		Physics.SyncTransforms();
	}

	private static void UpdateSerializedPosition(MapEditorObject mapEditorObject, Vector3 worldPosition)
	{
		Room room = mapEditorObject.Room;
		mapEditorObject.Base.Position = room.Name == MapGeneration.RoomName.Outside
			? worldPosition
			: room.Transform.InverseTransformPoint(worldPosition);
	}

	/// <summary>
	/// The <see cref="Dictionary{TKey, TValue}"/> which contains all <see cref="Player"/> and <see cref="CoroutineHandle"/> pairs.
	/// </summary>
	private static readonly Dictionary<Player, CoroutineHandle> GrabbingPlayers = [];
	private static readonly Dictionary<Player, GrabPhysicsState> PhysicsStates = [];

	private static void DisablePhysicsState(Player player, MapEditorObject mapEditorObject)
	{
		RestorePhysicsState(player);

		var colliders = mapEditorObject.GetComponentsInChildren<Collider>(true);
		var rigidbodies = mapEditorObject.GetComponentsInChildren<Rigidbody>(true);

		var state = new GrabPhysicsState(
			colliders.Select(c => new ColliderState(c, c.enabled)).ToArray(),
			rigidbodies.Select(r => new RigidbodyState(r, r.isKinematic, r.detectCollisions)).ToArray());

		foreach (Collider collider in colliders)
			collider.enabled = false;

		foreach (Rigidbody rigidbody in rigidbodies)
		{
			rigidbody.isKinematic = true;
			rigidbody.detectCollisions = false;
		}

		PhysicsStates[player] = state;
		Physics.SyncTransforms();
	}

	private static void RestorePhysicsState(Player player)
	{
		if (!PhysicsStates.TryGetValue(player, out GrabPhysicsState state))
			return;

		foreach (ColliderState colliderState in state.Colliders)
		{
			if (colliderState.Collider != null)
				colliderState.Collider.enabled = colliderState.Enabled;
		}

		foreach (RigidbodyState rigidbodyState in state.Rigidbodies)
		{
			if (rigidbodyState.Rigidbody != null)
			{
				rigidbodyState.Rigidbody.isKinematic = rigidbodyState.IsKinematic;
				rigidbodyState.Rigidbody.detectCollisions = rigidbodyState.DetectCollisions;
			}
		}

		PhysicsStates.Remove(player);
		Physics.SyncTransforms();
	}

	private readonly struct GrabPhysicsState
	{
		public GrabPhysicsState(ColliderState[] colliders, RigidbodyState[] rigidbodies)
		{
			Colliders = colliders;
			Rigidbodies = rigidbodies;
		}

		public ColliderState[] Colliders { get; }

		public RigidbodyState[] Rigidbodies { get; }
	}

	private readonly struct ColliderState
	{
		public ColliderState(Collider collider, bool enabled)
		{
			Collider = collider;
			Enabled = enabled;
		}

		public Collider Collider { get; }

		public bool Enabled { get; }
	}

	private readonly struct RigidbodyState
	{
		public RigidbodyState(Rigidbody rigidbody, bool isKinematic, bool detectCollisions)
		{
			Rigidbody = rigidbody;
			IsKinematic = isKinematic;
			DetectCollisions = detectCollisions;
		}

		public Rigidbody Rigidbody { get; }

		public bool IsKinematic { get; }

		public bool DetectCollisions { get; }
	}
}
