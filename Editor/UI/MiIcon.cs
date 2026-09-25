using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// The design's line icons, drawn as vectors in the current text colour. Each one is its
    /// SVG path data on a 24 × 24 grid, exactly as in the design's HTML; set the size with a
    /// USS class (.mi-icon--12 / 16 / 24) and the colour with <c>color</c>.
    /// </summary>
    internal sealed class MiIcon : VisualElement
    {
        public enum Kind
        {
            Home, Status, More, ChevronRight, ChevronDown, ChevronLeft, Check, Lock, Error,
            Download, Refresh, Copy, Chip, Skip, Folder, Trash, File, Columns, Undo, Ad, Chart
        }

        private static readonly Dictionary<Kind, string[]> Paths = new Dictionary<Kind, string[]>
        {
            [Kind.Home] = new[] { "M3 11l9-7 9 7", "M5 10v10h14V10", "M10 20v-6h4v6" },
            [Kind.Status] = new[] { "M3 12h4l3-7 4 14 3-7h4" },
            [Kind.More] = new[] { Circle(12, 5, 1.2f), Circle(12, 12, 1.2f), Circle(12, 19, 1.2f) },
            [Kind.ChevronRight] = new[] { "M9 6l6 6-6 6" },
            [Kind.ChevronDown] = new[] { "M6 9l6 6 6-6" },
            [Kind.ChevronLeft] = new[] { "M15 6l-6 6 6 6" },
            [Kind.Check] = new[] { "M5 12.5l4.5 4.5L19 7.5" },
            [Kind.Lock] = new[] { Rect(5, 11, 14, 9, 2), "M8 11V8a4 4 0 0 1 8 0v3" },
            [Kind.Error] = new[] { Circle(12, 12, 9), "M12 7.5v5.5", "M12 16.5v.01" },
            [Kind.Download] = new[] { "M12 4v11", "M7 10l5 5 5-5", "M5 20h14" },
            [Kind.Refresh] = new[] { "M20 11a8 8 0 0 0-14.5-4.5L4 8", "M4 4v4h4", "M4 13a8 8 0 0 0 14.5 4.5L20 16", "M20 20v-4h-4" },
            [Kind.Copy] = new[] { Rect(8, 8, 12, 12, 2), "M16 8V5a1 1 0 0 0-1-1H5a1 1 0 0 0-1 1v10a1 1 0 0 0 1 1h3" },
            [Kind.Chip] = new[] { Rect(4, 4, 16, 16, 3), "M9 9h6v6H9z" },
            [Kind.Skip] = new[] { "M6 7l5 5-5 5", "M13 7l5 5-5 5" },
            [Kind.Folder] = new[] { "M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" },
            [Kind.Trash] = new[] { "M4 7h16", "M9 7V4h6v3", "M6 7l1 13h10l1-13" },
            [Kind.File] = new[] { "M14 3H6a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V8z", "M14 3v5h5" },
            [Kind.Columns] = new[] { Rect(3, 4, 8, 16, 1.5f), Rect(13, 4, 8, 16, 1.5f) },
            [Kind.Undo] = new[] { "M9 14L4 9l5-5", "M4 9h10a6 6 0 0 1 0 12h-3" },
            [Kind.Ad] = new[] { Rect(3, 5, 18, 14, 2), "M7 15l2.5-6 2.5 6", "M7.8 13h3.4", "M15 9v6h1.5a2.5 3 0 0 0 0-6z" },
            [Kind.Chart] = new[] { "M4 20V10", "M10 20V4", "M16 20v-7", "M22 20H2" }
        };

        private static readonly Dictionary<Kind, List<List<Vector2>>> Cache = new Dictionary<Kind, List<List<Vector2>>>();

        private readonly Kind _kind;
        private readonly float _strokeWidth;

        /// <param name="strokeWidth">In 24-unit grid units, as the design's stroke-width.</param>
        public MiIcon(Kind kind, float strokeWidth = 1.75f, string sizeClass = null)
        {
            _kind = kind;
            _strokeWidth = strokeWidth;
            AddToClassList("mi-icon");
            if (sizeClass != null) AddToClassList(sizeClass);
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            var size = Mathf.Min(contentRect.width, contentRect.height);
            if (size <= 0) return;

            var scale = size / 24f;
            var offset = new Vector2((contentRect.width - size) / 2f, (contentRect.height - size) / 2f);

            var painter = context.painter2D;
            painter.strokeColor = resolvedStyle.color;
            painter.fillColor = resolvedStyle.color;
            painter.lineWidth = Mathf.Max(1f, _strokeWidth * scale);
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;

            foreach (var polyline in Polylines(_kind))
            {
                // A zero-length stroke (the SVG "v.01" dot) draws nothing with round caps here,
                // so draw it as the dot it stands for.
                if (Length(polyline) < 0.2f)
                {
                    painter.BeginPath();
                    painter.Arc(offset + polyline[0] * scale, painter.lineWidth / 2f, 0f, 360f);
                    painter.Fill();
                    continue;
                }

                painter.BeginPath();
                painter.MoveTo(offset + polyline[0] * scale);
                for (var i = 1; i < polyline.Count; i++) painter.LineTo(offset + polyline[i] * scale);
                painter.Stroke();
            }
        }

        private static float Length(List<Vector2> polyline)
        {
            var total = 0f;
            for (var i = 1; i < polyline.Count; i++) total += Vector2.Distance(polyline[i - 1], polyline[i]);
            return total;
        }

        private static List<List<Vector2>> Polylines(Kind kind)
        {
            if (Cache.TryGetValue(kind, out var cached)) return cached;

            var polylines = new List<List<Vector2>>();
            foreach (var path in Paths[kind]) polylines.AddRange(ParsePath(path));
            return Cache[kind] = polylines;
        }

        // ── SVG helpers ─────────────────────────────────────────────────────────

        private static string Circle(float cx, float cy, float r) =>
            F($"M{cx - r} {cy}a{r} {r} 0 1 0 {2 * r} 0a{r} {r} 0 1 0 {-2 * r} 0z");

        private static string Rect(float x, float y, float w, float h, float r) =>
            F($"M{x + r} {y}h{w - 2 * r}a{r} {r} 0 0 1 {r} {r}v{h - 2 * r}a{r} {r} 0 0 1 {-r} {r}") +
            F($"h{-(w - 2 * r)}a{r} {r} 0 0 1 {-r} {-r}v{-(h - 2 * r)}a{r} {r} 0 0 1 {r} {-r}z");

        private static string F(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

        private static readonly Regex Token = new Regex(@"[MmLlHhVvZzAa]|-?(?:\d+\.?\d*|\.\d+)(?:[eE]-?\d+)?");

        /// <summary>
        /// The subset of SVG path data the design uses: M L H V Z A, absolute and relative.
        /// Arcs are flattened into short line segments.
        /// </summary>
        private static List<List<Vector2>> ParsePath(string data)
        {
            var result = new List<List<Vector2>>();
            var tokens = new List<string>();
            foreach (Match match in Token.Matches(data)) tokens.Add(match.Value);

            List<Vector2> current = null;
            var position = Vector2.zero;
            var start = Vector2.zero;
            var command = 'M';
            var index = 0;

            float Next() => float.Parse(tokens[index++], CultureInfo.InvariantCulture);
            bool HasNumber() => index < tokens.Count && !char.IsLetter(tokens[index][0]);

            while (index < tokens.Count)
            {
                if (char.IsLetter(tokens[index][0])) command = tokens[index++][0];
                var relative = char.IsLower(command);

                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                    {
                        var point = new Vector2(Next(), Next());
                        position = relative ? position + point : point;
                        start = position;
                        current = new List<Vector2> { position };
                        result.Add(current);
                        command = relative ? 'l' : 'L'; // further pairs are line-tos
                        break;
                    }
                    case 'L':
                    {
                        var point = new Vector2(Next(), Next());
                        position = relative ? position + point : point;
                        current.Add(position);
                        break;
                    }
                    case 'H':
                    {
                        var x = Next();
                        position = new Vector2(relative ? position.x + x : x, position.y);
                        current.Add(position);
                        break;
                    }
                    case 'V':
                    {
                        var y = Next();
                        position = new Vector2(position.x, relative ? position.y + y : y);
                        current.Add(position);
                        break;
                    }
                    case 'A':
                    {
                        var rx = Next();
                        var ry = Next();
                        Next(); // x-axis rotation: always 0 in these icons
                        var largeArc = Next() != 0;
                        var sweep = Next() != 0;
                        var end = new Vector2(Next(), Next());
                        if (relative) end += position;
                        AddArc(current, position, end, rx, ry, largeArc, sweep);
                        position = end;
                        break;
                    }
                    case 'Z':
                        current?.Add(start);
                        position = start;
                        break;
                }

                // A letter with no numbers after it (Z) must not loop forever.
                if (char.ToUpperInvariant(command) == 'Z' && HasNumber()) command = 'L';
            }

            return result;
        }

        /// <summary>SVG endpoint arc → points (spec F.6.5), rotation 0.</summary>
        private static void AddArc(List<Vector2> points, Vector2 from, Vector2 to, float rx, float ry,
            bool largeArc, bool sweep)
        {
            if (rx == 0 || ry == 0) { points.Add(to); return; }

            var half = (from - to) / 2f;
            var lambda = half.x * half.x / (rx * rx) + half.y * half.y / (ry * ry);
            if (lambda > 1) { var s = Mathf.Sqrt(lambda); rx *= s; ry *= s; }

            var numerator = rx * rx * ry * ry - rx * rx * half.y * half.y - ry * ry * half.x * half.x;
            var denominator = rx * rx * half.y * half.y + ry * ry * half.x * half.x;
            var factor = Mathf.Sqrt(Mathf.Max(0, numerator / denominator)) * (largeArc == sweep ? -1 : 1);
            var centerPrime = new Vector2(factor * rx * half.y / ry, -factor * ry * half.x / rx);
            var center = centerPrime + (from + to) / 2f;

            float Angle(Vector2 v) => Mathf.Atan2(v.y, v.x);
            var startAngle = Angle(new Vector2((half.x - centerPrime.x) / rx, (half.y - centerPrime.y) / ry));
            var endAngle = Angle(new Vector2((-half.x - centerPrime.x) / rx, (-half.y - centerPrime.y) / ry));
            var delta = endAngle - startAngle;
            if (sweep && delta < 0) delta += 2 * Mathf.PI;
            if (!sweep && delta > 0) delta -= 2 * Mathf.PI;

            var segments = Mathf.Max(4, Mathf.CeilToInt(Mathf.Abs(delta) / (Mathf.PI / 12)));
            for (var i = 1; i <= segments; i++)
            {
                var angle = startAngle + delta * i / segments;
                points.Add(center + new Vector2(rx * Mathf.Cos(angle), ry * Mathf.Sin(angle)));
            }
        }
    }
}
