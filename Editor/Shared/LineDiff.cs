using System;
using System.Collections.Generic;
using System.Linq;

namespace GameDistrict.MeticaIntegrationTools
{
    /// <summary>
    /// Line diff (Myers' O((N+M)·D) algorithm) and its grouping into hunks, for showing how a
    /// project file differs from the original. Texts are compared after
    /// <see cref="FileText.Normalize"/>, so line endings and trailing spaces never count.
    /// </summary>
    internal static class LineDiff
    {
        public enum Op { Same, Added, Removed }

        public struct Line
        {
            public Op Op;
            public string Text;

            /// <summary>1-based line number in the original; 0 for an added line.</summary>
            public int OldNumber;

            /// <summary>1-based line number in the project file; 0 for a removed line.</summary>
            public int NewNumber;
        }

        public sealed class Hunk
        {
            public readonly List<Line> Lines = new List<Line>();
            public int OldStart, NewStart;
        }

        /// <summary>A normalized text's lines; null or empty text has none.</summary>
        public static string[] LinesOf(string text)
        {
            var normalized = FileText.Normalize(text);
            if (string.IsNullOrEmpty(normalized) || normalized == "\n") return Array.Empty<string>();
            return normalized.Substring(0, normalized.Length - 1).Split('\n');
        }

        /// <summary>Every line of both texts, in order, marked same / added / removed.</summary>
        public static List<Line> Compare(string oldText, string newText) =>
            Compare(LinesOf(oldText), LinesOf(newText));

        public static List<Line> Compare(string[] a, string[] b)
        {
            // Common head and tail first: most files differ in a few places, and this keeps
            // the quadratic-in-the-worst-case part small.
            var head = 0;
            while (head < a.Length && head < b.Length && a[head] == b[head]) head++;
            var tail = 0;
            while (tail < a.Length - head && tail < b.Length - head
                   && a[a.Length - 1 - tail] == b[b.Length - 1 - tail]) tail++;

            var ops = new List<Op>(a.Length + b.Length);
            for (var i = 0; i < head; i++) ops.Add(Op.Same);
            ops.AddRange(Myers(
                new ArraySegment<string>(a, head, a.Length - head - tail),
                new ArraySegment<string>(b, head, b.Length - head - tail)));
            for (var i = 0; i < tail; i++) ops.Add(Op.Same);

            var lines = new List<Line>(ops.Count);
            int x = 0, y = 0;
            foreach (var op in ops)
            {
                switch (op)
                {
                    case Op.Same:
                        lines.Add(new Line { Op = op, Text = b[y], OldNumber = x + 1, NewNumber = y + 1 });
                        x++;
                        y++;
                        break;
                    case Op.Removed:
                        lines.Add(new Line { Op = op, Text = a[x], OldNumber = x + 1 });
                        x++;
                        break;
                    default:
                        lines.Add(new Line { Op = op, Text = b[y], NewNumber = y + 1 });
                        y++;
                        break;
                }
            }

            return lines;
        }

        /// <summary>Changed lines with <paramref name="context"/> unchanged lines around each change.</summary>
        public static List<Hunk> Hunks(List<Line> lines, int context = 3)
        {
            var hunks = new List<Hunk>();
            Hunk current = null;
            var lastChange = -1;

            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Op == Op.Same) continue;

                var from = Math.Max(0, i - context);
                if (current == null || from > lastChange + context + 1)
                {
                    if (current != null) Close(current, lines, lastChange, context);
                    current = new Hunk();
                    hunks.Add(current);
                    for (var j = from; j < i; j++) current.Lines.Add(lines[j]);
                }
                else
                {
                    for (var j = lastChange + 1; j < i; j++) current.Lines.Add(lines[j]);
                }

                current.Lines.Add(lines[i]);
                lastChange = i;
            }

            if (current != null) Close(current, lines, lastChange, context);

            foreach (var hunk in hunks)
            {
                hunk.OldStart = hunk.Lines.Select(l => l.OldNumber).FirstOrDefault(n => n > 0);
                hunk.NewStart = hunk.Lines.Select(l => l.NewNumber).FirstOrDefault(n => n > 0);
            }

            return hunks;
        }

        private static void Close(Hunk hunk, List<Line> lines, int lastChange, int context)
        {
            for (var j = lastChange + 1; j < Math.Min(lines.Count, lastChange + 1 + context); j++)
                hunk.Lines.Add(lines[j]);
        }

        /// <summary>Myers' greedy shortest edit script between two line ranges.</summary>
        private static List<Op> Myers(ArraySegment<string> a, ArraySegment<string> b)
        {
            int n = a.Count, m = b.Count;
            var result = new List<Op>(n + m);
            if (n == 0 && m == 0) return result;

            var max = n + m;
            var offset = max + 1;
            var v = new int[2 * max + 3];
            var trace = new List<int[]>();

            string A(int i) => a.Array[a.Offset + i];
            string B(int i) => b.Array[b.Offset + i];

            var found = -1;
            for (var d = 0; d <= max && found < 0; d++)
            {
                trace.Add((int[])v.Clone());
                for (var k = -d; k <= d; k += 2)
                {
                    var x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])
                        ? v[offset + k + 1]
                        : v[offset + k - 1] + 1;
                    var y = x - k;
                    while (x < n && y < m && A(x) == B(y))
                    {
                        x++;
                        y++;
                    }

                    v[offset + k] = x;
                    if (x >= n && y >= m)
                    {
                        found = d;
                        break;
                    }
                }
            }

            // Walk the trace back from the end, collecting the edit in reverse.
            int cx = n, cy = m;
            for (var d = found; d >= 0; d--)
            {
                var vd = trace[d];
                var k = cx - cy;
                var prevK = k == -d || (k != d && vd[offset + k - 1] < vd[offset + k + 1]) ? k + 1 : k - 1;
                var prevX = vd[offset + prevK];
                var prevY = prevX - prevK;

                while (cx > prevX && cy > prevY)
                {
                    result.Add(Op.Same);
                    cx--;
                    cy--;
                }

                if (d > 0) result.Add(cx == prevX ? Op.Added : Op.Removed);
                cx = prevX;
                cy = prevY;
            }

            result.Reverse();
            return result;
        }
    }
}
