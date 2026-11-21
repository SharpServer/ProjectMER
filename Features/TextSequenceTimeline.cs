using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MerText
{
    public enum TextStepMode { Hold, ScrollLeft, ScrollRight, Blank }

    public enum TextStepAlignment { Center, Left, Right, PrefabDefault }

    public enum TextLineMode { Default, Wrap, FitToWidth = 3 }

    [Serializable]
    public sealed class TextPresentation
    {
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Tooltip("Fit To Width keeps the text on one line by shrinking it to the display width.")]
#endif
        public TextLineMode LineMode;
        public TextStepAlignment Alignment = TextStepAlignment.PrefabDefault;
        public string Color = string.Empty;
        public bool Bold;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Tooltip("Extra space between characters, in font-size units (em).")]
#endif
        public float CharacterSpacing;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Tooltip("Moves the text up or down, in font-size units (em).")]
#endif
        public float BaselineOffset;

        public bool HasOverrides() =>
            LineMode != TextLineMode.Default || Alignment != TextStepAlignment.PrefabDefault ||
            !string.IsNullOrWhiteSpace(Color) || Bold || CharacterSpacing != 0f || BaselineOffset != 0f;
    }

    [Serializable]
    public sealed class TextSequenceStep
    {
        public TextStepMode Mode;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.TextArea(2, 6)]
#endif
        public string Text = string.Empty;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Tooltip("Use the original TextMeshPro text instead of the text in this step.")]
#endif
        public bool UseSourceText;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Min(0f), UnityEngine.Tooltip("Seconds. Use 0 on a scroll step to play one full pass.")]
#endif
        public float Duration = 3f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Range(0.1f, 30f), UnityEngine.Tooltip("Character widths per second for scroll steps.")]
#endif
        public float Speed = 5f;
        public TextStepAlignment Alignment;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Range(0.05f, 10f)]
#endif
        public float FontSizeMultiplier = 1f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Tooltip("Optional hex tint, for example #FFAA33.")]
#endif
        public string Color = string.Empty;
        public bool Bold;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Tooltip("For Hold steps. Default inherits the Text Component's line mode.")]
#endif
        public TextLineMode LineMode;
        public float CharacterSpacing;
        public float BaselineOffset;
        public TextStepEffects Effects = new();

        public float EffectiveFontSizeMultiplier() => FontSizeMultiplier > 0f
            ? Math.Min(10f, Math.Max(0.05f, FontSizeMultiplier)) : 1f;

        public TextPresentation HoldPresentation(TextLineMode inherited) => new()
        {
            LineMode = LineMode == TextLineMode.Default ? inherited : LineMode,
            Alignment = Alignment,
            Color = Color,
            Bold = Bold,
            CharacterSpacing = CharacterSpacing,
            BaselineOffset = BaselineOffset,
        };
    }

    // Keep this file in sync between ProjectMER and SL-CustomObjects-dev so the
    // editor preview and the networked TextToy use the same timing and clipping.
    public sealed class TextSequenceTimeline
    {
        private const string NoParseBreak = "</noparse><noparse>";
        private static readonly Regex AbsoluteSize = new(
            @"<size\s*=\s*([+-]?\d+(?:\.\d+)?)(?:px)?\s*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex InlineSize = new(
            @"<size\s*=\s*([+-]?(?:\d+(?:\.\d+)?|\.\d+))(px|em|%)?\s*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex LineBreak = new(
            @"\r\n|[\r\n\v\f\u0085\u2028\u2029]|<br\s*/?>|<cr\s*/?>|\\[nr]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex HexColor = new(
            @"\A#?(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\z", RegexOptions.CultureInvariant);

        private readonly List<FrameStep> _steps = new();
        private readonly bool _loop;
        private readonly double _duration;

        public TextSequenceTimeline(IEnumerable<TextSequenceStep> steps, bool loop, string source, bool richText,
            float width, float height, float fontSize, float inlineSizeScale,
            TextLineMode defaultLineMode = TextLineMode.Default, IReadOnlyList<float> fitScales = null!)
        {
            _loop = loop;
            int index = 0;
            foreach (TextSequenceStep step in steps)
            {
                float fitScale = fitScales != null && index < fitScales.Count ? fitScales[index] : 1f;
                index++;
                if (step == null) continue;
                FrameStep frame = new(step, step.UseSourceText ? source : step.Text ?? string.Empty,
                    richText, width, height, fontSize, inlineSizeScale, defaultLineMode, fitScale);
                _duration += frame.Duration;
                _steps.Add(frame);
            }
        }

        public bool HasSteps => _steps.Count > 0;

        public string Sample(double elapsed)
        {
            if (_steps.Count == 0) return string.Empty;
            elapsed = Math.Max(0, elapsed);
            if (elapsed >= _duration)
            {
                if (!_loop) return _steps[_steps.Count - 1].SampleFinal();
                elapsed %= _duration;
            }

            foreach (FrameStep step in _steps)
            {
                if (elapsed < step.Duration) return step.Sample(elapsed);
                elapsed -= step.Duration;
            }

            return string.Empty;
        }

        public static string FormatText(string text, bool richText, float fontSize, float inlineSizeScale)
        {
            if (richText && inlineSizeScale != 1f)
            {
                text = AbsoluteSize.Replace(text, match =>
                {
                    string number = match.Groups[1].Value;
                    if (!float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out float size))
                        return match.Value;
                    string sign = number.StartsWith("+", StringComparison.Ordinal) ? "+" : string.Empty;
                    return "<size=" + sign + Number(size * inlineSizeScale) + ">";
                });
            }
            else if (!richText)
            {
                text = "<noparse>" + EscapeLiteral(text) + "</noparse>";
            }
            return "<size=" + Number(fontSize) + ">" + text + "</size>";
        }

        internal static string EscapeLiteral(string text) =>
            text.Replace("<", "<" + NoParseBreak).Replace("\\", "\\" + NoParseBreak);

        private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

        public static string FormatHold(string text, bool richText, float fontSize, float inlineSizeScale,
            TextPresentation presentation, float fitScale = 1f)
        {
            if (text.Length == 0) return string.Empty;

            bool singleLine = presentation.LineMode == TextLineMode.FitToWidth;
            if (!singleLine || !(fitScale > 0f)) fitScale = 1f;
            fitScale = Math.Min(1f, fitScale);

            if (singleLine)
            {
                text = LineBreak.Replace(text, match =>
                    richText || match.Value[0] != '<' && match.Value[0] != '\\' ? " " : match.Value);
                if (richText)
                {
                    // TMP's relative sizes use the component's configured font size, not the enclosing tag.
                    text = InlineSize.Replace(text, match =>
                    {
                        string number = match.Groups[1].Value;
                        if (!float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                            return match.Value;
                        string unit = match.Groups[2].Value.ToLowerInvariant();
                        float points = unit == "%" ? fontSize * value / 100f : unit == "em" ? fontSize * value :
                            number[0] == '+' || number[0] == '-' ? fontSize + value * inlineSizeScale : value * inlineSizeScale;
                        return "<size=" + Number(Math.Max(0.00001f, points * fitScale)) + ">";
                    });
                    inlineSizeScale = 1f;
                }
            }

            string align = presentation.Alignment switch
            {
                TextStepAlignment.Left => "left",
                TextStepAlignment.Right => "right",
                TextStepAlignment.Center => "center",
                _ => string.Empty,
            };
            string color = presentation.Color?.Trim() ?? string.Empty;
            bool tint = HexColor.IsMatch(color);
            float size = fontSize * fitScale;
            float spacing = Clamp(presentation.CharacterSpacing, -1f, 4f, 0f) * size;
            float offset = Clamp(presentation.BaselineOffset, -10f, 10f, 0f) * size;
            return (align.Length > 0 ? "<align=" + align + ">" : string.Empty) +
                (singleLine ? "<nobr>" : string.Empty) +
                (spacing != 0f ? "<cspace=" + Number(spacing) + ">" : string.Empty) +
                (offset != 0f ? "<voffset=" + Number(offset) + ">" : string.Empty) +
                (tint ? "<color=#" + color.TrimStart('#') + ">" : string.Empty) +
                (presentation.Bold ? "<b>" : string.Empty) +
                FormatText(text, richText, size, inlineSizeScale) +
                (presentation.Bold ? "</b>" : string.Empty) + (tint ? "</color>" : string.Empty) +
                (offset != 0f ? "</voffset>" : string.Empty) + (spacing != 0f ? "</cspace>" : string.Empty) +
                (singleLine ? "</nobr>" : string.Empty) + (align.Length > 0 ? "</align>" : string.Empty);
        }

        private static float Clamp(float value, float min, float max, float fallback) =>
            float.IsNaN(value) ? fallback : Math.Max(min, Math.Min(max, value));

        private sealed class FrameStep
        {
            private readonly bool _right;
            private readonly double _speed;
            private readonly double _visibleWidth;
            private readonly double _passDuration;
            private readonly MarqueeText _marquee = null!;
            private readonly string _prefix = string.Empty;
            private readonly string _suffix = string.Empty;
            private readonly TextEffectPlayback _effects = null!;

            public double Duration { get; }
            public string FinalFrame { get; } = string.Empty;

            public FrameStep(TextSequenceStep step, string text, bool richText, float width, float height,
                float fontSize, float inlineSizeScale, TextLineMode defaultLineMode, float fitScale)
            {
                Duration = Clamp(step.Duration, 0.05f, 3600f, 3f);
                if (step.Mode == TextStepMode.Blank || text.Length == 0) return;

                string tint = step.Color?.Trim() ?? string.Empty;
                string colorOpen = HexColor.IsMatch(tint) ? "<color=#" + tint.TrimStart('#') + ">" : string.Empty;
                string colorClose = colorOpen.Length > 0 ? "</color>" : string.Empty;
                string styleOpen = colorOpen + (step.Bold ? "<b>" : string.Empty);
                string styleClose = (step.Bold ? "</b>" : string.Empty) + colorClose;
                // Unity can initialize a newly added list element with zeroed fields.
                float multiplier = step.EffectiveFontSizeMultiplier();
                fontSize *= multiplier;
                if (step.Mode != TextStepMode.ScrollLeft && step.Mode != TextStepMode.ScrollRight)
                {
                    FinalFrame = FormatHold(text, richText, fontSize, inlineSizeScale * multiplier,
                        step.HoldPresentation(defaultLineMode), fitScale);
                    _effects = CreateEffects(step, Duration, FinalFrame);
                    return;
                }

                if (!(width > 0f && height > 0f) || float.IsInfinity(width) || float.IsInfinity(height)) return;
                _marquee = new MarqueeText(text, richText, fontSize, inlineSizeScale * multiplier, width, height);
                if (!(_marquee.Width > 0)) return;
                _right = step.Mode == TextStepMode.ScrollRight;
                _speed = Clamp(step.Speed, 0.1f, 30f, 5f) * _marquee.WidestCell;
                _visibleWidth = width;
                _passDuration = (_visibleWidth + _marquee.Width) / _speed;
                if (step.Duration <= 0f) Duration = _passDuration;
                _prefix = "<align=left><nobr>" + styleOpen;
                _suffix = styleClose + "</nobr></align>";
                _effects = CreateEffects(step, Duration, string.Empty);
            }

            private static TextEffectPlayback CreateEffects(TextSequenceStep step, double duration, string text)
            {
                if (step.Effects == null) return null!;
                TextEffectPlayback playback = new(step.Effects, duration, text);
                return playback.Active ? playback : null!;
            }

            public string SampleFinal() => _effects?.Apply(FinalFrame, Duration) ?? FinalFrame;

            public string Sample(double elapsed)
            {
                if (!(_passDuration > 0)) return _effects?.Apply(FinalFrame, elapsed) ?? FinalFrame;
                double distance = (elapsed % _passDuration) * _speed;
                double offset = _right ? distance - _marquee.Width : _visibleWidth - distance;
                string frame = _marquee.Sample(offset, _visibleWidth, _prefix, _suffix);
                return _effects?.Apply(frame, elapsed) ?? frame;
            }
        }
    }
}
