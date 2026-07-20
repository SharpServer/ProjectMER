using System.ComponentModel;

namespace ProjectMER.Configs;

public class Config
{
	[Description("Enables FileSystemWatcher in this plugin. What it does is when you manually change values in a currently loaded map file, after saving the file the plugin will automatically reload the map in-game with the new changes so you won't need to do it yourself.")]
    public bool EnableFileSystemWatcher { get; set; } = false;

	[Description("Whether the object will be auto selected when spawning it.")]
	public bool AutoSelect { get; set; } = true;

	[Description("Enables ProjectMER's TextToy/image culling.")]
	public bool EnableTextToyOptimization { get; set; } = true;

	[Description("Enables distance-based culling for TextToys. When enabled, large text images are only sent to players within CullingDistance.")]
	public bool EnableCulling { get; set; } = true;

	[Description("The maximum distance (in meters) at which cullable objects are visible to players. Objects beyond this distance are not sent to the client.")]
	public float CullingDistance { get; set; } = 60f;

	[Description("How often (in seconds) the culling system checks and updates object visibility for all players.")]
	public float CullingUpdateInterval { get; set; } = 0.25f;

	[Description("How often queued object spawn/hide messages are drained.")]
	public float CullingSendInterval { get; set; } = 0.05f;

	[Description("Maximum TextToy/native identities shown or hidden per player during one send tick.")]
	public int CullingObjectsPerUpdate { get; set; } = 2;

	[Description("Extra distance required before an already visible object is hidden. Prevents repeated loading at distance boundaries.")]
	public float CullingHysteresis { get; set; } = 10f;

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
