using System;
using LabApi.Events.Arguments.Interfaces;
using LabApi.Features.Wrappers;
using ProjectMER.Features.Objects;

namespace ProjectMER.Events.Arguments;

public class EventInvokeMarkerInvokedEventArgs : EventArgs, IPlayerEvent
{
    public EventInvokeMarkerInvokedEventArgs(Player player, EventInvokeMarkerObject marker)
    {
        Player = player;
        Marker = marker;
    }

    public Player Player { get; }
    public EventInvokeMarkerObject Marker { get; }
    public string Id => Marker.Id;
    public string Tag => Marker.Tag;
    public float Distance => Marker.Distance;
    public MapEditorObject? MapObject => Marker.MapObject;
    public SchematicObject? Schematic => Marker.Schematic;
}
