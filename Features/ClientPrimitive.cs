using System;
using System.Collections.Generic;
using AdminToys;
using CentralAuth;
using Mirror;
using ProjectMER.Features.Objects;
using UnityEngine;

namespace ProjectMER.Features;

/// <summary>
/// Immutable spawn information for a primitive which is kept server-side and sent directly to
/// interested clients. The native object remains the authority for collisions and map scripts.
/// </summary>
public sealed class ClientPrimitive
{
    // Mirror's reliable channel accepts very large fragmented messages, but a vanilla client can
    // still stall while deserializing one. Oversized primitives remain native instead.
    private const int MaximumSafeSpawnMessageBytes = 24 * 1024;
    private readonly SpawnMessage _spawnMessage;
    private readonly ObjectDestroyMessage _destroyMessage;
    private readonly Dictionary<int, NetworkConnectionToClient> _viewers = [];

    private ClientPrimitive(
        SpawnMessage spawnMessage,
        int spawnMessageSize,
        ObjectDestroyMessage destroyMessage,
        int destroyMessageSize,
        SchematicObject? owner,
        PrimitiveObjectToy primitive)
    {
        _spawnMessage = spawnMessage;
        _destroyMessage = destroyMessage;
        SpawnMessageSize = spawnMessageSize;
        DestroyMessageSize = destroyMessageSize;
        Owner = owner;
        NativePrimitive = primitive;
        LocalPosition = spawnMessage.position;
        LocalRotation = spawnMessage.rotation;
        LocalScale = spawnMessage.scale;
    }

    /// <summary>Gets the synthetic Mirror net ID used by this client-only object.</summary>
    public uint NetId => _spawnMessage.netId;

    /// <summary>Gets the exact packed byte size of the spawn message sent to clients.</summary>
    public int SpawnMessageSize { get; }

    /// <summary>Gets the exact packed byte size of the destroy message sent to clients.</summary>
    public int DestroyMessageSize { get; }

    /// <summary>Gets the schematic which owns this primitive, when created by the optimizer.</summary>
    public SchematicObject? Owner { get; }

    /// <summary>Gets the native primitive retained for server collision and script access.</summary>
    internal PrimitiveObjectToy NativePrimitive { get; }

    /// <summary>Gets the direct native parent net ID used by the payload's local anchor.</summary>
    public uint ParentNetId { get; internal set; }

    /// <summary>Gets the local transform encoded in the Mirror spawn message.</summary>
    public Vector3 LocalPosition { get; }
    public Quaternion LocalRotation { get; }
    public Vector3 LocalScale { get; }

    /// <summary>Gets whether the retained native collider is authoritative for this primitive.</summary>
    public bool IsCollidable { get; internal init; }

    /// <summary>Gets the cached size priority used when selecting structural primitives.</summary>
    public float SizePriority { get; internal init; }

    /// <summary>Gets the cached collision priority used by culling/teleport integrations.</summary>
    public float CollisionPriority { get; internal init; }

    /// <summary>Gets a stable ordering value for deterministic cluster transitions.</summary>
    public int Order { get; internal set; }

    /// <summary>Gets whether this synthetic object has been invalidated by lifecycle cleanup.</summary>
    public bool IsDestroyed { get; private set; }

    /// <summary>Gets the connection IDs which currently have this object displayed.</summary>
    public IReadOnlyCollection<int> Viewers => _viewers.Keys;

    /// <summary>Gets whether this object is currently tracked as visible to the given client.</summary>
    public bool IsVisibleTo(NetworkConnectionToClient? connection)
    {
        return connection != null && _viewers.TryGetValue(connection.connectionId, out NetworkConnectionToClient? tracked) &&
            ReferenceEquals(tracked, connection);
    }

    /// <summary>
    /// Creates a client primitive entirely on the Unity/main thread. The payload is generated through
    /// Mirror's own initial-state serializer, so it follows the exact server assembly's layout.
    /// </summary>
    public static bool TryCreate(PrimitiveObjectToy primitive, out ClientPrimitive? clientPrimitive)
        => TryCreate(null, primitive, out clientPrimitive);

    internal static bool TryCreate(
        SchematicObject? owner,
        PrimitiveObjectToy primitive,
        out ClientPrimitive? clientPrimitive)
    {
        clientPrimitive = null;
        if (primitive == null || primitive.netIdentity == null)
            return false;

        Transform? parent = primitive.transform.parent;
        if (parent == null || !parent.TryGetComponent(out NetworkIdentity parentIdentity) || parentIdentity.netId == 0 ||
            parentIdentity.serverOnly)
            return false;

        NetworkIdentity identity = primitive.netIdentity;
        identity.InitializeNetworkBehaviours();

        // AdminToyBase serializes its local transform fields, not Transform directly. Synchronize
        // those fields before asking Mirror to produce the initial-state payload.
        primitive.UpdatePositionServer();
        uint assetId = identity.assetId;
        if (assetId == 0 && PrefabManager.PrimitiveObject != null)
            assetId = PrefabManager.PrimitiveObject.netIdentity.assetId;
        if (assetId == 0)
            return false;
        uint netId = NetworkIdentity.GetNextNetworkId();

        ArraySegment<byte> serialized;
        using (NetworkWriterPooled ownerWriter = NetworkWriterPool.Get())
        using (NetworkWriterPooled observersWriter = NetworkWriterPool.Get())
        {
            serialized = NetworkServer.CreateSpawnMessagePayload(
                isOwner: false,
                identity,
                ownerWriter,
                observersWriter);

            byte[] bytes = new byte[serialized.Count];
            if (serialized.Count > 0 && serialized.Array != null)
                Buffer.BlockCopy(serialized.Array, serialized.Offset, bytes, 0, serialized.Count);

            SpawnMessage spawnMessage = new()
            {
                netId = netId,
                isLocalPlayer = false,
                isOwner = false,
                sceneId = identity.sceneId,
                assetId = assetId,
                position = primitive.transform.localPosition,
                rotation = primitive.transform.localRotation,
                scale = primitive.transform.localScale,
                payload = new ArraySegment<byte>(bytes),
            };

            ObjectDestroyMessage destroyMessage = new() { netId = netId };
            using NetworkWriterPooled packedWriter = NetworkWriterPool.Get();
            NetworkMessages.Pack(spawnMessage, packedWriter);
            int spawnMessageSize = packedWriter.Position;
            if (spawnMessageSize > Math.Min(NetworkMessages.MaxMessageSize(0), MaximumSafeSpawnMessageBytes))
                return false;

            packedWriter.Position = 0;
            NetworkMessages.Pack(destroyMessage, packedWriter);
            int destroyMessageSize = packedWriter.Position;

            clientPrimitive = new ClientPrimitive(
                spawnMessage,
                spawnMessageSize,
                destroyMessage,
                destroyMessageSize,
                owner,
                primitive)
            {
                ParentNetId = parentIdentity.netId,
                IsCollidable = primitive.PrimitiveFlags.HasFlag(PrimitiveFlags.Collidable),
                SizePriority = GetSizePriority(primitive),
                CollisionPriority = GetCollisionPriority(primitive),
                Order = primitive.transform.GetSiblingIndex(),
            };
        }

        return true;
    }

    /// <summary>Shows this object to one ready client.</summary>
    public bool Show(NetworkConnectionToClient? connection)
    {
        if (IsDestroyed || !IsReadyClient(connection))
            return false;

        int connectionId = connection!.connectionId;
        if (_viewers.TryGetValue(connectionId, out NetworkConnectionToClient? previous))
        {
            if (ReferenceEquals(previous, connection))
                return false;
            _viewers.Remove(connectionId);
        }

        _viewers[connectionId] = connection;
        try
        {
            // A disconnect can race the readiness check. Do not leave a stale viewer entry when it does.
            if (!IsReadyClient(connection))
            {
                _viewers.Remove(connectionId);
                return false;
            }

            connection.Send(_spawnMessage);
            return true;
        }
        catch
        {
            _viewers.Remove(connectionId);
            return false;
        }
    }

    /// <summary>Hides this object from one ready client, tolerating disconnect races.</summary>
    public bool Hide(NetworkConnectionToClient? connection)
    {
        if (connection == null || !_viewers.TryGetValue(connection.connectionId, out NetworkConnectionToClient? tracked) ||
            !ReferenceEquals(tracked, connection))
            return false;

        if (IsDestroyed)
        {
            _viewers.Remove(connection.connectionId);
            return false;
        }

        if (!IsReadyClient(connection))
            return false;

        try
        {
            connection.Send(_destroyMessage);
            _viewers.Remove(connection.connectionId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Alias used by culling integrations which call the operation a destroy.</summary>
    public bool Destroy(NetworkConnectionToClient? connection) => Hide(connection);

    /// <summary>Forgets a connection which is permanently gone or whose client world was reset.</summary>
    internal void ForgetViewer(NetworkConnectionToClient? connection)
    {
        if (connection != null &&
            _viewers.TryGetValue(connection.connectionId, out NetworkConnectionToClient? tracked) &&
            ReferenceEquals(tracked, connection))
        {
            _viewers.Remove(connection.connectionId);
        }
    }

    /// <summary>Invalidates this object after its owner/manager has been unregistered.</summary>
    internal void Invalidate()
    {
        IsDestroyed = true;
        _viewers.Clear();
    }

    private static bool IsReadyClient(NetworkConnectionToClient? connection)
    {
        if (connection == null || !connection.isReady)
            return false;

        try
        {
            return ReferenceHub.TryGetHub(connection, out ReferenceHub hub) &&
                hub != null && hub.Mode == ClientInstanceMode.ReadyClient;
        }
        catch
        {
            return false;
        }
    }

    private static float GetSizePriority(PrimitiveObjectToy primitive)
    {
        Vector3 scale = primitive.transform.lossyScale;
        return Math.Abs(scale.x) + Math.Abs(scale.y) + Math.Abs(scale.z);
    }

    private static float GetCollisionPriority(PrimitiveObjectToy primitive)
    {
        if (primitive._collider != null)
            return primitive._collider.bounds.size.sqrMagnitude;

        Vector3 scale = primitive.transform.lossyScale;
        return scale.sqrMagnitude;
    }
}
