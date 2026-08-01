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
	[Description("Enables ProjectMER's primitive optimization. Do not enable this while the external MEROptimizer plugin is active; applies from the next round.")]
	// External MEROptimizer must not run simultaneously; this setting applies on the next round.
	public bool EnablePrimitiveOptimization { get; set; } = false;

	[YamlMember(Alias = "primitive_culling_distance")]
	[Description("Maximum distance (in meters) at which primitive objects are considered visible.")]
	public float PrimitiveCullingDistance { get; set; } = 50f;

	[YamlMember(Alias = "primitive_schematic_culling_distances")]
	[Description("Optional per-schematic primitive culling distance overrides.")]
	public Dictionary<string, float> PrimitiveSchematicCullingDistances { get; set; } = [];

	[YamlMember(Alias = "primitive_cluster_size")]
	[Description("Spatial cell size (in meters) used to cluster primitive objects.")]
	public float PrimitiveClusterSize { get; set; } = 2.5f;

	[YamlMember(Alias = "primitive_cluster_max_objects")]
	[Description("Maximum number of primitive objects assigned to one spatial cluster.")]
	public int PrimitiveClusterMaxObjects { get; set; } = 100;

	[YamlMember(Alias = "primitive_always_visible_size")]
	[Description("Primitive objects at or above this size remain visible regardless of distance.")]
	public float PrimitiveAlwaysVisibleSize { get; set; } = 10f;

	[YamlMember(Alias = "primitive_objects_per_update")]
	[Description("Maximum primitive objects processed per player update.")]
	public int PrimitiveObjectsPerUpdate { get; set; } = 2;

	[YamlMember(Alias = "primitive_global_objects_per_update")]
	[Description("Maximum primitive objects processed globally per update.")]
	public int PrimitiveGlobalObjectsPerUpdate { get; set; } = 32;

	[YamlMember(Alias = "culling_global_objects_per_update")]
	[Description("Maximum culling objects processed globally per update.")]
	public int CullingGlobalObjectsPerUpdate { get; set; } = 64;

	[YamlMember(Alias = "culling_spatial_cell_size")]
	[Description("Spatial cell size (in meters) used by culling.")]
	public float CullingSpatialCellSize { get; set; } = 50f;

	[YamlMember(Alias = "primitive_finalize_frame_budget_ms")]
	[Description("Maximum time (in milliseconds) spent finalizing primitive optimization work in one frame.")]
	public float PrimitiveFinalizeFrameBudgetMs { get; set; } = 2f;

	[YamlMember(Alias = "primitive_cluster_worker_count")]
	[Description("Number of worker tasks used to build primitive clusters.")]
	public int PrimitiveClusterWorkerCount { get; set; } = 2;

	[YamlMember(Alias = "primitive_excluded_schematic_name_patterns")]
	[Description("Schematic name patterns excluded from primitive optimization.")]
	public List<string> PrimitiveExcludedSchematicNamePatterns { get; set; } = [];

	[YamlMember(Alias = "primitive_assume_static_schematic_name_patterns")]
	[Description("Schematic name patterns whose otherwise-unmarked primitives may be treated as static. Only use this for schematics that are never moved, animated, or visibility-controlled at runtime.")]
	public List<string> PrimitiveAssumeStaticSchematicNamePatterns { get; set; } = [];

	[Description("Enables distance-based culling for TextToys. When enabled, large text images are only sent to players within CullingDistance.")]
	public bool EnableCulling { get; set; } = true;

	[Description("The maximum distance (in meters) at which cullable objects are visible to players. Objects beyond this distance are not sent to the client. Per-block 'ImageCullingDistance' (set automatically by the image-to-TextToy tool based on image size, roughly 18-30) overrides this for TextToy images; this value is only the fallback for cullables without that property.")]
	public float CullingDistance { get; set; } = 24f;

	[Description("How often (in seconds) the culling system checks and updates object visibility for all players.")]
	public float CullingUpdateInterval { get; set; } = 0.25f;

	[Description("How often queued object spawn/hide messages are drained.")]
	public float CullingSendInterval { get; set; } = 0.05f;

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
