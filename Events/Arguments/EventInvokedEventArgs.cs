using System;
using LabApi.Features.Wrappers;
using ProjectMER.Features.Objects;

namespace ProjectMER.Events.Arguments;

/// <summary>
/// Contains data for a named ProjectMER event raised by a marker or arbitrary plugin code.
/// </summary>
public sealed class EventInvokedEventArgs : EventArgs
{
    public EventInvokedEventArgs(string name, Player? player = null, object? source = null, object? data = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("An event name is required.", nameof(name));

        Name = name;
        Player = player;
        Source = source;
        Data = data;
    }

    public string Name { get; }

    public Player? Player { get; }

    public object? Source { get; }

    public object? Data { get; }

    public EventInvokeMarkerObject? Marker => Source as EventInvokeMarkerObject;

    public bool IsMarkerEvent => Marker != null;
}
