using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Features.Wrappers;
using MEC;
using Mirror;
using ProjectMER.Features.Objects;
using ProjectMER.Features.Serializable.Schematics;
using UnityEngine;

namespace ProjectMER.Features;

public static class CullingManager
{
    private sealed class CullEntry
    {
        public NetworkIdentity Identity = null!;
        public int Order;
    }

    private sealed class CullGroup
    {
        public string Key = string.Empty;
        public Transform Anchor = null!;
        public SchematicObject? Owner;
        public float HighDetailDistance;
        public float MediumDetailDistance;
        public float CullingDistance;
        public float Hysteresis;
        public bool PersistLowestLod;
        public float HardCullDistance;
        public readonly Dictionary<int, List<CullEntry>> Lods = [];

        public Vector3 AnchorPosition => Anchor == null ? Vector3.zero : Anchor.position;

        // 最も detail の低い（数値が大きい）Lod キー。GetLod(-1) と衝突しないよう Lods が空なら -1。
        public int LowestLod => Lods.Count == 0 ? -1 : Lods.Keys.Max();

        public IReadOnlyList<CullEntry> GetLod(int lod)
        {
            if (lod < 0 || !Lods.TryGetValue(lod, out List<CullEntry>? entries))
                return Array.Empty<CullEntry>();
            entries.RemoveAll(entry => entry.Identity == null);
            entries.Sort((left, right) => left.Order.CompareTo(right.Order));
            return entries;
        }

        public int SelectLod(float squaredDistance, int currentLod)
        {
            float limit = CullingDistance + (currentLod >= 0 ? Hysteresis : 0f);
            if (squaredDistance > limit * limit)
            {
                // 完全非表示にする代わりに最低詳細 Lod を維持する。TextToy 画像の再表示は
                // 全文字列の再パース + 再メッシュ生成を伴い重いため、境界付近を往復する
                // プレイヤーに対してその再発生を避ける。複数 Lod を持つグループにのみ適用する
                // （単一 Lod のグループでは「最低詳細」が「フル詳細」と同じになってしまうため）。
                if (PersistLowestLod && Lods.Count > 1)
                {
                    float hardLimit = Mathf.Max(HardCullDistance, limit);
                    if (squaredDistance <= hardLimit * hardLimit)
                        return LowestLod;
                }

                return -1;
            }

            int requested;
            if (currentLod == 0 && squaredDistance <= (HighDetailDistance + Hysteresis) * (HighDetailDistance + Hysteresis))
                requested = 0;
            else if (currentLod == 1 && squaredDistance <= (MediumDetailDistance + Hysteresis) * (MediumDetailDistance + Hysteresis))
                requested = squaredDistance <= HighDetailDistance * HighDetailDistance ? 0 : 1;
            else if (currentLod == 2)
                requested = squaredDistance <= HighDetailDistance * HighDetailDistance
                    ? 0
                    : squaredDistance <= MediumDetailDistance * MediumDetailDistance ? 1 : 2;
            else
                requested = squaredDistance <= HighDetailDistance * HighDetailDistance
                    ? 0
                    : squaredDistance <= MediumDetailDistance * MediumDetailDistance ? 1 : 2;
            if (Lods.ContainsKey(requested))
                return requested;
            if (Lods.ContainsKey(0))
                return 0;
            return Lods.Count == 0 ? -1 : Lods.Keys.OrderBy(key => Math.Abs(key - requested)).First();
        }
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

        // ダウングレード（詳細度を下げる、または完全カリング）の保留状態。
        // NoPendingDowngrade のときは保留なし。
        public int PendingDowngradeLod = NoPendingDowngrade;
        public float PendingDowngradeSince;
    }

    private static readonly Dictionary<string, CullGroup> Groups = [];
    private static readonly Dictionary<NetworkIdentity, CullGroup> IdentityGroups = [];
    private static readonly Dictionary<NetworkConnectionToClient, Dictionary<CullGroup, ViewState>> PlayerStates = [];
    private static CoroutineHandle _cullingCoroutine;
    private static float _nextErrorLogTime;

    private static bool IsEnabled => ProjectMER.Singleton?.Config?.EnableCulling == true &&
        ProjectMER.Singleton.Config.EnableTextToyOptimization;
    private static float DefaultDistance => Math.Max(0.1f, ProjectMER.Singleton.Config!.CullingDistance);
    private static float DefaultHysteresis => Math.Max(0f, ProjectMER.Singleton.Config!.CullingHysteresis);
    private static float UpdateInterval => Math.Max(0.05f, ProjectMER.Singleton.Config!.CullingUpdateInterval);
    private static float SendInterval => Math.Max(0.02f, ProjectMER.Singleton.Config!.CullingSendInterval);
    private static int ObjectsPerUpdate => Math.Max(1, ProjectMER.Singleton.Config!.CullingObjectsPerUpdate);
    private static float DowngradeDelay => Math.Max(0f, ProjectMER.Singleton.Config!.CullingDowngradeDelay);
    private static bool PersistLowestLod => ProjectMER.Singleton.Config!.CullingPersistLowestLod;
    private static float DefaultHardCullDistance => Math.Max(DefaultDistance, ProjectMER.Singleton.Config!.CullingHardCullDistance);

    // ViewState.PendingDowngradeLod の「保留なし」を表す番兵値。
    private const int NoPendingDowngrade = int.MinValue;

    public static void PrepareStandalone(NetworkIdentity identity)
    {
        if (!IsEnabled || identity == null)
            return;

        string key = $"standalone:{identity.GetInstanceID()}";
        CullGroup group = new()
        {
            Key = key,
            Anchor = identity.transform,
            HighDetailDistance = DefaultDistance,
            MediumDetailDistance = DefaultDistance,
            CullingDistance = DefaultDistance,
            Hysteresis = DefaultHysteresis,
            PersistLowestLod = PersistLowestLod,
            HardCullDistance = DefaultHardCullDistance,
        };
        RegisterGroup(group);
        AddIdentity(group, identity, 0, 0);
    }

    public static void PrepareSchematicText(SchematicObject schematic, SchematicBlockData block, NetworkIdentity identity)
    {
        if (!IsEnabled || schematic == null || block == null || identity == null)
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
            group = new CullGroup
            {
                Key = key,
                Anchor = identity.transform,
                Owner = schematic,
                HighDetailDistance = GetFloat(properties, "ImageHighDetailDistance", DefaultDistance),
                MediumDetailDistance = GetFloat(properties, "ImageMediumDetailDistance", DefaultDistance),
                CullingDistance = GetFloat(properties, "ImageCullingDistance", DefaultDistance),
                Hysteresis = GetFloat(properties, "ImageCullingHysteresis", DefaultHysteresis),
                PersistLowestLod = PersistLowestLod,
                HardCullDistance = DefaultHardCullDistance,
            };
            RegisterGroup(group);
        }

        AddIdentity(group, identity, GetInt(properties, "ImageLod", 0), GetInt(properties, "ImageTileOrder", block.ObjectId));
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
        if (group.Lods.Values.All(entries => entries.Count == 0))
            RemoveGroup(group);
    }

    public static void UnregisterSchematic(SchematicObject schematic)
    {
        foreach (CullGroup group in Groups.Values.Where(group => group.Owner == schematic).ToArray())
            RemoveGroup(group);
    }

    public static void Start()
    {
        Stop();
        if (IsEnabled)
            _cullingCoroutine = Timing.RunCoroutine(CullingLoop());
    }

    public static void Stop()
    {
        Timing.KillCoroutines(_cullingCoroutine);
        _cullingCoroutine = default;
        RestoreAndClear();
    }

    private static void RegisterGroup(CullGroup group) => Groups[group.Key] = group;

    private static void AddIdentity(CullGroup group, NetworkIdentity identity, int lod, int order)
    {
        identity.visible = Visibility.ForceHidden;
        if (!group.Lods.TryGetValue(lod, out List<CullEntry>? entries))
            group.Lods[lod] = entries = [];
        entries.Add(new CullEntry { Identity = identity, Order = order });
        IdentityGroups[identity] = group;
    }

    private static void RemoveGroup(CullGroup group)
    {
        Groups.Remove(group.Key);
        foreach (List<CullEntry> entries in group.Lods.Values)
            foreach (CullEntry entry in entries)
                IdentityGroups.Remove(entry.Identity);
        foreach (Dictionary<CullGroup, ViewState> states in PlayerStates.Values)
            states.Remove(group);
    }

    private static IEnumerator<float> CullingLoop()
    {
        float nextScan = 0f;
        while (IsEnabled)
        {
            bool scan = Time.realtimeSinceStartup >= nextScan;
            if (scan)
                nextScan = Time.realtimeSinceStartup + UpdateInterval;

            foreach (Player player in Player.List)
            {
                if (player.Connection is not NetworkConnectionToClient connection || !connection.isReady)
                    continue;
                try
                {
                    UpdatePlayer(player, connection, scan);
                }
                catch (Exception e)
                {
                    if (Time.realtimeSinceStartup >= _nextErrorLogTime)
                    {
                        _nextErrorLogTime = Time.realtimeSinceStartup + 30f;
                        Logger.Error($"[CullingManager] Update failed (further errors suppressed for 30s): {e}");
                    }
                }
            }

            NetworkConnectionToClient[] disconnected = PlayerStates.Keys
                .Where(connection => connection == null || !connection.isReady)
                .ToArray();
            foreach (NetworkConnectionToClient connection in disconnected)
                PlayerStates.Remove(connection);

            yield return Timing.WaitForSeconds(SendInterval);
        }

        RestoreAndClear();
    }

    private static void RestoreAndClear()
    {
        foreach (NetworkIdentity identity in IdentityGroups.Keys.ToArray())
        {
            if (identity == null)
                continue;
            identity.visible = Visibility.Default;
            if (NetworkServer.active && identity.isServer)
                NetworkServer.RebuildObservers(identity, true);
        }
        foreach (CullGroup group in Groups.Values.ToArray())
            RemoveGroup(group);
        Groups.Clear();
        IdentityGroups.Clear();
        PlayerStates.Clear();
        _nextErrorLogTime = 0f;
    }

    private static void UpdatePlayer(Player player, NetworkConnectionToClient connection, bool scan)
    {
        if (!PlayerStates.TryGetValue(connection, out Dictionary<CullGroup, ViewState>? states))
            PlayerStates[connection] = states = [];

        if (scan)
        {
            float now = Time.realtimeSinceStartup;
            foreach (CullGroup group in Groups.Values.ToArray())
            {
                if (group.Anchor == null)
                    continue;
                if (!states.TryGetValue(group, out ViewState? state))
                    states[group] = state = new ViewState();

                float squaredDistance = (player.Position - group.AnchorPosition).sqrMagnitude;
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
        }

        int budget = ObjectsPerUpdate;
        foreach (KeyValuePair<CullGroup, ViewState> pair in states
            .Where(pair => pair.Value.IsTransitioning)
            .OrderBy(pair => pair.Value.LastSquaredDistance))
        {
            if (budget <= 0)
                break;
            budget -= ProcessTransition(pair.Key, pair.Value, connection, budget);
        }
    }

    // 詳細度を上げる要求（アップグレード、および非表示状態からの初回表示）は即座に適用する。
    // 詳細度を下げる要求（ダウングレード、完全カリングを含む）は DowngradeDelay 秒
    // 継続して要求され続けた場合のみ適用する。境界付近を往復するプレイヤーが
    // 毎スキャンで高コストな再表示/再パースを引き起こすのを防ぐ。
    private static bool ShouldApplyRequest(ViewState state, int current, int requested, float now)
    {
        if (!IsWorse(requested, current))
        {
            state.PendingDowngradeLod = NoPendingDowngrade;
            return true;
        }

        float delay = DowngradeDelay;
        if (delay <= 0f)
            return true;

        if (state.PendingDowngradeLod != requested)
        {
            state.PendingDowngradeLod = requested;
            state.PendingDowngradeSince = now;
            return false;
        }

        if (now - state.PendingDowngradeSince < delay)
            return false;

        state.PendingDowngradeLod = NoPendingDowngrade;
        return true;
    }

    // -1（完全カリング）はどの表示状態よりも「悪い」。Lod 番号は大きいほど詳細度が低い。
    // current が非表示（-1）の場合は「初回表示」であり、ダウングレードとは扱わない。
    private static bool IsWorse(int requested, int current)
    {
        if (requested == current)
            return false;
        if (current < 0)
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
        NetworkIdentity identity = entry.Identity;
        if (identity == null || !identity.isServer || identity.observers.ContainsKey(connection.connectionId))
            return false;
        identity.AddObserver(connection);
        return true;
    }

    private static bool Hide(CullEntry entry, NetworkConnectionToClient connection)
    {
        NetworkIdentity identity = entry.Identity;
        if (identity == null || !identity.isServer || !identity.observers.ContainsKey(connection.connectionId))
            return false;
        connection.RemoveFromObserving(identity, false);
        identity.RemoveObserver(connection);
        return true;
    }

    private static string GetString(Dictionary<string, object> properties, string key) =>
        properties.TryGetValue(key, out object? value) ? Convert.ToString(value) ?? string.Empty : string.Empty;

    private static int GetInt(Dictionary<string, object> properties, string key, int fallback) =>
        properties.TryGetValue(key, out object? value) ? Convert.ToInt32(value) : fallback;

    private static float GetFloat(Dictionary<string, object> properties, string key, float fallback) =>
        properties.TryGetValue(key, out object? value) ? Convert.ToSingle(value) : fallback;
}
