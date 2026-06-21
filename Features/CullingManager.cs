using System;
using System.Collections.Generic;
using LabApi.Features.Wrappers;
using MEC;
using Mirror;
using ProjectMER.Features.Extensions;
using ProjectMER.Features.Objects;
using UnityEngine;

namespace ProjectMER.Features;

/// <summary>
/// Manages distance-based culling for heavy MapEditorObjects (TextToys, Primitives, etc.).
/// Prevents sending spawn data to players who are too far away, and dynamically shows/hides
/// objects as players move.
/// </summary>
public static class CullingManager
{
	/// <summary>
	/// When true, the <see cref="Patches.CullingSendSpawnMessagePatch"/> will not intercept spawn messages.
	/// Used internally when intentionally sending spawn messages to specific players.
	/// </summary>
	public static bool BypassCulling;

	/// <summary>
	/// All NetworkIdentities registered for distance-based culling.
	/// </summary>
	private static readonly HashSet<NetworkIdentity> CullableIdentities = [];

	/// <summary>
	/// Tracks which NetworkIdentities are currently hidden from which connections.
	/// If a connection is in the set for a given identity, the object has NOT been spawned for that player.
	/// </summary>
	private static readonly Dictionary<NetworkIdentity, HashSet<NetworkConnection>> HiddenFrom = [];

	private static CoroutineHandle _cullingCoroutine;

	private static float CullingDistance => ProjectMER.Singleton.Config!.CullingDistance;
	private static float CullingUpdateInterval => ProjectMER.Singleton.Config!.CullingUpdateInterval;
	private static bool IsEnabled => ProjectMER.Singleton.Config!.EnableCulling;

	/// <summary>
	/// Registers a <see cref="NetworkIdentity"/> for distance-based culling.
	/// Objects registered here will only be sent to players within <see cref="CullingDistance"/>.
	/// </summary>
	public static void RegisterCullable(NetworkIdentity identity)
	{
		if (!IsEnabled)
			return;

		CullableIdentities.Add(identity);
	}

	/// <summary>
	/// Unregisters a <see cref="NetworkIdentity"/> from culling.
	/// </summary>
	public static void UnregisterCullable(NetworkIdentity identity)
	{
		CullableIdentities.Remove(identity);
		HiddenFrom.Remove(identity);
	}

	/// <summary>
	/// Returns true if the given <see cref="NetworkIdentity"/> is registered for culling.
	/// </summary>
	public static bool IsCullable(NetworkIdentity identity) => CullableIdentities.Contains(identity);

	/// <summary>
	/// Records that a spawn message was blocked for the given connection.
	/// Called by the Harmony patch when a cullable object is too far from the player.
	/// </summary>
	public static void MarkAsHidden(NetworkIdentity identity, NetworkConnection conn)
	{
		if (!HiddenFrom.TryGetValue(identity, out HashSet<NetworkConnection> set))
		{
			set = [];
			HiddenFrom[identity] = set;
		}

		set.Add(conn);
	}

	/// <summary>
	/// Checks if the given connection is within culling distance of the identity's position.
	/// </summary>
	public static bool IsWithinCullingDistance(NetworkIdentity identity, NetworkConnection conn)
	{
		if (conn.identity == null)
			return true;

		float sqrDistance = (conn.identity.transform.position - identity.transform.position).sqrMagnitude;
		return sqrDistance <= CullingDistance * CullingDistance;
	}

	/// <summary>
	/// Starts the periodic culling update coroutine.
	/// </summary>
	public static void Start()
	{
		if (!IsEnabled)
			return;

		Stop();
		_cullingCoroutine = Timing.RunCoroutine(CullingLoop());
	}

	/// <summary>
	/// Stops the culling coroutine and clears all tracking data.
	/// </summary>
	public static void Stop()
	{
		Timing.KillCoroutines(_cullingCoroutine);
		CullableIdentities.Clear();
		HiddenFrom.Clear();
	}

	/// <summary>
	/// Periodic coroutine that updates object visibility for all players based on distance.
	/// </summary>
	private static IEnumerator<float> CullingLoop()
	{
		while (true)
		{
			yield return Timing.WaitForSeconds(CullingUpdateInterval);

			foreach (Player player in Player.List)
			{
				try
				{
					UpdateCullingForPlayer(player);
				}
				catch (Exception e)
				{
					Logger.Error($"[CullingManager] Error updating culling for {player.Nickname}: {e}");
				}
			}
		}
	}

	/// <summary>
	/// Updates which cullable objects are visible to the given player.
	/// Shows objects that are now within range, hides objects that moved out of range.
	/// </summary>
	private static void UpdateCullingForPlayer(Player player)
	{
		if (player.Connection == null)
			return;

		Vector3 playerPos = player.Position;
		float sqrCullingDistance = CullingDistance * CullingDistance;

		foreach (NetworkIdentity identity in CullableIdentities)
		{
			if (identity == null)
				continue;

			float sqrDistance = (playerPos - identity.transform.position).sqrMagnitude;
			bool shouldBeVisible = sqrDistance <= sqrCullingDistance;
			bool isHidden = HiddenFrom.TryGetValue(identity, out HashSet<NetworkConnection> hiddenSet) && hiddenSet.Contains(player.Connection);

			if (shouldBeVisible && isHidden)
			{
				BypassCulling = true;
				try
				{
					player.SpawnNetworkIdentity(identity);
				}
				finally
				{
					BypassCulling = false;
				}

				hiddenSet.Remove(player.Connection);
			}
			else if (!shouldBeVisible && !isHidden)
			{
				player.DestroyNetworkIdentity(identity);
				MarkAsHidden(identity, player.Connection);
			}
		}
	}
}
