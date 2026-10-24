using LabApi.Events;
using ProjectMER.Events.Arguments;

namespace ProjectMER.Events.Handlers;

public static class CustomHitbox
{
    public static event LabEventHandler<CustomHitboxDamagingEventArgs> Damaging;
    public static event LabEventHandler<CustomHitboxDamagedEventArgs> Damaged;
    public static event LabEventHandler<CustomHitboxDiedEventArgs> Died;

    internal static void OnDamaging(CustomHitboxDamagingEventArgs ev) => Damaging.InvokeEvent(ev);
    internal static void OnDamaged(CustomHitboxDamagedEventArgs ev) => Damaged.InvokeEvent(ev);
    internal static void OnDied(CustomHitboxDiedEventArgs ev) => Died.InvokeEvent(ev);
}
