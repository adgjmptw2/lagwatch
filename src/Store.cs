using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using static LagWatch.Lang;

namespace LagWatch
{
    internal enum Kind { Leak, Commit, Ram, CpuFull, GameWait, Others, OneCore, Driver, Slow, Procs, Game, Hitch }

    internal sealed class Ev
    {
        public DateTime Start, End;
        public Kind Kind;
        public string Key = "", Text = "";
        public double Peak;
        public bool InGame;

        // 실행 중에만 쓰는 값
        public double LastAt;
        public bool Open;
    }

    internal static class Kinds
    {
        static readonly string[] Name = { Tx("메모리 누수", "Memory leak"), Txt.Title(Snap.Vm), Txt.Title(Snap.Ram), "CPU 100%", Tx("게임 CPU 대기", "Game CPU wait"), Tx("다른 프로그램", "Other apps"), Tx("코어 1개 100%", "One core 100%"), Tx("드라이버", "Driver"), Tx("CPU 클럭", "CPU clock"), Tx("프로세스 수", "Process count"), Tx("게임", "Game"), Tx("끊김", "Stutter") };
        public static string Label(Kind k) { return Name[(int)k]; }
        /// <summary>0 = 참고, 1 = 주의, 2 = 심각</summary>
        public static int Level(Kind k)
        {
            switch (k)
            {
                case Kind.Commit: case Kind.Ram: case Kind.CpuFull: case Kind.GameWait: case Kind.Hitch: return 2;
                case Kind.Game: case Kind.Procs: case Kind.OneCore: case Kind.Others: return 0;
                default: return 1;
            }
        }
    }

    /// <summary>같은 문제는 한 줄로 이어 붙인다. 파일은 이 PC 안에만 둔다.</summary>
    internal sealed class EventLog
    {
        const int Max = 400;
        const double MergeSec = 60;
        readonly string file;
        public readonly List<Ev> Items = new List<Ev>(); // 오래된 것부터
        bool dirty;
        double savedAt;

        public EventLog(string file)
        {
            this.file = file;
            if (file != null) Load();
        }

        public Func<DateTime> Now = () => DateTime.Now;

        Ev FindOpen(Kind k, string key)
        {
            for (int i = Items.Count - 1; i >= 0 && i >= Items.Count - 40; i--)
                if (Items[i].Open && Items[i].Kind == k && Items[i].Key == key) return Items[i];
            return null;
        }

        /// <summary>새로 생긴 기록이면 true</summary>
        public bool Report(Kind k, string key, double peak, double since, double now, bool inGame, Func<string> text)
        {
            var e = FindOpen(k, key);
            if (e != null)
            {
                e.End = Now(); e.LastAt = now;
                if (peak > e.Peak * 1.02) { e.Peak = peak; e.Text = text(); }
                dirty = true;
                return false;
            }
            DateTime t = Now();
            e = new Ev { Kind = k, Key = key, Peak = peak, End = t, Start = t.AddSeconds(-Math.Max(0, now - since)), InGame = inGame, LastAt = now, Open = true, Text = text() };
            Items.Add(e);
            if (Items.Count > Max) Items.RemoveRange(0, Items.Count - Max);
            dirty = true;
            return true;
        }

        public void Add(Kind k, string key, string text, double now, bool inGame)
        {
            Items.Add(new Ev { Kind = k, Key = key, Text = text, Start = Now(), End = Now(), InGame = inGame, LastAt = now });
            if (Items.Count > Max) Items.RemoveRange(0, Items.Count - Max);
            dirty = true;
        }

        public void Tick(double now)
        {
            for (int i = Items.Count - 1; i >= 0 && i >= Items.Count - 40; i--)
                if (Items[i].Open && now - Items[i].LastAt > MergeSec) { Items[i].Open = false; dirty = true; }
            if (dirty && now - savedAt > 60) Save(now);
        }

        public Ev Active(double now)
        {
            Ev best = null;
            for (int i = Items.Count - 1; i >= 0 && i >= Items.Count - 40; i--)
            {
                var e = Items[i];
                if (!e.Open || now - e.LastAt > 12) continue;
                if (best == null || Kinds.Level(e.Kind) > Kinds.Level(best.Kind)) best = e;
            }
            return best;
        }

        public void Clear() { Items.Clear(); dirty = true; }

        public void Save(double now)
        {
            savedAt = now; dirty = false;
            if (file == null) return;
            try
            {
                var sb = new StringBuilder();
                foreach (var e in Items)
                    sb.Append(e.Start.ToString("s")).Append('\t').Append(e.End.ToString("s")).Append('\t').Append(e.Kind).Append('\t')
                      .Append(e.InGame ? "1" : "0").Append('\t').Append(e.Peak.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(One(e.Key)).Append('\t').Append(One(e.Text)).Append('\n');
                string tmp = file + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(file)) File.Replace(tmp, file, null); else File.Move(tmp, file);
            }
            catch (Exception) { dirty = true; }
        }

        static string One(string s) { return s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '); }

        void Load()
        {
            try
            {
                if (!File.Exists(file)) return;
                foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
                {
                    var f = line.Split('\t');
                    if (f.Length < 7) continue;
                    Kind k; DateTime a, b; double p;
                    if (!Enum.TryParse(f[2], out k) || !DateTime.TryParse(f[0], out a) || !DateTime.TryParse(f[1], out b)) continue;
                    double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out p);
                    Items.Add(new Ev { Start = a, End = b, Kind = k, InGame = f[3] == "1", Peak = p, Key = f[5], Text = f[6] });
                }
                if (Items.Count > Max) Items.RemoveRange(0, Items.Count - Max);
            }
            catch (Exception) { }
        }
    }

    internal sealed class Settings
    {
        readonly string file;
        public readonly HashSet<string> Ignore = new HashSet<string>();
        static readonly string[] Defaults = { "EternalReturn", "League of Legends" };
        public readonly HashSet<string> Games = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "EternalReturn", "League of Legends" };
        public readonly List<string> Rules = new List<string>();
        public bool Notify = true;
        public string Lang = "";        // "ko", "en", 비면 윈도우 표시 언어
        public bool AutoTrim, AutoTrimGame;
        public int AutoTrimPct = 85;
        public bool Bg;                 // 게임 중 다른 프로그램 코어 제한
        public bool Blame = true;       // 게임 중 끊긴 까닭을 재기
        public int Boot;                // 고른 boot.config. 0 = 게임 기본값
        public int Sort = 1;
        public bool Asc;
        public int W, H;
        public readonly List<string> TempOff = new List<string>(), TempOn = new List<string>();

        public Settings(string file)
        {
            this.file = file;
            if (file == null || !File.Exists(file)) return;
            try
            {
                foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i < 1) continue;
                    string k = line.Substring(0, i), v = line.Substring(i + 1);
                    int n; int.TryParse(v, out n);
                    switch (k)
                    {
                        case "notify": Notify = n != 0; break;
                        case "lang": Lang = v; break;
                        case "sort": Sort = Math.Max(0, Math.Min(5, n)); break;
                        case "asc": Asc = n != 0; break;
                        case "w": W = n; break;
                        case "h": H = n; break;
                        case "ignore": Ignore.Add(v); break;
                        case "game": Games.Add(v); break;
                        case "notgame": Games.Remove(v); break;
                        case "rule": Rules.Add(v); break;
                        case "trim": AutoTrim = n != 0; break;
                        case "trimgame": AutoTrimGame = n != 0; break;
                        case "bg": Bg = n != 0; break;
                        case "blame": Blame = n != 0; break;
                        case "boot": Boot = Math.Max(0, Math.Min(2, n)); break;
                        case "trimpct": AutoTrimPct = Math.Max(60, Math.Min(97, n)); break;
                        case "temp": if (!TempOff.Contains(v)) TempOff.Add(v); break;
                        case "tempon": if (!TempOn.Contains(v)) TempOn.Add(v); break;
                    }
                }
            }
            catch (Exception) { }
        }

        public void Save()
        {
            if (file == null) return;
            try
            {
                var sb = new StringBuilder();
                if (Lang.Length > 0) sb.Append("lang=").Append(Lang).Append('\n');
                sb.Append("notify=").Append(Notify ? 1 : 0).Append('\n').Append("sort=").Append(Sort).Append('\n')
                  .Append("asc=").Append(Asc ? 1 : 0).Append('\n')
                  .Append("w=").Append(W).Append('\n').Append("h=").Append(H).Append('\n');
                sb.Append("trim=").Append(AutoTrim ? 1 : 0).Append('\n').Append("trimgame=").Append(AutoTrimGame ? 1 : 0).Append('\n').Append("trimpct=").Append(AutoTrimPct).Append('\n').Append("bg=").Append(Bg ? 1 : 0).Append('\n').Append("boot=").Append(Boot).Append('\n').Append("blame=").Append(Blame ? 1 : 0).Append('\n');
                foreach (var s in TempOff) sb.Append("temp=").Append(s).Append('\n');
                foreach (var s in TempOn) sb.Append("tempon=").Append(s).Append('\n');
                foreach (var s in Rules) sb.Append("rule=").Append(s).Append('\n');
                foreach (var s in Ignore) sb.Append("ignore=").Append(s).Append('\n');
                foreach (var s in Games) sb.Append("game=").Append(s).Append('\n');
                foreach (var s in Defaults) if (!Games.Contains(s)) sb.Append("notgame=").Append(s).Append('\n');
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }
    }
}
