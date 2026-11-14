using System.Collections.Generic;
using AdminToys;
using UnityEngine;

namespace ProjectMER.Features;

/// <summary>
/// Animator の <c>MER:{path}:{property}</c> float パラメーターを Primitive / Light の SyncVar へ反映する。
/// 前フレームから変化したチャンネルだけを書き込む。
/// </summary>
public class AnimatedPropertySync : MonoBehaviour
{
	private const string Prefix = "MER:";

	private enum Prop
	{
		ColorR, ColorG, ColorB, ColorA,
		Visible, Collidable, Active, Enabled,
		Intensity, Range, SpotAngle, InnerSpotAngle, ShadowStrength,
	}

	private class PrimitiveState
	{
		public PrimitiveObjectToy Toy;
		public bool Active = true;
		public PrimitiveFlags Stored;
	}

	private class LightState
	{
		public LightSourceToy Toy;
		public bool Enabled = true;
		public bool Active = true;
		public float StoredIntensity;
	}

	private struct Binding
	{
		public int Hash;
		public Prop Prop;
		public float Last;
		public PrimitiveState Primitive;
		public LightState Light;
	}

	private Animator _animator;
	private Binding[] _bindings;

	private void Awake()
	{
		_animator = GetComponent<Animator>();
		if (_animator == null)
		{
			Destroy(this);
			return;
		}

		Dictionary<GameObject, PrimitiveState> primitives = new();
		Dictionary<GameObject, LightState> lights = new();
		List<Binding> list = new();
		HashSet<string> warned = new();

		foreach (AnimatorControllerParameter parameter in _animator.parameters)
		{
			if (parameter.type != AnimatorControllerParameterType.Float || !parameter.name.StartsWith(Prefix))
				continue;

			string body = parameter.name.Substring(Prefix.Length);
			int split = body.LastIndexOf(':');
			if (split < 0)
			{
				Warn(warned, parameter.name, "property separator not found");
				continue;
			}

			string path = body.Substring(0, split);
			string property = body.Substring(split + 1);

			Transform target = path.Length == 0 ? transform : transform.Find(path);
			if (target == null)
			{
				Warn(warned, parameter.name, "target not found");
				continue;
			}

			if (!TryParse(property, out Prop prop, out bool forPrimitive, out bool forLight))
			{
				Warn(warned, parameter.name, "unknown property");
				continue;
			}

			Binding binding = new() { Hash = parameter.nameHash, Prop = prop, Last = parameter.defaultFloat };
			GameObject go = target.gameObject;

			if (forPrimitive && go.TryGetComponent(out PrimitiveObjectToy primitive))
			{
				if (!primitives.TryGetValue(go, out PrimitiveState state))
					primitives[go] = state = new PrimitiveState { Toy = primitive };
				binding.Primitive = state;
			}
			else if (forLight && go.TryGetComponent(out LightSourceToy light))
			{
				if (!lights.TryGetValue(go, out LightState state))
					lights[go] = state = new LightState { Toy = light, StoredIntensity = light.NetworkLightIntensity };
				binding.Light = state;
			}
			else
			{
				Warn(warned, parameter.name, "target type mismatch");
				continue;
			}

			list.Add(binding);
		}

		if (list.Count == 0)
		{
			Destroy(this);
			return;
		}

		_bindings = list.ToArray();
	}

	private static void Warn(HashSet<string> warned, string name, string reason)
	{
		if (warned.Add(name))
			Logger.Warn($"AnimatedPropertySync: ignoring parameter \"{name}\" ({reason}).");
	}

	private static bool TryParse(string property, out Prop prop, out bool forPrimitive, out bool forLight)
	{
		forPrimitive = false;
		forLight = false;
		switch (property)
		{
			case "Color.r": prop = Prop.ColorR; forPrimitive = forLight = true; break;
			case "Color.g": prop = Prop.ColorG; forPrimitive = forLight = true; break;
			case "Color.b": prop = Prop.ColorB; forPrimitive = forLight = true; break;
			case "Color.a": prop = Prop.ColorA; forPrimitive = forLight = true; break;
			case "Visible": prop = Prop.Visible; forPrimitive = true; break;
			case "Collidable": prop = Prop.Collidable; forPrimitive = true; break;
			case "Active": prop = Prop.Active; forPrimitive = forLight = true; break;
			case "Enabled": prop = Prop.Enabled; forLight = true; break;
			case "Intensity": prop = Prop.Intensity; forLight = true; break;
			case "Range": prop = Prop.Range; forLight = true; break;
			case "SpotAngle": prop = Prop.SpotAngle; forLight = true; break;
			case "InnerSpotAngle": prop = Prop.InnerSpotAngle; forLight = true; break;
			case "ShadowStrength": prop = Prop.ShadowStrength; forLight = true; break;
			default: prop = default; return false;
		}

		return true;
	}

	private void LateUpdate()
	{
		for (int i = 0; i < _bindings.Length; i++)
		{
			ref Binding b = ref _bindings[i];
			float value = _animator.GetFloat(b.Hash);
			if (Mathf.Approximately(value, b.Last))
				continue;

			b.Last = value;
			if (b.Primitive != null)
			{
				if (b.Primitive.Toy != null)
					ApplyPrimitive(b.Primitive, b.Prop, value);
			}
			else if (b.Light.Toy != null)
			{
				ApplyLight(b.Light, b.Prop, value);
			}
		}
	}

	private static Color WithChannel(Color color, Prop prop, float value)
	{
		switch (prop)
		{
			case Prop.ColorR: color.r = value; break;
			case Prop.ColorG: color.g = value; break;
			case Prop.ColorB: color.b = value; break;
			case Prop.ColorA: color.a = value; break;
		}

		return color;
	}

	private static PrimitiveFlags WithFlag(PrimitiveFlags flags, PrimitiveFlags flag, bool on) =>
		on ? flags | flag : flags & ~flag;

	private static void ApplyPrimitive(PrimitiveState s, Prop prop, float value)
	{
		bool on = value >= 0.5f;
		switch (prop)
		{
			case Prop.ColorR:
			case Prop.ColorG:
			case Prop.ColorB:
			case Prop.ColorA:
				s.Toy.NetworkMaterialColor = WithChannel(s.Toy.NetworkMaterialColor, prop, value);
				break;
			case Prop.Visible:
			case Prop.Collidable:
				PrimitiveFlags flag = prop == Prop.Visible ? PrimitiveFlags.Visible : PrimitiveFlags.Collidable;
				if (s.Active)
					s.Toy.NetworkPrimitiveFlags = WithFlag(s.Toy.NetworkPrimitiveFlags, flag, on);
				else
					s.Stored = WithFlag(s.Stored, flag, on);
				break;
			case Prop.Active:
				if (on == s.Active)
					break;
				s.Active = on;
				if (on)
				{
					s.Toy.NetworkPrimitiveFlags = s.Stored;
				}
				else
				{
					s.Stored = s.Toy.NetworkPrimitiveFlags;
					s.Toy.NetworkPrimitiveFlags = PrimitiveFlags.None;
				}

				break;
		}
	}

	private static void ApplyLight(LightState s, Prop prop, float value)
	{
		LightSourceToy toy = s.Toy;
		switch (prop)
		{
			case Prop.ColorR:
			case Prop.ColorG:
			case Prop.ColorB:
			case Prop.ColorA:
				toy.NetworkLightColor = WithChannel(toy.NetworkLightColor, prop, value);
				break;
			case Prop.Intensity:
				if (s.Enabled && s.Active)
					toy.NetworkLightIntensity = value;
				else
					s.StoredIntensity = value;
				break;
			case Prop.Enabled:
			case Prop.Active:
				bool wasLit = s.Enabled && s.Active;
				if (prop == Prop.Enabled)
					s.Enabled = value >= 0.5f;
				else
					s.Active = value >= 0.5f;

				bool lit = s.Enabled && s.Active;
				if (lit == wasLit)
					break;
				if (lit)
				{
					toy.NetworkLightIntensity = s.StoredIntensity;
				}
				else
				{
					s.StoredIntensity = toy.NetworkLightIntensity;
					toy.NetworkLightIntensity = 0f;
				}

				break;
			case Prop.Range: toy.NetworkLightRange = value; break;
			case Prop.SpotAngle: toy.NetworkSpotAngle = value; break;
			case Prop.InnerSpotAngle: toy.NetworkInnerSpotAngle = value; break;
			case Prop.ShadowStrength: toy.NetworkShadowStrength = value; break;
		}
	}
}
