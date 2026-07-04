using ProjectMER.Features.Enums;
using ProjectMER.Features.Serializable.Schematics;
using UnityEngine;

namespace ProjectMER.Features.Objects;

/// <summary>
/// スキマティック内の各ブロック GO に付与され、元の SchematicBlockData を保持するコンポーネント。
/// 名前・BlockType・ObjectId などでの実行時検索を可能にする。
/// </summary>
public class SchematicBlock : MonoBehaviour
{
    /// <summary>このブロックが属するスキマティック。</summary>
    public SchematicObject Schematic { get; internal set; } = null!;

    /// <summary>シリアライズ元のブロックデータ。</summary>
    public SchematicBlockData Data { get; internal set; } = null!;

    /// <summary>エディタ上で付けられたブロック名。</summary>
    public string BlockName => Data.Name;

    /// <summary>スキマティック内で一意なオブジェクト ID。</summary>
    public int ObjectId => Data.ObjectId;

    /// <summary>親ブロックのオブジェクト ID。</summary>
    public int ParentId => Data.ParentId;

    /// <summary>ブロックの種類（Primitive / Light / Pickup / Interactable など）。</summary>
    public BlockType BlockType => Data.BlockType;

    /// <summary>エディタで割り当てられたアニメーター名（無ければ空）。</summary>
    public string AnimatorName => Data.AnimatorName ?? string.Empty;

    /// <summary>
    /// Unity 側の ObjectPrefabSchematicInfo で割り当てられたキー。
    /// 空でなければこのブロックは ObjectPrefab の管理対象。
    /// </summary>
    public string ObjectPrefabKey =>
        Data.Properties != null && Data.Properties.TryGetValue("ObjectPrefabKey", out object key)
            ? key?.ToString() ?? string.Empty
            : string.Empty;

    /// <summary>ObjectPrefab 管理キーを持つか。</summary>
    public bool HasObjectPrefabKey => ObjectPrefabKey.Length > 0;

    internal void Init(SchematicObject schematic, SchematicBlockData data)
    {
        Schematic = schematic;
        Data = data;
    }
}
