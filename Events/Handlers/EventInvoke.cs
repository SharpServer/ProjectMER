using LabApi.Events;
using LabApi.Features.Wrappers;
using ProjectMER.Events.Arguments;

namespace ProjectMER.Events.Handlers;

/// <summary>
/// Provides a named event bus that can be raised independently of map markers.
/// </summary>
public static class EventInvoke
{
    /// <summary>
    /// Raised whenever <see cref="Invoke"/> is called or an EventInvokeMarker is entered.
    /// </summary>
    public static event LabEventHandler<EventInvokedEventArgs> Invoked;

    /// <summary>
    /// Raises a named event for subscribers.
    /// </summary>
    public static void Invoke(string name, Player? player = null, object? source = null, object? data = null)
    {
        Invoked.InvokeEvent(new EventInvokedEventArgs(name, player, source, data));
    }
}
