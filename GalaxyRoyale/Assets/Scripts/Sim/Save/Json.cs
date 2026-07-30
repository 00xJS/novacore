// Minimal JSON reader/writer for the save codec. Hand-rolled on purpose:
//  - Zero reflection → immune to IL2CPP code stripping (JsonUtility can't do
//    dictionaries; Newtonsoft's reflection needs link.xml babysitting on iOS).
//  - Pure C# → lives in the Sim assembly (noEngineReferences) and ports
//    unchanged to a future authoritative server.
//
// Model: object = Dictionary<string, object?> (insertion-ordered), array =
// List<object?>, number = long (integral) or double, plus string/bool/null.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GalaxyRoyale.Sim.Save
{
    public static class Json
    {
        // ---------- writing ----------

        public static string Write(object? value)
        {
            var sb = new StringBuilder(256);
            WriteValue(sb, value);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object? value)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string s: WriteString(sb, s); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case double d: WriteDouble(sb, d); break;
                case Dictionary<string, object?> obj: WriteObject(sb, obj); break;
                case List<object?> arr: WriteArray(sb, arr); break;
                default: throw new InvalidOperationException($"Json.Write: unsupported type {value.GetType()}");
            }
        }

        static void WriteDouble(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
                throw new InvalidOperationException("Json.Write: NaN/Infinity not representable");
            // Integral doubles print like TS (no trailing ".0"); "R" round-trips the rest.
            if (d == Math.Floor(d) && Math.Abs(d) < 9.2e18)
                sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
            else
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        static void WriteObject(StringBuilder sb, Dictionary<string, object?> obj)
        {
            sb.Append('{');
            bool first = true;
            foreach (var kv in obj)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteString(sb, kv.Key);
                sb.Append(':');
                WriteValue(sb, kv.Value);
            }
            sb.Append('}');
        }

        static void WriteArray(StringBuilder sb, List<object?> arr)
        {
            sb.Append('[');
            for (int i = 0; i < arr.Count; i++)
            {
                if (i > 0) sb.Append(',');
                WriteValue(sb, arr[i]);
            }
            sb.Append(']');
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------- parsing ----------

        public static object? Parse(string text)
        {
            int i = 0;
            var value = ParseValue(text, ref i);
            SkipWs(text, ref i);
            if (i != text.Length) throw new FormatException($"Json.Parse: trailing content at {i}");
            return value;
        }

        static object? ParseValue(string t, ref int i)
        {
            SkipWs(t, ref i);
            if (i >= t.Length) throw new FormatException("Json.Parse: unexpected end");
            char c = t[i];
            switch (c)
            {
                case '{': return ParseObject(t, ref i);
                case '[': return ParseArray(t, ref i);
                case '"': return ParseString(t, ref i);
                case 't': Expect(t, ref i, "true"); return true;
                case 'f': Expect(t, ref i, "false"); return false;
                case 'n': Expect(t, ref i, "null"); return null;
                default: return ParseNumber(t, ref i);
            }
        }

        static Dictionary<string, object?> ParseObject(string t, ref int i)
        {
            var obj = new Dictionary<string, object?>();
            i++; // {
            SkipWs(t, ref i);
            if (i < t.Length && t[i] == '}') { i++; return obj; }
            while (true)
            {
                SkipWs(t, ref i);
                string key = ParseString(t, ref i);
                SkipWs(t, ref i);
                if (i >= t.Length || t[i] != ':') throw new FormatException($"Json.Parse: expected ':' at {i}");
                i++;
                obj[key] = ParseValue(t, ref i);
                SkipWs(t, ref i);
                if (i >= t.Length) throw new FormatException("Json.Parse: unterminated object");
                if (t[i] == ',') { i++; continue; }
                if (t[i] == '}') { i++; return obj; }
                throw new FormatException($"Json.Parse: expected ',' or '}}' at {i}");
            }
        }

        static List<object?> ParseArray(string t, ref int i)
        {
            var arr = new List<object?>();
            i++; // [
            SkipWs(t, ref i);
            if (i < t.Length && t[i] == ']') { i++; return arr; }
            while (true)
            {
                arr.Add(ParseValue(t, ref i));
                SkipWs(t, ref i);
                if (i >= t.Length) throw new FormatException("Json.Parse: unterminated array");
                if (t[i] == ',') { i++; continue; }
                if (t[i] == ']') { i++; return arr; }
                throw new FormatException($"Json.Parse: expected ',' or ']' at {i}");
            }
        }

        static string ParseString(string t, ref int i)
        {
            if (t[i] != '"') throw new FormatException($"Json.Parse: expected string at {i}");
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= t.Length) throw new FormatException("Json.Parse: unterminated string");
                char c = t[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= t.Length) throw new FormatException("Json.Parse: bad escape");
                char e = t[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > t.Length) throw new FormatException("Json.Parse: bad \\u escape");
                        sb.Append((char)ushort.Parse(t.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException($"Json.Parse: bad escape '\\{e}'");
                }
            }
        }

        static object ParseNumber(string t, ref int i)
        {
            int start = i;
            if (i < t.Length && (t[i] == '-' || t[i] == '+')) i++;
            bool integral = true;
            while (i < t.Length)
            {
                char c = t[i];
                if (c >= '0' && c <= '9') { i++; continue; }
                if (c == '.' || c == 'e' || c == 'E' || c == '-' || c == '+') { integral = c is '-' or '+' ? integral : false; i++; continue; }
                break;
            }
            string s = t.Substring(start, i - start);
            if (s.Length == 0) throw new FormatException($"Json.Parse: bad number at {start}");
            if (integral && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                return l;
            return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static void Expect(string t, ref int i, string word)
        {
            if (i + word.Length > t.Length || t.Substring(i, word.Length) != word)
                throw new FormatException($"Json.Parse: expected '{word}' at {i}");
            i += word.Length;
        }

        static void SkipWs(string t, ref int i)
        {
            while (i < t.Length && (t[i] == ' ' || t[i] == '\t' || t[i] == '\n' || t[i] == '\r')) i++;
        }
    }
}
