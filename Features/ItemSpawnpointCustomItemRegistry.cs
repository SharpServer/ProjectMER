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

public delegate bool CustomItemTypeResolver(string customItemKey, out ItemType baseType);

public static class ItemSpawnpointCustomItemRegistry
{
	private static readonly List<Provider> Providers = [];

	public static void Register(ItemSpawnpointCustomItemSpawner spawner, ItemSpawnpointCustomItemGiver giver)
		=> Register(spawner, giver, null);

	public static void Register(ItemSpawnpointCustomItemSpawner spawner, ItemSpawnpointCustomItemGiver giver,
		CustomItemTypeResolver? typeResolver)
	{
		if (Providers.Any(provider => provider.Spawner == spawner && provider.Giver == giver))
			return;

		Providers.Add(new Provider(spawner, giver, typeResolver));
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

	public static bool TryGetBaseType(string key, out ItemType baseType)
	{
		baseType = ItemType.None;
		foreach (Provider provider in Providers.ToList())
		{
			try
			{
				if (provider.TypeResolver != null && provider.TypeResolver(key, out baseType)) return true;
			}
			catch (Exception exception) { Logger.Warn($"Custom item metadata for '{key}' failed: {exception}"); }
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

	private readonly struct Provider(ItemSpawnpointCustomItemSpawner spawner, ItemSpawnpointCustomItemGiver giver,
		CustomItemTypeResolver? typeResolver)
	{
		public ItemSpawnpointCustomItemSpawner Spawner { get; } = spawner;

		public ItemSpawnpointCustomItemGiver Giver { get; } = giver;

		public CustomItemTypeResolver? TypeResolver { get; } = typeResolver;
	}
}
