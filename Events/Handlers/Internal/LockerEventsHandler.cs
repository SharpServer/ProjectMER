using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.CustomHandlers;
using LabApiLocker = LabApi.Features.Wrappers.Locker;
using NativeLocker = MapGeneration.Distributors.Locker;

namespace ProjectMER.Events.Handlers.Internal;

/// <summary>
/// InteractLock が指定されたロッカーの挙動を実装する。
/// 一度でも操作されたロッカーは、それ以降だれも操作できなくなる。
/// </summary>
public class LockerEventsHandler : CustomEventsHandler
{
	private static readonly HashSet<NativeLocker> InteractLockLockers = [];
	private static readonly HashSet<NativeLocker> LockedLockers = [];

	/// <summary>
	/// ロッカーを「一度操作したらロックされる」対象として登録する。
	/// </summary>
	internal static void RegisterInteractLock(NativeLocker locker)
	{
		if (locker == null)
			return;

		// 破棄済みロッカーの残骸を貯め込まないよう、登録のたびに掃除する
		InteractLockLockers.RemoveWhere(entry => entry == null);
		LockedLockers.RemoveWhere(entry => entry == null);

		InteractLockLockers.Add(locker);
		LockedLockers.Remove(locker);
	}

	/// <summary>ロッカーの InteractLock 登録を解除する。</summary>
	internal static void UnregisterInteractLock(NativeLocker locker)
	{
		if (locker == null)
			return;

		InteractLockLockers.Remove(locker);
		LockedLockers.Remove(locker);
	}

	public override void OnPlayerInteractingLocker(PlayerInteractingLockerEventArgs ev)
	{
		if (TryGetBase(ev.Locker, out NativeLocker locker) && LockedLockers.Contains(locker))
			ev.IsAllowed = false;
	}

	public override void OnPlayerInteractedLocker(PlayerInteractedLockerEventArgs ev)
	{
		if (!ev.CanOpen || !TryGetBase(ev.Locker, out NativeLocker locker))
			return;

		if (InteractLockLockers.Contains(locker))
			LockedLockers.Add(locker);
	}

	private static bool TryGetBase(LabApiLocker? wrapper, out NativeLocker locker)
	{
		locker = wrapper?.Base!;
		return locker != null;
	}
}
