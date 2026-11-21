using AdminToys;
using System.Globalization;
using MerText;
using ProjectMER.Features.Extensions;
using UnityEngine;

namespace ProjectMER.Features.Objects;

internal sealed class SchematicTextAnimation : MonoBehaviour
{
    private TextToy _toy = null!;
    private TextSequenceTimeline _timeline = null!;
    private double _startedAt;
    private double _nextUpdate;

    internal static void Attach(TextToy toy, Dictionary<string, object> properties)
    {
        if (properties.ContainsKey("ImageGroup"))
            return;

        List<TextSequenceStep> steps = [];
        foreach (object entry in properties.GetList("TextSequence"))
        {
            if (entry.AsDictionary() is not { } step) continue;
            steps.Add(new TextSequenceStep
            {
                Mode = (TextStepMode)step.GetInt("Mode"),
                Text = step.GetString("Text"),
                UseSourceText = step.GetBool("UseSourceText"),
                Duration = step.GetFloat("Duration", 3f),
                Speed = step.GetFloat("Speed", 5f),
                Alignment = (TextStepAlignment)step.GetInt("Alignment"),
                FontSizeMultiplier = step.GetFloat("FontSizeMultiplier", 1f),
                Color = step.GetString("Color"),
                Bold = step.GetBool("Bold"),
                LineMode = (TextLineMode)step.GetInt("LineMode"),
                CharacterSpacing = step.GetFloat("CharacterSpacing"),
                BaselineOffset = step.GetFloat("BaselineOffset"),
                Effects = ReadEffects(step),
            });
        }

        bool loop = properties.GetBool("TextSequenceLoop", true);
        if (steps.Count == 0)
        {
            if (!properties.GetBool("ScrollEnabled"))
                return;

            loop = true;
            steps.Add(new TextSequenceStep
            {
                Mode = properties.GetBool("ScrollRight") ? TextStepMode.ScrollRight : TextStepMode.ScrollLeft,
                UseSourceText = true,
                Duration = 0f,
                Speed = properties.GetFloat("ScrollSpeed", 5f),
            });
        }

        float scale = PositiveOrDefault(properties.GetFloat("FontSizeScale", 2f), 2f);
        float fontSize = PositiveOrDefault(properties.GetFloat("FontSize", 9f) * scale, 18f);
        Vector2 size = toy.DisplaySize;
        TextSequenceTimeline timeline = new(steps, loop, properties.GetString("Text"),
            properties.GetBool("RichText", true), size.x, size.y, fontSize, scale,
            SchematicTextFormatting.ReadPresentation(properties).LineMode, ReadFitScales(properties));
        SchematicTextAnimation animation = toy.gameObject.AddComponent<SchematicTextAnimation>();
        animation._toy = toy;
        animation._timeline = timeline;
        animation._startedAt = Time.unscaledTimeAsDouble;
        toy.TextFormat = timeline.Sample(0);
    }

    private static float PositiveOrDefault(float value, float fallback) =>
        value > 0f && !float.IsInfinity(value) ? value : fallback;

    private static float[] ReadFitScales(Dictionary<string, object> properties) =>
        properties.GetList("TextSequenceFitScales").Select(value =>
            float.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float scale) ? scale : 1f).ToArray();

    private static TextStepEffects ReadEffects(Dictionary<string, object> step)
    {
        if (!step.TryGetValueSafe("Effects", out object raw) || raw.AsDictionary() is not { } effects)
            return new TextStepEffects();

        return new TextStepEffects
        {
            ColorMotion = (TextColorMotion)effects.GetInt("ColorMotion"),
            Palette = effects.GetStringList("Palette"),
            ColorPeriod = effects.GetFloat("ColorPeriod"),
            SmoothColors = effects.GetBool("SmoothColors"),
            FadeIn = effects.GetFloat("FadeIn"),
            FadeOut = effects.GetFloat("FadeOut"),
            BlinkPeriod = effects.GetFloat("BlinkPeriod"),
            BlinkVisibleFraction = effects.GetFloat("BlinkVisibleFraction", 0.5f),
            RevealDuration = effects.GetFloat("RevealDuration"),
            RevealCursor = effects.GetBool("RevealCursor"),
        };
    }

    private void Update()
    {
        double now = Time.unscaledTimeAsDouble;
        if (now < _nextUpdate)
            return;

        // Keep TextToy SyncVar updates capped even when the server runs at higher FPS.
        _nextUpdate = now + 0.05;
        string frame = _timeline.Sample(now - _startedAt);
        if (_toy.TextFormat != frame)
            _toy.TextFormat = frame;
    }
}
