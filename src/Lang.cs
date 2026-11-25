using System;
using System.IO;
using System.Runtime.InteropServices;

namespace LagWatch
{
    /// <summary>
    /// 화면 말. 한국어와 영어를 같은 자리에 적는다: Tx("램 청소", "Trim RAM").
    /// 어순이 다른 문장은 자리표로: Tx("{0}가 CPU를 많이 씀", "{1} is using a lot of CPU", Ga(x), x).
    /// 켤 때 한 번 정하고, 바꾸면 다시 켠다.
    /// </summary>
    internal static class Lang
    {
        public static bool En;
        [DllImport("kernel32.dll")] static extern ushort GetUserDefaultUILanguage();

        /// <summary>--lang 이 먼저, 그다음 설정 파일, 없으면 윈도우 표시 언어(한국어가 아니면 영어).</summary>
        public static void Init(string arg, string settings)
        {
            string v = arg;
            if (v == null)
                try { if (File.Exists(settings)) foreach (var line in File.ReadAllLines(settings)) if (line.StartsWith("lang=")) v = line.Substring(5); }
                catch (Exception) { }
            if (v == "en") En = true;
            else if (v == "ko") En = false;
            else try { En = (GetUserDefaultUILanguage() & 0x3FF) != 0x12; } catch (Exception) { En = false; }
        }

        public static string Tx(string ko, string en) { return En ? en : ko; }
        public static string Tx(string ko, string en, params object[] a) { return string.Format(En ? en : ko, a); }
        /// <summary>영어 단·복수. Pl(3, "match", "matches") → "matches"</summary>
        public static string Pl(double n, string one, string many) { return n == 1 ? one : many; }
    }
}
