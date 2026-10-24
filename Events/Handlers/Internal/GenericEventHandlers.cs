using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.CustomHandlers;
using MEC;
using ProjectMER.Features;
using ProjectMER.Features.Objects;
using ProjectMER.Features.Serializable;
using ProjectMER.Features.ToolGun;

namespace ProjectMER.Events.Handlers.Internal;

public class GenericEventsHandler : CustomEventsHandler
{
	public override void OnServerWaitingForPlayers()
	{
		// 前ラウンドの内部状態を破棄してから、新しい世代を開始する。
		CullingManager.ResetRoundState();
		// WaitingForPlayers is also reached after forced/admin restarts where RoundEnded may not
		// have completed. The server tears down scene objects; only stop pending work and forget
		// references here so already-destroyed Unity objects are never destroyed a second time.
		MapUtils.ResetRoundState();
		PrimitiveOptimizationManager.Stop();

		PrefabManager.RegisterPrefabs();

		ToolGunItem.ItemDictionary.Clear();
		ToolGunHandler.PlayerSelectedObjectDict.Clear();
		PickupEventsHandler.ButtonPickups.Clear();
		PickupEventsHandler.PickupUsesLeft.Clear();
		PickupEventsHandler.CustomItemPickupUses.Clear();

		PrimitiveOptimizationManager.Start();
		CullingManager.Start();
	}

	public override void OnServerRoundEnded(RoundEndedEventArgs ev)
	{
		// Mirror の netId カウンタが次ラウンド用にリセットされる前に、
		// クライアント専用 Primitive の内部状態を停止する。シーン実体はサーバーの
		// ラウンド teardown が破棄するため、ProjectMER は参照だけを解放する。
		CullingManager.ResetRoundState();
		MapUtils.ResetRoundState();
		PrimitiveOptimizationManager.Stop();
	}

	public override void OnServerShutdown()
	{
		CullingManager.Stop();
		PrimitiveOptimizationManager.Stop();
	}

	public override void OnPlayerSpawning(PlayerSpawningEventArgs ev)
	{
		if (!ev.UseSpawnPoint)
			return;

		List<MapEditorObject> list = [];
		foreach (MapSchematic map in MapUtils.LoadedMaps.Values)
		{
			foreach (KeyValuePair<string, SerializablePlayerSpawnpoint> spawnpoint in map.PlayerSpawnpoints)
			{
				if (!spawnpoint.Value.Roles.Contains(ev.Role.RoleTypeId))
					continue;

				list.AddRange(map.SpawnedObjects.Where(x => x.Id == spawnpoint.Key));
			}
		}

		if (list.Count == 0)
			return;

		MapEditorObject randomElement = list[UnityEngine.Random.Range(0, list.Count)];

		ev.SpawnLocation = randomElement.transform.position;
		Timing.CallDelayed(0.05f, () =>
		{
			try
			{
				ev.Player.LookRotation = randomElement.transform.eulerAngles;
			}
			catch (Exception e)
			{
				Logger.Error(e);
			}
		});
	}

	public override void OnPlayerChangedSpectator(PlayerChangedSpectatorEventArgs ev)
	{
		CullingManager.RequestPlayerRescan(ev.Player);
	}

	public override void OnPlayerInteractingShootingTarget(PlayerInteractingShootingTargetEventArgs ev)
	{
		if (ev.ShootingTarget.GameObject.TryGetComponent(out MapEditorObject _))
			ev.IsAllowed = false;
	}
}
