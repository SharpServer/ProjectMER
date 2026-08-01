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
		// 前ラウンドの synthetic netId を先に破棄してから、新しい世代を開始する。
		CullingManager.Stop();
		PrimitiveOptimizationManager.Stop();

		PrefabManager.RegisterPrefabs();

		MapUtils.LoadedMaps.Clear();
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
		// クライアント専用 Primitive を全クライアントから除去する。RoundEnding は
		// キャンセル可能なので、確定後の RoundEnded でマップ自体も明示的に破棄する。
		CullingManager.Stop();
		foreach (string mapName in MapUtils.LoadedMaps.Keys.ToArray())
			MapUtils.UnloadMap(mapName);
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

	public override void OnPlayerInteractingShootingTarget(PlayerInteractingShootingTargetEventArgs ev)
	{
		if (ev.ShootingTarget.GameObject.TryGetComponent(out MapEditorObject _))
			ev.IsAllowed = false;
	}
}
