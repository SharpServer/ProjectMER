using UnityEngine;

namespace ProjectMER.Features.Objects;

/// <summary>Access settings attached to the actual spawned native door.</summary>
public sealed class DoorAccessFeedback : MonoBehaviour
{
    public string DenialMessage { get; set; } = string.Empty;
    public string SecretCode { get; set; } = string.Empty;
}
