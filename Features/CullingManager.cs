using System;
using System.Collections.Generic;
using CentralAuth;
using LabApi.Features.Wrappers;
using MEC;
using Mirror;
using ProjectMER.Features.Objects;
using ProjectMER.Features.Serializable.Schematics;
using UnityEngine;

namespace ProjectMER.Features;

/// <summary>
/// Applies distance/LOD visibility to native network identities and compact client primitives.
/// Unity and Mirror operations stay on the main thread; registration groups many objects behind a
/// single distance check and a global, fair send budget.
/// </summary>
public static class CullingManager
{
    private sealed class CullEntry
    {
        public NetworkIdentity? Identity;
        public ClientPrimitive? ClientPrimitive;
        public int Order;

        public bool IsPrimitive => ClientPrimitive != null;
        public bool IsAlive => Identity != null || ClientPrimitive is { IsDestroyed: false };
    }

    private sealed class CullGroup
    {
        public string Key = string.Empty;
        public Transform? Anchor;
        public Vector3 LocalAnchor;
        public bool UsesLocalAnchor;
        public bool IsPrimitiveGroup;
        public bool AlwaysVisible;
        public SpatialCell? Cell;
        public bool HasCollidablePrimitive;
        public float DrawPriority;
        public SchematicObject? Owner;
        public float HighDetailDistance;
        public float MediumDetailDistance;
        public float CullingDistance;
        public float Hysteresis;
        public bool PersistLowestLod;
        public float HardCullDistance;
        public readonly Dictionary<int, List<CullEntry>> Lods = [];

        public Vector3 AnchorPosition
        {
            get
            {
                if (Anchor == null)
                    return Vector3.zero;
                return UsesLocalAnchor ? Anchor.TransformPoint(LocalAnchor) : Anchor.position;
            }
        }

        public int LowestLod
        {
            get
            {
                int result = -1;
                foreach (int lod in Lods.Keys)
                    result = Math.Max(result, lod);
                return result;
            }
        }

        public IReadOnlyList<CullEntry> GetLod(int lod)
        {
            if (lod < 0 || !Lods.TryGetValue(lod, out List<CullEntry>? entries))
                return Array.Empty<CullEntry>();

            entries.RemoveAll(entry => !entry.IsAlive);
            entries.Sort((left, right) => left.Order.CompareTo(right.Order));
            return entries;
        }

        public int SelectLod(float squaredDistance, int currentLod)
        {
            if (AlwaysVisible)
                return Lods.ContainsKey(0) ? 0 : LowestLod;

            float limit = CullingDistance + (currentLod >= 0 ? Hysteresis : 0f);
            if (squaredDistance > limit * limit)
            {
                if (PersistLowestLod && Lods.Count > 1)
                {
                    float hardLimit = Mathf.Max(HardCullDistance, limit);
                    if (squaredDistance <= hardLimit * hardLimit)
                        return LowestLod;
                }
                return -1;
            }

            int requested;
            if (currentLod == 0 && IsWithin(squaredDistance, HighDetailDistance + Hysteresis))
                requested = 0;
            else if (currentLod == 1 && IsWithin(squaredDistance, MediumDetailDistance + Hysteresis))
                requested = IsWithin(squaredDistance, HighDetailDistance) ? 0 : 1;
            else
                requested = IsWithin(squaredDistance, HighDetailDistance)
                    ? 0
                    : IsWithin(squaredDistance, MediumDetailDistance) ? 1 : 2;

            if (Lods.ContainsKey(requested))
                return requested;
            if (Lods.ContainsKey(0))
                return 0;

            int nearest = -1;
            int nearestDelta = int.MaxValue;
            foreach (int lod in Lods.Keys)
            {
                int delta = Math.Abs(lod - requested);
                if (delta >= nearestDelta)
                    continue;
                nearest = lod;
                nearestDelta = delta;
            }
            return nearest;
        }

        private static bool IsWithin(float squaredDistance, float distance) =>
            squaredDistance <= distance * distance;
    }

    private sealed class ViewState
    {
        public int CurrentLod = -1;
        public int TargetLod = -1;
        public int RequestedLod = -1;
        public int ShowIndex;
        public int HideIndex;
        public IReadOnlyList<CullEntry> Showing = Array.Empty<CullEntry>();
        public IReadOnlyList<CullEntry> Hiding = Array.Empty<CullEntry>();
        public bool IsTransitioning;
        public float LastSquaredDistance = float.PositiveInfinity;
        public int PendingDowngradeLod = NoPendingDowngrade;
        public float PendingDowngradeSince;
    }

    private sealed class PlayerState
    {
        public readonly Dictionary<CullGroup, ViewState> Views = [];
        public readonly HashSet<CullGroup> ScanGroups = [];
        public readonly List<TransitionWork> Transitions = [];
        public readonly List<CullGroup> StaleGroups = [];
    }

    private sealed class OwnerIndexState
    {
        public SchematicObject Owner = null!;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public readonly List<CullGroup> Groups = [];
    }

    private readonly struct TransitionWork
    {
        public readonly CullGroup Group;
        public readonly ViewState State;

        public TransitionWork(CullGroup group, ViewState state)
        {
            Group = group;
            State = state;
        }
    }

    private readonly struct SpatialCell : IEquatable<SpatialCell>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;

        public SpatialCell(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(SpatialCell other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object? obj) => obj is SpatialCell other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X;
                hash = (hash * 397) ^ Y;
                return (hash * 397) ^ Z;
            }
        }
    }

    private static readonly Dictionary<string, CullGroup> Groups = [];
    private static readonly Dictionary<NetworkIdentity, CullGroup> IdentityGroups = [];
    private static readonly Dictionary<ClientPrimitive, CullGroup> ClientPrimitiveGroups = [];
    private static readonly Dictionary<NetworkConnectionToClient, PlayerState> PlayerStates = [];
    private static readonly Dictionary<SpatialCell, HashSet<CullGroup>> SpatialBuckets = [];
    private static readonly Dictionary<SchematicObject, OwnerIndexState> OwnerIndexes = [];
    private static readonly HashSet<CullGroup> DynamicGroups = [];
    private static readonly HashSet<CullGroup> AlwaysVisibleGroups = [];
    private static readonly List<(Player Player, NetworkConnectionToClient Connection)> ReadyPlayers = [];
    private static readonly HashSet<NetworkConnectionToClient> AliveConnections = [];
    private static readonly List<NetworkConnectionToClient> DisconnectedConnections = [];
	private static readonly List<CullGroup> GroupScratch = [];
	private static readonly List<SchematicObject> OwnerScratch = [];
	private static readonly Dictionary<string, float> PrimitiveSchematicDistances = new(StringComparer.OrdinalIgnoreCase);

    private const int NoPendingDowngrade = int.MinValue;
    private static CoroutineHandle _cullingCoroutine;
    private static bool _running;
    private static bool _textEnabled;
    private static bool _primitiveEnabled;
    private static float _defaultDistance;
    private static float _primitiveDistance;
    private static float _defaultHysteresis;
    private static float _updateInterval;
    private static float _sendInterval;
    private static int _objectsPerUpdate;
    private static int _primitiveObjectsPerUpdate;
    private static int _globalObjectsPerUpdate;
    private static int _globalPrimitiveObjectsPerUpdate;
    private static float _downgradeDelay;
    private static bool _persistLowestLod;
    private static float _defaultHardCullDistance;
    private static float _spatialCellSize;
    private static float _maxSpatialDistance;
    private static float _nextErrorLogTime;
    private static int _playerCursor;
    private static int _nextPrimitiveGroupId;

    public static void PrepareStandalone(NetworkIdentity identity)
    {
        if (!_textEnabled || identity == null)
            return;

        CullGroup group = new()
        {
            Key = $"standalone:{identity.GetInstanceID()}",
            Anchor = identity.transform,
            HighDetailDistance = _defaultDistance,
            MediumDetailDistance = _defaultDistance,
            CullingDistance = _defaultDistance,
            Hysteresis = _defaultHysteresis,
            PersistLowestLod = _persistLowestLod,
            HardCullDistance = _defaultHardCullDistance,
        };
        RegisterGroup(group);
        AddIdentity(group, identity, 0, 0);
    }

    public static void PrepareSchematicText(SchematicObject schematic, SchematicBlockData block, NetworkIdentity identity)
    {
        if (!_textEnabled || schematic == null || block == null || identity == null)
            return;

        Dictionary<string, object> properties = block.Properties ?? [];
        string imageGroup = GetString(properties, "ImageGroup");
        string text = GetString(properties, "Text");
        if (string.IsNullOrWhiteSpace(imageGroup) && text.Length < 2048)
            return;

        string keyPart = string.IsNullOrWhiteSpace(imageGroup) ? $"legacy:{block.ParentId}" : imageGroup;
        string key = $"schematic:{schematic.GetInstanceID()}:{keyPart}";
        if (!Groups.TryGetValue(key, out CullGroup? group))
        {
            float cullingDistance = Math.Max(0.1f, GetFloat(properties, "ImageCullingDistance", _defaultDistance));
            float hardCullDistance = Math.Max(
                cullingDistance,
                GetFloat(properties, "ImageHardCullDistance", _defaultHardCullDistance));
            group = new CullGroup
            {
                Key = key,
                Anchor = schematic.transform,
                LocalAnchor = schematic.transform.InverseTransformPoint(identity.transform.position),
                UsesLocalAnchor = true,
                Owner = schematic,
                HighDetailDistance = Math.Max(0.1f, GetFloat(properties, "ImageHighDetailDistance", _defaultDistance)),
                MediumDetailDistance = Math.Max(0.1f, GetFloat(properties, "ImageMediumDetailDistance", _defaultDistance)),
                CullingDistance = cullingDistance,
                Hysteresis = Math.Max(0f, GetFloat(properties, "ImageCullingHysteresis", _defaultHysteresis)),
                PersistLowestLod = _persistLowestLod,
                HardCullDistance = hardCullDistance,
            };
            RegisterGroup(group);
        }

        AddIdentity(group, identity, GetInt(properties, "ImageLod", 0), GetInt(properties, "ImageTileOrder", block.ObjectId));
    }

    /// <summary>Registers one worker-planned cluster on the main thread.</summary>
    internal static void PrepareClientPrimitives(
        SchematicObject owner,
        IReadOnlyList<ClientPrimitive> primitives,
        Vector3 localCenter,
        float radius,
        bool alwaysVisible)
    {
        if (!_primitiveEnabled || owner == null || primitives == null || primitives.Count == 0)
            return;

		float baseDistance = PrimitiveSchematicDistances.TryGetValue(owner.Name ?? string.Empty, out float configuredDistance)
			? Math.Max(0.1f, configuredDistance)
			: _primitiveDistance;
		float effectiveDistance = baseDistance + Math.Max(0f, radius);
        CullGroup group = new()
        {
            Key = $"primitive:{owner.GetInstanceID()}:{_nextPrimitiveGroupId++}",
            Anchor = owner.transform,
            LocalAnchor = localCenter,
            UsesLocalAnchor = true,
            IsPrimitiveGroup = true,
            AlwaysVisible = alwaysVisible,
            Owner = owner,
            HighDetailDistance = effectiveDistance,
            MediumDetailDistance = effectiveDistance,
            CullingDistance = effectiveDistance,
            Hysteresis = _defaultHysteresis,
            HardCullDistance = effectiveDistance,
        };

        List<CullEntry> entries = [];
        group.Lods[0] = entries;
        foreach (ClientPrimitive primitive in primitives)
        {
            if (primitive == null || primitive.IsDestroyed)
                continue;
            entries.Add(new CullEntry
            {
                ClientPrimitive = primitive,
                Order = primitive.Order,
            });
            ClientPrimitiveGroups[primitive] = group;
            if (primitive.IsCollidable)
                group.HasCollidablePrimitive = true;
            group.DrawPriority = Math.Max(group.DrawPriority, primitive.CollisionPriority);
        }

        if (entries.Count == 0)
            return;
        RegisterGroup(group);
    }

    public static void RegisterCullable(NetworkIdentity identity) => PrepareStandalone(identity);

    public static bool IsCullable(NetworkIdentity identity) => identity != null && IdentityGroups.ContainsKey(identity);

    public static void UnregisterCullable(NetworkIdentity identity)
    {
        if (identity == null || !IdentityGroups.TryGetValue(identity, out CullGroup? group))
            return;

        IdentityGroups.Remove(identity);
        foreach (List<CullEntry> entries in group.Lods.Values)
            entries.RemoveAll(entry => entry.Identity == null || entry.Identity == identity);
        if (IsEmpty(group))
            RemoveGroup(group);
    }

    internal static void UnregisterClientPrimitive(ClientPrimitive primitive)
    {
        if (primitive == null || !ClientPrimitiveGroups.TryGetValue(primitive, out CullGroup? group))
            return;

        ClientPrimitiveGroups.Remove(primitive);
        foreach (NetworkConnectionToClient connection in PlayerStates.Keys)
            primitive.Hide(connection);
        foreach (List<CullEntry> entries in group.Lods.Values)
            entries.RemoveAll(entry => entry.ClientPrimitive == null || entry.ClientPrimitive == primitive);
        if (IsEmpty(group))
            RemoveGroup(group);
    }

    public static void UnregisterSchematic(SchematicObject schematic)
    {
        if (schematic == null)
            return;
        GroupScratch.Clear();
        foreach (CullGroup group in Groups.Values)
            if (group.Owner == schematic)
                GroupScratch.Add(group);
        foreach (CullGroup group in GroupScratch)
            RemoveGroup(group);
        GroupScratch.Clear();
    }

    public static void Start()
    {
        Stop();
        if (ProjectMER.Singleton?.Config == null)
            return;

        var config = ProjectMER.Singleton.Config;
        _textEnabled = config.EnableCulling && config.EnableTextToyOptimization;
        _primitiveEnabled = config.EnableCulling && config.EnablePrimitiveOptimization;
        _running = _textEnabled || _primitiveEnabled;
        _defaultDistance = Math.Max(0.1f, config.CullingDistance);
		_primitiveDistance = Math.Max(0.1f, config.PrimitiveCullingDistance);
		PrimitiveSchematicDistances.Clear();
		foreach (KeyValuePair<string, float> pair in config.PrimitiveSchematicCullingDistances ?? [])
		{
			if (!string.IsNullOrWhiteSpace(pair.Key))
				PrimitiveSchematicDistances[pair.Key.Trim()] = Math.Max(0.1f, pair.Value);
		}
        _defaultHysteresis = Math.Max(0f, config.CullingHysteresis);
        _updateInterval = Math.Max(0.05f, config.CullingUpdateInterval);
        _sendInterval = Math.Max(0.02f, config.CullingSendInterval);
        _objectsPerUpdate = Math.Max(1, config.CullingObjectsPerUpdate);
        _primitiveObjectsPerUpdate = Math.Max(1, config.PrimitiveObjectsPerUpdate);
        _globalObjectsPerUpdate = Math.Max(_objectsPerUpdate, config.CullingGlobalObjectsPerUpdate);
        _globalPrimitiveObjectsPerUpdate = Math.Max(_primitiveObjectsPerUpdate, config.PrimitiveGlobalObjectsPerUpdate);
        _downgradeDelay = Math.Max(0f, config.CullingDowngradeDelay);
        _persistLowestLod = config.CullingPersistLowestLod;
        _defaultHardCullDistance = Math.Max(_defaultDistance, config.CullingHardCullDistance);
        _spatialCellSize = Math.Max(10f, config.CullingSpatialCellSize);
        if (_running)
            _cullingCoroutine = Timing.RunCoroutine(CullingLoop());
    }

    public static void Stop()
    {
        _running = false;
        Timing.KillCoroutines(_cullingCoroutine);
        _cullingCoroutine = default;
        RestoreAndClear();
        _textEnabled = false;
        _primitiveEnabled = false;
    }

    private static void RegisterGroup(CullGroup group)
    {
        Groups[group.Key] = group;
        if (group.AlwaysVisible)
        {
            AlwaysVisibleGroups.Add(group);
            return;
        }

        _maxSpatialDistance = Math.Max(_maxSpatialDistance, group.CullingDistance + group.Hysteresis);
        if (group.Owner == null)
        {
            DynamicGroups.Add(group);
            return;
        }

        if (!OwnerIndexes.TryGetValue(group.Owner, out OwnerIndexState? ownerState))
        {
            ownerState = new OwnerIndexState
            {
                Owner = group.Owner,
                Position = group.Owner.transform.position,
                Rotation = group.Owner.transform.rotation,
                Scale = group.Owner.transform.lossyScale,
            };
            OwnerIndexes[group.Owner] = ownerState;
        }
        ownerState.Groups.Add(group);
        AddToSpatialBucket(group);
    }

    private static void AddIdentity(CullGroup group, NetworkIdentity identity, int lod, int order)
    {
        identity.visible = Visibility.ForceHidden;
        if (!group.Lods.TryGetValue(lod, out List<CullEntry>? entries))
            group.Lods[lod] = entries = [];
        entries.Add(new CullEntry { Identity = identity, Order = order });
        IdentityGroups[identity] = group;
    }

    private static void AddToSpatialBucket(CullGroup group)
    {
        if (group.Anchor == null)
            return;
        SpatialCell cell = GetSpatialCell(group.AnchorPosition);
        if (!SpatialBuckets.TryGetValue(cell, out HashSet<CullGroup>? bucket))
            SpatialBuckets[cell] = bucket = [];
        bucket.Add(group);
        group.Cell = cell;
    }

    private static void RemoveFromSpatialBucket(CullGroup group)
    {
        if (!group.Cell.HasValue)
            return;
        SpatialCell cell = group.Cell.Value;
        if (SpatialBuckets.TryGetValue(cell, out HashSet<CullGroup>? bucket))
        {
            bucket.Remove(group);
            if (bucket.Count == 0)
                SpatialBuckets.Remove(cell);
        }
        group.Cell = null;
    }

    private static SpatialCell GetSpatialCell(Vector3 position) => new(
        Mathf.FloorToInt(position.x / _spatialCellSize),
        Mathf.FloorToInt(position.y / _spatialCellSize),
        Mathf.FloorToInt(position.z / _spatialCellSize));

    private static bool IsEmpty(CullGroup group)
    {
        foreach (List<CullEntry> entries in group.Lods.Values)
            if (entries.Count > 0)
                return false;
        return true;
    }

    private static void RemoveGroup(CullGroup group)
    {
        if (!Groups.Remove(group.Key))
            return;

        AlwaysVisibleGroups.Remove(group);
        DynamicGroups.Remove(group);
        RemoveFromSpatialBucket(group);
        if (group.Owner != null && OwnerIndexes.TryGetValue(group.Owner, out OwnerIndexState? ownerState))
        {
            ownerState.Groups.Remove(group);
            if (ownerState.Groups.Count == 0)
                OwnerIndexes.Remove(group.Owner);
        }

        foreach (List<CullEntry> entries in group.Lods.Values)
        {
            foreach (CullEntry entry in entries)
            {
                if (entry.Identity != null)
                    IdentityGroups.Remove(entry.Identity);
                if (entry.ClientPrimitive == null)
                    continue;
                ClientPrimitiveGroups.Remove(entry.ClientPrimitive);
                foreach (NetworkConnectionToClient connection in PlayerStates.Keys)
                    entry.ClientPrimitive.Hide(connection);
            }
        }

        foreach (PlayerState playerState in PlayerStates.Values)
            playerState.Views.Remove(group);
    }

    private static IEnumerator<float> CullingLoop()
    {
        float nextScan = 0f;
        while (_running)
        {
            float now = Time.realtimeSinceStartup;
            bool scan = now >= nextScan;
            if (scan)
            {
                nextScan = now + _updateInterval;
                RefreshMovedGroups();
            }

            UpdateAllPlayers(scan);
            yield return Timing.WaitForSeconds(_sendInterval);
        }
    }

    private static void UpdateAllPlayers(bool scan)
    {
        ReadyPlayers.Clear();
        foreach (Player player in Player.List)
        {
            if (TryGetReadyConnection(player, out NetworkConnectionToClient? connection))
                ReadyPlayers.Add((player, connection!));
        }
        AliveConnections.Clear();
        foreach ((Player _, NetworkConnectionToClient connection) in ReadyPlayers)
            AliveConnections.Add(connection);
        DisconnectedConnections.Clear();
        foreach (NetworkConnectionToClient connection in PlayerStates.Keys)
            if (!AliveConnections.Contains(connection))
                DisconnectedConnections.Add(connection);
        foreach (NetworkConnectionToClient connection in DisconnectedConnections)
        {
            foreach (ClientPrimitive primitive in ClientPrimitiveGroups.Keys)
                primitive.Hide(connection);
            PlayerStates.Remove(connection);
        }
        DisconnectedConnections.Clear();

        if (ReadyPlayers.Count == 0)
            return;

        int identityGlobalBudget = _globalObjectsPerUpdate;
        int primitiveGlobalBudget = _globalPrimitiveObjectsPerUpdate;
        int start = _playerCursor % ReadyPlayers.Count;
        for (int offset = 0; offset < ReadyPlayers.Count; offset++)
        {
            (Player player, NetworkConnectionToClient connection) = ReadyPlayers[(start + offset) % ReadyPlayers.Count];
            try
            {
                UpdatePlayer(player, connection, scan, ref identityGlobalBudget, ref primitiveGlobalBudget);
            }
            catch (Exception exception)
            {
                if (Time.realtimeSinceStartup < _nextErrorLogTime)
                    continue;
                _nextErrorLogTime = Time.realtimeSinceStartup + 30f;
                Logger.Error($"[CullingManager] Update failed (further errors suppressed for 30s): {exception}");
            }
        }
        _playerCursor = (start + 1) % ReadyPlayers.Count;
    }

	private static void RefreshMovedGroups()
	{
		OwnerScratch.Clear();
        foreach (SchematicObject owner in OwnerIndexes.Keys)
            OwnerScratch.Add(owner);

        foreach (SchematicObject owner in OwnerScratch)
        {
            if (!OwnerIndexes.TryGetValue(owner, out OwnerIndexState? state))
                continue;
            if (owner == null)
            {
                foreach (CullGroup group in state.Groups.ToArray())
                    RemoveGroup(group);
                OwnerIndexes.Remove(owner!);
                continue;
            }
            Transform tf = owner.transform;
            if ((tf.position - state.Position).sqrMagnitude <= 0.0001f &&
                Quaternion.Angle(tf.rotation, state.Rotation) <= 0.05f &&
                (tf.lossyScale - state.Scale).sqrMagnitude <= 0.0001f)
                continue;

            foreach (CullGroup group in state.Groups)
            {
                RemoveFromSpatialBucket(group);
                AddToSpatialBucket(group);
            }
            state.Position = tf.position;
            state.Rotation = tf.rotation;
            state.Scale = tf.lossyScale;
        }
        OwnerScratch.Clear();
    }

    private static void UpdatePlayer(
        Player player,
        NetworkConnectionToClient connection,
        bool scan,
        ref int identityGlobalBudget,
        ref int primitiveGlobalBudget)
    {
        if (!PlayerStates.TryGetValue(connection, out PlayerState? playerState))
            PlayerStates[connection] = playerState = new PlayerState();

        if (scan)
            ScanPlayer(player, playerState);

        playerState.Transitions.Clear();
        foreach (KeyValuePair<CullGroup, ViewState> pair in playerState.Views)
            if (pair.Value.IsTransitioning)
                playerState.Transitions.Add(new TransitionWork(pair.Key, pair.Value));
        playerState.Transitions.Sort(CompareTransitions);

        int identityBudget = Math.Min(_objectsPerUpdate, identityGlobalBudget);
        int primitiveBudget = Math.Min(_primitiveObjectsPerUpdate, primitiveGlobalBudget);
        foreach (TransitionWork transition in playerState.Transitions)
        {
            if (transition.Group.IsPrimitiveGroup)
            {
                if (primitiveBudget <= 0)
                    continue;
                int used = ProcessTransition(transition.Group, transition.State, connection, primitiveBudget);
                primitiveBudget -= used;
                primitiveGlobalBudget -= used;
            }
            else
            {
                if (identityBudget <= 0)
                    continue;
                int used = ProcessTransition(transition.Group, transition.State, connection, identityBudget);
                identityBudget -= used;
                identityGlobalBudget -= used;
            }
            if (identityBudget <= 0 && primitiveBudget <= 0)
                break;
        }
    }

    private static void ScanPlayer(Player player, PlayerState playerState)
    {
        HashSet<CullGroup> scanGroups = playerState.ScanGroups;
        scanGroups.Clear();
        scanGroups.UnionWith(AlwaysVisibleGroups);
        scanGroups.UnionWith(DynamicGroups);

        SpatialCell center = GetSpatialCell(player.Position);
        int radius = Mathf.CeilToInt(_maxSpatialDistance / _spatialCellSize) + 1;
        for (int x = center.X - radius; x <= center.X + radius; x++)
            for (int y = center.Y - radius; y <= center.Y + radius; y++)
                for (int z = center.Z - radius; z <= center.Z + radius; z++)
                    if (SpatialBuckets.TryGetValue(new SpatialCell(x, y, z), out HashSet<CullGroup>? bucket))
                        scanGroups.UnionWith(bucket);

        foreach (KeyValuePair<CullGroup, ViewState> pair in playerState.Views)
            if (pair.Value.CurrentLod >= 0 || pair.Value.TargetLod >= 0 || pair.Value.IsTransitioning)
                scanGroups.Add(pair.Key);

        float now = Time.realtimeSinceStartup;
        foreach (CullGroup group in scanGroups)
        {
            if (group.Anchor == null)
                continue;
            if (!playerState.Views.TryGetValue(group, out ViewState? state))
                playerState.Views[group] = state = new ViewState();

            float squaredDistance = group.AlwaysVisible
                ? 0f
                : (player.Position - group.AnchorPosition).sqrMagnitude;
            state.LastSquaredDistance = squaredDistance;
            int current = state.IsTransitioning ? state.TargetLod : state.CurrentLod;
            int requested = group.SelectLod(squaredDistance, current);
            if (!ShouldApplyRequest(state, current, requested, now))
                continue;

            if (!state.IsTransitioning && requested != state.CurrentLod)
                BeginTransition(group, state, requested);
            else if (state.IsTransitioning && requested != state.TargetLod)
                state.RequestedLod = requested;
        }

        playerState.StaleGroups.Clear();
        foreach (KeyValuePair<CullGroup, ViewState> pair in playerState.Views)
            if (!scanGroups.Contains(pair.Key) && !pair.Value.IsTransitioning && pair.Value.CurrentLod < 0)
                playerState.StaleGroups.Add(pair.Key);
        foreach (CullGroup stale in playerState.StaleGroups)
            playerState.Views.Remove(stale);
        playerState.StaleGroups.Clear();
    }

    private static int CompareTransitions(TransitionWork left, TransitionWork right)
    {
        bool leftShowing = left.State.TargetLod >= 0;
        bool rightShowing = right.State.TargetLod >= 0;
        if (leftShowing != rightShowing)
            return leftShowing ? -1 : 1;
        if (leftShowing && left.Group.IsPrimitiveGroup != right.Group.IsPrimitiveGroup)
            return left.Group.IsPrimitiveGroup ? -1 : 1;
        if (leftShowing && left.Group.IsPrimitiveGroup)
        {
            if (left.Group.HasCollidablePrimitive != right.Group.HasCollidablePrimitive)
                return left.Group.HasCollidablePrimitive ? -1 : 1;
            int priority = right.Group.DrawPriority.CompareTo(left.Group.DrawPriority);
            if (priority != 0)
                return priority;
        }
        return left.State.LastSquaredDistance.CompareTo(right.State.LastSquaredDistance);
    }

    private static bool ShouldApplyRequest(ViewState state, int current, int requested, float now)
    {
        if (!IsWorse(requested, current))
        {
            state.PendingDowngradeLod = NoPendingDowngrade;
            return true;
        }
        if (_downgradeDelay <= 0f)
            return true;
        if (state.PendingDowngradeLod != requested)
        {
            state.PendingDowngradeLod = requested;
            state.PendingDowngradeSince = now;
            return false;
        }
        if (now - state.PendingDowngradeSince < _downgradeDelay)
            return false;
        state.PendingDowngradeLod = NoPendingDowngrade;
        return true;
    }

    private static bool IsWorse(int requested, int current)
    {
        if (requested == current || current < 0)
            return false;
        if (requested < 0)
            return true;
        return requested > current;
    }

    private static void BeginTransition(CullGroup group, ViewState state, int targetLod)
    {
        state.TargetLod = targetLod;
        state.RequestedLod = targetLod;
        state.Showing = group.GetLod(targetLod);
        state.Hiding = group.GetLod(state.CurrentLod);
        state.ShowIndex = 0;
        state.HideIndex = 0;
        state.IsTransitioning = true;
    }

    private static int ProcessTransition(CullGroup group, ViewState state, NetworkConnectionToClient connection, int budget)
    {
        int processed = 0;
        while (processed < budget && state.ShowIndex < state.Showing.Count)
        {
            if (Show(state.Showing[state.ShowIndex++], connection))
                processed++;
        }
        while (processed < budget && state.ShowIndex >= state.Showing.Count && state.HideIndex < state.Hiding.Count)
        {
            if (Hide(state.Hiding[state.HideIndex++], connection))
                processed++;
        }
        if (state.ShowIndex >= state.Showing.Count && state.HideIndex >= state.Hiding.Count)
        {
            state.CurrentLod = state.TargetLod;
            state.IsTransitioning = false;
            if (state.RequestedLod != state.CurrentLod)
                BeginTransition(group, state, state.RequestedLod);
        }
        return processed;
    }

    private static bool Show(CullEntry entry, NetworkConnectionToClient connection)
    {
        if (entry.ClientPrimitive != null)
            return entry.ClientPrimitive.Show(connection);
        NetworkIdentity? identity = entry.Identity;
        if (identity == null || !identity.isServer || identity.observers.ContainsKey(connection.connectionId))
            return false;
        identity.AddObserver(connection);
        return true;
    }

    private static bool Hide(CullEntry entry, NetworkConnectionToClient connection)
    {
        if (entry.ClientPrimitive != null)
            return entry.ClientPrimitive.Hide(connection);
        NetworkIdentity? identity = entry.Identity;
        if (identity == null || !identity.isServer || !identity.observers.ContainsKey(connection.connectionId))
            return false;
        connection.RemoveFromObserving(identity, false);
        identity.RemoveObserver(connection);
        return true;
    }

    private static bool TryGetReadyConnection(Player player, out NetworkConnectionToClient? connection)
    {
        connection = null;
        try
        {
            ReferenceHub? hub = player.ReferenceHub;
            if (hub == null || hub.Mode != ClientInstanceMode.ReadyClient || hub.netId == 0 ||
                player.Connection is not NetworkConnectionToClient ready || !ready.isReady)
                return false;
            connection = ready;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RestoreAndClear()
    {
        foreach (NetworkIdentity identity in IdentityGroups.Keys)
        {
            if (identity == null)
                continue;
            identity.visible = Visibility.Default;
            if (NetworkServer.active && identity.isServer)
                NetworkServer.RebuildObservers(identity, true);
        }

        foreach (ClientPrimitive primitive in ClientPrimitiveGroups.Keys)
            foreach (NetworkConnectionToClient connection in PlayerStates.Keys)
                primitive.Hide(connection);

        Groups.Clear();
        IdentityGroups.Clear();
        ClientPrimitiveGroups.Clear();
        PlayerStates.Clear();
        SpatialBuckets.Clear();
        OwnerIndexes.Clear();
        DynamicGroups.Clear();
        AlwaysVisibleGroups.Clear();
        ReadyPlayers.Clear();
        AliveConnections.Clear();
        DisconnectedConnections.Clear();
		GroupScratch.Clear();
		OwnerScratch.Clear();
		PrimitiveSchematicDistances.Clear();
		_maxSpatialDistance = 0f;
        _nextErrorLogTime = 0f;
        _playerCursor = 0;
        _nextPrimitiveGroupId = 0;
    }

    private static string GetString(Dictionary<string, object> properties, string key) =>
        properties.TryGetValue(key, out object? value) ? Convert.ToString(value) ?? string.Empty : string.Empty;

    private static int GetInt(Dictionary<string, object> properties, string key, int fallback) =>
        properties.TryGetValue(key, out object? value) ? Convert.ToInt32(value) : fallback;

    private static float GetFloat(Dictionary<string, object> properties, string key, float fallback) =>
        properties.TryGetValue(key, out object? value) ? Convert.ToSingle(value) : fallback;
}
