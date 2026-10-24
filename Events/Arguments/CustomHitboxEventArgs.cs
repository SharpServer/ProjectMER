using System;
using LabApi.Events.Arguments.Interfaces;
using PlayerStatsSystem;
using ProjectMER.Features.Objects;
using UnityEngine;

namespace ProjectMER.Events.Arguments;

public sealed class CustomHitboxDamagingEventArgs : EventArgs, ICancellableEvent
{
    public CustomHitboxDamagingEventArgs(SchematicCustomHitbox hitbox, float damage, DamageHandlerBase handler, Vector3 hitPosition)
    {
        Hitbox = hitbox;
        Damage = damage;
        Handler = handler;
        HitPosition = hitPosition;
        IsAllowed = true;
    }

    public SchematicCustomHitbox Hitbox { get; }
    public float Damage { get; set; }
    public DamageHandlerBase Handler { get; }
    public Vector3 HitPosition { get; }
    public bool IsAllowed { get; set; }
}

public sealed class CustomHitboxDamagedEventArgs : EventArgs
{
    public CustomHitboxDamagedEventArgs(SchematicCustomHitbox hitbox, float damage, DamageHandlerBase handler, Vector3 hitPosition)
    {
        Hitbox = hitbox;
        Damage = damage;
        Handler = handler;
        HitPosition = hitPosition;
    }

    public SchematicCustomHitbox Hitbox { get; }
    public float Damage { get; }
    public DamageHandlerBase Handler { get; }
    public Vector3 HitPosition { get; }
}

public sealed class CustomHitboxDiedEventArgs : EventArgs
{
    public CustomHitboxDiedEventArgs(SchematicCustomHitbox hitbox, DamageHandlerBase handler, Vector3 hitPosition)
    {
        Hitbox = hitbox;
        Handler = handler;
        HitPosition = hitPosition;
    }

    public SchematicCustomHitbox Hitbox { get; }
    public DamageHandlerBase Handler { get; }
    public Vector3 HitPosition { get; }
}
