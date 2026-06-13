using System.Reflection;
using LabApi.Features.Wrappers;

namespace ProjectMER.Features.Extensions;

internal static class ExiledRoomTypeResolver
{
    private static bool _initialized;
    private static bool _available;
    private static PropertyInfo? _roomsProperty;
    private static PropertyInfo? _baseProperty;
    private static PropertyInfo? _typeProperty;

    public static void TryInitialize()
    {
        if (_initialized)
            return;

        _initialized = true;

        Assembly? exiledApi = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(x => x.GetName().Name == "Exiled.API");
        if (exiledApi == null)
            return;

        Type? roomType = exiledApi.GetType("Exiled.API.Features.Room");
        if (roomType == null)
            return;

        _roomsProperty = roomType.GetProperty("List", BindingFlags.Public | BindingFlags.Static);
        _baseProperty = roomType.GetProperty("Base", BindingFlags.Public | BindingFlags.Instance);
        _typeProperty = roomType.GetProperty("Type", BindingFlags.Public | BindingFlags.Instance);

        _available = _roomsProperty != null && _baseProperty != null && _typeProperty != null;
        if (_available)
            Logger.Info("[RoomTypeResolver] Using EXILED Room.Type as primary room type source.");
    }

    public static bool TryGetRoomTypeName(Room room, out string typeName)
    {
        typeName = string.Empty;
        TryInitialize();

        if (!_available || room.Base == null)
            return false;

        if (_roomsProperty!.GetValue(null) is not System.Collections.IEnumerable rooms)
            return false;

        foreach (object exiledRoom in rooms)
        {
            object? baseRoom = _baseProperty!.GetValue(exiledRoom);
            if (!ReferenceEquals(baseRoom, room.Base))
                continue;

            object? value = _typeProperty!.GetValue(exiledRoom);
            if (value == null)
                return false;

            typeName = value.ToString();
            return !string.IsNullOrWhiteSpace(typeName);
        }

        return false;
    }
}
