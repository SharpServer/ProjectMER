using System.Collections.Generic;
using LabApi.Features.Wrappers;
using ProjectMER.Events.Arguments;
using UnityEngine;

namespace ProjectMER.Features.Objects;

public class EventInvokeMarkerObject : MonoBehaviour
{
    private const float ExitMargin = 0.15f;
    private static readonly float[] BodySampleHeights = [0f, 0.9f, 1.7f];
    // 破棄済み ReferenceHub は GetHashCode() が gameObject に触れて NRE になるため、
    // ハッシュキーには PlayerId (int) を使う。
    private readonly HashSet<int> _insidePlayers = [];
    private readonly Dictionary<int, Vector3> _previousPositions = [];

    public string Id { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public float Distance { get; set; } = 1f;
    public MapEditorObject? MapObject { get; set; }
    public SchematicObject? Schematic { get; set; }

    private void FixedUpdate()
    {
        if (MapObject == null && TryGetComponent(out MapEditorObject mapObject))
        {
            MapObject = mapObject;
            Id = mapObject.Id;
        }

        float radius = Mathf.Max(0.01f, Distance);
        float exitRadius = radius + ExitMargin;
        Vector3 center = transform.position;
		HashSet<int> readyIds = [];

        foreach (Player player in Player.ReadyList)
        {
            ReferenceHub hub = player.ReferenceHub;
			if (hub == null)
				continue;
			int playerId = player.PlayerId;
			readyIds.Add(playerId);

            Vector3 current = player.Position;
            Vector3 previous = _previousPositions.TryGetValue(playerId, out Vector3 value) ? value : current;
            _previousPositions[playerId] = current;

            bool isNear = false;
            foreach (float height in BodySampleHeights)
            {
                Vector3 offset = Vector3.up * height;
                if (DistanceToSegmentSquared(center, previous + offset, current + offset) <= radius * radius)
                {
                    isNear = true;
                    break;
                }
            }
            if (isNear)
            {
                if (_insidePlayers.Add(playerId))
                {
                    Events.Handlers.EventInvokeMarker.OnInvoked(new EventInvokeMarkerInvokedEventArgs(player, this));
                    Events.Handlers.EventInvoke.Invoke(
                        string.IsNullOrWhiteSpace(Tag) ? Id : Tag,
                        player,
                        this);
                }
            }
            else if (DistanceToVerticalBodySquared(center, current) > exitRadius * exitRadius)
            {
                _insidePlayers.Remove(playerId);
            }
        }

		_insidePlayers.RemoveWhere(id => !readyIds.Contains(id));
        foreach (int id in _previousPositions.Keys.ToArray())
        {
			if (!readyIds.Contains(id))
				_previousPositions.Remove(id);
        }
    }

    private static float DistanceToSegmentSquared(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector3 segment = end - start;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared <= Mathf.Epsilon)
            return (point - end).sqrMagnitude;

        float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSquared);
        return (point - (start + segment * t)).sqrMagnitude;
    }

    private static float DistanceToVerticalBodySquared(Vector3 point, Vector3 foot)
    {
        return DistanceToSegmentSquared(point, foot, foot + Vector3.up * BodySampleHeights[BodySampleHeights.Length - 1]);
    }
}
