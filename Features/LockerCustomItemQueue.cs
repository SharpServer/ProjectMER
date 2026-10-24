using UnityEngine;
using LockerChamber = LabApi.Features.Wrappers.LockerChamber;

namespace ProjectMER.Features;

/// <summary>Native chambers can defer content creation until their first opening.</summary>
internal sealed class LockerCustomItemQueue : MonoBehaviour
{
    private LockerChamber? chamber;
    private readonly List<string> pending = [];

    internal void Add(LockerChamber source, string key)
    {
        if (chamber == null)
        {
            chamber = source;
            source.Base.OnDoorStatusSet += Opened;
        }
        pending.Add(key);
    }

    internal void Clear() => pending.Clear();

    private void Opened()
    {
        if (chamber == null || chamber.Base == null || !chamber.Base.IsOpen) return;
        string[] items = pending.ToArray();
        pending.Clear();
        foreach (string key in items)
            if (!LockerConfigurator.TrySpawnCustomItemInChamber(chamber, key))
                Logger.Warn($"Cannot spawn custom locker item '{key}'. Check the Slafight class name.");
    }

    private void OnDestroy()
    {
        if (chamber?.Base != null) chamber.Base.OnDoorStatusSet -= Opened;
        chamber = null;
        pending.Clear();
    }
}
