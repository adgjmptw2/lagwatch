using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LagWatch
{
    /// <summary>한 시점의 컴퓨터 상태. 비율은 0~1.</summary>
    internal struct MPoint
    {
        public int T;               // UTC 초
        public float Cpu, Core, Ram, Commit, CommitGb, Drv, GMain, GReady, GDisk, Ghz;
        public ushort Game;         // 0 = 게임 없음, 아니면 이름 번호
    }

    /// <summary>프로그램 하나의 1분 기록. 최근 24시간.</summary>
    internal sealed class AppHist
    {
        const int Cap = 1440, Cut = 240;
        int start, count;
        ushort[] commit = new ushort[8], ws = new ushort[8], cores = new ushort[8];

        public bool Any { get { return count > 0; } }
        public int First { get { return start; } }
        public int Last { get { return start + count - 1; } }

        public void Set(int minute, double commitMb, double wsMb, double c)
        {
            if (count == 0) start = minute;
            if (minute < start) return;
            int i = minute - start;
            if (i >= count + Cap) { count = 0; start = minute; i = 0; }
            while (i >= Cap) { Shift(); i = minute - start; }
            Grow(i + 1);
            for (int k = count; k < i; k++) { commit[k] = ws[k] = cores[k] = 0; }
            commit[i] = U(commitMb); ws[i] = U(wsMb); cores[i] = U(c * 1000);
            if (i + 1 > count) count = i + 1;
        }

        static ushort U(double v) { return v <= 0 ? (ushort)0 : v >= 65535 ? (ushort)65535 : (ushort)Math.Round(v); }

        void Shift()
        {
            int n = Math.Min(Cut, count);
            if (n == 0) { start += Cut; return; }
            Array.Copy(commit, n, commit, 0, count - n); Array.Copy(ws, n, ws, 0, count - n); Array.Copy(cores, n, cores, 0, count - n);
            count -= n; start += n;
        }

        void Grow(int n)
        {
            if (n <= commit.Length) return;
            int len = Math.Min(Cap, Math.Max(n, commit.Length * 2));
            Array.Resize(ref commit, len); Array.Resize(ref ws, len); Array.Resize(ref cores, len);
        }

        /// <summary>없던 시간은 false</summary>
        public bool Get(int minute, out float commitMb, out float wsMb, out float c)
        {
            int i = minute - start;
            if (i < 0 || i >= count) { commitMb = wsMb = c = 0; return false; }
            commitMb = commit[i]; wsMb = ws[i]; c = cores[i] / 1000f;
            return commit[i] != 0 || ws[i] != 0;
        }

        /// <summary>그 시각 이후 처음 켜져 있던 때의 커밋(MB). 없으면 -1</summary>
        public float Baseline(int minute)
        {
            for (int i = Math.Max(0, minute - start); i < count; i++) if (commit[i] != 0) return commit[i];
            return -1;
        }

        public float BaselineWs(int minute)
        {
            for (int i = Math.Max(0, minute - start); i < count; i++) if (ws[i] != 0) return ws[i];
            return -1;
        }

        /// <summary>구간 동안 쓴 코어·분</summary>
        public double SumCores(int from, int to)
        {
            double s = 0;
            for (int i = Math.Max(0, from - start); i < count && i <= to - start; i++) s += cores[i];
            return s / 1000;
        }

        public float Peak()
        {
            ushort m = 0;
            for (int i = 0; i < count; i++) { if (commit[i] > m) m = commit[i]; }
            return m;
        }
        public float PeakCores()
        {
            ushort m = 0;
            for (int i = 0; i < count; i++) { if (cores[i] > m) m = cores[i]; }
            return m / 1000f;
        }
    }

    internal struct AppAt
    {
        public ushort Id;
        public float Commit, Ws, Cores;
    }

    internal struct Block
    {
        public int Minute;
        public AppAt[] Apps;
    }

    internal sealed class Day
    {
        public readonly List<MPoint> Points = new List<MPoint>();
        public readonly List<Block> Blocks = new List<Block>();
    }

    /// <summary>
    /// 최근 2시간은 10초, 하루는 1분 간격으로 들고 있고, 5분마다 한 번 파일에 덧붙인다(하루 약 150KB, 35일 보관).
    /// </summary>
    internal sealed class History
    {
        public const int KeepDays = 35, TopApps = 40;
        const int RecM = 26, RecA = 8;

        readonly string dir;
        readonly MPoint[] ten = new MPoint[720], min = new MPoint[1440];
        int tenHead, tenCount, minHead, minCount;
        public int StartedAt;                           // 이번에 켠 시각(UTC 초)
        readonly List<MPoint> pending = new List<MPoint>();
        readonly List<Block> pendingBlocks = new List<Block>();

        // 누적
        MPoint sumT, sumM;
        double wT, wM;
        int slotT, slotM;

        // 이름 번호
        readonly Dictionary<string, ushort> ids = new Dictionary<string, ushort>();
        readonly List<string> keys = new List<string> { "" }, labels = new List<string> { "" };
        bool namesDirty;

        public readonly Dictionary<int, Day> Cache = new Dictionary<int, Day>();

        readonly bool readOnly; // 그림 저장·측정용. 본체의 기록에 아무것도 쓰지 않는다

        public History(string dir, bool readOnly = false)
        {
            this.dir = dir; this.readOnly = readOnly;
            if (dir == null) return;
            try { if (!readOnly) Directory.CreateDirectory(dir); LoadNames(); if (!readOnly) Prune(); } catch (Exception) { }
        }

        public static int Epoch(DateTime utc) { return (int)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; }
        public static DateTime Local(double t) { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(t).ToLocalTime(); }
        static int DayOf(int t) { var d = Local(t); return d.Year * 10000 + d.Month * 100 + d.Day; }

        public ushort Id(string key, string label)
        {
            ushort id;
            if (ids.TryGetValue(key, out id)) { if (labels[id] != label) { labels[id] = label; namesDirty = true; } return id; }
            if (keys.Count >= 60000) return 0;
            id = (ushort)keys.Count;
            ids[key] = id; keys.Add(key); labels.Add(label); namesDirty = true;
            return id;
        }
        public string Label(int id) { return id > 0 && id < labels.Count ? labels[id] : ""; }
        public string Key(int id) { return id > 0 && id < keys.Count ? keys[id] : ""; }
        public ushort FindId(string key) { ushort id; return ids.TryGetValue(key, out id) ? id : (ushort)0; }

        // ---- 쌓기 ----

        /// <summary>표본 하나를 더한다. 1분이 넘어가면 true.</summary>
        public bool Add(MPoint p, double dt)
        {
            if (StartedAt == 0) { StartedAt = p.T; slotT = p.T / 10; slotM = p.T / 60; }
            bool minute = false;
            if (p.T / 10 != slotT) { Flush(ref sumT, ref wT, slotT * 10 + 5, true); slotT = p.T / 10; }
            if (p.T / 60 != slotM) { Flush(ref sumM, ref wM, slotM * 60 + 30, false); slotM = p.T / 60; minute = true; }
            Acc(ref sumT, ref wT, p, dt); Acc(ref sumM, ref wM, p, dt);
            return minute;
        }

        static void Acc(ref MPoint s, ref double w, MPoint p, double dt)
        {
            float d = (float)dt;
            s.Cpu += p.Cpu * d; s.Ram += p.Ram * d; s.Commit += p.Commit * d; s.CommitGb += p.CommitGb * d; s.GMain += p.GMain * d; s.Ghz += p.Ghz * d;
            // 짧게 튀는 값은 최고를 남긴다
            if (p.Core > s.Core) s.Core = p.Core;
            if (p.Drv > s.Drv) s.Drv = p.Drv;
            if (p.GReady > s.GReady) s.GReady = p.GReady;
            if (p.GDisk > s.GDisk) s.GDisk = p.GDisk;
            if (p.Game != 0) s.Game = p.Game;
            w += dt;
        }

        void Flush(ref MPoint s, ref double w, int t, bool tenSec)
        {
            if (w > 0)
            {
                float k = (float)(1 / w);
                s.Cpu *= k; s.Ram *= k; s.Commit *= k; s.CommitGb *= k; s.GMain *= k; s.Ghz *= k; s.T = t;
                if (tenSec) { ten[tenHead] = s; tenHead = (tenHead + 1) % ten.Length; if (tenCount < ten.Length) tenCount++; }
                else
                {
                    min[minHead] = s; minHead = (minHead + 1) % min.Length; if (minCount < min.Length) minCount++;
                    pending.Add(s);
                }
            }
            s = new MPoint(); w = 0;
        }

        public void AddBlock(Block b) { pendingBlocks.Add(b); }

        // ---- 파일 ----

        string F(string prefix, int day) { return Path.Combine(dir, prefix + "-" + day + ".bin"); }

        public void Save()
        {
            if (dir == null || readOnly) { pending.Clear(); pendingBlocks.Clear(); return; }
            try
            {
                if (namesDirty) SaveNames();
                WriteGroup(pending.Count, i => DayOf(pending[i].T), "m", (w, i) => WriteM(w, pending[i]));
                WriteGroup(pendingBlocks.Count, i => DayOf(pendingBlocks[i].Minute * 60), "a", (w, i) => WriteBlock(w, pendingBlocks[i]));
                pending.Clear(); pendingBlocks.Clear();
            }
            catch (Exception) { if (pending.Count > 2000) pending.Clear(); if (pendingBlocks.Count > 400) pendingBlocks.Clear(); }
        }

        void WriteGroup(int n, Func<int, int> day, string prefix, Action<BinaryWriter, int> write)
        {
            int i = 0;
            while (i < n)
            {
                int d = day(i);
                using (var fs = new FileStream(F(prefix, d), FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var w = new BinaryWriter(fs))
                    while (i < n && day(i) == d) write(w, i++);
                Day c; if (Cache.TryGetValue(d, out c)) Cache.Remove(d);
                if (Cache.Count > 2) Cache.Clear(); // 지난 날짜가 쌓이지 않게
            }
        }

        static ushort Q(float v, float k) { double x = Math.Round(v * k); return x <= 0 ? (ushort)0 : x >= 65535 ? (ushort)65535 : (ushort)x; }

        static void WriteM(BinaryWriter w, MPoint p)
        {
            w.Write(p.T);
            w.Write(Q(p.Cpu, 10000)); w.Write(Q(p.Core, 10000)); w.Write(Q(p.Ram, 10000)); w.Write(Q(p.Commit, 10000)); w.Write(Q(p.CommitGb, 100));
            w.Write(Q(p.Drv, 10000)); w.Write(Q(p.GMain, 10000)); w.Write(Q(p.GReady, 10000)); w.Write(Q(p.GDisk, 10000)); w.Write(Q(p.Ghz, 1000));
            w.Write(p.Game);
        }

        static MPoint ReadM(BinaryReader r)
        {
            var p = new MPoint { T = r.ReadInt32() };
            p.Cpu = r.ReadUInt16() / 10000f; p.Core = r.ReadUInt16() / 10000f; p.Ram = r.ReadUInt16() / 10000f; p.Commit = r.ReadUInt16() / 10000f; p.CommitGb = r.ReadUInt16() / 100f;
            p.Drv = r.ReadUInt16() / 10000f; p.GMain = r.ReadUInt16() / 10000f; p.GReady = r.ReadUInt16() / 10000f; p.GDisk = r.ReadUInt16() / 10000f; p.Ghz = r.ReadUInt16() / 1000f;
            p.Game = r.ReadUInt16();
            return p;
        }

        static void WriteBlock(BinaryWriter w, Block b)
        {
            w.Write(b.Minute); w.Write((ushort)b.Apps.Length);
            foreach (var a in b.Apps) { w.Write(a.Id); w.Write(Q(a.Commit, 1)); w.Write(Q(a.Ws, 1)); w.Write(Q(a.Cores, 1000)); }
        }

        public Day LoadDay(int day)
        {
            Day d;
            if (Cache.TryGetValue(day, out d)) return d;
            d = new Day();
            if (dir != null)
            {
                try
                {
                    string f = F("m", day);
                    if (File.Exists(f))
                        using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var r = new BinaryReader(new BufferedStream(fs, 65536)))
                            for (long n = fs.Length / RecM; n > 0; n--) d.Points.Add(ReadM(r));
                    f = F("a", day);
                    if (File.Exists(f))
                        using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var r = new BinaryReader(new BufferedStream(fs, 65536)))
                        {
                            long pos = 0;
                            while (pos + 6 <= fs.Length)
                            {
                                var b = new Block { Minute = r.ReadInt32() };
                                int n = r.ReadUInt16(); pos += 6;
                                if (pos + n * RecA > fs.Length) break;
                                b.Apps = new AppAt[n];
                                for (int i = 0; i < n; i++)
                                    b.Apps[i] = new AppAt { Id = r.ReadUInt16(), Commit = r.ReadUInt16(), Ws = r.ReadUInt16(), Cores = r.ReadUInt16() / 1000f };
                                pos += n * RecA;
                                d.Blocks.Add(b);
                            }
                        }
                }
                catch (Exception) { }
            }
            Cache[day] = d;
            return d;
        }

        void LoadNames()
        {
            string f = Path.Combine(dir, "names.tsv");
            if (!File.Exists(f)) return;
            foreach (var line in File.ReadAllLines(f, Encoding.UTF8))
            {
                int i = line.IndexOf('\t');
                string k = i < 0 ? line : line.Substring(0, i), l = i < 0 ? line : line.Substring(i + 1);
                if (ids.ContainsKey(k) || k.Length == 0) k = "?" + keys.Count; // 줄 번호가 이름 번호라 자리를 지킨다
                ids[k] = (ushort)keys.Count; keys.Add(k); labels.Add(l);
            }
        }

        void SaveNames()
        {
            var sb = new StringBuilder();
            for (int i = 1; i < keys.Count; i++) sb.Append(Clean(keys[i])).Append('\t').Append(Clean(labels[i])).Append('\n');
            string f = Path.Combine(dir, "names.tsv"), tmp = f + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(f)) File.Replace(tmp, f, null); else File.Move(tmp, f);
            namesDirty = false;
        }
        static string Clean(string s) { return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }

        void Prune()
        {
            var lim = DateTime.Today.AddDays(-KeepDays);
            foreach (var f in Directory.GetFiles(dir, "*-*.bin"))
            {
                string n = Path.GetFileNameWithoutExtension(f);
                DateTime d;
                if ((n.StartsWith("m-") || n.StartsWith("a-")) && DateTime.TryParseExact(n.Substring(2), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) && d < lim) File.Delete(f);
            }
        }

        /// <summary>기록이 있는 가장 오래된 시각</summary>
        public int Oldest()
        {
            int best = StartedAt;
            if (dir == null) return best;
            try
            {
                string first = null;
                foreach (var f in Directory.GetFiles(dir, "m-*.bin")) { string n = Path.GetFileNameWithoutExtension(f); if (first == null || string.CompareOrdinal(n, first) < 0) first = n; }
                if (first != null)
                {
                    var d = LoadDay(int.Parse(first.Substring(2)));
                    if (d.Points.Count > 0 && (best == 0 || d.Points[0].T < best)) best = d.Points[0].T;
                }
            }
            catch (Exception) { }
            return best;
        }

        // ---- 읽기 ----

        /// <summary>구간의 점. 이번에 켠 뒤는 메모리에서, 그 전은 파일에서.</summary>
        public void Range(double t0, double t1, List<MPoint> to)
        {
            to.Clear();
            bool fine = t1 - t0 <= 2.2 * 3600 && tenCount > 0;
            double memFrom = fine ? At(ten, tenHead, tenCount, 0).T : minCount > 0 ? At(min, minHead, minCount, 0).T : double.MaxValue;
            if (t0 < memFrom)
            {
                double to2 = Math.Min(t1, memFrom - 1);
                for (var d = Local(t0).Date; d <= Local(to2).Date; d = d.AddDays(1))
                    foreach (var p in LoadDay(d.Year * 10000 + d.Month * 100 + d.Day).Points)
                        if (p.T >= t0 && p.T <= to2) to.Add(p);
                // 아직 파일에 안 쓴 것
                foreach (var p in pending) if (p.T >= t0 && p.T <= to2 && (to.Count == 0 || p.T > to[to.Count - 1].T)) to.Add(p);
            }
            if (fine) { for (int i = 0; i < tenCount; i++) { var p = At(ten, tenHead, tenCount, i); if (p.T >= t0 && p.T <= t1) to.Add(p); } }
            else for (int i = 0; i < minCount; i++) { var p = At(min, minHead, minCount, i); if (p.T >= t0 && p.T <= t1 && (to.Count == 0 || p.T > to[to.Count - 1].T)) to.Add(p); }
        }

        static MPoint At(MPoint[] a, int head, int count, int i) { return a[(head - count + i + a.Length) % a.Length]; }

        /// <summary>파일 기록에서 그 시각 바로 앞 5분 묶음. 없으면 null</summary>
        public AppAt[] BlockAt(int minute)
        {
            var d = Local(minute * 60.0);
            AppAt[] best = null; int gap = 11;
            for (int k = -1; k <= 0; k++)
            {
                var x = d.AddDays(k);
                foreach (var b in LoadDay(x.Year * 10000 + x.Month * 100 + x.Day).Blocks)
                { int g = Math.Abs(minute - b.Minute); if (g < gap) { gap = g; best = b.Apps; } }
            }
            foreach (var b in pendingBlocks) { int g = Math.Abs(minute - b.Minute); if (g < gap) { gap = g; best = b.Apps; } }
            return best;
        }

        public void Blocks(double t0, double t1, List<Block> to)
        {
            to.Clear();
            for (var d = Local(t0).Date; d <= Local(t1).Date; d = d.AddDays(1))
                foreach (var b in LoadDay(d.Year * 10000 + d.Month * 100 + d.Day).Blocks)
                    if (b.Minute * 60.0 >= t0 && b.Minute * 60.0 <= t1) to.Add(b);
            foreach (var b in pendingBlocks) if (b.Minute * 60.0 >= t0 && b.Minute * 60.0 <= t1) to.Add(b);
        }

        /// <summary>지난 24시간의 프로그램 기록. 다시 켰을 때 이어 보이게 한다.</summary>
        public Dictionary<string, List<KeyValuePair<int, AppAt>>> LastDay(int now)
        {
            var map = new Dictionary<string, List<KeyValuePair<int, AppAt>>>();
            var l = new List<Block>();
            Blocks(now - 86400, now, l);
            l.Sort((x, y) => x.Minute.CompareTo(y.Minute));
            foreach (var b in l) foreach (var a in b.Apps)
                {
                    string k = Key(a.Id);
                    if (k.Length == 0) continue;
                    List<KeyValuePair<int, AppAt>> v;
                    if (!map.TryGetValue(k, out v)) map[k] = v = new List<KeyValuePair<int, AppAt>>();
                    v.Add(new KeyValuePair<int, AppAt>(b.Minute, a));
                }
            DropCache();
            return map;
        }

        public void DropCache() { Cache.Clear(); }
    }

    internal static class Forecast
    {
        /// <summary>
        /// 최근 흐름이 이어진다고 볼 때의 기울기(초당). 뚜렷하게 오르는 중이 아니면 false.
        /// 두 점씩 짝지은 기울기의 가운데 값을 써서 튀는 값에 흔들리지 않게 한다.
        /// </summary>
        public static bool Slope(double[] t, double[] v, int n, double minRise, out double slope)
        {
            slope = 0;
            if (n < 10) return false;
            var s = new List<double>(n * n / 2);
            int up = 0;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    double dt = t[j] - t[i];
                    if (dt <= 0) continue;
                    double k = (v[j] - v[i]) / dt;
                    s.Add(k);
                    if (k > 0) up++;
                }
            if (s.Count < 20) return false;
            s.Sort();
            slope = s[s.Count / 2];
            return slope > 0 && up >= 0.75 * s.Count && slope * (t[n - 1] - t[0]) >= minRise;
        }

        /// <summary>점 목록에서 최대 40개만 골라 계산</summary>
        public static bool Commit(List<MPoint> pts, double now, double windowSec, out double slope, out double level)
        {
            slope = level = 0;
            int a = 0;
            while (a < pts.Count && pts[a].T < now - windowSec) a++;
            int n = pts.Count - a;
            if (n < 10 || pts[pts.Count - 1].T - pts[a].T < 2400) return false;
            int m = Math.Min(40, n);
            var t = new double[m]; var v = new double[m];
            for (int i = 0; i < m; i++)
            {
                int lo = a + (int)((long)n * i / m), hi = Math.Max(lo + 1, a + (int)((long)n * (i + 1) / m));
                double st = 0, sv = 0;
                for (int k = lo; k < hi; k++) { st += pts[k].T; sv += pts[k].Commit; }
                t[i] = st / (hi - lo); v[i] = sv / (hi - lo);
            }
            level = v[m - 1];
            return Slope(t, v, m, 0.015, out slope);
        }
    }
}
