using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MerText
{
    public enum TextColorMotion { None, Once, Loop, PingPong }

    [Serializable]
    public sealed class TextStepEffects
    {
        public TextColorMotion ColorMotion;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Tooltip("Colors in order, as hex values such as #FFAA33 or #FFAA33CC. Animated colors replace inline color tags.")]
#endif
        public List<string> Palette = new();
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Min(0f), UnityEngine.Tooltip("Seconds for one color cycle. Zero uses the step duration.")]
#endif
        public float ColorPeriod;
        public bool SmoothColors;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Min(0f)]
#endif
        public float FadeIn;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Min(0f)]
#endif
        public float FadeOut;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Min(0f), UnityEngine.Tooltip("Seconds per blink cycle. Zero disables blinking.")]
#endif
        public float BlinkPeriod;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Range(0.05f, 0.95f)]
#endif
        public float BlinkVisibleFraction = 0.5f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Min(0f), UnityEngine.Tooltip("Seconds to reveal the text one character at a time. Only used by Hold steps.")]
#endif
        public float RevealDuration;
        public bool RevealCursor;
    }

    // Synchronized with the other MER project alongside TextSequenceTimeline.
    internal sealed class TextEffectPlayback
    {
        private static readonly Regex Tags = new(
            @"<(?<close>/)?(?<name>[a-z][a-z0-9-]*|#[0-9a-f]+)(?<args>[^<>]*)>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private readonly TextStepEffects _effect;
        private readonly uint[] _colors;
        private readonly int[] _revealEnds;
        private readonly bool[] _revealInsideLiteral;
        private readonly string _fixedText;
        private readonly double _duration;

        public TextEffectPlayback(TextStepEffects effect, double duration, string fixedText = "")
        {
            _effect = effect;
            _duration = duration;
            _fixedText = fixedText;
            List<uint> colors = new();
            if (effect.ColorMotion != TextColorMotion.None && effect.Palette != null)
            {
                foreach (string value in effect.Palette)
                {
                    if (TryColor(value?.Trim() ?? string.Empty, out uint color))
                        colors.Add(color);
                }
            }
            _colors = colors.ToArray();

            List<int> ends = new();
            List<bool> insideLiteral = new();
            if (effect.RevealDuration > 0 && fixedText.Length > 0)
                CountGlyphs(fixedText, ends, insideLiteral);
            _revealEnds = ends.ToArray();
            _revealInsideLiteral = insideLiteral.ToArray();
        }

        public bool Active => _colors.Length > 0 || _effect.FadeIn > 0f || _effect.FadeOut > 0f ||
            _effect.BlinkPeriod > 0f || _revealEnds.Length > 0;

        public string Apply(string text, double elapsed)
        {
            if (text.Length == 0) return text;
            elapsed = Math.Max(0d, Math.Min(_duration, elapsed));
            if (_effect.BlinkPeriod > 0f)
            {
                double period = Math.Max(0.1, _effect.BlinkPeriod);
                double duty = _effect.BlinkVisibleFraction > 0 ? Math.Min(0.95, _effect.BlinkVisibleFraction) : 0.5;
                if (elapsed % period >= period * duty) return string.Empty;
            }

            if (_revealEnds.Length > 0 && elapsed < _effect.RevealDuration && text == _fixedText)
            {
                int glyphs = (int)(elapsed / _effect.RevealDuration * _revealEnds.Length);
                if (glyphs == 0)
                    text = _effect.RevealCursor ? "|" : string.Empty;
                else
                {
                    int i = Math.Min(glyphs, _revealEnds.Length - 1) - 1;
                    text = text.Substring(0, _revealEnds[i]) + (_effect.RevealCursor ? "|" : string.Empty) +
                        (_revealInsideLiteral[i] ? "</noparse>" : string.Empty);
                }
            }

            double opacity = 1d;
            if (_effect.FadeIn > 0f) opacity = Math.Min(opacity, elapsed / _effect.FadeIn);
            if (_effect.FadeOut > 0f) opacity = Math.Min(opacity, (_duration - elapsed) / _effect.FadeOut);
            if (opacity <= 0d || text.Length == 0) return string.Empty;
            if (_colors.Length == 0 && opacity >= 1d) return text;

            uint color = _colors.Length > 0 ? GetColor(elapsed) : 0;
            return ApplyColorAndOpacity(text, color, opacity, _colors.Length > 0);
        }

        private uint GetColor(double elapsed)
        {
            if (_colors.Length == 1) return _colors[0];
            double period = _effect.ColorPeriod > 0f ? Math.Max(0.05, _effect.ColorPeriod) : _duration;
            double phase = Math.Max(0d, elapsed / period);
            bool loop = _effect.ColorMotion == TextColorMotion.Loop;
            phase = loop ? phase % 1d : _effect.ColorMotion == TextColorMotion.PingPong
                ? 1d - Math.Abs((phase % 2d) - 1d) : Math.Min(1d, phase);
            double position = phase * (loop ? _colors.Length : _colors.Length - 1);
            int first = Math.Min((int)position, _colors.Length - 1);
            int next = (first + 1) % _colors.Length;
            double fraction = position - first;
            if (_effect.SmoothColors) fraction = fraction * fraction * (3d - 2d * fraction);
            uint blended = 0;
            for (int shift = 24; shift >= 0; shift -= 8)
            {
                double from = (_colors[first] >> shift) & 255;
                double to = (_colors[next] >> shift) & 255;
                blended |= (uint)Math.Round(from + (to - from) * fraction) << shift;
            }
            return blended;
        }

        private static string ApplyColorAndOpacity(string text, uint color, double opacity, bool overrideColor)
        {
            StringBuilder result = new();
            if (overrideColor)
            {
                uint fadedColor = (color & 0xFFFFFF00) | (uint)Math.Round((color & 255) * opacity);
                result.Append("<color=#").Append(fadedColor.ToString("X8", CultureInfo.InvariantCulture)).Append('>');
            }
            else AppendOpacity(result, 255, opacity);

            Stack<uint> activeColors = new();
            bool literal = false;
            int start = 0;
            foreach (Match match in Tags.Matches(text))
            {
                string name = match.Groups["name"].Value.ToLowerInvariant();
                bool closing = match.Groups["close"].Success;
                if (name == "noparse")
                {
                    literal = !closing;
                    continue;
                }
                if (literal || (name != "color" && name != "alpha" && !name.StartsWith("#", StringComparison.Ordinal)))
                    continue;

                result.Append(text, start, match.Index - start);
                if (!overrideColor)
                {
                    string args = match.Groups["args"].Value.TrimStart('=').Trim();
                    if (name == "alpha")
                    {
                        if (byte.TryParse(args.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte alpha))
                            AppendOpacity(result, alpha, opacity);
                    }
                    else
                    {
                        result.Append(match.Value);
                        if (closing)
                        {
                            if (activeColors.Count > 0) activeColors.Pop();
                        }
                        else activeColors.Push(TryColor(name.StartsWith("#", StringComparison.Ordinal) ? name : args, out uint tagColor) ? tagColor & 255 : 255);
                        AppendOpacity(result, activeColors.Count > 0 ? activeColors.Peek() : 255, opacity);
                    }
                }
                start = match.Index + match.Length;
            }
            result.Append(text, start, text.Length - start);
            if (overrideColor) result.Append("</color>");
            return result.ToString();
        }

        private static void AppendOpacity(StringBuilder result, uint alpha, double opacity) =>
            result.Append("<alpha=#").Append(((byte)Math.Round(alpha * opacity)).ToString("X2", CultureInfo.InvariantCulture)).Append('>');

        private static bool TryColor(string value, out uint color)
        {
            value = value.TrimStart('#');
            if (value.Length == 3 || value.Length == 4)
            {
                StringBuilder expanded = new();
                foreach (char c in value) expanded.Append(c, 2);
                value = expanded.ToString();
            }
            if ((value.Length == 6 || value.Length == 8) &&
                uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color))
            {
                if (value.Length == 6) color = (color << 8) | 255;
                return true;
            }
            color = 0;
            return false;
        }

        private static void CountGlyphs(string text, List<int> ends, List<bool> insideLiteral)
        {
            bool literal = false;
            int start = 0;
            foreach (Match tag in Tags.Matches(text))
            {
                string name = tag.Groups["name"].Value.ToLowerInvariant();
                if (literal && name != "noparse") continue;
                AddText(start, tag.Index);
                if (name == "noparse") literal = !tag.Groups["close"].Success;
                else if (name == "sprite") Add(tag.Index + tag.Length);
                start = tag.Index + tag.Length;
            }
            AddText(start, text.Length);

            void AddText(int from, int to)
            {
                if (from >= to) return;
                TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text.Substring(from, to - from));
                while (elements.MoveNext())
                    Add(from + elements.ElementIndex + elements.GetTextElement().Length);
            }

            void Add(int end)
            {
                ends.Add(end);
                insideLiteral.Add(literal);
            }
        }
    }
}
