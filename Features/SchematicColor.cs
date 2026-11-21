using UnityEngine;

namespace ProjectMER.Features;

/// <summary>
/// プリミティブの色を有限パレットへ丸める。
/// クライアントの <c>PrimitiveObjectToy.CachedMaterials</c> は色ごとにマテリアルを作り、
/// ラウンド中は解放されない。アニメーションとパーティクルの両方から同じ格子を使うことで
/// 合計のマテリアル数に上限が効く。
/// </summary>
internal static class SchematicColor
{
	/// <summary>RGB 32 段 / アルファ 16 段。共有シミュレーションの格子と同じ。</summary>
	public static Color Quantize(Color color) => ParticleSim.Quantize(color);

	/// <summary>チャンネル単体を丸める（アニメーションの色チャンネル比較用）。</summary>
	public static float QuantizeChannel(float value, bool alpha) =>
		alpha ? Mathf.Round(value * 15f) / 15f : Mathf.Round(value * 31f) / 31f;
}
