using System;
using System.Collections.Generic;
using AdminToys;
using LabApi.Features.Wrappers;
using Mirror;
using UnityEngine;
using ProjectMER.Features.Extensions;
using PrimitiveObjectToy = AdminToys.PrimitiveObjectToy;

namespace ProjectMER.Features.Objects;

/// <summary>
/// サーバー側のパーティクルエミッタ。Unity のプレビューと同じ <see cref="ParticleSim"/> を
/// 毎フレーム進め、結果をプリミティブ AdminToy のプールへ反映する。
///
/// 本物の ParticleSystem はバニアラントへ届かないため、粒子 1 個 = PrimitiveObjectToy 1 個。
/// 色は <see cref="SchematicColor.Quantize"/> を通して有限パレットに収める。
/// </summary>
public sealed class SchematicParticleEmitter : MonoBehaviour
{
	/// <summary>既定の描画距離（この範囲にプレイヤーが居なければシミュレーションを止める）。</summary>
	private const float DefaultCullDistance = 40f;

	/// <summary>SyncVar の書き込み頻度。帯域を抑えるため 20 Hz に留める。</summary>
	private const float SyncInterval = 1f / 20f;

	private ParticleSim.ParticleSettings _settings;
	private ParticleSim.Emitter _emitter;
	private PrimitiveObjectToy[] _toys;
	private PrimitiveType _primitiveType;
	private Vector3 _visualScale = Vector3.one;
	private Vector3 _positionOffset = Vector3.zero;
	private float _sizeScale = 1f;
	private float _distanceSizeGrowth = 0f;
	private float _particleSpacing = 1f;
	private float _expansionDistance = 0f;
	private int _seed;
	private float _cullDistance = DefaultCullDistance;
	private float _timer;
	private bool _configured;

	/// <summary>スキマティック書き出し時に設定を渡す。実際の生成は Start まで遅延する。</summary>
	public void Configure(
		ParticleSim.ParticleSettings settings,
		int seed,
		float cullDistance,
		Vector3 positionOffset = default,
		float sizeScale = 1f,
		float distanceSizeGrowth = 0f,
		float particleSpacing = 1f,
		float expansionDistance = 0f)
	{
		_settings = settings ?? new ParticleSim.ParticleSettings();
		_settings.Sanitize();
		_seed = seed;
		_cullDistance = Mathf.Max(0f, cullDistance);
		_positionOffset = positionOffset;
		_sizeScale = Mathf.Max(0f, sizeScale);
		_distanceSizeGrowth = Mathf.Max(0f, distanceSizeGrowth);
		_particleSpacing = Mathf.Max(0f, particleSpacing);
		_expansionDistance = Mathf.Max(0f, expansionDistance);
		_configured = true;
	}

	private void Start()
	{
		if (!_configured)
		{
			_settings = new ParticleSim.ParticleSettings();
			_settings.Sanitize();
			_configured = true;
		}

		(_primitiveType, _visualScale) = ResolveVisual(_settings.Visual);

		_emitter = new ParticleSim.Emitter();
		_emitter.Reset(_settings, _seed);

		_toys = new PrimitiveObjectToy[ParticleSim.MaxParticles];
		for (int i = 0; i < _toys.Length; i++)
		{
			PrimitiveObjectToy toy = Instantiate(PrefabManager.PrimitiveObject);
			toy.NetworkPrimitiveType = _primitiveType;
			toy.NetworkPrimitiveFlags = PrimitiveFlags.None;
			toy.NetworkMaterialColor = Color.clear;
			toy.NetworkIsStatic = false;
			toy.NetworkMovementSmoothing = 60;
			NetworkServer.Spawn(toy.gameObject);
			_toys[i] = toy;
		}
	}

	private void Update()
	{
		if (_emitter == null || _toys == null)
			return;

		_timer += Time.deltaTime;
		if (_timer < SyncInterval)
			return;

		float deltaTime = _timer;
		_timer = 0f;

		if (!IsAnyPlayerNear())
		{
			HideAll();
			return;
		}

		// エミッタのローカル空間でオフセットしてからワールドへ変換する。
		// Unity 側のプレビューと同じ行列になるよう、Translate は右から掛ける。
		Matrix4x4 space = transform.localToWorldMatrix * Matrix4x4.Translate(_positionOffset);
		_emitter.Step(_settings, deltaTime, space);

		ParticleSim.Particle[] particles = _emitter.RawParticles;
		for (int i = 0; i < _toys.Length; i++)
		{
			ParticleSim.Particle p = particles[i];
			PrimitiveObjectToy toy = _toys[i];
			if (toy == null)
				continue;

			if (!p.Alive)
			{
				SetVisible(toy, false);
				continue;
			}

			SetVisible(toy, true);

			// Unity 側の DrawPreview と同じ変換。基準は各粒子の放出点 p.Origin。
			Vector3 fromOrigin = p.Position - p.Origin;
			Vector3 radialDirection = fromOrigin.sqrMagnitude > 0.000001f
				? fromOrigin.normalized
				: p.Velocity.normalized;
			if (radialDirection.sqrMagnitude < 0.000001f)
				radialDirection = Vector3.up;

			float ageRatio = p.Lifetime > 0f ? Mathf.Clamp01(p.Age / p.Lifetime) : 1f;
			Vector3 drawPosition = p.Origin + fromOrigin * _particleSpacing +
			                       radialDirection * (_expansionDistance * ageRatio);
			float distanceScale = 1f + Vector3.Distance(drawPosition, p.Origin) * _distanceSizeGrowth;
			Vector3 size = Vector3.Scale(_visualScale,
				Vector3.one * Mathf.Max(0.001f, p.Size * _sizeScale * distanceScale));
			toy.transform.SetPositionAndRotation(drawPosition, p.Rotation);
			toy.transform.localScale = size;
			// EvaluateColor は共有シミュレーション側で既に量子化済み。
			toy.NetworkMaterialColor = p.Color;
		}
	}

	private bool IsAnyPlayerNear()
	{
		Vector3 origin = transform.position;
		float maxSqr = _cullDistance * _cullDistance;

		foreach (Player player in Player.ReadyList)
		{
			// ラウンド切替時に破棄されたハブが残ることがある。
			if (player.ReferenceHub == null)
				continue;

			try
			{
				if ((player.Position - origin).sqrMagnitude <= maxSqr)
					return true;
			}
			catch (Exception)
			{
				// 壊れかけのハブは無視する。
			}
		}

		return false;
	}

	private static void SetVisible(PrimitiveObjectToy toy, bool visible)
	{
		PrimitiveFlags flags = visible ? PrimitiveFlags.Visible : PrimitiveFlags.None;
		if (toy.NetworkPrimitiveFlags != flags)
			toy.NetworkPrimitiveFlags = flags;
	}

	private void HideAll()
	{
		if (_toys == null)
			return;
		foreach (PrimitiveObjectToy toy in _toys)
			if (toy != null)
				SetVisible(toy, false);
	}

	private void OnDestroy()
	{
		if (_toys == null)
			return;

		foreach (PrimitiveObjectToy toy in _toys)
		{
			if (toy == null)
				continue;
			try
			{
				NetworkServer.Destroy(toy.gameObject);
			}
			catch (Exception)
			{
				// ラウンド終了でネットワークが既に停止している場合は無視する。
			}
		}

		_toys = Array.Empty<PrimitiveObjectToy>();
	}

	private static (PrimitiveType, Vector3) ResolveVisual(ParticleVisual visual)
	{
		switch (visual)
		{
			case ParticleVisual.Cube:
				return (PrimitiveType.Cube, Vector3.one);
			case ParticleVisual.Quad:
				return (PrimitiveType.Quad, Vector3.one);
			case ParticleVisual.Capsule:
				return (PrimitiveType.Capsule, Vector3.one);
			case ParticleVisual.Cylinder:
				return (PrimitiveType.Cylinder, Vector3.one);
			case ParticleVisual.Plane:
				// SL の Plane は 10x10。他プリミティブと同じ見た目に揃える。
				return (PrimitiveType.Plane, Vector3.one * 0.1f);
			default:
				return (PrimitiveType.Sphere, Vector3.one);
		}
	}

	/// <summary>Properties のネスト辞書から設定を復元する。</summary>
	public static ParticleSim.ParticleSettings ParseSettings(Dictionary<string, object> properties)
	{
		ParticleSim.ParticleSettings settings = new ParticleSim.ParticleSettings();
		if (properties == null ||
			!properties.TryGetValueSafe("ParticleSettings", out object raw) ||
			raw.AsDictionary() is not Dictionary<string, object> data)
			return settings;

		settings.Shape = (ParticleShape)data.GetInt("Shape", (int)settings.Shape);
		settings.ShapeRadius = data.GetFloat("ShapeRadius", settings.ShapeRadius);
		settings.ShapeSize = ReadVector(data, "ShapeSize", settings.ShapeSize);
		settings.ShapeAngle = data.GetFloat("ShapeAngle", settings.ShapeAngle);
		settings.Rate = data.GetFloat("Rate", settings.Rate);
		settings.Burst = data.GetInt("Burst", settings.Burst);
		settings.BurstInterval = data.GetFloat("BurstInterval", settings.BurstInterval);
		settings.StartDelay = data.GetFloat("StartDelay", settings.StartDelay);
		settings.Loop = data.GetBool("Loop", settings.Loop);
		settings.LifetimeMin = data.GetFloat("LifetimeMin", settings.LifetimeMin);
		settings.LifetimeMax = data.GetFloat("LifetimeMax", settings.LifetimeMax);
		settings.Direction = ReadVector(data, "Direction", settings.Direction);
		settings.Spread = data.GetFloat("Spread", settings.Spread);
		settings.SpeedMin = data.GetFloat("SpeedMin", settings.SpeedMin);
		settings.SpeedMax = data.GetFloat("SpeedMax", settings.SpeedMax);
		settings.Gravity = data.GetFloat("Gravity", settings.Gravity);
		settings.Drag = data.GetFloat("Drag", settings.Drag);
		settings.Turbulence = data.GetFloat("Turbulence", settings.Turbulence);
		settings.VortexRadius = data.GetFloat("VortexRadius", settings.VortexRadius);
		settings.VortexSpeed = data.GetFloat("VortexSpeed", settings.VortexSpeed);
		settings.SpeedOverLife = data.GetFloat("SpeedOverLife", settings.SpeedOverLife);
		settings.Visual = (ParticleVisual)data.GetInt("Visual", (int)settings.Visual);
		settings.SizeStart = data.GetFloat("SizeStart", settings.SizeStart);
		settings.SizeEnd = data.GetFloat("SizeEnd", settings.SizeEnd);
		settings.SizeCurve = (ParticleSizeCurve)data.GetInt("SizeCurve", (int)settings.SizeCurve);
		settings.Spin = data.GetFloat("Spin", settings.Spin);
		settings.MotionScale = data.GetFloat("MotionScale", settings.MotionScale);
		settings.FallbackColor = ReadColor(data, "FallbackColor", settings.FallbackColor);

		List<ParticleSim.ColorKey> colors = new List<ParticleSim.ColorKey>();
		foreach (object entry in data.GetList("Colors"))
		{
			Dictionary<string, object>? key = entry.AsDictionary();
			if (key == null)
				continue;
			colors.Add(new ParticleSim.ColorKey(
				key.GetFloat("Time", 0f),
				ReadChannels(key, Color.white)));
		}
		settings.Colors = colors.ToArray();

		settings.Sanitize();
		return settings;
	}

	private static Vector3 ReadVector(Dictionary<string, object> data, string key, Vector3 fallback)
	{
		Dictionary<string, object>? value = data.TryGetValueSafe(key, out object raw) ? raw.AsDictionary() : null;
		if (value == null)
			return fallback;
		return new Vector3(
			value.GetFloat("x", fallback.x),
			value.GetFloat("y", fallback.y),
			value.GetFloat("z", fallback.z));
	}

	private static Color ReadColor(Dictionary<string, object> data, string key, Color fallback)
	{
		Dictionary<string, object>? value = data.TryGetValueSafe(key, out object raw) ? raw.AsDictionary() : null;
		return value == null ? fallback : ReadChannels(value, fallback);
	}

	private static Color ReadChannels(Dictionary<string, object> data, Color fallback) => new Color(
		data.GetFloat("r", fallback.r),
		data.GetFloat("g", fallback.g),
		data.GetFloat("b", fallback.b),
		data.GetFloat("a", fallback.a));
}
