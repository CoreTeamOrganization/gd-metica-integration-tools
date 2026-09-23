using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GameDistrict.MeticaIntegrationTools
{
    internal static class GenreCodeGenerator
    {
        private const string Ns = "GameDistrict.MeticaAnalytics";

        public static string Interface(string genre, List<GenreEventDef> events)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"namespace {Ns}");
            sb.AppendLine("{");
            sb.AppendLine($"    public interface I{genre}Analytics");
            sb.AppendLine("    {");
            foreach (var evt in events)
                sb.AppendLine($"        void {evt.Name}({genre}{evt.Name} data);");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static string Data(string genre, List<GenreEventDef> events)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"namespace {Ns}");
            sb.AppendLine("{");
            foreach (var evt in events)
            {
                sb.AppendLine($"    public class {genre}{evt.Name} : AnalyticsEventData");
                sb.AppendLine("    {");

                foreach (var f in evt.Fields)
                    sb.AppendLine($"        public {TypeName(f.Type)} {f.Name} {{ get; }}");

                if (evt.Fields.Count > 0)
                {
                    sb.AppendLine();
                    var ctorParams = string.Join(", ", evt.Fields.Select(f => $"{TypeName(f.Type)} {ToCamelCase(f.Name)}"));
                    sb.AppendLine($"        public {genre}{evt.Name}({ctorParams})");
                    sb.AppendLine("        {");
                    foreach (var f in evt.Fields)
                        sb.AppendLine($"            {f.Name} = {ToCamelCase(f.Name)};");
                    sb.AppendLine("        }");
                }

                sb.AppendLine("    }");
                sb.AppendLine();
            }
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static string Implementation(string genre, List<GenreEventDef> events)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine($"namespace {Ns}");
            sb.AppendLine("{");
            sb.AppendLine($"    [CreateAssetMenu(fileName = \"{genre}Analytics\", menuName = \"MeticaAnalytics/{genre}\")]");
            sb.AppendLine($"    public class {genre}Analytics : GDMeticaAnalytics, I{genre}Analytics");
            sb.AppendLine("    {");
            sb.AppendLine($"        private static {genre}Analytics _instance;");
            sb.AppendLine($"        public static {genre}Analytics Instance =>");
            sb.AppendLine($"            _instance != null ? _instance : (_instance = Resources.Load<{genre}Analytics>(\"{genre}Analytics\"));");
            sb.AppendLine();
            foreach (var evt in events)
            {
                sb.AppendLine($"        public void {evt.Name}({genre}{evt.Name} data)");
                sb.AppendLine("        {");
                sb.AppendLine("            var payload = CreateBaseEvent();");
                foreach (var f in evt.Fields)
                    sb.AppendLine($"            payload[\"{f.PayloadKey ?? f.Name}\"] = data.{f.Name};");
                sb.AppendLine($"            MergeCustomFields(payload, data);");
                sb.AppendLine($"            LogCustomEvent(\"{ToCamelCase(evt.Name)}\", payload);");
                sb.AppendLine("        }");
                sb.AppendLine();
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        internal static string TypeName(GenreFieldType t) => t switch
        {
            GenreFieldType.Int   => "int",
            GenreFieldType.Float => "float",
            GenreFieldType.Bool  => "bool",
            _                    => "string",
        };

        internal static string ToSnakeCase(string s)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < s.Length; i++)
            {
                if (char.IsUpper(s[i]) && i > 0) sb.Append('_');
                sb.Append(char.ToLower(s[i]));
            }
            return sb.ToString();
        }

        internal static string ToCamelCase(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToLower(s[0]) + s[1..];

        internal static string ToPascal(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
    }
}