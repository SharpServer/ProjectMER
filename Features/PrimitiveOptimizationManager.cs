using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdminToys;
using MEC;
using Mirror;
using ProjectMER.Configs;
using ProjectMER.Features.Objects;
using ProjectMER.Features.Serializable.Schematics;
using UnityEngine;

namespace ProjectMER.Features;

/// <summary>
/// Defers static schematic primitives and replaces their per-client network objects with compact,
/// culling-aware spawn messages. All Unity/Mirror work remains on the main thread; only immutable
/// cluster coordinates are processed by worker tasks.
/// </summary>
public static class PrimitiveOptimizationManager
{
    private sealed class OptimizedPrimitive
    {
        public SchematicObject Owner = null!;
        public PrimitiveObjectToy Primitive = null!;
        public ClientPrimitive Client = null!;
        public bool WasEnabled;
        public bool WasRendererEnabled;
        public bool WasColliderEnabled;
    }

    private sealed class OwnerState
    {
        public int Generation;
        public readonly List<OptimizedPrimitive> Items = [];
    }

    private sealed class FinalizeWork
    {
        public SchematicObject Owner = null!;
        public int Generation;
        public List<PrimitiveObjectToy> Candidates = null!;
        public Matrix4x4 OwnerWorldToLocal;
        public int Index;
        public readonly List<OptimizedPrimitive> Optimized = [];
    }

    private readonly struct ClusterInput
    {
        public readonly int Index;
        public readonly PackedVector3 WorldPosition;
        public readonly PackedVector3 OwnerLocalPosition;
        public readonly float HalfDiagonal;
        public readonly float SizePriority;
        public readonly bool IsCollidable;

        public ClusterInput(
            int index,
            PackedVector3 worldPosition,
            PackedVector3 ownerLocalPosition,
            float halfDiagonal,
            float sizePriority,
            bool isCollidable)
        {
            Index = index;
            WorldPosition = worldPosition;
            OwnerLocalPosition = ownerLocalPosition;
            HalfDiagonal = halfDiagonal;
            SizePriority = sizePriority;
            IsCollidable = isCollidable;
        }
    }

    // Worker-side value type. Keeping Unity vectors out of the worker lets cluster construction stay
    // independent from Unity/Mirror state and makes the ownership boundary explicit.
    private readonly struct PackedVector3
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public PackedVector3(Vector3 value)
        {
            X = value.x;
            Y = value.y;
            Z = value.z;
        }

        public PackedVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Vector3 ToUnity() => new(X, Y, Z);
    }

    private sealed class ClusterSpec
    {
        public int[] Indices = Array.Empty<int>();
        public PackedVector3 LocalCenter;
        public float Radius;
        public bool AlwaysVisible;
    }

    private sealed class ClusterPlan
    {
        public ClusterSpec[] Clusters = Array.Empty<ClusterSpec>();
    }

    private readonly struct ClusterSettings
    {
        public readonly float CellSize;
        public readonly int MaxPerCluster;
        public readonly float AlwaysVisibleSize;

        public ClusterSettings(float cellSize, int maxPerCluster, float alwaysVisibleSize)
        {
            CellSize = cellSize;
            MaxPerCluster = maxPerCluster;
            AlwaysVisibleSize = alwaysVisibleSize;
        }
    }

    private sealed class ClusterJob
    {
        public SchematicObject Owner = null!;
        public int Generation;
        public List<OptimizedPrimitive> Items = null!;
        public Task<ClusterPlan> Task = null!;
    }

    private static readonly Dictionary<SchematicObject, List<PrimitiveObjectToy>> Pending = [];
    private static readonly Dictionary<SchematicObject, OwnerState> Owners = [];
    private static readonly Queue<FinalizeWork> FinalizationQueue = new();
    private static readonly Queue<OptimizedPrimitive> NativeRestoreQueue = new();
    private static readonly List<ClusterJob> ClusterJobs = [];

    private static CoroutineHandle _finalizationCoroutine;
    private static CancellationTokenSource? _lifecycleCancellation;
    private static SemaphoreSlim? _clusterWorkers;
    private static int _mainThreadId;
    private static bool _started;

    private static Config? CurrentConfig => ProjectMER.Singleton?.Config;

    private static bool IsEnabled
    {
        get
        {
            Config? config = CurrentConfig;
            return config?.EnablePrimitiveOptimization == true &&
                config.EnableCulling && !ExternalMeroOptimizerLoaded();
        }
    }

    /// <summary>
    /// Records a visible primitive immediately before its native NetworkServer.Spawn call. Returning
    /// true tells the caller to skip that spawn; no Unity work is performed on a worker thread.
    /// </summary>
    public static bool TryDefer(SchematicObject owner, PrimitiveObjectToy primitive)
    {
        if (!IsMainThread() || owner == null || primitive == null || !NetworkServer.active || !IsEnabled ||
            !IsStaticCandidate(owner, primitive))
            return false;

        if (!Pending.TryGetValue(owner, out List<PrimitiveObjectToy>? primitives))
            Pending[owner] = primitives = [];
        if (!primitives.Contains(primitive))
            primitives.Add(primitive);
        return true;
    }

    /// <summary>Queues finalization after all schematic runtime components have been attached.</summary>
    public static void FinalizeSchematic(SchematicObject owner)
    {
        if (!IsMainThread() || owner == null || !Pending.TryGetValue(owner, out List<PrimitiveObjectToy>? pending))
            return;

        Pending.Remove(owner);
        if (pending.Count == 0)
            return;

        if (!Owners.TryGetValue(owner, out OwnerState? state))
            Owners[owner] = state = new OwnerState();

        FinalizationQueue.Enqueue(new FinalizeWork
        {
            Owner = owner,
            Generation = state.Generation,
            Candidates = pending,
            OwnerWorldToLocal = owner.transform.worldToLocalMatrix,
        });
    }

    /// <summary>Starts the frame-budgeted main-thread finalizer and bounded cluster workers.</summary>
    public static void Start()
    {
        if (_mainThreadId == 0)
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        if (!IsMainThread())
            return;

        Stop();
        _lifecycleCancellation = new CancellationTokenSource();
        int workerCount = Math.Max(1, CurrentConfig?.PrimitiveClusterWorkerCount ?? 1);
        _clusterWorkers = new SemaphoreSlim(workerCount, workerCount);
        _started = true;
        if (IsEnabled)
            _finalizationCoroutine = Timing.RunCoroutine(FinalizationLoop());
    }

    /// <summary>
    /// Stops processing and invalidates pending worker plans. Existing synthetic objects are removed
    /// through CullingManager, but native objects are never mass-spawned during shutdown.
    /// </summary>
	public static void Stop(bool restoreNative = false)
	{
        if (_started && !IsMainThread())
            return;

        Timing.KillCoroutines(_finalizationCoroutine);
        _finalizationCoroutine = default;
        _started = false;
        _lifecycleCancellation?.Cancel();
        _lifecycleCancellation?.Dispose();
        _lifecycleCancellation = null;

		foreach (OwnerState state in Owners.Values)
		{
			foreach (OptimizedPrimitive item in state.Items)
			{
				if (item.Client != null)
					CullingManager.UnregisterClientPrimitive(item.Client);
				if (restoreNative)
					RestoreNative(item, spawn: NetworkServer.active);
				item.Client?.Invalidate();
			}
		}

		if (restoreNative)
		{
			foreach (List<PrimitiveObjectToy> primitives in Pending.Values)
				foreach (PrimitiveObjectToy primitive in primitives)
					SpawnNative(primitive);

			foreach (FinalizeWork work in FinalizationQueue)
			{
				foreach (OptimizedPrimitive item in work.Optimized)
					RestoreNative(item, spawn: NetworkServer.active);
				foreach (PrimitiveObjectToy primitive in work.Candidates)
					SpawnNative(primitive);
			}
		}

        foreach (ClusterJob job in ClusterJobs)
            foreach (OptimizedPrimitive item in job.Items)
                item.Client?.Invalidate();

        Pending.Clear();
        Owners.Clear();
        FinalizationQueue.Clear();
        NativeRestoreQueue.Clear();
        ClusterJobs.Clear();
        _clusterWorkers = null;
    }

    /// <summary>Unregisters all pending and optimized primitives owned by a schematic.</summary>
    public static void UnregisterSchematic(SchematicObject owner)
    {
        if (!IsMainThread() || owner == null)
            return;

        Pending.Remove(owner);
        foreach (FinalizeWork work in FinalizationQueue.Where(work => work.Owner == owner).ToArray())
        {
            // Removing from the queue is done below; invalidating the generation makes a raced worker
            // harmless even when its completion callback has already been queued.
            work.Generation++;
        }

        if (!Owners.TryGetValue(owner, out OwnerState? state))
            return;

        state.Generation++;
        foreach (OptimizedPrimitive item in state.Items)
        {
            CullingManager.UnregisterClientPrimitive(item.Client);
            item.Client.Invalidate();
        }
        state.Items.Clear();
        Owners.Remove(owner);

        for (int i = ClusterJobs.Count - 1; i >= 0; i--)
        {
            if (ClusterJobs[i].Owner != owner)
                continue;
            foreach (OptimizedPrimitive item in ClusterJobs[i].Items)
                item.Client.Invalidate();
            ClusterJobs.RemoveAt(i);
        }
    }

    /// <summary>
    /// Explicitly restores one deferred primitive and gives it a native server identity. No automatic
    /// change detection or promotion is performed by this manager.
    /// </summary>
    public static bool Promote(PrimitiveObjectToy primitive)
    {
        if (!IsMainThread() || primitive == null)
            return false;

        foreach (KeyValuePair<SchematicObject, OwnerState> pair in Owners.ToArray())
        {
            OptimizedPrimitive? item = pair.Value.Items.FirstOrDefault(candidate => candidate.Primitive == primitive);
            if (item == null)
                continue;

            pair.Value.Items.Remove(item);
            CullingManager.UnregisterClientPrimitive(item.Client);
            item.Client.Invalidate();
            RestoreNative(item, spawn: NetworkServer.active);
            if (pair.Value.Items.Count == 0)
                Owners.Remove(pair.Key);
            return true;
        }

        foreach (KeyValuePair<SchematicObject, List<PrimitiveObjectToy>> pair in Pending.ToArray())
        {
            if (!pair.Value.Remove(primitive))
                continue;
            if (NetworkServer.active && primitive.netIdentity != null && primitive.netIdentity.netId == 0)
                NetworkServer.Spawn(primitive.gameObject);
            return true;
        }

        return false;
    }

    private static IEnumerator<float> FinalizationLoop()
    {
        while (_started)
        {
            ApplyCompletedPlans();
            ProcessFinalizationBudget();
            ProcessNativeRestoreBudget();
            yield return Timing.WaitForOneFrame;
        }

        ApplyCompletedPlans();
    }

    private static void ProcessNativeRestoreBudget()
    {
        if (NativeRestoreQueue.Count == 0)
            return;

        float budgetMs = Math.Max(0.1f, CurrentConfig?.PrimitiveFinalizeFrameBudgetMs ?? 2f);
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (NativeRestoreQueue.Count > 0 && stopwatch.Elapsed.TotalMilliseconds < budgetMs)
        {
            OptimizedPrimitive item = NativeRestoreQueue.Dequeue();
            if (item.Owner == null || item.Primitive == null ||
                !Owners.TryGetValue(item.Owner, out OwnerState? state) || !state.Items.Remove(item))
                continue;

            CullingManager.UnregisterClientPrimitive(item.Client);
            item.Client.Invalidate();
            RestoreNative(item, spawn: NetworkServer.active);
            if (state.Items.Count == 0)
                Owners.Remove(item.Owner);
        }
    }

    private static void ProcessFinalizationBudget()
    {
        if (FinalizationQueue.Count == 0)
            return;

        float budgetMs = Math.Max(0.1f, CurrentConfig?.PrimitiveFinalizeFrameBudgetMs ?? 2f);
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (FinalizationQueue.Count > 0 && stopwatch.Elapsed.TotalMilliseconds < budgetMs)
        {
            FinalizeWork work = FinalizationQueue.Peek();
            if (!IsOwnerGenerationAlive(work.Owner, work.Generation))
            {
                FinalizationQueue.Dequeue();
                continue;
            }

            if (work.Index < work.Candidates.Count)
            {
                PrimitiveObjectToy primitive = work.Candidates[work.Index++];
                TryOptimize(work, primitive);
                continue;
            }

            FinalizationQueue.Dequeue();
            CommitFinalization(work);
        }
    }

    private static void TryOptimize(FinalizeWork work, PrimitiveObjectToy primitive)
    {
        if (primitive == null || !IsStaticCandidate(work.Owner, primitive) ||
            !ClientPrimitive.TryCreate(work.Owner, primitive, out ClientPrimitive? client) || client == null)
        {
            if (primitive != null)
                SpawnNative(primitive);
            return;
        }

        OptimizedPrimitive optimized = new()
        {
            Owner = work.Owner,
            Primitive = primitive,
            Client = client,
            WasEnabled = primitive.enabled,
            WasRendererEnabled = primitive._renderer != null && primitive._renderer.enabled,
            WasColliderEnabled = primitive._collider != null && primitive._collider.enabled,
        };

        // The primitive stays in the hierarchy for collisions, scripts and SchematicBlock lookups.
        // Only its network behaviour and renderer are hibernated; non-collidable colliders are disabled.
        primitive.enabled = false;
        if (primitive._renderer != null)
            primitive._renderer.enabled = false;
        if (primitive._collider != null)
            primitive._collider.enabled = client.IsCollidable;
        primitive.transform.hasChanged = false;
        work.Optimized.Add(optimized);
    }

    private static void CommitFinalization(FinalizeWork work)
    {
        if (work.Optimized.Count == 0)
            return;
        if (!Owners.TryGetValue(work.Owner, out OwnerState? state) || state.Generation != work.Generation ||
            !IsOwnerGenerationAlive(work.Owner, work.Generation))
        {
            foreach (OptimizedPrimitive item in work.Optimized)
                item.Client.Invalidate();
            return;
        }

        state.Items.AddRange(work.Optimized);
        ClusterInput[] inputs = BuildClusterInputs(work.Optimized, work.OwnerWorldToLocal);
        CancellationToken token = _lifecycleCancellation?.Token ?? CancellationToken.None;
        Config? config = CurrentConfig;
        ClusterSettings settings = new(
            Math.Max(0.5f, config?.PrimitiveClusterSize ?? 8f),
            Math.Max(1, config?.PrimitiveClusterMaxObjects ?? 256),
            Math.Max(0f, config?.PrimitiveAlwaysVisibleSize ?? 0f));
        Task<ClusterPlan> task = BuildClusterPlanAsync(inputs, settings, token);
        ClusterJobs.Add(new ClusterJob
        {
            Owner = work.Owner,
            Generation = work.Generation,
            Items = work.Optimized,
            Task = task,
        });
    }

    private static ClusterInput[] BuildClusterInputs(List<OptimizedPrimitive> items, Matrix4x4 ownerWorldToLocal)
    {
        ClusterInput[] inputs = new ClusterInput[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            OptimizedPrimitive item = items[i];
            PrimitiveObjectToy primitive = item.Primitive;
            Vector3 worldPosition = primitive.transform.position;
            Vector3 ownerLocalPosition = ownerWorldToLocal.MultiplyPoint3x4(worldPosition);
            float halfDiagonal = primitive._collider != null
                ? primitive._collider.bounds.extents.magnitude
                : primitive.transform.lossyScale.magnitude * 0.5f;
            inputs[i] = new ClusterInput(
                i,
                new PackedVector3(worldPosition),
                new PackedVector3(ownerLocalPosition),
                halfDiagonal,
                item.Client.SizePriority,
                item.Client.IsCollidable);
        }
        return inputs;
    }

    private static async Task<ClusterPlan> BuildClusterPlanAsync(
        ClusterInput[] inputs,
        ClusterSettings settings,
        CancellationToken token)
    {
        SemaphoreSlim? workers = _clusterWorkers;
        bool acquired = false;
        if (workers != null)
        {
            await workers.WaitAsync(token).ConfigureAwait(false);
            acquired = true;
        }

        try
        {
            return await Task.Run(() => BuildClusterPlan(inputs, settings, token), token).ConfigureAwait(false);
        }
        finally
        {
            if (acquired)
                workers!.Release();
        }
    }

    private static ClusterPlan BuildClusterPlan(
        ClusterInput[] inputs,
        ClusterSettings settings,
        CancellationToken token)
    {
        List<ClusterSpec> clusters = [];

        List<ClusterInput> alwaysVisible = [];
        List<ClusterInput> clustered = [];
        foreach (ClusterInput input in inputs)
        {
            token.ThrowIfCancellationRequested();
            if (settings.AlwaysVisibleSize > 0f && input.SizePriority >= settings.AlwaysVisibleSize)
                alwaysVisible.Add(input);
            else
                clustered.Add(input);
        }

        if (alwaysVisible.Count > 0)
            clusters.Add(CreateCluster(alwaysVisible, alwaysVisible: true));

        var cells = clustered.GroupBy(input => (
            FloorToInt(input.WorldPosition.X / settings.CellSize),
            FloorToInt(input.WorldPosition.Y / settings.CellSize),
            FloorToInt(input.WorldPosition.Z / settings.CellSize)));
        foreach (IGrouping<(int, int, int), ClusterInput> cell in cells)
        {
            token.ThrowIfCancellationRequested();
            List<ClusterInput> ordered = cell
                .OrderByDescending(input => input.IsCollidable)
                .ThenByDescending(input => input.SizePriority)
                .ThenBy(input => input.Index)
                .ToList();
            for (int offset = 0; offset < ordered.Count; offset += settings.MaxPerCluster)
                clusters.Add(CreateCluster(ordered.Skip(offset).Take(settings.MaxPerCluster).ToList(), alwaysVisible: false));
        }

        return new ClusterPlan { Clusters = clusters.ToArray() };
    }

    private static ClusterSpec CreateCluster(List<ClusterInput> inputs, bool alwaysVisible)
    {
        float localX = 0f;
        float localY = 0f;
        float localZ = 0f;
        float worldX = 0f;
        float worldY = 0f;
        float worldZ = 0f;
        foreach (ClusterInput input in inputs)
        {
            localX += input.OwnerLocalPosition.X;
            localY += input.OwnerLocalPosition.Y;
            localZ += input.OwnerLocalPosition.Z;
            worldX += input.WorldPosition.X;
            worldY += input.WorldPosition.Y;
            worldZ += input.WorldPosition.Z;
        }
        localX /= inputs.Count;
        localY /= inputs.Count;
        localZ /= inputs.Count;
        worldX /= inputs.Count;
        worldY /= inputs.Count;
        worldZ /= inputs.Count;

        float radius = 0f;
        foreach (ClusterInput input in inputs)
        {
            float dx = worldX - input.WorldPosition.X;
            float dy = worldY - input.WorldPosition.Y;
            float dz = worldZ - input.WorldPosition.Z;
            float distance = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            radius = Math.Max(radius, distance + input.HalfDiagonal);
        }

        return new ClusterSpec
        {
            Indices = inputs.Select(input => input.Index).ToArray(),
            LocalCenter = new PackedVector3(localX, localY, localZ),
            Radius = radius,
            AlwaysVisible = alwaysVisible,
        };
    }

    private static void ApplyCompletedPlans()
    {
        for (int i = ClusterJobs.Count - 1; i >= 0; i--)
        {
            ClusterJob job = ClusterJobs[i];
            if (!job.Task.IsCompleted)
                continue;
            ClusterJobs.RemoveAt(i);

            if (job.Task.IsCanceled || job.Task.IsFaulted)
            {
                foreach (OptimizedPrimitive item in job.Items)
                    NativeRestoreQueue.Enqueue(item);
                if (job.Task.IsFaulted)
                    Logger.Error($"[PrimitiveOptimizationManager] Cluster planning failed; restoring native primitives over subsequent frames: {job.Task.Exception}");
                continue;
            }

            if (!IsOwnerGenerationAlive(job.Owner, job.Generation) || !Owners.TryGetValue(job.Owner, out OwnerState? state))
            {
                foreach (OptimizedPrimitive item in job.Items)
                    item.Client.Invalidate();
                continue;
            }

            ClusterPlan plan = job.Task.GetAwaiter().GetResult();
            foreach (ClusterSpec cluster in plan.Clusters)
            {
                ClientPrimitive[] clientItems = cluster.Indices
                    .Where(index => index >= 0 && index < job.Items.Count)
                    .Select(index => job.Items[index].Client)
                    .Where(client => !client.IsDestroyed)
                    .ToArray();
                if (clientItems.Length == 0)
                    continue;
                CullingManager.PrepareClientPrimitives(
                    job.Owner,
                    clientItems,
                    cluster.LocalCenter.ToUnity(),
                    cluster.Radius,
                    cluster.AlwaysVisible);
            }
        }
    }

    private static bool IsStaticCandidate(SchematicObject owner, PrimitiveObjectToy primitive)
    {
		if (owner == null || primitive == null || primitive.netIdentity == null || primitive.netIdentity.netId != 0 ||
			!primitive.PrimitiveFlags.HasFlag(PrimitiveFlags.Visible) || primitive._renderer == null ||
			!primitive._renderer.enabled)
			return false;
		if (!primitive.IsStatic && !MatchesAny(owner.Name, CurrentConfig?.PrimitiveAssumeStaticSchematicNamePatterns))
			return false;
		// A schematic parented under a player or another runtime object is explicitly dynamic even if
		// its exported blocks were marked static (for example SCP-513 manifestations).
		if (owner.transform.parent != null)
			return false;

        Transform? parent = primitive.transform.parent;
        if (parent == null || !parent.TryGetComponent(out NetworkIdentity parentIdentity) || parentIdentity.netId == 0 ||
            parentIdentity.serverOnly)
            return false;

        // A primitive with any child is a network parent, not a leaf candidate. Keeping this strict
        // also protects marker/trigger components nested below an otherwise visual primitive.
        if (primitive.transform.childCount != 0)
            return false;
        if (primitive.TryGetComponent(out SchematicBlock schematicBlock) && schematicBlock != null && schematicBlock.HasObjectPrefabKey)
            return false;

        for (Transform? current = primitive.transform; current != null; current = current.parent)
        {
            if (current.TryGetComponent<Animator>(out _) || current.TryGetComponent<Rigidbody>(out _))
                return false;

            foreach (Component component in current.GetComponents<Component>())
            {
                if (component == null || component is Transform || component is Renderer || component is Collider ||
                    component is MeshFilter || component is NetworkIdentity || component is AdminToyBase ||
                    component is SchematicObject || component is SchematicBlock)
                    continue;
                // Anything else (including non-MonoBehaviour runtime components such as audio,
                // particle or network transform components) can alter state after this snapshot.
                return false;
            }

            if (current == owner.transform)
                break;
        }

		return !IsExcluded(owner.Name, primitive.name);
    }

    private static int FloorToInt(float value) => (int)Math.Floor(value);

	private static bool IsExcluded(string? ownerName, string? primitiveName)
	{
		IEnumerable<string>? patterns = CurrentConfig?.PrimitiveExcludedSchematicNamePatterns;
		if (patterns == null)
			return false;

        foreach (string? pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;
            string value = pattern.Trim();
            if (GlobMatch(ownerName ?? string.Empty, value) || GlobMatch(primitiveName ?? string.Empty, value))
                return true;
        }
		return false;
	}

	private static bool MatchesAny(string? input, IEnumerable<string>? patterns)
	{
		if (patterns == null)
			return false;
		foreach (string? pattern in patterns)
		{
			if (!string.IsNullOrWhiteSpace(pattern) && GlobMatch(input ?? string.Empty, pattern.Trim()))
				return true;
		}
		return false;
	}

    private static bool GlobMatch(string input, string pattern)
    {
        if (pattern.IndexOfAny(['*', '?']) < 0)
            return input.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;

        int inputIndex = 0;
        int patternIndex = 0;
        int starIndex = -1;
        int matchIndex = 0;
        while (inputIndex < input.Length)
        {
            if (patternIndex < pattern.Length &&
                (pattern[patternIndex] == '?' || char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(input[inputIndex])))
            {
                inputIndex++;
                patternIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                matchIndex = inputIndex;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                inputIndex = ++matchIndex;
            }
            else
                return false;
        }
        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            patternIndex++;
        return patternIndex == pattern.Length;
    }

    private static void RestoreNative(OptimizedPrimitive item, bool spawn)
    {
        PrimitiveObjectToy primitive = item.Primitive;
        if (primitive == null)
            return;
        primitive.enabled = item.WasEnabled;
        if (primitive._renderer != null)
            primitive._renderer.enabled = item.WasRendererEnabled;
        if (primitive._collider != null)
            primitive._collider.enabled = item.WasColliderEnabled;
        primitive.Position = primitive.transform.localPosition;
        primitive.Rotation = primitive.transform.localRotation;
        primitive.Scale = primitive.transform.localScale;
        primitive.transform.hasChanged = false;
        if (spawn)
            SpawnNative(primitive);
    }

    private static void SpawnNative(PrimitiveObjectToy primitive)
    {
        if (primitive != null && NetworkServer.active && primitive.netIdentity != null && primitive.netIdentity.netId == 0)
            NetworkServer.Spawn(primitive.gameObject);
    }

    private static bool IsOwnerGenerationAlive(SchematicObject owner, int generation)
    {
        return owner != null && owner.gameObject != null && Owners.TryGetValue(owner, out OwnerState? state) &&
            state.Generation == generation && _lifecycleCancellation?.IsCancellationRequested != true;
    }

    private static bool IsMainThread() => _mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId;

    private static bool ExternalMeroOptimizerLoaded()
    {
        // MEROptimizer owns the same native PrimitiveObjectToy spawn path. Do not activate both
        // optimizers; the integrated option is intentionally opt-in and defaults off in Config.
        return AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
            assembly.GetName().Name?.IndexOf("MEROptimizer", StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
