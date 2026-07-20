using LabApi.Events;
using ProjectMER.Events.Arguments;

namespace ProjectMER.Events.Handlers;

public static class EventInvokeMarker
{
    public static event LabEventHandler<EventInvokeMarkerInvokedEventArgs> Invoked;

    internal static void OnInvoked(EventInvokeMarkerInvokedEventArgs ev) => Invoked.InvokeEvent(ev);
}
