using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

namespace GameDistrict.MeticaIntegrationTools
{
    internal static class GenreExcelParser
    {
        private static readonly HashSet<string> SkipParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "adid", "appToken", "abTest", "abGroup", "abTestStartDate" };

        private static readonly HashSet<string> SkipEvents = BuildCoreEventNames();

        public static List<string> GetSheetNames(string xlsxPath)
        {
            var names = new List<string>();
            using (var zip = ZipFile.OpenRead(xlsxPath))
            {
                var entry = zip.GetEntry("xl/workbook.xml");
                if (entry == null) return names;
                var doc = LoadXml(entry);
                var ns  = CreateNs(doc);
                foreach (XmlNode n in doc.SelectNodes("//a:sheet", ns))
                    names.Add(n.Attributes?["name"]?.Value ?? "Sheet");
            }
            return names;
        }

        public static (string genreName, List<GenreEventDef> events) Parse(string xlsxPath, int sheetIndex)
        {
            using (var zip = ZipFile.OpenRead(xlsxPath))
            {
                var shared = LoadSharedStrings(zip);

                var entry = zip.GetEntry($"xl/worksheets/sheet{sheetIndex + 1}.xml");
                if (entry == null) throw new Exception($"Sheet {sheetIndex + 1} not found in workbook.");

                var doc  = LoadXml(entry);
                var ns   = CreateNs(doc);
                var grid = BuildGrid(doc, ns, shared);

                int eventCol, paramCol, valueCol;
                int headerRow = FindHeader(grid, out eventCol, out paramCol, out valueCol);
                if (headerRow < 0)
                    throw new Exception("Could not find header row. Expected a 'parameter' column.");

                return ("", BuildEvents(grid, headerRow, eventCol, paramCol, valueCol));
            }
        }

        private static List<List<string>> BuildGrid(XmlDocument doc, XmlNamespaceManager ns, List<string> shared)
        {
            var grid = new List<List<string>>();
            foreach (XmlNode row in doc.SelectNodes("//a:row", ns))
            {
                var cells = new List<string>();
                foreach (XmlNode cell in row.SelectNodes("a:c", ns))
                {
                    int col = CellRefToCol(cell.Attributes?["r"]?.Value ?? "");
                    while (cells.Count <= col) cells.Add("");

                    XmlNode v = cell.SelectSingleNode("a:v", ns);
                    if (v == null) continue;

                    string t = cell.Attributes?["t"]?.Value ?? "";
                    cells[col] = (t == "s" && int.TryParse(v.InnerText, out int idx)) ? shared[idx] : v.InnerText;
                }
                grid.Add(cells);
            }
            return grid;
        }

        private static int FindHeader(List<List<string>> grid, out int eventCol, out int paramCol, out int valueCol)
        {
            eventCol = paramCol = valueCol = -1;
            for (int r = 0; r < Math.Min(grid.Count, 10); r++)
            {
                for (int c = 0; c < grid[r].Count; c++)
                {
                    string cell = grid[r][c].Trim().ToLowerInvariant();
                    if (cell == "metica event name") eventCol = c;
                    else if (cell == "parameter")    paramCol = c;
                    else if (cell == "value")        valueCol = c;
                }
                if (paramCol >= 0)
                {
                    if (eventCol < 0) eventCol = paramCol - 1;
                    return r;
                }
            }
            return -1;
        }

        private static List<GenreEventDef> BuildEvents(
            List<List<string>> grid, int headerRow,
            int eventCol, int paramCol, int valueCol)
        {
            var eventMap = new Dictionary<string, List<GenreFieldDef>>(StringComparer.OrdinalIgnoreCase);
            var order    = new List<string>();

            for (int r = headerRow + 1; r < grid.Count; r++)
            {
                var    row   = grid[r];
                string param = Cell(row, paramCol);
                if (string.IsNullOrEmpty(param)) continue;
                if (SkipParams.Contains(param))  continue;

                string rawEvent = Cell(row, eventCol);
                if (string.IsNullOrEmpty(rawEvent) || rawEvent.Contains(' ')) continue;
                if (SkipEvents.Contains(rawEvent)) continue;

                string evtName   = ToPascal(rawEvent);
                string fieldName = ToPascal(param);
                string value     = Cell(row, valueCol);

                if (!eventMap.ContainsKey(evtName)) { eventMap[evtName] = new List<GenreFieldDef>(); order.Add(evtName); }
                if (eventMap[evtName].All(f => f.Name != fieldName))
                    eventMap[evtName].Add(new GenreFieldDef(fieldName, InferType(value), param));
            }

            return order
                .Where(e => eventMap[e].Count > 0)
                .Select(e => new GenreEventDef(e, eventMap[e]))
                .ToList();
        }

        private static List<string> LoadSharedStrings(ZipArchive zip)
        {
            var result = new List<string>();
            var entry  = zip.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return result;

            var doc = LoadXml(entry);
            var ns  = CreateNs(doc);

            foreach (XmlNode si in doc.SelectNodes("//a:si", ns))
            {
                XmlNode t = si.SelectSingleNode("a:t", ns);
                if (t != null)
                {
                    result.Add(t.InnerText);
                }
                else
                {
                    var sb = new StringBuilder();
                    foreach (XmlNode rt in si.SelectNodes("a:r/a:t", ns))
                        sb.Append(rt.InnerText);
                    result.Add(sb.ToString());
                }
            }
            return result;
        }

        private static HashSet<string> BuildCoreEventNames()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var method in typeof(GDMeticaAnalytics).GetMethods())
            {
                string n = method.Name;
                if (n.StartsWith("Log") && n.EndsWith("Event") && n.Length > 8)
                {
                    string middle = n.Substring(3, n.Length - 8);
                    if (!string.IsNullOrEmpty(middle))
                        result.Add(char.ToLower(middle[0]) + middle.Substring(1));
                }
            }
            return result;
        }

        private static XmlDocument LoadXml(ZipArchiveEntry entry)
        {
            var doc = new XmlDocument();
            using (var s = entry.Open()) doc.Load(s);
            return doc;
        }

        private static XmlNamespaceManager CreateNs(XmlDocument doc)
        {
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("a", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            return ns;
        }

        private static int CellRefToCol(string cellRef)
        {
            int col = 0;
            foreach (char c in cellRef)
            {
                if (!char.IsLetter(c)) break;
                col = col * 26 + (char.ToUpper(c) - 'A' + 1);
            }
            return col - 1;
        }

        private static string Cell(List<string> row, int col) =>
            col >= 0 && col < row.Count ? row[col].Trim() : "";

        private static string ToPascal(string s) => GenreCodeGenerator.ToPascal(s);

        private static GenreFieldType InferType(string value)
        {
            if (string.IsNullOrEmpty(value)) return GenreFieldType.String;
            if (value.Equals("true",  StringComparison.OrdinalIgnoreCase) ||
                value.Equals("false", StringComparison.OrdinalIgnoreCase))
                return GenreFieldType.Bool;
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                return (f == (float)Math.Floor(f) && f >= int.MinValue && f <= int.MaxValue)
                    ? GenreFieldType.Int
                    : GenreFieldType.Float;
            return GenreFieldType.String;
        }
    }
}