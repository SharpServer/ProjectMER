using System.ComponentModel;
using YamlDotNet.Serialization;

namespace ProjectMER.Configs;

public class Config
{
	[Description("Enables FileSystemWatcher in this plugin. What it does is when you manually change values in a currently loaded map file, after saving the file the plugin will automatically reload the map in-game with the new changes so you won't need to do it yourself.")]
    public bool EnableFileSystemWatcher { get; set; } = false;

	[Description("Whether the object will be auto selected when spawning it.")]
	public bool AutoSelect { get; set; } = true;

	[Description("Enables ProjectMER's TextToy/image culling.")]
	public bool EnableTextToyOptimization { get; set; } = true;

	[YamlMember(Alias = "enable_primitive_optimization")]
	[Description("Enables ProjectMER's integrated MEROptimizer backend. Remove the standalone MEROptimizer plugin before enabling; applies from the next round.")]
	public bool EnablePrimitiveOptimization { get; set; } = true;

	[YamlMember(Alias = "primitive_optimize_only_non_collidable")]
	[Description("When enabled, the integrated MEROptimizer backend leaves collidable primitives native.")]
	public bool PrimitiveOptimizeOnlyNonCollidable { get; set; } = false;

	[YamlMember(Alias = "primitive_clusterize_schematic")]
	[Description("When disabled, optimized primitives remain client-side but are shown at every distance.")]
	public bool PrimitiveClusterizeSchematic { get; set; } = true;

	[YamlMember(Alias = "primitive_culling_distance")]
	[Description("Maximum distance (in meters) at which primitive objects are considered visible.")]
	public float PrimitiveCullingDistance { get; set; } = 18f;

	[YamlMember(Alias = "primitive_schematic_culling_distances")]
	[Description("Optional per-schematic primitive culling distance overrides.")]
	public Dictionary<string, float> PrimitiveSchematicCullingDistances { get; set; } = [];

	[YamlMember(Alias = "primitive_cluster_size")]
	[Description("Maximum distance from a MEROptimizer cluster seed used to group primitive objects.")]
	public float PrimitiveClusterSize { get; set; } = 2.5f;

	[YamlMember(Alias = "primitive_cluster_max_objects")]
	[Description("Maximum number of primitive objects assigned to one spatial cluster.")]
	public int PrimitiveClusterMaxObjects { get; set; } = 50;

	[YamlMember(Alias = "primitive_always_visible_size")]
	[Description("Primitives whose absolute world-scale axis sum reaches this value remain visible regardless of distance. Set to zero to disable.")]
	public float PrimitiveAlwaysVisibleSize { get; set; } = 10f;

	[YamlMember(Alias = "primitive_always_visible_name_patterns")]
	[Description("Primitive name patterns which remain visible at every distance, matching MEROptimizer's excluded-unspawning object behavior.")]
	public List<string> PrimitiveAlwaysVisibleNamePatterns { get; set; } = [];

	[YamlMember(Alias = "primitive_objects_per_update")]
	[Description("Emergency ceiling for adaptive primitive send work per player tick. Collidable primitives consume two work units; sustained throughput is controlled by the adaptive rate and pressure limits.")]
	public int PrimitiveObjectsPerUpdate { get; set; } = 96;

	[YamlMember(Alias = "primitive_global_objects_per_update")]
	[Description("Emergency ceiling for adaptive primitive send work globally per tick. Sustained throughput is controlled automatically from frame time, RTT, and send queue pressure.")]
	public int PrimitiveGlobalObjectsPerUpdate { get; set; } = 768;

	[YamlMember(Alias = "primitive_transition_burst_objects")]
	[Description("Maximum weighted primitive work sent for one nearest cluster before rotating to another. Higher values finish nearby geometry sooner instead of leaving many clusters partially visible.")]
	public int PrimitiveTransitionBurstObjects { get; set; } = 48;

	[YamlMember(Alias = "culling_global_objects_per_update")]
	[Description("Maximum culling objects processed globally per update.")]
	public int CullingGlobalObjectsPerUpdate { get; set; } = 64;

	[YamlMember(Alias = "culling_spatial_cell_size")]
	[Description("Spatial cell size (in meters) used by culling.")]
	public float CullingSpatialCellSize { get; set; } = 50f;

	[YamlMember(Alias = "primitive_finalize_frame_budget_ms")]
	[Description("Maximum time (in milliseconds) spent finalizing primitive optimization work in one frame. This is a main-thread budget, not a fixed object count.")]
	public float PrimitiveFinalizeFrameBudgetMs { get; set; } = 10f;

	[YamlMember(Alias = "primitive_finalize_adaptive_burst")]
	[Description("Preferred number of primitive finalization items processed per round-robin burst. The frame-time budget remains the hard limit and bursts are reduced when several schematics are pending.")]
	public int PrimitiveFinalizeAdaptiveBurst { get; set; } = 1024;

	[YamlMember(Alias = "primitive_min_objects_per_second")]
	[Description("Minimum adaptive primitive send rate per ready client while under soft frame/RTT pressure. Hard reliable-queue pressure still pauses sending.")]
	public float PrimitiveMinObjectsPerSecond { get; set; } = 1200f;

	[YamlMember(Alias = "primitive_initial_objects_per_second")]
	[Description("Initial adaptive primitive send rate per ready client. The existing per-tick object ceiling still applies as an emergency cap.")]
	public float PrimitiveInitialObjectsPerSecond { get; set; } = 2400f;

	[YamlMember(Alias = "primitive_initial_burst_objects")]
	[Description("Startup object tokens granted when a ready client first enters the adaptive primitive sender. This is clamped by the per-tick emergency ceiling.")]
	public int PrimitiveInitialBurstObjects { get; set; } = 96;

	[YamlMember(Alias = "primitive_max_objects_per_second")]
	[Description("Maximum adaptive primitive send rate per ready client before backpressure reduces it.")]
	public float PrimitiveMaxObjectsPerSecond { get; set; } = 4800f;

	[YamlMember(Alias = "primitive_global_initial_objects_per_second")]
	[Description("Initial adaptive primitive send rate shared by all ready clients.")]
	public float PrimitiveGlobalInitialObjectsPerSecond { get; set; } = 12000f;

	[YamlMember(Alias = "primitive_global_max_objects_per_second")]
	[Description("Maximum adaptive primitive send rate shared by all ready clients.")]
	public float PrimitiveGlobalMaxObjectsPerSecond { get; set; } = 30000f;

	[YamlMember(Alias = "primitive_byte_burst_bytes")]
	[Description("Maximum per-client primitive byte tokens retained between ticks. It is automatically raised to at least the reliable batch hard limit.")]
	public int PrimitiveByteBurstBytes { get; set; } = 512 * 1024;

	[YamlMember(Alias = "primitive_global_byte_burst_bytes")]
	[Description("Global primitive send byte burst shared by all ready clients.")]
	public int PrimitiveGlobalByteBurstBytes { get; set; } = 4 * 1024 * 1024;

	[YamlMember(Alias = "primitive_max_bytes_per_second")]
	[Description("Maximum adaptive primitive bandwidth generated per ready client. Reliable queue and RTT pressure can reduce or pause it automatically.")]
	public int PrimitiveMaxBytesPerSecond { get; set; } = 4 * 1024 * 1024;

	[YamlMember(Alias = "primitive_global_max_bytes_per_second")]
	[Description("Maximum adaptive primitive bandwidth generated across all ready clients.")]
	public int PrimitiveGlobalMaxBytesPerSecond { get; set; } = 24 * 1024 * 1024;

	[YamlMember(Alias = "primitive_batch_soft_limit_bytes")]
	[Description("Reliable batch byte pressure threshold at which adaptive primitive sends begin backing off.")]
	public int PrimitiveBatchSoftLimitBytes { get; set; } = 128 * 1024;

	[YamlMember(Alias = "primitive_batch_hard_limit_bytes")]
	[Description("Reliable batch byte pressure threshold at which adaptive primitive sends pause until the queue drains.")]
	public int PrimitiveBatchHardLimitBytes { get; set; } = 512 * 1024;

	[YamlMember(Alias = "primitive_reliable_queue_soft_limit")]
	[Description("Reliable packet queue depth at which adaptive primitive sends begin backing off.")]
	public int PrimitiveReliableQueueSoftLimit { get; set; } = 64;

	[YamlMember(Alias = "primitive_reliable_queue_hard_limit")]
	[Description("Reliable packet queue depth at which adaptive primitive sends pause until the queue drains.")]
	public int PrimitiveReliableQueueHardLimit { get; set; } = 192;

	[YamlMember(Alias = "primitive_diagnostics_enabled")]
	[Description("Logs a low-frequency primitive optimization/sender snapshot with fallback, pending, send, byte, pressure, and completion counters.")]
	public bool PrimitiveDiagnosticsEnabled { get; set; } = false;

	[YamlMember(Alias = "primitive_cluster_worker_count")]
	[Description("Number of worker tasks used to build primitive clusters.")]
	public int PrimitiveClusterWorkerCount { get; set; } = 4;

	[YamlMember(Alias = "primitive_excluded_schematic_name_patterns")]
	[Description("Schematic name patterns excluded from primitive optimization.")]
	public List<string> PrimitiveExcludedSchematicNamePatterns { get; set; } = [];

	[YamlMember(Alias = "primitive_assume_static_schematic_name_patterns")]
	[Description("Schematic name patterns whose otherwise-unmarked primitives may be treated as static. Only use this for schematics that are never moved, animated, or visibility-controlled at runtime.")]
	public List<string> PrimitiveAssumeStaticSchematicNamePatterns { get; set; } = [];

	[YamlMember(Alias = "primitive_deactivate_noncollidable_assumed_static")]
	[Description("Deactivates non-collidable optimized leaf primitive GameObjects after their client payload and cluster metadata are captured. Components and GameObjects remain available for ProjectMER metadata and are restored on native promotion/unload.")]
	public bool PrimitiveDeactivateNoncollidableAssumedStatic { get; set; } = true;

	[Description("Enables distance-based culling for TextToys. When enabled, large text images are only sent to players within CullingDistance.")]
	public bool EnableCulling { get; set; } = true;

	[Description("The maximum distance (in meters) at which cullable objects are visible to players. Objects beyond this distance are not sent to the client. Per-block 'ImageCullingDistance' (set automatically by the image-to-TextToy tool based on image size, roughly 18-30) overrides this for TextToy images; this value is only the fallback for cullables without that property.")]
	public float CullingDistance { get; set; } = 24f;

	[Description("How often (in seconds) the culling system checks and updates object visibility for all players.")]
	public float CullingUpdateInterval { get; set; } = 0.1f;

	[Description("How often queued object spawn/hide messages are drained.")]
	public float CullingSendInterval { get; set; } = 0.02f;

	[Description("Maximum TextToy/native identities shown or hidden per player during one send tick.")]
	public int CullingObjectsPerUpdate { get; set; } = 2;

	[Description("Extra distance required before an already visible object is hidden. Prevents repeated loading at distance boundaries.")]
	public float CullingHysteresis { get; set; } = 4f;

	[Description("Schematic names (as used in the map's schematic_name field) that should be spawned first during a staggered map load, in the given order. Useful for prioritizing schematics visible during waiting-for-players (e.g. the surface/spawn area) so players see the finished area sooner instead of waiting for the whole map to finish loading.")]
	public List<string> PrioritySchematics { get; set; } = [];

	[Description("Seconds a LOD downgrade (including a full cull) must be continuously requested before it is applied. Upgrades toward higher detail always apply immediately. Prevents a player lingering near a boundary from repeatedly triggering the expensive full re-parse/re-mesh that a fresh show/LOD switch causes.")]
	public float CullingDowngradeDelay { get; set; } = 1.5f;

	[Description("If enabled, a multi-LOD cullable group (e.g. a TextToy image with LODs) never fully despawns while within CullingHardCullDistance - it downgrades to its lowest-detail LOD instead of hiding completely. This avoids the full re-parse/re-mesh cost of a fresh show when a player hovers around the culling boundary. Groups with only one LOD tier are unaffected (they still fully cull at CullingDistance+CullingHysteresis).")]
	public bool CullingPersistLowestLod { get; set; } = true;

	[Description("Distance (in meters) beyond which a persisted lowest-LOD object is fully culled even with CullingPersistLowestLod enabled. Can be overridden per TextToy block via the 'ImageHardCullDistance' property (same way 'ImageCullingDistance' overrides CullingDistance).")]
	public float CullingHardCullDistance { get; set; } = 60f;

	[Description(
	"\n" +
	"# ------------------------------Actions on event------------------------------\n" +
	"# Below is the list of in-game events that you can use to call certain action.\n" +
	"# ----------------------------------------------------------------------------\n" +
	"# \n" +
	"# Map loading/unloading\n" +
	"# You can use it to load or unload a map on demand. It supports basic pattern matching, loading/unloading multiple maps or loading/unloading a random map from a list. Loading the same map again reloads it. Unloading the already unloaded map won't do anything.\n" +
	"# \n" +
	"# - load:CoolMap\n" +
	"#   Loads a map that is called CoolMap\n" +
	"# \n" +
	"# - unload:CoolMap\n" +
	"#   Unloads a map that is called CoolMap\n" +
	"# \n" +
	"# - load:LczMap,HczMap,EzMap\n" +
	"#   Loads ALL of the maps listed. You can also just load them individualy with multiple loads.\n" +
	"# \n" +
	"# - load:VariantA||VariantB||VariantC\n" +
	"#   Loads ONE of the maps listed, chances are equal, you can increase them by typing same map name multiple times\n" +
	"# \n" +
	"# - load:*\n" +
	"#   Loads all saved maps\n" +
	"# \n" +
	"# - unload:*\n" +
	"#   Loads all loaded maps, including the Untitled one\n" +
	"# \n" +
	"# Console command\n" +
	"# You can use it to run a custom console command. Remote Admin commands must be prefixed with \"/\"\n" +
	"# \n" +
	"# - console:buildinfo\n" +
	"#   Prints a buildinfo of the server\n" +
	"# \n" +
	"# - console:/bc 10 MER is cool\n" +
	"#   Sends a broadcast to all players\n"
	)]
	public List<string> OnWaitingForPlayers { get; set; } = [];
	public List<string> OnRoundStarted { get; set; } = [];
	public List<string> OnLczDecontaminationStarted { get; set; } = [];
	public List<string> OnWarheadStarted { get; set; } = [];
	public List<string> OnWarheadStopped { get; set; } = [];
	public List<string> OnWarheadDetonated { get; set; } = [];
}
