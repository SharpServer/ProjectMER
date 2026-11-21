using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// パーティクル放出の形状。エミッタのローカル空間で解釈する。
/// </summary>
public enum ParticleShape
{
    /// <summary>1 点から放出する。</summary>
    Point = 0,

    /// <summary>半径 <see cref="ParticleSettings.ShapeRadius"/> の球の表面付近から放出する。</summary>
    Sphere = 1,

    /// <summary><see cref="ParticleSettings.ShapeSize"/> の箱の内部から放出する。</summary>
    Box = 2,

    /// <summary>上方向へ <see cref="ParticleSettings.ShapeAngle"/> 度の円錐から放出する。</summary>
    Cone = 3,
}

/// <summary>
/// パーティクルの基準形状。サーバーが生成する AdminToy の種類に対応する。
/// </summary>
public enum ParticleVisual
{
    /// <summary>球。煙・光点・泡に向く。</summary>
    Sphere = 0,

    /// <summary>箱。瓦礫・火花・結晶に向く。</summary>
    Cube = 1,

    /// <summary>板。向きを固定した火花や雨粒に向く。</summary>
    Quad = 2,

    /// <summary>カプセル。雨粒や雫に向く。</summary>
    Capsule = 3,

    /// <summary>円柱。ビーム・柱・蒸気の筋に向く。</summary>
    Cylinder = 4,

    /// <summary>平面。衝撃波・地面の波紋に向く（SL の Plane は 10x10 なので自動で小さく補正する）。</summary>
    Plane = 5,
}

/// <summary>寿命に応じたサイズ変化の仕方。</summary>
public enum ParticleSizeCurve
{
    /// <summary>線形に補間する。</summary>
    Linear = 0,

    /// <summary>最初は速く、後で緩やかに変化する。</summary>
    EaseOut = 1,

    /// <summary>ゆっくり始まり、加速しながら変化する。</summary>
    EaseIn = 2,

    /// <summary>急に現れてすぐ縮む（火花向け）。</summary>
    Burst = 3,
}

/// <summary>
/// サーバーとエディタのプレビューで共有するパーティクルシミュレーション。
///
/// このファイルは Unity プロジェクトと ProjectMER の両方に同じ内容で置かれ、
/// 書き出したパラメータがプレビューと実機で同じ結果になることを保証する。
/// Unity 依存は UnityEngine の数学型のみで、サーバー側でもそのままコンパイルできる。
/// </summary>
public static class ParticleSim
{
    /// <summary>1 エミッタが同時に保持できる粒子数の上限。</summary>
    public const int MaxParticles = 24;

    /// <summary>カラーランプに置けるキー数の上限。</summary>
    public const int MaxColorKeys = 6;

    /// <summary>同時刻に放出する粒子数の上限。</summary>
    public const int MaxBurst = 12;

    /// <summary>
    /// 色を有限のパレットへ丸める。クライアントの <c>PrimitiveObjectToy.CachedMaterials</c> は
    /// 色ごとにマテリアルを作りラウンド中解放しないため、連続値を送ると無限に増えてしまう。
    /// RGB は 32 段、アルファは 16 段。HDR 発光（成分 &gt; 1）はそのまま保持する。
    /// </summary>
    /// <remarks>
    /// サーバー側の <c>SchematicColor.Quantize</c> と同じ格子を使うこと。
    /// アニメーションとパーティクルでパレットを共有すれば、合計のマテリアル数に上限が効く。
    /// </remarks>
    public static Color Quantize(Color c)
    {
        return new Color(
            Mathf.Round(c.r * 31f) / 31f,
            Mathf.Round(c.g * 31f) / 31f,
            Mathf.Round(c.b * 31f) / 31f,
            Mathf.Round(c.a * 15f) / 15f);
    }

    [Serializable]
    public struct ColorKey
    {
        /// <summary>寿命に対する位置（0 = 放出直後、1 = 消滅）。</summary>
        public float Time;

        /// <summary>この位置での色。成分が 1 を超えると発光として扱う。</summary>
        public Color Color;

        public ColorKey(float time, Color color)
        {
            Time = time;
            Color = color;
        }
    }

    [Serializable]
    public class ParticleSettings
    {
        // ---- 放出 ----
        public ParticleShape Shape = ParticleShape.Sphere;
        public float ShapeRadius = 0.2f;
        public Vector3 ShapeSize = new Vector3(0.5f, 0.5f, 0.5f);
        public float ShapeAngle = 25f;

        /// <summary>毎秒放出する粒子数。0 ならバーストのみ。</summary>
        public float Rate = 10f;

        /// <summary>一度に放出する粒子数。</summary>
        public int Burst = 0;

        /// <summary>バーストの間隔（秒）。0 なら開始時に一度だけ。</summary>
        public float BurstInterval = 0f;

        /// <summary>エミッタの起動から最初の放出までの待ち時間。</summary>
        public float StartDelay = 0f;

        public bool Loop = true;

        // ---- 粒子の寿命と運動 ----
        public float LifetimeMin = 0.8f;
        public float LifetimeMax = 1.5f;

        /// <summary>放出方向。エミッタのローカル空間で解釈する。</summary>
        public Vector3 Direction = Vector3.up;

        /// <summary>放出方向のばらつき（度）。</summary>
        public float Spread = 60f;

        public float SpeedMin = 0.3f;
        public float SpeedMax = 0.8f;

        public float Gravity = 0.1f;
        public float Drag = 0.4f;

        /// <summary>進行方向に直交する揺らぎの強さ。</summary>
        public float Turbulence = 0f;

        /// <summary>放出位置を中心とした回転半径。0 なら直進。</summary>
        public float VortexRadius = 0f;

        /// <summary>回転の速さ（度/秒）。VortexRadius が 0 なら効果なし。</summary>
        public float VortexSpeed = 0f;

        /// <summary>寿命に対する運動の進行量。1 で等速、0.5 で減速、2 で加速。</summary>
        public float SpeedOverLife = 1f;

        // ---- 見た目 ----
        public ParticleVisual Visual = ParticleVisual.Sphere;

        public float SizeStart = 0.12f;
        public float SizeEnd = 0.4f;
        public ParticleSizeCurve SizeCurve = ParticleSizeCurve.EaseOut;

        /// <summary>寿命を通した色の変化。空なら EmissiveFallback を使う。</summary>
        public ColorKey[] Colors =
        {
            new ColorKey(0f, new Color(1f, 1f, 1f, 0.8f)),
            new ColorKey(1f, new Color(0.6f, 0.6f, 0.7f, 0f)),
        };

        /// <summary>カラーランプが空のときに使う色。</summary>
        public Color FallbackColor = new Color(1f, 1f, 1f, 0.7f);

        /// <summary>粒子の自転速度（度/秒）。</summary>
        public float Spin = 0f;

        /// <summary>重力や抗力の影響をどれだけ受けるか（0 = その場に留まる）。</summary>
        public float MotionScale = 1f;

        /// <summary>カラーランプの最大値を使ってよいかを判定する。</summary>
        public bool HasColorRamp => Colors != null && Colors.Length > 0;

        public ParticleSettings Clone()
        {
            ParticleSettings copy = (ParticleSettings)MemberwiseClone();
            copy.Colors = Colors == null ? Array.Empty<ColorKey>() : (ColorKey[])Colors.Clone();
            return copy;
        }

        /// <summary>値域と整合性を強制する。サーバー・エディタ双方で書き出し前後に呼ぶ。</summary>
        public void Sanitize()
        {
            Rate = Mathf.Max(0f, Rate);
            Burst = Mathf.Clamp(Burst, 0, MaxBurst);
            BurstInterval = Mathf.Max(0f, BurstInterval);
            StartDelay = Mathf.Max(0f, StartDelay);

            LifetimeMin = Mathf.Max(0.05f, LifetimeMin);
            LifetimeMax = Mathf.Max(LifetimeMin, LifetimeMax);

            SpeedMin = Mathf.Max(0f, SpeedMin);
            SpeedMax = Mathf.Max(SpeedMin, SpeedMax);

            ShapeRadius = Mathf.Max(0f, ShapeRadius);
            ShapeSize = new Vector3(
                Mathf.Max(0f, ShapeSize.x),
                Mathf.Max(0f, ShapeSize.y),
                Mathf.Max(0f, ShapeSize.z));
            ShapeAngle = Mathf.Clamp(ShapeAngle, 0f, 180f);
            Spread = Mathf.Clamp(Spread, 0f, 180f);

            Drag = Mathf.Clamp01(Drag);
            Turbulence = Mathf.Max(0f, Turbulence);
            VortexRadius = Mathf.Max(0f, VortexRadius);
            SpeedOverLife = Mathf.Clamp(SpeedOverLife, 0f, 4f);
            MotionScale = Mathf.Clamp01(MotionScale);

            SizeStart = Mathf.Max(0f, SizeStart);
            SizeEnd = Mathf.Max(0f, SizeEnd);

            if (Vector3.SqrMagnitude(Direction) < 1e-6f)
                Direction = Vector3.up;
            else
                Direction = Direction.normalized;

            if (Colors != null && Colors.Length > MaxColorKeys)
            {
                ColorKey[] trimmed = new ColorKey[MaxColorKeys];
                Array.Copy(Colors, trimmed, MaxColorKeys);
                Colors = trimmed;
            }

            if (Colors != null)
            {
                for (int i = 0; i < Colors.Length; i++)
                    Colors[i].Time = Mathf.Clamp01(Colors[i].Time);
            }
        }
    }

    /// <summary>シミュレーション中の 1 粒子。サーバーはこれをそのまま AdminToy へ反映する。</summary>
    public struct Particle
    {
        public Vector3 Position;
        public Vector3 Velocity;
        /// <summary>放出時のエミッタ位置（ワールド空間）。渦の中心に使う。</summary>
        public Vector3 Origin;
        public float Age;
        public float Lifetime;
        public float Seed;
        public float Size;
        public Color Color;
        public Quaternion Rotation;
        public bool Alive;
    }

    /// <summary>決定的な擬似乱数。プレビューとサーバーで同じ並びを作るために自前で持つ。</summary>
    private struct Rng
    {
        private uint _state;

        public Rng(uint seed)
        {
            _state = seed == 0u ? 0x9E3779B9u : seed;
        }

        public float Next()
        {
            // xorshift32。高速で、見た目に十分な分布を持つ。
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (_state & 0x00FFFFFFu) / 16777216f;
        }

        public float Range(float min, float max) => min + (max - min) * Next();
    }

    /// <summary>
    /// エミッタ 1 個分の状態。Unity 側・サーバー側の両方がこの型を保持する。
    /// </summary>
    public sealed class Emitter
    {
        private readonly Particle[] _particles = new Particle[MaxParticles];
        private Rng _rng;
        private float _time;
        private float _accumulator;
        private float _nextBurst;
        private bool _burstDone;
        private int _seed;
        private int _aliveCount;

        /// <summary>現在生きている粒子数。</summary>
        public int AliveCount => _aliveCount;

        /// <summary>経過時間。</summary>
        public float Time => _time;

        public ParticleSettings Settings { get; private set; } = new ParticleSettings();

        public IReadOnlyList<Particle> Particles => System.Array.AsReadOnly(_particles);

        /// <summary>全粒子スロットを直接読む（毎フレームの描画用に割り当てを避ける）。</summary>
        public Particle[] RawParticles => _particles;

        /// <summary>乱数種を差し替えて状態を初期化する。</summary>
        public void Reset(ParticleSettings settings, int seed)
        {
            Settings = settings ?? new ParticleSettings();
            Settings.Sanitize();
            _seed = seed;
            _rng = new Rng((uint)seed);
            _time = 0f;
            _accumulator = 0f;
            _nextBurst = Settings.StartDelay;
            _burstDone = false;
            _aliveCount = 0;
            for (int i = 0; i < _particles.Length; i++)
                _particles[i].Alive = false;
        }

        /// <summary>
        /// シミュレーションを <paramref name="deltaTime"/> 秒進める。
        /// <paramref name="space"/> はエミッタのワールド行列で、放出位置と方向の基準になる。
        /// </summary>
        public void Step(ParticleSettings settings, float deltaTime, Matrix4x4 space)
        {
            if (settings == null)
                return;

            Settings = settings;
            _time += deltaTime;

            UpdateParticles(deltaTime, space);
            Emit(deltaTime, space, settings);
        }

        private void UpdateParticles(float deltaTime, Matrix4x4 space)
        {
            ParticleSettings s = Settings;
            int alive = 0;

            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (!p.Alive)
                    continue;

                p.Age += deltaTime;
                if (p.Age >= p.Lifetime)
                {
                    p.Alive = false;
                    continue;
                }

                float t = p.Lifetime <= 0f ? 1f : p.Age / p.Lifetime;

                // 速度の進行量を積分し、寿命に対して非線形な移動を作る。
                float speedFactor = SpeedFactorAt(t, s.SpeedOverLife);
                Vector3 velocity = p.Velocity * speedFactor;

                velocity.y += s.Gravity * deltaTime;
                velocity *= Mathf.Clamp01(1f - s.Drag * deltaTime);

                if (s.Turbulence > 0f)
                {
                    Vector3 noise = new Vector3(
                        Mathf.Sin((p.Seed * 12.9898f + p.Age * 6.1f) * 1.7f),
                        Mathf.Sin((p.Seed * 78.233f + p.Age * 5.3f) * 1.3f),
                        Mathf.Sin((p.Seed * 39.425f + p.Age * 7.7f) * 1.9f));
                    velocity += noise * (s.Turbulence * deltaTime * 4f);
                }

                Vector3 local = p.Position + velocity * deltaTime * s.MotionScale;
                p.Velocity = velocity;

                if (s.VortexRadius > 0f && s.VortexSpeed != 0f)
                {
                    // 中心はワールド原点ではなく放出時のエミッタ位置。Y は上昇を維持する。
                    float angle = s.VortexSpeed * p.Age * Mathf.Deg2Rad;
                    local = new Vector3(
                        p.Origin.x + Mathf.Cos(angle) * s.VortexRadius,
                        local.y,
                        p.Origin.z + Mathf.Sin(angle) * s.VortexRadius);
                }

                p.Position = local;
                p.Size = EvaluateSize(s, t);
                p.Color = EvaluateColor(s, t);
                if (s.Spin != 0f)
                    p.Rotation = p.Rotation * Quaternion.Euler(0f, s.Spin * deltaTime, 0f);

                alive++;
            }

            _aliveCount = alive;
        }

        private void Emit(float deltaTime, Matrix4x4 space, ParticleSettings s)
        {
            if (s.StartDelay > 0f && _time < s.StartDelay)
                return;

            if (s.Burst > 0 && !_burstDone)
            {
                // BurstInterval=0 は「開始時に一度だけ」だが、Loop が有効なら
                // 寿命が尽きるたびに再放出する（火花・爆発のプリセットがこれに該当）。
                float interval = s.BurstInterval > 0f
                    ? s.BurstInterval
                    : s.Loop ? Mathf.Max(0.05f, s.LifetimeMax) : 0f;

                // フレームが飛んでも取りこぼさないよう、最大 8 回まで追いつく。
                int guard = 0;
                while (_time >= _nextBurst && guard++ < 8)
                {
                    Spawn(s.Burst, space, s);
                    if (interval <= 0f || !s.Loop)
                    {
                        _burstDone = true;
                        break;
                    }

                    _nextBurst += Mathf.Max(0.01f, interval);
                }

                // それでも追いついていない場合は現在時刻へ同期して暴走を防ぐ。
                if (guard >= 8 && _time >= _nextBurst)
                    _nextBurst = _time + Mathf.Max(0.01f, interval);
            }

            if (s.Rate <= 0f)
                return;

            if (!s.Loop && _time - s.StartDelay > Mathf.Max(s.LifetimeMax, 1f) * 4f)
                return;

            _accumulator += s.Rate * deltaTime;
            int spawnCount = 0;
            while (_accumulator >= 1f && spawnCount < MaxParticles)
            {
                _accumulator -= 1f;
                spawnCount++;
            }

            if (spawnCount > 0)
                Spawn(spawnCount, space, s);
        }

        private void Spawn(int count, Matrix4x4 space, ParticleSettings s)
        {
            for (int n = 0; n < count; n++)
            {
                int index = FindFreeSlot();
                if (index < 0)
                    return;

                ref Particle p = ref _particles[index];
                float seed = _rng.Range(0f, 1000f);

                Vector3 localPosition = SampleShape(s, ref _rng);
                Vector3 localDirection = SampleDirection(s, ref _rng);

                p.Position = space.MultiplyPoint3x4(localPosition);
                p.Origin = space.MultiplyPoint3x4(Vector3.zero);
                p.Velocity = space.MultiplyVector(localDirection) * _rng.Range(s.SpeedMin, s.SpeedMax);
                p.Age = 0f;
                p.Lifetime = _rng.Range(s.LifetimeMin, s.LifetimeMax);
                p.Seed = seed;
                p.Size = EvaluateSize(s, 0f);
                p.Color = EvaluateColor(s, 0f);
                p.Rotation = Quaternion.Euler(_rng.Range(0f, 360f), _rng.Range(0f, 360f), _rng.Range(0f, 360f));
                p.Alive = true;
                _aliveCount++;
            }
        }

        private int FindFreeSlot()
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                if (!_particles[i].Alive)
                    return i;
            }

            // 満杯なら最も古い粒子を再利用して、放出が完全に止まらないようにする。
            int oldest = 0;
            float best = -1f;
            for (int i = 0; i < _particles.Length; i++)
            {
                float ratio = _particles[i].Lifetime <= 0f ? 1f : _particles[i].Age / _particles[i].Lifetime;
                if (ratio > best)
                {
                    best = ratio;
                    oldest = i;
                }
            }

            return oldest;
        }

        private static Vector3 SampleShape(ParticleSettings s, ref Rng rng)
        {
            switch (s.Shape)
            {
                case ParticleShape.Sphere:
                {
                    Vector3 dir = RandomUnitVector(ref rng);
                    float radius = s.ShapeRadius * Mathf.Pow(rng.Next(), 1f / 3f);
                    return dir * radius;
                }
                case ParticleShape.Box:
                    return new Vector3(
                        rng.Range(-0.5f, 0.5f) * s.ShapeSize.x,
                        rng.Range(-0.5f, 0.5f) * s.ShapeSize.y,
                        rng.Range(-0.5f, 0.5f) * s.ShapeSize.z);
                case ParticleShape.Cone:
                {
                    float radius = s.ShapeRadius * Mathf.Sqrt(rng.Next());
                    float angle = rng.Next() * Mathf.PI * 2f;
                    return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                }
                default:
                    return Vector3.zero;
            }
        }

        private static Vector3 SampleDirection(ParticleSettings s, ref Rng rng)
        {
            Vector3 baseDirection = s.Direction;

            if (s.Shape == ParticleShape.Cone)
            {
                float halfAngle = s.ShapeAngle * 0.5f * Mathf.Deg2Rad;
                float cosLimit = Mathf.Cos(halfAngle);
                float cosTheta = rng.Range(cosLimit, 1f);
                float sinTheta = Mathf.Sqrt(Mathf.Max(0f, 1f - cosTheta * cosTheta));
                float phi = rng.Next() * Mathf.PI * 2f;
                Vector3 local = new Vector3(Mathf.Cos(phi) * sinTheta, cosTheta, Mathf.Sin(phi) * sinTheta);
                return Quaternion.FromToRotation(Vector3.up, baseDirection) * local;
            }

            if (s.Spread <= 0f)
                return baseDirection;

            Vector3 random = RandomUnitVector(ref rng);
            Vector3 blended = Vector3.Slerp(baseDirection, random, Mathf.Clamp01(s.Spread / 180f));
            return blended.sqrMagnitude < 1e-6f ? baseDirection : blended.normalized;
        }

        private static Vector3 RandomUnitVector(ref Rng rng)
        {
            float z = rng.Range(-1f, 1f);
            float angle = rng.Next() * Mathf.PI * 2f;
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(Mathf.Cos(angle) * r, z, Mathf.Sin(angle) * r);
        }

        private static float SpeedFactorAt(float t, float exponent)
        {
            if (Mathf.Approximately(exponent, 1f))
                return 1f;
            return Mathf.Pow(Mathf.Clamp01(1f - t), Mathf.Max(0f, exponent - 1f));
        }

        /// <summary>寿命に対するサイズを評価する。</summary>
        public static float EvaluateSize(ParticleSettings s, float t)
        {
            float curve = EvaluateSizeCurve(s.SizeCurve, t);
            return Mathf.Lerp(s.SizeStart, s.SizeEnd, curve);
        }

        private static float EvaluateSizeCurve(ParticleSizeCurve curve, float t)
        {
            t = Mathf.Clamp01(t);
            switch (curve)
            {
                case ParticleSizeCurve.EaseOut:
                    return 1f - (1f - t) * (1f - t);
                case ParticleSizeCurve.EaseIn:
                    return t * t * t;
                case ParticleSizeCurve.Burst:
                    return 1f - Mathf.Sqrt(t);
                default:
                    return t;
            }
        }

        /// <summary>寿命に対する色を評価する。戻り値は量子化済み。</summary>
        public static Color EvaluateColor(ParticleSettings s, float t)
        {
            ColorKey[] colors = s.Colors;
            if (colors == null || colors.Length == 0)
                return Quantize(s.FallbackColor);

            t = Mathf.Clamp01(t);
            if (colors.Length == 1)
                return Quantize(colors[0].Color);

            // キーは昇順とは限らないので、素直に区間を探す。
            int upper = -1;
            for (int i = 0; i < colors.Length; i++)
            {
                if (colors[i].Time >= t)
                {
                    upper = i;
                    break;
                }
            }

            if (upper < 0)
                return Quantize(colors[colors.Length - 1].Color);
            if (upper == 0)
                return Quantize(colors[0].Color);

            ColorKey from = colors[upper - 1];
            ColorKey to = colors[upper];
            float span = to.Time - from.Time;
            float k = span <= 1e-5f ? 1f : (t - from.Time) / span;
            return Quantize(Color.LerpUnclamped(from.Color, to.Color, Mathf.Clamp01(k)));
        }
    }
}
