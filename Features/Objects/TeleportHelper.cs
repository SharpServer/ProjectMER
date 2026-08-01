using System.Collections.Generic;
using LabApi.Features.Wrappers;
using PlayerRoles.FirstPersonControl;
using UnityEngine;

namespace ProjectMER.Features.Objects;

/// <summary>
/// Shared logic for teleporters. Detection is polling-based (per-frame OBB containment test)
/// instead of physics triggers: SCP:SL players move with a <see cref="CharacterController"/>
/// which tunnels through thin triggers while grounded and only reliably fires them while airborne,
/// which is why entering used to require a jump.
/// </summary>
internal static class TeleportHelper
{
	private const float MinimumPlayerCooldown = 0.35f;
	private const float ExitMargin = 0.6f;
	private const float ArrivalSlotWindow = 0.25f;
	private const float ArrivalSlotSpacing = 0.8f;
	private const float DefaultPlayerRadius = 0.5f;
	private const float PlayerRadiusPadding = 0.05f;
	private const float MinimumSweepStep = 0.2f;
	private const int MaxSweepSamples = 8;
	private static readonly float[] VerticalSampleHeights = [0f, 0.9f, 1.7f];
	private static readonly Dictionary<ReferenceHub, float> PlayerNextUseTimes = [];
	private static readonly Dictionary<int, ArrivalReservation> ArrivalReservations = [];
	private static readonly Dictionary<ReferenceHub, PlayerMotionSample> PlayerMotionSamples = [];

	public static void ConfigureTrigger(BoxCollider boxCollider, Vector3 size)
	{
		// Kept as a trigger so it never blocks movement; detection no longer relies on physics events.
		boxCollider.isTrigger = true;
		boxCollider.size = size;
	}

	/// <summary>
	/// Runs one detection pass for a teleporter. Call from <c>FixedUpdate</c>.
	/// </summary>
	public static void Tick(ITeleporter source)
	{
		BoxCollider? trigger = source.Trigger;
		if (trigger == null)
			return;

		HashSet<ReferenceHub> ignoredPlayers = source.IgnoredPlayers;

		foreach (Player player in Player.ReadyList)
		{
			ReferenceHub hub = player.ReferenceHub;

			if (player.RoleBase is not IFpcRole fpcRole || !IsInside(trigger, player, fpcRole))
			{
				// Keep the arrival hold for the whole cooldown. The server-side motor can briefly
				// correct a freshly teleported player back into the destination trigger on a later
				// physics step; releasing the hold immediately on the first outside sample then
				// allows one delayed bounce as soon as the cooldown expires.
				if (!IsOnCooldown(player))
					ignoredPlayers.Remove(hub);

				continue;
			}

			// Player is inside. Skip if they just arrived here or are still cooling down.
			if (ignoredPlayers.Contains(hub) || IsOnCooldown(player))
				continue;

			ITeleporter? target = source.GetTarget();
			if (target == null)
			{
				// Misconfigured/unreachable teleporter (bad id, target in an unloaded room): rate-limit the
				// FindObjectsByType scan so a player standing on a dead pad doesn't run it every physics tick.
				PlayerNextUseTimes[hub] = Time.time + MinimumPlayerCooldown;
				continue;
			}

			// Prevent the destination from immediately bouncing the player back out, and also
			// re-ignore this pad for the player: if the destination's exit position still
			// overlaps this trigger (common for closely-placed/paired teleporters), the next
			// Tick would otherwise see them "freshly entering" here once only the cooldown
			// gate remains, causing a fast A<->B ping-pong.
			ignoredPlayers.Add(hub);
			target.IgnoredPlayers.Add(hub);

			Teleport(player, target.Transform, target.Trigger, source.TeleportCooldown);
		}
	}

	public static bool IsOnCooldown(Player player)
	{
		if (!PlayerNextUseTimes.TryGetValue(player.ReferenceHub, out float nextUseTime))
			return false;

		if (nextUseTime > Time.time)
			return true;

		PlayerNextUseTimes.Remove(player.ReferenceHub);
		return false;
	}

	public static void Teleport(Player player, Transform targetTransform, BoxCollider? targetCollider, float cooldown)
	{
		Vector3 exitPosition = GetExitPosition(targetTransform, targetCollider);
		player.Position = exitPosition;
		SetMotionSample(player, exitPosition);
		player.LookRotation = targetTransform.eulerAngles;
		NeutralizeFallVelocity(player);
		PlayerNextUseTimes[player.ReferenceHub] = Time.time + Mathf.Max(MinimumPlayerCooldown, cooldown);
	}

	public static void ClearArrivalReservation(Transform targetTransform)
	{
		if (targetTransform != null)
			ArrivalReservations.Remove(targetTransform.GetInstanceID());
	}

	/// <summary>
	/// Cancels the downward velocity carried by the player's motor across the teleport.
	/// <see cref="FirstPersonMovementModule.ServerOverridePosition"/> only moves the player; it leaves
	/// <see cref="FpcMotor.MoveDirection"/> untouched, so a falling player keeps their accumulated
	/// <c>MoveDirection.y</c> and gets punched through the destination floor on the next motor step.
	/// This is the same thing a manual jump did by overwriting <c>MoveDirection.y</c>.
	/// </summary>
	private static void NeutralizeFallVelocity(Player player)
	{
		if (player.RoleBase is not IFpcRole fpcRole)
			return;

		FpcMotor motor = fpcRole.FpcModule.Motor;
		Vector3 moveDirection = motor.MoveDirection;
		moveDirection.y = 0f;
		motor.MoveDirection = moveDirection;
		motor.Velocity = Vector3.zero;
		motor.ResetFallDamageCooldown();
	}

	private static bool IsInside(BoxCollider box, Player player, IFpcRole fpcRole)
	{
		PlayerMotionSample motionSample = GetMotionSample(player);
		float playerRadius = GetPlayerRadius(fpcRole);
		Vector3 delta = motionSample.CurrentPosition - motionSample.PreviousPosition;
		float distance = delta.magnitude;
		int sweepSamples = distance <= 0f
			? 1
			: Mathf.Clamp(Mathf.CeilToInt(distance / Mathf.Max(MinimumSweepStep, playerRadius * 0.5f)) + 1, 2, MaxSweepSamples);

		// Sample several points up the player's body and across the previous/current movement span.
		// The radius check restores capsule-like contact for very thin trigger boxes: the center line no
		// longer has to pass through the box exactly.
		for (int i = 0; i < sweepSamples; i++)
		{
			float t = sweepSamples == 1 ? 1f : i / (float)(sweepSamples - 1);
			Vector3 foot = Vector3.Lerp(motionSample.PreviousPosition, motionSample.CurrentPosition, t);

			if (IsBodySliceInside(box, foot, playerRadius))
				return true;
		}

		return false;
	}

	private static bool IsBodySliceInside(BoxCollider box, Vector3 foot, float playerRadius)
	{
		foreach (float height in VerticalSampleHeights)
		{
			if (Contains(box, foot + Vector3.up * height, playerRadius))
				return true;
		}

		return false;
	}

	private static bool Contains(BoxCollider box, Vector3 worldPoint, float playerRadius)
	{
		// InverseTransformPoint divides out position, rotation and (parent) scale, so comparing against
		// the local half-size is a correct oriented-bounding-box test for any transform hierarchy.
		Vector3 local = box.transform.InverseTransformPoint(worldPoint) - box.center;
		Vector3 half = box.size * 0.5f;
		if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z)
			return true;

		Vector3 closestPoint = box.ClosestPoint(worldPoint);
		return (closestPoint - worldPoint).sqrMagnitude <= playerRadius * playerRadius;
	}

	private static float GetPlayerRadius(IFpcRole fpcRole)
	{
		FirstPersonMovementModule module = fpcRole.FpcModule;
		if (module.CharControllerSet && module.CharController != null)
			return module.CharController.radius + module.CharController.skinWidth + PlayerRadiusPadding;

		return DefaultPlayerRadius + PlayerRadiusPadding;
	}

	private static PlayerMotionSample GetMotionSample(Player player)
	{
		ReferenceHub hub = player.ReferenceHub;
		float fixedTime = Time.fixedTime;
		Vector3 currentPosition = player.Position;

		if (!PlayerMotionSamples.TryGetValue(hub, out PlayerMotionSample motionSample))
		{
			motionSample = new PlayerMotionSample
			{
				FixedTime = fixedTime,
				PreviousPosition = currentPosition,
				CurrentPosition = currentPosition,
			};

			PlayerMotionSamples[hub] = motionSample;
			return motionSample;
		}

		if (Mathf.Approximately(motionSample.FixedTime, fixedTime))
			return motionSample;

		motionSample.PreviousPosition = motionSample.CurrentPosition;
		motionSample.CurrentPosition = currentPosition;
		motionSample.FixedTime = fixedTime;
		PlayerMotionSamples[hub] = motionSample;
		return motionSample;
	}

	private static void SetMotionSample(Player player, Vector3 position)
	{
		PlayerMotionSamples[player.ReferenceHub] = new PlayerMotionSample
		{
			FixedTime = Time.fixedTime,
			PreviousPosition = position,
			CurrentPosition = position,
		};
	}

	private static Vector3 GetExitPosition(Transform targetTransform, BoxCollider? targetCollider)
	{
		Vector3 forward = Quaternion.Euler(0f, targetTransform.eulerAngles.y, 0f) * Vector3.forward;
		Vector3 right = Quaternion.Euler(0f, targetTransform.eulerAngles.y, 0f) * Vector3.right;
		Vector3 origin = targetCollider != null ? targetCollider.bounds.center : targetTransform.position;
		float triggerExtent = targetCollider != null ? GetProjectedExtent(targetCollider.bounds.extents, forward) : 0f;

		return origin +
		       forward * Mathf.Max(ExitMargin, triggerExtent + ExitMargin) +
		       right * GetArrivalSideOffset(targetTransform.GetInstanceID());
	}

	private static float GetProjectedExtent(Vector3 extents, Vector3 direction)
	{
		return Mathf.Abs(direction.x) * extents.x +
		       Mathf.Abs(direction.y) * extents.y +
		       Mathf.Abs(direction.z) * extents.z;
	}

	private static float GetArrivalSideOffset(int targetId)
	{
		float now = Time.time;

		if (!ArrivalReservations.TryGetValue(targetId, out ArrivalReservation reservation) ||
		    now - reservation.LastUseTime > ArrivalSlotWindow)
		{
			reservation = new ArrivalReservation();
		}

		int slot = reservation.NextSlot++;
		reservation.LastUseTime = now;
		ArrivalReservations[targetId] = reservation;

		if (slot == 0)
			return 0f;

		int direction = (slot & 1) == 1 ? 1 : -1;
		int distanceStep = (slot + 1) / 2;
		return direction * distanceStep * ArrivalSlotSpacing;
	}

	private struct ArrivalReservation
	{
		public float LastUseTime;
		public int NextSlot;
	}

	private struct PlayerMotionSample
	{
		public float FixedTime;
		public Vector3 PreviousPosition;
		public Vector3 CurrentPosition;
	}
}

/// <summary>
/// Abstraction over the two teleporter components (<see cref="TeleportObject"/> for the map editor and
/// <see cref="SchematicTeleportObject"/> for compiled schematics) so <see cref="TeleportHelper.Tick"/>
/// can drive both.
/// </summary>
internal interface ITeleporter
{
	Transform Transform { get; }

	BoxCollider? Trigger { get; }

	float TeleportCooldown { get; }

	HashSet<ReferenceHub> IgnoredPlayers { get; }

	ITeleporter? GetTarget();
}
