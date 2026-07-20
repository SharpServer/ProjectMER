using LabApi.Features.Wrappers;
using MapGeneration;
using NorthwoodLib.Pools;
using ProjectMER.Features.Serializable;
using UnityEngine;
using ExiledRoom = Exiled.API.Features.Room;
using RoomType = Exiled.API.Enums.RoomType;

namespace ProjectMER.Features.Extensions;

public static class RoomExtensions
{
	public static Room GetRoomAtPosition(Vector3 position) => Room.TryGetRoomAtPosition(position, out Room? room) ? room : Room.List.First(x => x.Base != null && x.Name == RoomName.Outside);

	public static string GetRoomStringId(this Room room) => room.FindRoomTypeName();

	public static List<Room> GetRooms(this SerializableObject serializableObject)
	{
		if (IsOutsideRoomId(serializableObject.Room))
			return ListPool<Room>.Shared.Rent(Room.List.Where(x => x.Base != null && x.Name == RoomName.Outside));

		return ListPool<Room>.Shared.Rent(Room.List.Where(x => x.Base != null && x.FindRoomTypeName().Equals(serializableObject.Room, StringComparison.OrdinalIgnoreCase)));
	}

	public static int GetRoomIndex(this Room room)
	{
		string roomType = room.FindRoomTypeName();
		List<Room> list = ListPool<Room>.Shared.Rent(Room.List.Where(x => x.Base != null && x.FindRoomTypeName() == roomType));
		int index = list.IndexOf(room);
		ListPool<Room>.Shared.Return(list);
		return index;
	}

	public static Vector3 GetAbsolutePosition(this Room? room, Vector3 position)
	{
		if (room is null || room.Name == RoomName.Outside)
			return position;

		return room.Transform.TransformPoint(position);
	}

	public static Quaternion GetAbsoluteRotation(this Room? room, Vector3 eulerAngles)
	{
		if (room is null || room.Name == RoomName.Outside)
			return Quaternion.Euler(eulerAngles);

		return room.Transform.rotation * Quaternion.Euler(eulerAngles);
	}

	/// <summary>
	/// Determines the <see cref="RoomType"/> of a room by delegating to EXILED's room wrapper,
	/// so identification stays in sync with EXILED instead of a local name-to-type mapping.
	/// </summary>
	public static RoomType FindRoomType(this Room room)
	{
		if (room.Base == null)
			return RoomType.Unknown;

		if (room.Name == RoomName.Outside)
			return RoomType.Surface;

		ExiledRoom? exiledRoom = ExiledRoom.Get(room.Base);
		return exiledRoom == null ? RoomType.Unknown : exiledRoom.Type;
	}

	public static string FindRoomTypeName(this Room room) => room.FindRoomType().ToString();

	private static bool IsOutsideRoomId(string? roomId)
	{
		if (roomId == null || roomId.Trim().Length == 0)
			return true;

		return roomId.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
			|| roomId.Equals("Surface", StringComparison.OrdinalIgnoreCase)
			|| roomId.Equals("Outside", StringComparison.OrdinalIgnoreCase);
	}
}
