using System.Globalization;
using System.Text.RegularExpressions;

namespace ProjectMER.Features;

/// <summary>Translate authored TMP font points to the native TextToy's Canvas units.</summary>
public static class SchematicTextFormatting
{
    private static readonly Regex AbsoluteSize = new(
        @"<size\s*=\s*([+-]?\d+(?:\.\d+)?)(?:px)?\s*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string ForNativeCanvas(Dictionary<string, object> properties)
    {
        string text = Convert.ToString(properties["Text"], CultureInfo.InvariantCulture) ?? string.Empty;
        // Old exports retain their native-size behavior until re-exported from Unity.
        if (!properties.TryGetValue("FontSize", out object? sizeValue)) return text;
        float size = Convert.ToSingle(sizeValue, CultureInfo.InvariantCulture);
        float factor = properties.TryGetValue("FontSizeScale", out object? factorValue)
            ? Convert.ToSingle(factorValue, CultureInfo.InvariantCulture) : 2f;
        if (float.IsNaN(size) || float.IsInfinity(size) || size <= 0f ||
            float.IsNaN(factor) || float.IsInfinity(factor) || factor <= 0f) return text;
        bool rich = !properties.TryGetValue("RichText", out object? richValue) || Convert.ToBoolean(richValue);
        if (rich)
            text = AbsoluteSize.Replace(text, match =>
            {
                string number = match.Groups[1].Value;
                float points = float.Parse(number, CultureInfo.InvariantCulture) * factor;
                string sign = number.StartsWith("+", StringComparison.Ordinal) ? "+" : string.Empty;
                return "<size=" + sign + points.ToString("0.######", CultureInfo.InvariantCulture) + ">";
            });
        else
            text = "<noparse>" + text + "</noparse>";
        return "<size=" + (size * factor).ToString("0.######", CultureInfo.InvariantCulture) + ">" + text + "</size>";
    }
}
