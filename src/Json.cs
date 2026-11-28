using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LagWatch
{
    /// <summary>OBS 메시지와 설정 파일용 작은 JSON. 객체는 Dictionary, 배열은 List, 숫자는 double.</summary>
    internal static class Json
    {
        public static object Parse(string s) { int i = 0; var v = Value(s, ref i); Ws(s, ref i); if (i != s.Length) throw new FormatException("JSON 끝에 남은 글자"); return v; }

        public static Dictionary<string, object> Obj(object o, string key) { var d = o as Dictionary<string, object>; object v; return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null; }
        public static List<object> Arr(object o, string key) { var d = o as Dictionary<string, object>; object v; return d != null && d.TryGetValue(key, out v) ? v as List<object> : null; }
        public static string Str(object o, string key) { var d = o as Dictionary<string, object>; object v; return d != null && d.TryGetValue(key, out v) ? v as string : null; }
        public static double Num(object o, string key, double none = double.NaN) { var d = o as Dictionary<string, object>; object v; return d != null && d.TryGetValue(key, out v) && v is double ? (double)v : none; }
        public static bool Bool(object o, string key) { var d = o as Dictionary<string, object>; object v; return d != null && d.TryGetValue(key, out v) && v is bool && (bool)v; }

        public static string Quote(string s)
        {
            var b = new StringBuilder(s.Length + 2).Append('"');
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') b.Append('\\').Append(c);
                else if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4"));
                else b.Append(c);
            }
            return b.Append('"').ToString();
        }

        static void Ws(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 끝");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(); i++; Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i); string k = Text(s, ref i); Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new FormatException("':' 없음"); i++;
                    d[k] = Value(s, ref i); Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    throw new FormatException("객체가 안 닫힘");
                }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++; Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i)); Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return l; }
                    throw new FormatException("배열이 안 닫힘");
                }
            }
            if (c == '"') return Text(s, ref i);
            if (Word(s, ref i, "true")) return true;
            if (Word(s, ref i, "false")) return false;
            if (Word(s, ref i, "null")) return null;
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (st == i) throw new FormatException("알 수 없는 글자 " + c);
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static bool Word(string s, ref int i, string w) { if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) return false; i += w.Length; return true; }

        static string Text(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') throw new FormatException("문자열이 아님");
            var b = new StringBuilder(); i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return b.ToString();
                if (c != '\\') { b.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': b.Append('\n'); break;
                    case 't': b.Append('\t'); break;
                    case 'r': b.Append('\r'); break;
                    case 'b': b.Append('\b'); break;
                    case 'f': b.Append('\f'); break;
                    case 'u': if (i + 4 > s.Length) throw new FormatException("\\u 짧음"); b.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber)); i += 4; break;
                    default: b.Append(e); break;
                }
            }
            throw new FormatException("문자열이 안 닫힘");
        }
    }
}
