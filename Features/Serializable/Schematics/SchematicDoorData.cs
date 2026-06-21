using Interactables.Interobjects.DoorUtils;
using ProjectMER.Features.Enums;
using UnityEngine;

namespace ProjectMER.Features.Serializable.Schematics;

public class SchematicDoorData
{
    public string Name { get; set; } = string.Empty;

    public int ObjectId { get; set; }

    public int ParentId { get; set; }

    public Vector3 Position { get; set; }

    public Vector3 Rotation { get; set; }

    public Vector3 Scale { get; set; } = Vector3.one;

    public DoorType DoorType { get; set; } = DoorType.Lcz;

    public bool IsOpen { get; set; }

    public bool IsLocked { get; set; }

    public DoorPermissionFlags RequiredPermissions { get; set; } = DoorPermissionFlags.None;

    public bool RequireAll { get; set; } = true;
}

public class SchematicDoorJsonData
{
    public string Name { get; set; } = string.Empty;

    public int ObjectId { get; set; }

    public int ParentId { get; set; }

    public Vector3 Position { get; set; }

    public Vector3 Rotation { get; set; }

    public Vector3 Scale { get; set; } = Vector3.one;

    public int DoorType { get; set; }

    public bool IsOpen { get; set; }

    public bool IsLocked { get; set; }

    public int RequiredPermissions { get; set; }

    public bool RequireAll { get; set; } = true;

    public SchematicDoorData ToDoorData() => new()
    {
        Name = Name,
        ObjectId = ObjectId,
        ParentId = ParentId,
        Position = Position,
        Rotation = Rotation,
        Scale = Scale,
        DoorType = (global::ProjectMER.Features.Enums.DoorType)DoorType,
        IsOpen = IsOpen,
        IsLocked = IsLocked,
        RequiredPermissions = (DoorPermissionFlags)RequiredPermissions,
        RequireAll = RequireAll,
    };
}
