using Mirror;
using PlayerStatsSystem;
using ProjectMER.Events.Arguments;
using ProjectMER.Features.Extensions;
using UnityEngine;

namespace ProjectMER.Features.Objects;

/// <summary>A group of schematic colliders sharing health and a network hit identity.</summary>
public sealed class SchematicCustomHitbox : MonoBehaviour
{
    private static uint _nextFallbackNetworkId = uint.MaxValue;
    private readonly List<SchematicBlock> _blocks = [];
    private readonly List<Collider> _colliders = [];
    private uint _fallbackNetworkId;
    private bool _dead;

    public string Group { get; private set; } = string.Empty;
    public string Tag { get; private set; } = string.Empty;
    public float MaxHealth { get; private set; } = 100f;
    public float Health { get; private set; } = 100f;
    public float DamageMultiplier { get; private set; } = 1f;
    public bool Enabled { get; private set; } = true;
    public bool IsAlive => !_dead;
    public SchematicObject Schematic { get; private set; } = null!;
    public IReadOnlyList<SchematicBlock> Blocks => _blocks;
    public IReadOnlyList<Collider> Colliders => _colliders;
    public event Action<CustomHitboxDamagingEventArgs>? Damaging;
    public event Action<CustomHitboxDamagedEventArgs>? Damaged;
    public event Action<CustomHitboxDiedEventArgs>? Died;

    public uint NetworkId
    {
        get
        {
            foreach (SchematicBlock block in _blocks)
            foreach (NetworkIdentity identity in block.GetComponentsInChildren<NetworkIdentity>(true))
                if (identity != null && identity.netId != 0)
                    return identity.netId;

            return _fallbackNetworkId;
        }
    }

    internal static void Attach(SchematicBlock block)
    {
        string group = block.Data.Properties.GetString("CustomHitboxGroup").Trim();
        if (group.Length == 0)
            return;

        SchematicCustomHitbox hitbox = block.Schematic.GetComponents<SchematicCustomHitbox>()
            .FirstOrDefault(candidate => string.Equals(candidate.Group, group, StringComparison.OrdinalIgnoreCase));
        if (hitbox == null)
        {
            hitbox = block.Schematic.gameObject.AddComponent<SchematicCustomHitbox>();
            hitbox.Schematic = block.Schematic;
            hitbox.Group = group;
            hitbox.Tag = block.Data.Properties.GetString("CustomHitboxTag").Trim();
            hitbox.MaxHealth = Math.Max(1f, block.Data.Properties.GetFloat("CustomHitboxMaxHealth", 100f));
            hitbox.Health = hitbox.MaxHealth;
            hitbox.DamageMultiplier = Math.Max(0f, block.Data.Properties.GetFloat("CustomHitboxDamageMultiplier", 1f));
            hitbox._fallbackNetworkId = _nextFallbackNetworkId--;
        }

        hitbox.AddBlock(block);
    }

    public static SchematicCustomHitbox? Get(SchematicObject schematic, string group) =>
        schematic.GetComponents<SchematicCustomHitbox>()
            .FirstOrDefault(candidate => string.Equals(candidate.Group, group, StringComparison.OrdinalIgnoreCase));

    public static SchematicCustomHitbox Create(
        SchematicObject schematic,
        string group,
        IEnumerable<SchematicBlock> blocks,
        float maxHealth = 100f,
        float damageMultiplier = 1f,
        string tag = "")
    {
        SchematicCustomHitbox hitbox = Get(schematic, group) ?? schematic.gameObject.AddComponent<SchematicCustomHitbox>();
        if (hitbox.Schematic == null)
        {
            hitbox.Schematic = schematic;
            hitbox.Group = group.Trim();
            hitbox.Tag = tag.Trim();
            hitbox._fallbackNetworkId = _nextFallbackNetworkId--;
        }

        hitbox.SetMaxHealth(maxHealth);
        hitbox.SetDamageMultiplier(damageMultiplier);
        foreach (SchematicBlock block in blocks)
            hitbox.AddBlock(block);
        return hitbox;
    }

    public void SetMaxHealth(float maxHealth, bool refill = true)
    {
        MaxHealth = Math.Max(1f, maxHealth);
        if (refill)
            ResetHealth();
        else
            Health = Math.Min(Health, MaxHealth);
    }

    public void ResetHealth()
    {
        Health = MaxHealth;
        _dead = false;
    }

    public void SetDamageMultiplier(float multiplier) => DamageMultiplier = Math.Max(0f, multiplier);

    public void SetEnabled(bool enabled) => Enabled = enabled;

    internal bool ApplyDamage(float damage, DamageHandlerBase handler, Vector3 hitPosition)
    {
        if (!Enabled || _dead || damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage))
            return false;

        var damaging = new CustomHitboxDamagingEventArgs(this, damage * DamageMultiplier, handler, hitPosition);
        Damaging?.Invoke(damaging);
        Events.Handlers.CustomHitbox.OnDamaging(damaging);
        if (!damaging.IsAllowed || damaging.Damage <= 0f || float.IsNaN(damaging.Damage) || float.IsInfinity(damaging.Damage))
            return false;

        Health = Math.Max(0f, Health - damaging.Damage);
        var damaged = new CustomHitboxDamagedEventArgs(this, damaging.Damage, handler, hitPosition);
        Damaged?.Invoke(damaged);
        Events.Handlers.CustomHitbox.OnDamaged(damaged);
        if (Health <= 0f)
        {
            _dead = true;
            var died = new CustomHitboxDiedEventArgs(this, handler, hitPosition);
            Died?.Invoke(died);
            Events.Handlers.CustomHitbox.OnDied(died);
        }

        return true;
    }

    public void AddBlock(SchematicBlock block)
    {
        if (!_blocks.Contains(block))
            _blocks.Add(block);

        int glassLayer = LayerMask.NameToLayer("Glass");
        foreach (Collider collider in block.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null || collider.GetComponentInParent<SchematicBlock>() != block ||
                collider.TryGetComponent<IDestructible>(out _))
                continue;

            var receiver = collider.gameObject.AddComponent<SchematicCustomHitboxReceiver>();
            receiver.Initialize(this);
            if (glassLayer >= 0)
                collider.gameObject.layer = glassLayer;
            _colliders.Add(collider);
        }
    }
}

public sealed class SchematicCustomHitboxReceiver : MonoBehaviour, IDestructible
{
    private SchematicCustomHitbox _owner = null!;
    public uint NetworkId => _owner?.NetworkId ?? 0;
    public Vector3 CenterOfMass => TryGetComponent(out Collider collider) ? collider.bounds.center : transform.position;
    internal void Initialize(SchematicCustomHitbox owner) => _owner = owner;
    public bool Damage(float damage, DamageHandlerBase handler, Vector3 exactHitPos) =>
        _owner != null && _owner.ApplyDamage(damage, handler, exactHitPos);
}
