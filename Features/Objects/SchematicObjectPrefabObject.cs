using System;
using System.Collections.Generic;
using ProjectMER.Features.Serializable.Schematics;
using UnityEngine;

namespace ProjectMER.Features.Objects;

public class SchematicObjectPrefabObject : MonoBehaviour
{
    /// <summary>
    /// マーカーが生成・更新されたときに発火する。
    /// スキマティック埋め込みマーカー（SchematicObject.Init 経由）と
    /// マップ配置マーカー（SerializableObjectPrefabMarker.SpawnOrUpdateObject 経由）の両方が対象。
    /// </summary>
    public static event Action<SchematicObjectPrefabObject>? Spawned;

    /// <summary>
    /// マーカーの GameObject が破棄されたときに発火する。
    /// </summary>
    public static event Action<SchematicObjectPrefabObject>? Destroyed;

    public string PrefabType = string.Empty;

    public string Tag = string.Empty;

    public int MaxRooms = 1;

    public bool AutoDestroyEnabled;

    public float AutoDestroyTime = -1f;

    public Dictionary<string, string> Options = [];

    public SchematicObject? Schematic { get; set; }

    /// <summary>
    /// Tag が未設定の場合に旧形式の Options["Tag"] へフォールバックした実効タグ。
    /// </summary>
    public string EffectiveTag =>
        !string.IsNullOrWhiteSpace(Tag)
            ? Tag
            : Options.TryGetValue("Tag", out string legacyTag) ? legacyTag : string.Empty;

    internal void Init(SchematicObjectPrefabData data, SchematicObject schematic)
    {
        PrefabType = data.PrefabType;
        Tag = data.Tag;
        MaxRooms = data.MaxRooms <= 0 ? 1 : data.MaxRooms;
        AutoDestroyEnabled = data.AutoDestroyEnabled;
        AutoDestroyTime = data.AutoDestroyTime;
        Options = data.Options ?? [];
        Schematic = schematic;
        NotifySpawned();
    }

    /// <summary>
    /// フィールド設定後に生成・更新を通知する。SerializableObjectPrefabMarker からも呼ばれる。
    /// </summary>
    internal void NotifySpawned() => Spawned?.Invoke(this);

    private void OnDestroy() => Destroyed?.Invoke(this);
}
