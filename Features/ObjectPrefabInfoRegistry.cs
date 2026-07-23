using ProjectMER.Features.Interfaces;

namespace ProjectMER.Features;

/// <summary>
/// Holds the optional <see cref="IObjectPrefabInfoProvider"/> registered by an external plugin.
/// ProjectMER itself has no knowledge of who registers it or what prefab types exist.
/// </summary>
public static class ObjectPrefabInfoRegistry
{
	public static IObjectPrefabInfoProvider? Provider { get; set; }
}
