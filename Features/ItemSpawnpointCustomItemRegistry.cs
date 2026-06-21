using System;
using System.Collections.Generic;
using System.Linq;
using InventorySystem.Items.Pickups;
using LabApi.Features.Wrappers;
using ProjectMER.Features.Serializable;
using UnityEngine;

namespace ProjectMER.Features;

public delegate bool ItemSpawnpointCustomItemSpawner(
	string customItemKey,
	SerializableItemSpawnpoint spawnpoint,
	Vector3 position,
	Quaternion rotation,
	Transform parent,
	out ItemPickupBase? pickup);

public delegate bool ItemSpawnpointCustomItemGiver(
	string customItemKey,
	ItemPickupBase pickup,
	Player player);

public static class ItemSpawnpointCustomItemRegistry
{
	private static readonly List<Provider> Providers = [];

	public static void Register(ItemSpawnpointCustomItemSpawner spawner, ItemSpawnpointCustomItemGiver giver)
	{
		if (Providers.Any(provider => provider.Spawner == spawner && provider.Giver == giver))
			return;

		Providers.Add(new Provider(spawner, giver));
	}

	public static void Unregister(ItemSpawnpointCustomItemSpawner spawner, ItemSpawnpointCustomItemGiver giver)
	{
		Providers.RemoveAll(provider => provider.Spawner == spawner && provider.Giver == giver);
	}

	public static bool TrySpawn(
		string customItemKey,
		SerializableItemSpawnpoint spawnpoint,
		Vector3 position,
		Quaternion rotation,
		Transform parent,
		out ItemPickupBase? pickup)
	{
		pickup = null;
		if (string.IsNullOrWhiteSpace(customItemKey))
			return false;

		foreach (Provider provider in Providers.ToList())
		{
			try
			{
				if (provider.Spawner(customItemKey, spawnpoint, position, rotation, parent, out pickup) && pickup != null)
					return true;
			}
			catch (Exception ex)
			{
				Logger.Warn($"Custom ItemSpawnpoint provider failed to spawn '{customItemKey}': {ex}");
			}
		}

		return false;
	}

	public static bool TryGive(string customItemKey, ItemPickupBase pickup, Player player)
	{
		if (string.IsNullOrWhiteSpace(customItemKey))
			return false;

		foreach (Provider provider in Providers.ToList())
		{
			try
			{
				if (provider.Giver(customItemKey, pickup, player))
					return true;
			}
			catch (Exception ex)
			{
				Logger.Warn($"Custom ItemSpawnpoint provider failed to give '{customItemKey}': {ex}");
			}
		}

		return false;
	}

	private readonly struct Provider(ItemSpawnpointCustomItemSpawner spawner, ItemSpawnpointCustomItemGiver giver)
	{
		public ItemSpawnpointCustomItemSpawner Spawner { get; } = spawner;

		public ItemSpawnpointCustomItemGiver Giver { get; } = giver;
	}
}
