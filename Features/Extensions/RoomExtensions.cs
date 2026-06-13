// -----------------------------------------------------------------------
// FindRoomType method and its GameObject name-to-RoomType mapping are
// derived from EXILED (https://github.com/ExMod-Team/EXILED)
// Copyright (c) ExMod Team. Licensed under the CC BY-SA 3.0 license.
// -----------------------------------------------------------------------

using LabApi.Features.Wrappers;
using MapGeneration;
using NorthwoodLib.Pools;
using ProjectMER.Features.Enums;
using ProjectMER.Features.Serializable;
using UnityEngine;

namespace ProjectMER.Features.Extensions;

public static class RoomExtensions
{
	public static Room GetRoomAtPosition(Vector3 position) => Room.TryGetRoomAtPosition(position, out Room? room) ? room : Room.List.First(x => x.Base != null && x.Name == RoomName.Outside);

	public static string GetRoomStringId(this Room room) => room.Name == RoomName.Outside ? nameof(RoomType.Surface) : room.FindRoomTypeName();

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
	/// Determines the <see cref="RoomType"/> of a room based on its GameObject name.
	/// Equivalent to Exiled's FindType method.
	/// </summary>
	public static RoomType FindRoomType(this Room room)
	{
		return Enum.TryParse(room.FindRoomTypeName(), true, out RoomType roomType)
			? roomType
			: RoomType.Unknown;
	}

	public static string FindRoomTypeName(this Room room)
	{
		if (room.Name == RoomName.Outside)
			return nameof(RoomType.Surface);

		if (ExiledRoomTypeResolver.TryGetRoomTypeName(room, out string exiledRoomType))
			return exiledRoomType;

		return room.FindFallbackRoomType().ToString();
	}

	private static bool IsOutsideRoomId(string? roomId)
	{
		if (roomId == null || roomId.Trim().Length == 0)
			return true;

		return roomId.Equals(nameof(RoomType.Unknown), StringComparison.OrdinalIgnoreCase) ||
		       roomId.Equals(nameof(RoomType.Surface), StringComparison.OrdinalIgnoreCase) ||
		       roomId.Equals("Outside", StringComparison.OrdinalIgnoreCase);
	}

	private static RoomType FindFallbackRoomType(this Room room)
	{
		if (room.Base == null)
			return RoomType.Unknown;

		string roomName = RemoveBracketsOnEndOfName(room.GameObject.name);

		return roomName switch
		{
			"PocketWorld" => RoomType.Pocket,
			"Outside" => RoomType.Surface,

			// Light Containment Zone
			"LCZ_Cafe" => RoomType.LczCafe,
			"LCZ_Toilets" => RoomType.LczToilets,
			"LCZ_TCross" => RoomType.LczTCross,
			"LCZ_Airlock" => RoomType.LczAirlock,
			"LCZ_ChkpA" => RoomType.LczCheckpointA,
			"LCZ_ChkpB" => RoomType.LczCheckpointB,
			"LCZ_Plants" => RoomType.LczPlants,
			"LCZ_Straight" => RoomType.LczStraight,
			"LCZ_Armory" => RoomType.LczArmory,
			"LCZ_Crossing" => RoomType.LczCrossing,
			"LCZ_Curve" => RoomType.LczCurve,
			"LCZ_173" => RoomType.Lcz173,
			"LCZ_330" => RoomType.Lcz330,
			"LCZ_372" => RoomType.LczGlassBox,
			"LCZ_914" => RoomType.Lcz914,
			"LCZ_ClassDSpawn" => RoomType.LczClassDSpawn,

			// Heavy Containment Zone
			"HCZ_Nuke" => RoomType.HczNuke,
			"HCZ_TArmory" => RoomType.HczArmory,
			"HCZ_MicroHID_New" => RoomType.HczHid,
			"HCZ_Crossroom_Water" => RoomType.HczCrossRoomWater,
			"HCZ_IncineratorWayside" => RoomType.HczIncineratorWayside,
			"HCZ_Testroom" => RoomType.HczTestRoom,
			"HCZ_049" => RoomType.Hcz049,
			"HCZ_079" => RoomType.Hcz079,
			"HCZ_096" => RoomType.Hcz096,
			"HCZ_106_Rework" => RoomType.Hcz106,
			"HCZ_939" => RoomType.Hcz939,
			"HCZ_Tesla_Rework" => RoomType.HczTesla,
			"HCZ_Curve" => RoomType.HczCurve,
			"HCZ_Crossing" => RoomType.HczCrossing,
			"HCZ_Intersection" => RoomType.HczIntersection,
			"HCZ_Intersection_Junk" => RoomType.HczIntersectionJunk,
			"HCZ_Corner_Deep" => RoomType.HczCornerDeep,
			"HCZ_Straight" => RoomType.HczStraight,
			"HCZ_Straight_C" => RoomType.HczStraightC,
			"HCZ_Straight_PipeRoom" => RoomType.HczStraightPipeRoom,
			"HCZ_Straight Variant" => RoomType.HczStraightVariant,
			"HCZ_ChkpA" => RoomType.HczElevatorA,
			"HCZ_ChkpB" => RoomType.HczElevatorB,
			"HCZ_127" => RoomType.Hcz127,
			"HCZ_ServerRoom" => RoomType.HczServerRoom,
			"HCZ_Intersection_Ramp" => RoomType.HczLoadingBay,

			// Entrance Zone
			"EZ_GateA" => RoomType.EzGateA,
			"EZ_GateB" => RoomType.EzGateB,
			"EZ_ThreeWay" => RoomType.EzTCross,
			"EZ_Crossing" => RoomType.EzCrossing,
			"EZ_Curve" => RoomType.EzCurve,
			"EZ_PCs" => RoomType.EzPcs,
			"EZ_upstairs" => RoomType.EzUpstairsPcs,
			"EZ_Intercom" => RoomType.EzIntercom,
			"EZ_Smallrooms2" => RoomType.EzSmallrooms,
			"EZ_PCs_small" => RoomType.EzDownstairsPcs,
			"EZ_Chef" => RoomType.EzChef,
			"EZ_Endoof" => RoomType.EzVent,
			"EZ_CollapsedTunnel" => RoomType.EzCollapsedTunnel,
			"EZ_Smallrooms1" => RoomType.EzConference,
			"EZ_Straight" => RoomType.EzStraight,
			"EZ_StraightColumn" => RoomType.EzStraightColumn,
			"EZ_Cafeteria" => RoomType.EzCafeteria,
			"EZ_Shelter" => RoomType.EzShelter,

			// Position-dependent rooms
			"EZ_HCZ_Checkpoint Part" => room.Transform.position.z > 95 ? RoomType.EzCheckpointHallwayA : RoomType.EzCheckpointHallwayB,
			"HCZ_EZ_Checkpoint Part" => room.Transform.position.z > 95 ? RoomType.HczEzCheckpointA : RoomType.HczEzCheckpointB,

			_ => RoomType.Unknown,
		};
	}

	private static string RemoveBracketsOnEndOfName(string name)
	{
		int bracketStart = name.IndexOf('(');
		if (bracketStart != -1)
			name = name.Substring(0, bracketStart).TrimEnd();

		return name;
	}
}
