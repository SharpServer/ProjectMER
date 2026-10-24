using UnityEngine;

namespace ProjectMER.Features;

/// <summary>
/// Controls optional visual overrides applied while a schematic is being spawned.
/// </summary>
public sealed class SchematicSpawnOptions
{
	/// <summary>
	/// Gets whether this schematic may be converted to client-side primitives by the
	/// integrated MEROptimizer backend. Disable this for moving or frequently changing previews.
	/// </summary>
	public bool OptimizePrimitives { get; init; } = true;

	/// <summary>
	/// Gets the color used for every visible primitive in the schematic.
	/// When null, each primitive keeps the color exported with the schematic.
	/// </summary>
	public Color? PrimitiveColorOverride { get; init; }

	/// <summary>
	/// Creates an appearance suitable for a placement preview: solid white with
	/// the requested opacity. This changes appearance only; collision and scripted
	/// behavior remain unchanged.
	/// </summary>
	public static SchematicSpawnOptions WhitePreview(float opacity = 0.35f) => new()
	{
		OptimizePrimitives = false,
		PrimitiveColorOverride = new Color(1f, 1f, 1f, Mathf.Clamp01(opacity)),
	};
}
