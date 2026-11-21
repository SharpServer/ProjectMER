using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MerText
{
    internal sealed class MarqueeText
    {
        private static readonly Regex Tag = new(
            @"<(?<close>/)?(?<name>[a-z][a-z0-9-]*|\#[0-9a-f]+)(?<args>[^<>]*)>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Size = new(
            @"\A=\s*(?<value>[+-]?(?:\d+(?:\.\d+)?|\.\d+))(?<unit>px|em|%)?\s*\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly HashSet<string> Styles = new(StringComparer.Ordinal)
        {
            "b", "i", "u", "s", "strikethrough", "color", "mark", "sub", "sup",
            "lowercase", "uppercase", "allcaps", "smallcaps", "font", "font-weight", "gradient", "link",
        };

        private readonly List<Cell> _cells = new();
        private readonly List<double> _edges = new() { 0 };
        private readonly List<OpenTag> _open = new();
        private readonly double _maxSize;
        private readonly double _baseSize;
        private readonly float _sizeScale;

        public MarqueeText(string text, bool richText, float fontSize, float sizeScale, float width, float height)
        {
            _maxSize = Math.Min(width, height * 0.8f);
            _baseSize = Math.Min(fontSize, _maxSize);
            _sizeScale = sizeScale;
            if (!(_baseSize > 0.000001)) return;

            if (!richText)
            {
                AddText(text);
                return;
            }

            bool literal = false;
            int start = 0;
            foreach (Match match in Tag.Matches(text))
            {
                string name = match.Groups["name"].Value.ToLowerInvariant();
                string args = match.Groups["args"].Value;
                bool closing = match.Groups["close"].Success;
                if (name == "noparse" && closing == literal && args.Length == 0)
                {
                    AddText(text.Substring(start, match.Index - start));
                    literal = !closing;
                }
                else
                {
                    if (literal) continue;
                    string key = StyleKey(name);
                    if (!closing && name == "br" && args.Length == 0)
                    {
                        AddText(text.Substring(start, match.Index - start));
                        AddText(" ");
                    }
                    else if (!closing && name == "sprite")
                    {
                        AddText(text.Substring(start, match.Index - start));
                        AddCell(match.Value, CreateRun(raw: true));
                    }
                    else if (closing && args.Length == 0)
                    {
                        int open = _open.FindLastIndex(tag => tag.Name == key);
                        if (open < 0) continue;
                        AddText(text.Substring(start, match.Index - start));
                        _open.RemoveAt(open);
                    }
                    else if (!closing && TryOpen(name, key, args, match.Value, out OpenTag tag))
                    {
                        AddText(text.Substring(start, match.Index - start));
                        _open.Add(tag);
                    }
                    else continue;
                }
                start = match.Index + match.Length;
            }
            AddText(text.Substring(start));
            _open.Clear();
        }

        public double Width => _edges[_edges.Count - 1];
        public double WidestCell { get; private set; }

        public string Sample(double left, double width, string prefix, string suffix)
        {
            double right = width - left;
            int first = _edges.BinarySearch(-left);
            if (first < 0) first = ~first;
            int end = _edges.BinarySearch(right);
            if (end < 0) end = ~end - 1;
            if (first >= end) return string.Empty;

            StringBuilder output = new(prefix);
            output.Append("<space=").Append(Number(left + _edges[first])).Append('>');
            Run current = _cells[first].Style;
            output.Append(current.Open);
            for (int i = first; i < end; i++)
            {
                Cell cell = _cells[i];
                if (cell.Style != current)
                {
                    output.Append(current.Close).Append(cell.Style.Open);
                    current = cell.Style;
                }
                output.Append(cell.Text);
            }
            return output.Append(current.Close).Append(suffix).ToString();
        }

        private static string StyleKey(string name) => name switch
        {
            "strikethrough" => "s",
            "allcaps" => "uppercase",
            _ => name.StartsWith("#", StringComparison.Ordinal) ? "color" : name,
        };

        private bool TryOpen(string name, string key, string args, string original, out OpenTag tag)
        {
            tag = default;
            if (name == "size")
            {
                Match size = Size.Match(args);
                if (!size.Success || !double.TryParse(size.Groups["value"].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double value) || double.IsInfinity(value))
                    return false;
                string unit = size.Groups["unit"].Value.ToLowerInvariant();
                string number = size.Groups["value"].Value;
                value = unit == "%" ? _baseSize * value / 100d :
                    unit == "em" ? _baseSize * value :
                    number[0] == '+' || number[0] == '-' ? _baseSize + value * _sizeScale : value * _sizeScale;
                tag = new OpenTag(key, string.Empty, Math.Max(0.00001, Math.Min(value, _maxSize)));
                return true;
            }

            if (!Styles.Contains(key)) return false;
            if (name.StartsWith("#", StringComparison.Ordinal) &&
                (args.Length != 0 || (name.Length != 4 && name.Length != 5 && name.Length != 7 && name.Length != 9)))
                return false;
            tag = new OpenTag(key, original, 0);
            return true;
        }

        private void AddText(string text)
        {
            if (text.Length == 0) return;
            Run run = CreateRun(raw: false);
            TextElementEnumerator characters = StringInfo.GetTextElementEnumerator(text);
            while (characters.MoveNext())
            {
                string character = characters.GetTextElement();
                AddCell(char.IsWhiteSpace(character, 0) || char.IsControl(character, 0)
                    ? "\u00A0" : TextSequenceTimeline.EscapeLiteral(character), run);
            }
        }

        private void AddCell(string text, Run run)
        {
            _cells.Add(new Cell(text, run));
            _edges.Add(Width + run.Size);
            WidestCell = Math.Max(WidestCell, run.Size);
        }

        private Run CreateRun(bool raw)
        {
            int sizeIndex = _open.FindLastIndex(tag => tag.Name == "size");
            double size = sizeIndex < 0 ? _baseSize : _open[sizeIndex].Size;
            StringBuilder open = new("<size=" + Number(size) + "><mspace=" + Number(size) + ">");
            StringBuilder close = new(raw ? string.Empty : "</noparse>");
            foreach (OpenTag tag in _open)
                open.Append(tag.Open);
            if (!raw) open.Append("<noparse>");
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                if (_open[i].Open.Length > 0)
                    close.Append("</").Append(_open[i].Name).Append('>');
            }
            close.Append("</mspace></size>");
            return new Run(size, open.ToString(), close.ToString());
        }

        private static string Number(double number) => number.ToString("0.######", CultureInfo.InvariantCulture);

        private readonly struct OpenTag
        {
            public readonly string Name;
            public readonly string Open;
            public readonly double Size;
            public OpenTag(string name, string open, double size) => (Name, Open, Size) = (name, open, size);
        }

        private readonly struct Cell
        {
            public readonly string Text;
            public readonly Run Style;
            public Cell(string text, Run style) => (Text, Style) = (text, style);
        }

        private sealed class Run
        {
            public readonly double Size;
            public readonly string Open;
            public readonly string Close;
            public Run(double size, string open, string close) => (Size, Open, Close) = (size, open, close);
        }
    }
}
