using LabApi.Features.Wrappers;
using ProjectMER.Commands.Modifying.Position.SubCommands;
using ProjectMER.Features.Extensions;
using ProjectMER.Features.Objects;
using ProjectMER.Features.Serializable;
using YamlDotNet.Serialization;

namespace ProjectMER.Features.ToolGun;

internal static class EditorHistory
{
    private const int HistoryLimit = 50;
    private static readonly Dictionary<Player, History> Histories = [];

    internal sealed record State(MapSchematic Map, string Id, SerializableObject Data, string? Snapshot)
    {
        internal State Absent() => this with { Snapshot = null };
        internal State CaptureCurrent() => this with { Snapshot = YamlParser.Serializer.Serialize(Data) };
        internal bool SamePlace(State other) => ReferenceEquals(Map, other.Map) && Id == other.Id;
    }

    internal sealed record Change(State Before, State After);

    private sealed record Entry(string Description, string Permission, Change[] Changes);

    private sealed class History
    {
        public readonly List<Entry> Undo = [];
        public readonly List<Entry> Redo = [];
    }

    internal static State Capture(MapEditorObject obj) => Capture(obj.Map, obj.Id, obj.Base);

    internal static State Capture(MapSchematic map, string id, SerializableObject data) =>
        new(map, id, data, YamlParser.Serializer.Serialize(data));

    internal static void RecordEdit(Player player, string description, string permission, IEnumerable<State> before) =>
        Record(player, description, permission, before.Select(state => new Change(state, state.CaptureCurrent())));

    internal static void Record(Player? player, string description, string permission, IEnumerable<Change> changes)
    {
        if (player == null) return;
        Change[] actual = changes.Where(change => change.Before != change.After).ToArray();
        if (actual.Length == 0) return;

        if (!Histories.TryGetValue(player, out History? history))
            Histories[player] = history = new History();

        history.Undo.Add(new Entry(description, permission, actual));
        if (history.Undo.Count > HistoryLimit) history.Undo.RemoveAt(0);
        history.Redo.Clear();
    }

    internal static bool Replay(Player player, bool redo, Func<string, bool> hasPermission, out string response)
    {
        if (Grab.GetGrabbedObjects(player).Count > 0)
        {
            response = "Release the grabbed objects before using undo or redo.";
            return false;
        }
        if (!Histories.TryGetValue(player, out History? history))
        {
            response = "No edit history.";
            return false;
        }

        List<Entry> from = redo ? history.Redo : history.Undo;
        List<Entry> to = redo ? history.Undo : history.Redo;
        if (from.Count == 0)
        {
            response = redo ? "Nothing to redo." : "Nothing to undo.";
            return false;
        }

        Entry entry = from[from.Count - 1];
        if (!hasPermission(entry.Permission))
        {
            response = $"This edit requires {entry.Permission}.";
            return false;
        }
        if (MapUtils.IsAnyStaggeredLoadInProgress)
        {
            response = "Wait until the map finishes loading.";
            return false;
        }

        Change[] changes = redo ? entry.Changes : entry.Changes.Select(change => new Change(change.After, change.Before)).ToArray();
        foreach (Change change in changes)
        {
            if (!IsLoaded(change.Before.Map) || !IsLoaded(change.After.Map) ||
                !Matches(change.Before) || (!change.Before.SamePlace(change.After) && change.After.Map.FindElement(change.After.Id) != null))
            {
                response = $"Cannot replay '{entry.Description}': {change.Before.Map.Name}/{change.Before.Id} changed since this edit.";
                return false;
            }
            if (Grab.IsGrabbing(change.Before.Data))
            {
                response = "Release the grabbed objects before using undo or redo.";
                return false;
            }
        }

        int applied = -1;
        try
        {
            SerializableObject?[] desired = changes.Select(change => Deserialize(change.After)).ToArray();
            for (int i = 0; i < changes.Length; i++)
            {
                applied = i;
                Apply(changes[i], desired[i]);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            for (int i = applied; i >= 0; i--)
            {
                Change reverse = new(changes[i].After, changes[i].Before);
                try { Apply(reverse, Deserialize(reverse.After)); }
                catch (Exception rollbackError) { Logger.Error(rollbackError); }
            }
            ToolGunHandler.RefreshSelections();
            response = $"Could not replay '{entry.Description}'. See the server console.";
            return false;
        }

        from.RemoveAt(from.Count - 1);
        to.Add(entry);
        ToolGunHandler.RefreshSelections();
        response = $"{(redo ? "Redid" : "Undid")} {entry.Description} ({changes.Length} object(s)).";
        return true;
    }

    private static bool IsLoaded(MapSchematic map) =>
        MapUtils.LoadedMaps.TryGetValue(map.Name, out MapSchematic? current) && ReferenceEquals(current, map);

    private static bool Matches(State state)
    {
        SerializableObject? current = state.Map.FindElement(state.Id);
        return state.Snapshot == null ? current == null :
            ReferenceEquals(current, state.Data) && YamlParser.Serializer.Serialize(state.Data) == state.Snapshot;
    }

    private static SerializableObject? Deserialize(State state)
    {
        if (state.Snapshot == null) return null;
        return YamlParser.Deserializer.Deserialize(state.Snapshot, state.Data.GetType()) as SerializableObject ??
            throw new InvalidOperationException($"Invalid undo snapshot for {state.Map.Name}/{state.Id}.");
    }

    internal static bool Relocate(Player player, MapEditorObject obj, MapSchematic map, string id, string description, out string response)
    {
        if (Grab.IsGrabbing(obj.Base) || MapUtils.IsAnyStaggeredLoadInProgress)
        {
            response = "Release the grabbed objects and wait for map loading to finish before relocating.";
            return false;
        }
        if (map.FindElement(id) != null || !ReferenceEquals(obj.Map.FindElement(obj.Id), obj.Base))
        {
            response = "The destination ID is already in use or the source is no longer available.";
            return false;
        }

        State before = Capture(obj);
        State after = before with { Map = map, Id = id };
        Change change = new(before, after);
        try
        {
            Apply(change, Deserialize(after));
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            try { Apply(new Change(after, before), Deserialize(before)); }
            catch (Exception rollbackError) { Logger.Error(rollbackError); }
            response = "Could not relocate the object. See the server console.";
            return false;
        }
        Record(player, description, "mpr.modify", [change]);
        ToolGunHandler.RefreshSelections();
        response = $"Changed {description}. Use mp undo to revert.";
        return true;
    }

    private static void Apply(Change change, SerializableObject? desired)
    {
        State source = change.Before;
        State target = change.After;
        if ((!source.SamePlace(target) || desired == null) && ReferenceEquals(source.Map.FindElement(source.Id), source.Data))
        {
            source.Map.TryRemoveElement(source.Id);
            source.Map.DestroyObject(source.Id);
        }

        if (desired == null) return;

        bool roomChanged = target.Data.Room != desired.Room || target.Data.Index != desired.Index;
        Restore(target.Data, desired);
        if (target.Map.FindElement(target.Id) == null && !target.Map.TryAddElement(target.Id, target.Data))
            throw new InvalidOperationException($"Could not restore {target.Map.Name}/{target.Id}.");
        target.Map.IsDirty = true;
        MapEditorObject? live = target.Map.SpawnedObjects.FirstOrDefault(obj => obj != null && obj.Id == target.Id);
        if (live != null && !roomChanged)
        {
            live.UpdateObjectAndCopies();
        }
        else
        {
            if (live != null) target.Map.DestroyObject(target.Id);
            target.Map.SpawnObject(target.Id, target.Data);
            foreach (MapEditorObject obj in target.Map.SpawnedObjects.ToList())
                if (obj != null && obj.Id == target.Id) IndicatorObject.TrySpawnOrUpdateIndicator(obj);
        }
        if (!source.SamePlace(target))
            ToolGunHandler.RelocateSelections(source, target);
    }

    internal static void Restore(State state)
    {
        if (Deserialize(state) is { } snapshot)
            Restore(state.Data, snapshot);
    }

    private static void Restore(SerializableObject data, SerializableObject snapshot)
    {
        foreach (var property in data.GetType().GetProperties())
        {
            if (property.GetSetMethod() != null && property.GetGetMethod() != null && property.GetIndexParameters().Length == 0 &&
                !Attribute.IsDefined(property, typeof(YamlIgnoreAttribute)))
                property.SetValue(data, property.GetValue(snapshot));
        }
    }

    internal static void Forget(Player player) => Histories.Remove(player);

    internal static void Clear() => Histories.Clear();

    internal static void ForgetMap(MapSchematic map)
    {
        foreach (History history in Histories.Values)
        {
            history.Undo.RemoveAll(entry => entry.Changes.Any(change => change.Before.Map == map || change.After.Map == map));
            history.Redo.RemoveAll(entry => entry.Changes.Any(change => change.Before.Map == map || change.After.Map == map));
        }
    }

    internal static void RebaseMap(MapSchematic source, MapSchematic target)
    {
        foreach (History history in Histories.Values)
        {
            Rebase(history.Undo);
            Rebase(history.Redo);
        }

        void Rebase(List<Entry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                entries[i] = entry with
                {
                    Changes = entry.Changes.Select(change => new Change(
                        change.Before.Map == source ? change.Before with { Map = target } : change.Before,
                        change.After.Map == source ? change.After with { Map = target } : change.After)).ToArray()
                };
            }
        }
    }
}
