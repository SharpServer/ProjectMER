namespace ProjectMER.Features.Interfaces;

/// <summary>
/// Optional integration point that lets an external plugin (e.g. one that interprets
/// <c>ObjectPrefabMarker.PrefabType</c>/<c>Options</c>) expose its registered prefab types
/// and their option schemas to ProjectMER's tooling.
/// </summary>
public interface IObjectPrefabInfoProvider
{
	IReadOnlyList<ObjectPrefabTypeInfo> GetPrefabTypes();

	bool TryGetOptionDefinitions(string prefabTypeName, out IReadOnlyList<ObjectPrefabOptionInfo> definitions, out string error);
}

/// <summary>
/// Metadata for one registered prefab type.
/// </summary>
public sealed class ObjectPrefabTypeInfo
{
	public string Key { get; init; } = string.Empty;

	public string DisplayName { get; init; } = string.Empty;

	public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Static definition (type, default value, constraint) for one option key of a prefab type.
/// Does not carry a "current value" - callers cross-reference this against their own
/// <c>Options</c> dictionary to display the effective value.
/// </summary>
public sealed class ObjectPrefabOptionInfo
{
	public string Name { get; init; } = string.Empty;

	public string ValueType { get; init; } = string.Empty;

	public string? DefaultValue { get; init; }

	public string? ConstraintDescription { get; init; }
}
