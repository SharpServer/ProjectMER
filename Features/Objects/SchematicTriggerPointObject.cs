using UnityEngine;

namespace ProjectMER.Features.Objects;

public class SchematicTriggerPointObject : MonoBehaviour
{
    public string Id = string.Empty;

    public string Tag = string.Empty;

    public SchematicObject? Schematic { get; internal set; }
}
