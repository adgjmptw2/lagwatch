using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using static LagWatch.Lang;

namespace LagWatch
{
    /// <summary>게임 설정 하나. 값은 화면이 열려 있을 때만 다시 읽는다.</summary>
    internal abstract class Knob
    {
        public string Label; public string[] Options = new string[0];
        public int Now = -1;            // 지금 값. -1 = 알 수 없음
        public int Rec = -1;            // 한 번에 적용할 때 고를 값. -1 = 건드리지 않음
        public string Note = "";
        /// <summary>되돌리기가 고르는 값</summary>
        public virtual int Home { get { return 0; } }
        public abstract void Read();
        /// <summary>안 되면 이유, 되면 null</summary>
        public abstract string Set(int i);
    }

    /// <summary>윈도우가 게임을 켤 때 줄 우선순위. 게임 프로세스는 열지 않는다.</summary>
    internal sealed class PrioKnob : Knob
    {
        public const string Ifeo = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\";
        const int High = 3;
        readonly string exe; readonly string save;
        public PrioKnob(string exe, string dir) { this.exe = exe; save = dir == null ? null : Path.Combine(dir, "prio-" + exe + ".txt"); Label = Tx("CPU 우선순위", "CPU priority"); Options = new[] { Tx("기본", "Normal"), Tx("높음", "High") }; Rec = 1; }

        static int? Value(string exe) { try { using (var k = Registry.LocalMachine.OpenSubKey(Ifeo + exe + @"\PerfOptions")) { object v = k == null ? null : k.GetValue("CpuPriorityClass"); return v is int ? (int?)(int)v : null; } } catch (Exception) { return null; } }
        public override void Read() { int? v = Value(exe); Now = v == null ? 0 : v == High ? 1 : -1; Note = Now < 0 ? Tx("다른 값 (", "Other value (") + v + ")" : ""; }

        public override string Set(int i)
        {
            try
            {
                if (i == 1)
                {
                    int? old = Value(exe);
                    if (old != High && save != null) { Directory.CreateDirectory(Path.GetDirectoryName(save)); File.WriteAllText(save, old == null ? "" : old.Value.ToString()); }
                    using (var k = Registry.LocalMachine.CreateSubKey(Ifeo + exe + @"\PerfOptions")) k.SetValue("CpuPriorityClass", High, RegistryValueKind.DWord);
                }
                else
                {
                    int old; bool had = false; old = 0;
                    try { had = save != null && File.Exists(save) && int.TryParse(File.ReadAllText(save), out old) && old != High; } catch (Exception) { }
                    using (var k = Registry.LocalMachine.OpenSubKey(Ifeo + exe + @"\PerfOptions", true))
                        if (k != null) { if (had) k.SetValue("CpuPriorityClass", old, RegistryValueKind.DWord); else k.DeleteValue("CpuPriorityClass", false); }
                    if (!had) { Prune(Ifeo + exe + @"\PerfOptions"); Prune(Ifeo + exe); }
                    if (save != null && File.Exists(save)) File.Delete(save);
                }
                Read();
                return null;
            }
            catch (Exception ex) { return ex is UnauthorizedAccessException || ex is System.Security.SecurityException ? Tx("관리자 권한 필요", "Administrator required") : ex.Message; }
        }
        /// <summary>우리가 만들어 비어 있게 된 키만 지운다.</summary>
        static void Prune(string path)
        {
            bool empty; using (var k = Registry.LocalMachine.OpenSubKey(path)) empty = k != null && k.ValueCount == 0 && k.SubKeyCount == 0;
            if (empty) Registry.LocalMachine.DeleteSubKey(path, false);
        }
    }

    /// <summary>롤 클라이언트의 "게임 중 클라이언트 닫기". 클라이언트가 꺼져 있을 때만 설정 파일을 바꾼다.</summary>
    internal sealed class ClientKnob : Knob
    {
        readonly Func<bool> running; readonly string bak, orig; public string FilePath;
        public int Pending = -1;
        /// <summary>처음 바꾸기 전 값. 원래 닫기를 쓰던 사람은 되돌려도 닫기.</summary>
        public override int Home { get { try { return orig != null && File.Exists(orig) && File.ReadAllText(orig).Trim() == "1" ? 1 : 0; } catch (Exception) { return 0; } } }
        /// <summary>그 줄의 값 자리. 한 줄뿐일 때만 true.</summary>
        static bool Find(string t, out int at, out bool on)
        {
            const string K = "closeLeagueClientDuringGame:"; at = -1; on = false; int n = 0, i = 0;
            while (i < t.Length)
            {
                int e = t.IndexOf('\n', i); if (e < 0) e = t.Length; string line = t.Substring(i, e - i), v = line.Trim();
                if (v.StartsWith(K, StringComparison.Ordinal)) { v = v.Substring(K.Length).Trim(); if (v == "true" || v == "false") { n++; at = i + line.IndexOf(v, line.IndexOf(K, StringComparison.Ordinal) + K.Length, StringComparison.Ordinal); on = v == "true"; } }
                i = e + 1;
            }
            return n == 1;
        }
        public ClientKnob(string lol, string dir, Func<bool> running) { this.running = running; FilePath = lol == null ? null : Path.Combine(lol, @"Config\LCULocalPreferences.yaml"); bak = dir == null ? null : Path.Combine(dir, "LCULocalPreferences.yaml.bak"); orig = dir == null ? null : Path.Combine(dir, "client-first.txt"); Label = Tx("게임 중 클라이언트", "Client in game"); Options = new[] { Tx("켜 두기", "Keep open"), Tx("닫기", "Close") }; Rec = 1; }

        public override void Read()
        {
            try { int at; bool on = false; Now = FilePath != null && File.Exists(FilePath) && Find(File.ReadAllText(FilePath), out at, out on) ? on ? 1 : 0 : -1; } catch (Exception) { Now = -1; }
            if (Pending >= 0 && Pending == Now) Pending = -1;
            Note = Now < 0 ? Tx("설정 파일 없음", "Settings file not found") : Pending >= 0 ? Tx("클라이언트를 끄면 적용", "Applies when the client closes") : "";
        }
        public override string Set(int i)
        {
            Read(); if (Now < 0) return Tx("설정 파일 없음", "Settings file not found");
            if (i == Now) { Pending = -1; Note = ""; return null; }
            if (running()) { Pending = i; Note = Tx("클라이언트를 끄면 적용", "Applies when the client closes"); return null; }
            try
            {
                string t = File.ReadAllText(FilePath); int at; bool on; if (!Find(t, out at, out on)) return Tx("설정 파일 형식이 다름", "Unexpected settings format");
                if (bak != null && !File.Exists(bak)) { Directory.CreateDirectory(Path.GetDirectoryName(bak)); File.Copy(FilePath, bak); }
                if (orig != null && !File.Exists(orig)) File.WriteAllText(orig, Now.ToString());
                Put(FilePath, t.Substring(0, at) + (i == 1 ? "true" : "false") + t.Substring(at + (on ? 4 : 5)));
                Pending = -1; Read(); return Now == i ? null : Tx("적용 안 됨", "Not applied");
            }
            catch (Exception ex) { return ex.Message; }
        }
        /// <summary>옆에 다 쓴 뒤 한 번에 바꿔 끼운다. 읽기 전용이면 풀었다가 다시 건다.</summary>
        public static void Put(string f, string text)
        {
            string tmp = f + ".lw"; File.WriteAllText(tmp, text, new UTF8Encoding(false)); var at = File.GetAttributes(f); bool ro = (at & FileAttributes.ReadOnly) != 0;
            try { if (ro) File.SetAttributes(f, at & ~FileAttributes.ReadOnly); File.Replace(tmp, f, null); }
            finally { if (ro && File.Exists(f)) File.SetAttributes(f, File.GetAttributes(f) | FileAttributes.ReadOnly); if (File.Exists(tmp)) File.Delete(tmp); }
        }
        /// <summary>미뤄 둔 것을 클라이언트가 꺼진 뒤에 한다.</summary>
        public void Later() { if (Pending >= 0 && !running()) Set(Pending); }
    }

    /// <summary>이터널 리턴 boot.config. 게임이 꺼져 있을 때만, 바꾸기 전 파일을 남기고 바꾼다.</summary>
    internal sealed class BootKnob : Knob
    {
        public const string App = "1049590";
        readonly Func<bool> running; readonly string dir; public string FilePath, Steam;
        public bool Reset;              // 고른 값이 패치로 원본이 됨
        public bool NoOrg;              // 원본을 몰라 게임 기본값으로 돌리려면 Steam 검사가 필요
        public Func<int> Wanted; public Action<int> Want;
        string manifestSha, manifestKey;

        public BootKnob(string er, string steam, string dir, Func<bool> running)
        {
            this.running = running; this.dir = dir; Steam = steam;
            FilePath = er == null ? null : Path.Combine(er, @"EternalReturn_Data\boot.config");
            Label = "boot.config"; Options = new[] { Tx("게임 기본값", "Game default"), Tx("내 설정", "Mine") };
        }

        // ---- 내용 다루기 ----

        public static List<KeyValuePair<string, string>> Parse(string t)
        {
            var l = new List<KeyValuePair<string, string>>();
            foreach (var raw in t.Split('\n')) { string s = raw.Trim(); int i = s.IndexOf('='); if (i > 0 && s[0] != '#') l.Add(new KeyValuePair<string, string>(s.Substring(0, i).Trim(), s.Substring(i + 1).Trim())); }
            return l;
        }
        public static string Guid(string t) { foreach (var p in Parse(t)) if (p.Key == "build-guid") return p.Value; return null; }

        /// <summary>원본에 바꿀 것만 얹는다. 나머지 줄과 build-guid 는 원본 그대로.</summary>
        public static string Merge(string original, List<KeyValuePair<string, string>> change)
        {
            if (Guid(original) == null) return null;
            var map = new Dictionary<string, string>(); foreach (var p in change) if (p.Key != "build-guid") map[p.Key] = p.Value;
            var sb = new StringBuilder(); var done = new HashSet<string>();
            foreach (var p in Parse(original))
            {
                if (p.Key == "build-guid") { foreach (var c in change) if (c.Key != "build-guid" && done.Add(c.Key)) sb.Append(c.Key).Append('=').Append(map[c.Key]).Append('\n'); sb.Append("build-guid=").Append(p.Value).Append('\n'); continue; }
                string v; sb.Append(p.Key).Append('=').Append(map.TryGetValue(p.Key, out v) ? v : p.Value).Append('\n'); done.Add(p.Key);
            }
            return sb.ToString();
        }
        /// <summary>원본과 다르거나 원본에 없는 줄</summary>
        public static string Diff(string original, string t)
        {
            var o = new Dictionary<string, string>(); foreach (var p in Parse(original)) o[p.Key] = p.Value;
            var sb = new StringBuilder(); foreach (var p in Parse(t)) { string v; if (p.Key != "build-guid" && p.Key != "job-worker-count" && (!o.TryGetValue(p.Key, out v) || v != p.Value)) sb.Append(p.Key).Append('=').Append(p.Value).Append('\n'); }
            return sb.ToString();
        }
        public string Build(int kind, string original, string mine)
        {
            if (kind == 0) return original;
            return mine == null ? null : Merge(original, Parse(mine).FindAll(p => p.Key != "job-worker-count")); // 원본으로 켜도 같은 수라 뺀다
        }
        static bool Same(string a, string b)
        {
            if (a == null || b == null) return false;
            var x = new Dictionary<string, string>(StringComparer.Ordinal); foreach (var p in Parse(a)) x[p.Key] = p.Value;
            var y = new Dictionary<string, string>(StringComparer.Ordinal); foreach (var p in Parse(b)) y[p.Key] = p.Value;
            if (x.Count != y.Count) return false; foreach (var p in x) { string v; if (!y.TryGetValue(p.Key, out v) || v != p.Value) return false; }
            return true;
        }

        string Keep(string name) { return dir == null ? null : Path.Combine(dir, name); }
        static string Text(string f) { try { return f != null && File.Exists(f) ? File.ReadAllText(f) : null; } catch (Exception) { return null; } }
        public string Original(string guid) { return guid == null ? null : Text(Keep("boot-" + guid + ".config")); }
        string Mine { get { return Text(Keep("boot-mine.config")); } }

        public override void Read()
        {
            Reset = false; Note = ""; NoOrg = false;
            string t = Text(FilePath); if (t == null) { Now = -1; Note = FilePath == null ? Tx("설치 안 됨", "Not installed") : Tx("파일 없음", "File not found"); return; }
            string guid = Guid(t), org = Original(guid);
            if (org == null && guid != null && Sha(FilePath) == SteamSha()) { Save("boot-" + guid + ".config", t); org = t; } // Steam 이 받은 그대로일 때만 원본으로 남긴다
            if (org != null && Same(t, org)) Now = 0;
            else
            {
                Now = 1;
                // 내 설정은 원본과 다른 줄만 남긴다. 그래야 패치로 바뀐 원본 값이 살아난다
                if (org == null) { if (Mine == null) Save("boot-mine.config", t); }
                else if (Mine == null || Diff(org, t) != Mine) Save("boot-mine.config", Diff(org, t));
            }
            Options[1] = (Mine ?? "").Contains("memorysetup-bucket-allocator") ? "EROptimizer" : Tx("내 설정", "Mine");
            int want = Wanted == null ? -1 : Wanted();
            if (want > 0 && Now == 0) { Reset = true; Note = Tx("패치로 초기화됨", "Reset by a patch"); }
            else if (org == null) Note = Tx("원본 확인 안 됨", "Original not verified");
            NoOrg = org == null;
        }
        void Save(string name, string t) { try { if (dir == null) return; Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, name), t, new UTF8Encoding(false)); } catch (Exception) { } }

        public override string Set(int i)
        {
            if (running()) return Tx("게임을 끈 뒤에 바꿀 수 있음", "Close the game first");
            string t = Text(FilePath); if (t == null) return Tx("파일 없음", "File not found");
            Read(); string guid = Guid(t), org = Original(guid);
            if (i == Now) { if (Want != null) Want(i); Read(); return null; }
            if (org == null)
            {
                if (i != 0) return Tx("원본을 몰라 바꾸지 않음. Steam에서 파일 무결성 확인 후 다시", "Original unknown, not changed. Verify the files in Steam, then try again");
                try { Sys.Open("steam://validate/" + App); } catch (Exception ex) { return ex.Message; }
                return Tx("Steam 파일 무결성 확인을 시작함", "Started Steam file verification");
            }
            string n = Build(i, org, Mine); if (n == null || Guid(n) != guid) return Tx("저장된 설정 없음", "No saved settings");
            try
            {
                string bd = Keep("boot-backup"); if (bd != null) { Directory.CreateDirectory(bd); File.WriteAllText(Path.Combine(bd, DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".config"), t, new UTF8Encoding(false)); Trim(bd, 20); }
                ClientKnob.Put(FilePath, n);
                if (Want != null) Want(i);
                Read(); return Now == i ? null : Tx("적용 안 됨", "Not applied");
            }
            catch (Exception ex) { return ex is UnauthorizedAccessException ? Tx("관리자 권한 필요", "Administrator required") : ex.Message; }
        }
        static void Trim(string d, int keep) { var f = new List<string>(Directory.GetFiles(d, "*.config")); f.Sort(StringComparer.Ordinal); for (int i = 0; i < f.Count - keep; i++) File.Delete(f[i]); }

        // ---- Steam 이 내려받은 원본의 해시 ----

        static string Sha(string f) { try { using (var s = SHA1.Create()) return Hex(s.ComputeHash(File.ReadAllBytes(f))); } catch (Exception) { return null; } }
        static string Hex(byte[] b) { var sb = new StringBuilder(40); foreach (byte x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }

        public string SteamSha()
        {
            try
            {
                string dc = Steam == null ? null : Path.Combine(Steam, "depotcache"); if (dc == null || !Directory.Exists(dc)) return null;
                // 설치된 매니페스트 번호는 appmanifest 에 있다
                string acf = null; foreach (var lib in Boost.Libraries(Steam)) { string a = Path.Combine(lib, @"steamapps\appmanifest_" + App + ".acf"); if (File.Exists(a)) acf = File.ReadAllText(a); }
                string best = null; foreach (var f in Directory.GetFiles(dc, "1049591_*.manifest")) { string id = Path.GetFileNameWithoutExtension(f).Substring(8); if (acf != null && acf.Contains("\"" + id + "\"")) best = f; }
                if (best == null) return null;
                string key = best + File.GetLastWriteTimeUtc(best).Ticks; if (key == manifestKey) return manifestSha;
                manifestSha = FindSha(File.ReadAllBytes(best), @"EternalReturn_Data\boot.config"); manifestKey = key; return manifestSha;
            }
            catch (Exception) { return null; }
        }
        /// <summary>Steam 매니페스트(protobuf)에서 파일 하나의 SHA-1</summary>
        public static string FindSha(byte[] d, string name)
        {
            int i = 0;
            while (i + 8 <= d.Length)
            {
                uint magic = BitConverter.ToUInt32(d, i); int n = BitConverter.ToInt32(d, i + 4); i += 8; if (n < 0 || i + n > d.Length) return null;
                if (magic == 0x71F617D0)
                {
                    int p = i, end = i + n;
                    while (p < end)
                    {
                        ulong k = Var(d, ref p); if ((k & 7) != 2) { Skip(d, ref p, (int)(k & 7)); continue; }
                        int len = (int)Var(d, ref p), e = p + len; if (len < 0 || e > end) return null;
                        if (k >> 3 == 1)
                        {
                            string fn = null, sha = null; int q = p;
                            while (q < e) { ulong kk = Var(d, ref q); if ((kk & 7) != 2) { Skip(d, ref q, (int)(kk & 7)); continue; } int l = (int)Var(d, ref q); if (l < 0 || q + l > e) return null; if (kk >> 3 == 1) fn = Encoding.UTF8.GetString(d, q, l); else if (kk >> 3 == 5 && l == 20) { var b = new byte[20]; Array.Copy(d, q, b, 0, 20); sha = Hex(b); } q += l; }
                            if (fn != null && fn.Replace('/', '\\').Equals(name.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase)) return sha;
                        }
                        p = e;
                    }
                }
                i += n;
            }
            return null;
        }
        static ulong Var(byte[] d, ref int p) { ulong r = 0; int s = 0; while (p < d.Length && s < 64) { byte c = d[p++]; r |= (ulong)(c & 127) << s; s += 7; if (c < 128) break; } return r; }
        static void Skip(byte[] d, ref int p, int t) { if (t == 0) Var(d, ref p); else if (t == 1) p += 8; else if (t == 5) p += 4; else p = d.Length; }
    }

    internal sealed class GameSet
    {
        public string Exe, Label, Path; public readonly List<Knob> Knobs = new List<Knob>();
        public bool Installed { get { return Path != null; } }
    }

    /// <summary>게임별 설정 모음</summary>
    internal sealed class Boost
    {
        public const string Er = "EternalReturn", Lol = "League of Legends";
        public readonly List<GameSet> Games = new List<GameSet>();
        public readonly BootKnob Boot; public readonly ClientKnob Client;
        readonly Monitor m; volatile bool busy; int readAt = -99999, checkAt;
        public Action Changed;

        public Boost(Monitor m, string dir)
        {
            this.m = m; string d = dir == null ? null : System.IO.Path.Combine(dir, "game");
            string steam = SteamDir(), er = ErDir(steam), lol = LolDir();
            var g = new GameSet { Exe = Er, Label = Names.ErName, Path = er }; Games.Add(g);
            g.Knobs.Add(new PrioKnob(Er + ".exe", d));
            Boot = new BootKnob(er, steam, d, () => Runs(Er)) { Wanted = () => m.Set.Boot, Want = i => { m.Set.Boot = i; m.Set.Save(); } }; g.Knobs.Add(Boot);
            g = new GameSet { Exe = Lol, Label = Names.LolName, Path = lol }; Games.Add(g);
            g.Knobs.Add(new PrioKnob(Lol + ".exe", d));
            Client = new ClientKnob(lol, d, () => Runs("LeagueClient")); g.Knobs.Add(Client);
        }
        public GameSet Of(string exe) { foreach (var g in Games) if (g.Exe == exe) return g; return null; }
        bool Runs(string name) { lock (m.Sync) foreach (var q in m.Procs.Values) if (q.Name != null && q.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return true; return false; }

        /// <summary>화면에서 볼 때. 뒤에서 읽는다.</summary>
        public void Look(int every)
        {
            if (busy || Environment.TickCount - readAt < every) return;
            busy = true; readAt = Environment.TickCount;
            System.Threading.ThreadPool.QueueUserWorkItem(o => { try { Read(); } catch (Exception) { } busy = false; if (Changed != null) Changed(); });
        }
        public void Read() { foreach (var g in Games) if (g.Installed) foreach (var k in g.Knobs) k.Read(); }

        /// <summary>1분마다. 미뤄 둔 것을 하고, 패치로 풀렸는지 본다.</summary>
        public void Slow() { if (Client.Pending >= 0) Client.Later(); if (m.Set.Boot > 0 && Boot.FilePath != null && ++checkAt % 5 == 1) Look(0); }

        public string Set(Knob k, int i) { string e = k.Set(i); readAt = -99999; if (Changed != null) Changed(); return e; }

        /// <summary>판마다 남길 설정 표시. 예: p1b0c1g1</summary>
        public string Sig(string exe)
        {
            var g = Of(exe); string s = "";
            if (g != null) foreach (var k in g.Knobs) s += (k is PrioKnob ? "p" : k is BootKnob ? "b" : "c") + (k.Now < 0 ? "x" : k.Now.ToString());
            return s + "g" + (m.Set.Bg ? 1 : 0);
        }

        // ---- 설치 위치 ----

        public static string SteamDir() { try { string s = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string; return s == null ? null : s.Replace('/', '\\'); } catch (Exception) { return null; } }
        public static List<string> Libraries(string steam)
        {
            var l = new List<string>(); if (steam == null) return l; l.Add(steam);
            try { foreach (var x in Sys.Quoted(File.ReadAllText(System.IO.Path.Combine(steam, @"steamapps\libraryfolders.vdf")), "path")) { string p = x.Replace(@"\\", @"\"); if (!l.Exists(y => y.Equals(p, StringComparison.OrdinalIgnoreCase))) l.Add(p); } } catch (Exception) { }
            return l;
        }
        static string ErDir(string steam)
        {
            try
            {
                foreach (var lib in Libraries(steam))
                {
                    string acf = System.IO.Path.Combine(lib, @"steamapps\appmanifest_" + BootKnob.App + ".acf"); if (!File.Exists(acf)) continue;
                    var x = Sys.Quoted(File.ReadAllText(acf), "installdir"); if (x.Count == 0) continue;
                    string d = System.IO.Path.Combine(lib, @"steamapps\common", x[0]); if (File.Exists(System.IO.Path.Combine(d, "EternalReturn.exe"))) return d;
                }
            }
            catch (Exception) { }
            return null;
        }
        /// <summary>"C:/…/League of Legends/": 꼴의 폴더들</summary>
        internal static List<string> Folders(string t)
        {
            var l = new List<string>(); int i = 0;
            while ((i = t.IndexOf("League of Legends", i, StringComparison.Ordinal)) >= 0)
            {
                int a = t.LastIndexOf('"', i), e = t.IndexOf('"', i); i += 17; if (a < 0 || e < 0) continue; string v = t.Substring(a + 1, e - a - 1); int k = e + 1; while (k < t.Length && char.IsWhiteSpace(t[k])) k++;
                if (k < t.Length && t[k] == ':' && v.Length > 3 && char.IsLetter(v[0]) && v[1] == ':' && v[2] == '/' && v.TrimEnd('/').EndsWith("League of Legends", StringComparison.Ordinal)) l.Add(v.Replace('/', '\\').TrimEnd('\\'));
            }
            return l;
        }
        static string LolDir()
        {
            try
            {
                string f = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Riot Games\RiotClientInstalls.json");
                if (File.Exists(f)) foreach (var x in Folders(File.ReadAllText(f))) if (Directory.Exists(System.IO.Path.Combine(x, "Config"))) return x;
                if (Directory.Exists(@"C:\Riot Games\League of Legends\Config")) return @"C:\Riot Games\League of Legends";
            }
            catch (Exception) { }
            return null;
        }
    }

    /// <summary>한 판의 프레임 기록</summary>
    internal sealed class Meter
    {
        public const float Step = .25f; public readonly int[] Bin = new int[1200]; // 300ms 까지
        public int N, Hitch, Ram, Disk, Cpu, Drv; public double Ms; public float Max;
        public void Add(double ms, bool hitch) { N++; Ms += ms; if (ms > Max) Max = (float)ms; if (hitch) Hitch++; Bin[Math.Min(Bin.Length - 1, (int)(ms / Step))]++; }
        public double Fps { get { return Ms > 0 ? N / Ms * 1000 : 0; } }
        /// <summary>느린 쪽 q 만큼의 경계 프레임 시간(ms). 0.01 = 하위 1%</summary>
        public double Slow(double q)
        {
            int need = Math.Max(1, (int)Math.Ceiling(N * q)), c = 0;
            for (int i = Bin.Length - 1; i >= 0; i--) { c += Bin[i]; if (c >= need) return i == Bin.Length - 1 ? Max : (i + .5) * Step; }
            return 0;
        }
    }

    /// <summary>끝난 한 판</summary>
    internal sealed class Round
    {
        public DateTime End; public string Game, Sig; public double Sec, Fps, Low, Max, Cpu, Wait; public int Hitch, HRam, HDisk, HCpu, HDrv, HGame; public DateTime Start;
        public double Fix = -1; // 여러 판의 가운데 값일 때
        public double Per10 { get { return Fix >= 0 ? Fix : Sec > 0 ? Hitch / Sec * 600 : 0; } }
        static readonly CultureInfo C = CultureInfo.InvariantCulture;
        public string Line() { return string.Join("\t", End.ToString("s"), Game, Sec.ToString("0", C), Fps.ToString("0.0", C), Low.ToString("0.0", C), Max.ToString("0", C), Hitch.ToString(C), Cpu.ToString("0", C), Wait.ToString("0.000", C), Sig, HRam.ToString(C), HDisk.ToString(C), HCpu.ToString(C), HDrv.ToString(C), HGame.ToString(C), Start.ToString("s")); }
        /// <summary>끊긴 까닭을 많은 순으로. 예: 램 부족 12, CPU 5, 그 외 3</summary>
        public string Why()
        {
            var l = new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>(Hit.Name[Hit.Ram], HRam), new KeyValuePair<string, int>(Hit.Name[Hit.Disk], HDisk), new KeyValuePair<string, int>("CPU", HCpu), new KeyValuePair<string, int>(Hit.Name[Hit.Drv], HDrv), new KeyValuePair<string, int>(Hit.Name[Hit.Game], HGame) };
            for (int i = 1; i < l.Count; i++) for (int j = i; j > 0 && l[j].Value > l[j - 1].Value; j--) { var t = l[j]; l[j] = l[j - 1]; l[j - 1] = t; } int rest = Hitch; var s = new List<string>(); foreach (var p in l) { rest -= p.Value; if (p.Value > 0) s.Add(p.Key + " " + p.Value); } if (rest > 0 && s.Count > 0) s.Add(Hit.Name[Hit.Etc] + " " + rest);
            return string.Join(", ", s.ToArray());
        }
        public static Round Parse(string s)
        {
            var f = s.Split('\t'); if (f.Length < 10) return null; DateTime t; double sec, fps, low, max, cpu, wait; int h;
            if (!DateTime.TryParseExact(f[0], "s", C, DateTimeStyles.None, out t) || !double.TryParse(f[2], NumberStyles.Float, C, out sec) || !double.TryParse(f[3], NumberStyles.Float, C, out fps) || !double.TryParse(f[4], NumberStyles.Float, C, out low) || !double.TryParse(f[5], NumberStyles.Float, C, out max) || !int.TryParse(f[6], out h) || !double.TryParse(f[7], NumberStyles.Float, C, out cpu) || !double.TryParse(f[8], NumberStyles.Float, C, out wait)) return null;
            int[] w = new int[5]; for (int i = 0; i < 5; i++) if (f.Length > 10 + i) int.TryParse(f[10 + i], out w[i]);
            DateTime st; if (f.Length < 16 || !DateTime.TryParseExact(f[15], "s", C, DateTimeStyles.None, out st)) st = t.AddSeconds(-sec);
            return new Round { HGame = w[4], Start = st, HRam = w[0], HDisk = w[1], HCpu = w[2], HDrv = w[3], End = t, Game = f[1], Sec = sec, Fps = fps, Low = low, Max = max, Hitch = h, Cpu = cpu, Wait = wait, Sig = f[9].Replace("b2", "b1") };
        }
    }

    /// <summary>판의 시작과 끝을 잡아 프레임을 모은다. 롤은 게임이 켜져 있는 동안, 이터널 리턴은 게임 로그의 시작·로비 줄 사이.</summary>
    internal sealed class Rounds
    {
        const int MinSec = 180, LolSkip = 75; public const int MinRounds = 5;
        readonly Frames fr; readonly string file; readonly Func<string, string> sig;
        public readonly List<Round> Items = new List<Round>(); // 오래된 것부터
        public bool Force; // 시험
        string game, mark; bool inRound; double since, cpu, wait; int n;
        LogTail tail; double tailAt;
        public static string ErLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow\NimbleNeuron\Eternal Return\Player.log");

        public Rounds(Frames f, string dir, Func<string, string> sig)
        {
            fr = f; this.sig = sig; file = dir == null ? null : Path.Combine(dir, "games.tsv");
            try { if (file != null && File.Exists(file)) foreach (var l in File.ReadAllLines(file, Encoding.UTF8)) { var r = Round.Parse(l); if (r != null) Items.Add(r); } } catch (Exception) { }
            if (Items.Count > 600) Items.RemoveRange(0, Items.Count - 600);
        }

        /// <summary>표본마다. exe 는 지금 게임(없으면 null)</summary>
        public void Tick(double now, string exe, double cpuPct, double ready, double disk = 0, bool ramLow = false, double drv = 0)
        {
            if (ramLow) tRam = now; if (disk > 0) tDisk = now; if (ready >= .08 || cpuPct >= 88) tCpu = now; if (drv >= .15) tDrv = now;
            if (exe != game) { Finish(now); game = exe; since = now; tail = exe == Boost.Er ? new LogTail(ErLog) : null; tailAt = -10; }
            if (game == null) return;
            if (tail != null)
            {
                if (now - tailAt >= 2) { tailAt = now; foreach (var l in tail.Read()) Mark(l, now); }
            }
            else if (!inRound && now - since >= LolSkip) { double fps, mx; int h; fr.Last(History.Epoch(DateTime.UtcNow), out fps, out mx, out h); if (fps >= 40 || Force) Begin(now); } // 로딩이 길면 화면이 안정된 뒤부터
            if (inRound)
            {
                cpu += cpuPct; wait += ready; n++;
                // 끊긴 때 바로 앞뒤(3초)에 무엇이 있었는지
                var me = fr.Rec; int k = me == null ? 0 : me.Hitch - hitSeen;
                if (k > 0) { hitSeen = me.Hitch; if (now - tRam <= 3) me.Ram += k; else if (now - tDisk <= 3) me.Disk += k; else if (now - tCpu <= 3) me.Cpu += k; else if (now - tDrv <= 3) me.Drv += k; }
            }
        }
        double tRam = -99, tDisk = -99, tCpu = -99, tDrv = -99; int hitSeen;
        public void Mark(string line, double now)
        {
            if (line.StartsWith("Total: ", StringComparison.Ordinal) && line.IndexOf("FindLiveObjects", StringComparison.Ordinal) > 0) { double v; int e = line.IndexOf(" ms", 7, StringComparison.Ordinal); if (e > 0 && double.TryParse(line.Substring(7, e - 7), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 25) { unloadAt = DateTime.Now; unloadMs = v; } return; }
            if (line.IndexOf("OnUpdateGamePlayPhase : PlayGame", StringComparison.Ordinal) >= 0) { if (!inRound) Begin(now); }
            // 결과 화면과 로비 로딩은 판이 아니다(자원을 정리하느라 1초쯤 멈춘다)
            else if (line.IndexOf("[GameResultTeamPlayerSlot:", StringComparison.Ordinal) >= 0 || line.IndexOf("[PROFILE TIME][LOADING][LOBBY]", StringComparison.Ordinal) >= 0 || line.IndexOf("[LobbyClient:Start:", StringComparison.Ordinal) >= 0) Finish(now);
        }
        /// <summary>남은 끊김을 마저 판정하고, 그 사이의 끊김을 돌려준다.</summary>
        public Func<string, DateTime, List<Hit>> Settle;
        DateTime startAt, unloadAt; double unloadMs;
        public DateTime? Live { get { return inRound ? (DateTime?)startAt : null; } }
        /// <summary>그때 게임이 안 쓰는 자원을 치우느라 멈춘 시간(ms). 없으면 0</summary>
        public double Unload(DateTime at) { return Math.Abs((at - unloadAt).TotalSeconds) <= 3 ? unloadMs : 0; }
        void Begin(double now) { inRound = true; startAt = DateTime.Now; cpu = wait = 0; n = 0; mark = sig == null ? "" : sig(game); hitSeen = 0; fr.Record(new Meter()); }
        void Finish(double now)
        {
            if (!inRound) return; inRound = false;
            var m = fr.Record(null); if (m == null || m.Ms < MinSec * 1000) return;
            var r = new Round { End = DateTime.Now, Game = game, Sec = m.Ms / 1000, Fps = m.Fps, Low = 1000 / Math.Max(.5, m.Slow(.01)), Max = m.Max, Hitch = m.Hitch, HRam = m.Ram, HDisk = m.Disk, HCpu = m.Cpu, HDrv = m.Drv, Cpu = n > 0 ? cpu / n : 0, Wait = n > 0 ? wait / n : 0, Sig = mark };
            r.Start = startAt;
            if (Settle != null)
            {
                r.HRam = r.HDisk = r.HCpu = r.HDrv = r.HGame = 0; var l = Settle(game, startAt);
                foreach (var h in l) { if (h.Kind == Hit.Ram) r.HRam++; else if (h.Kind == Hit.Disk) r.HDisk++; else if (h.Kind == Hit.Cpu) r.HCpu++; else if (h.Kind == Hit.Drv) r.HDrv++; else if (h.Kind == Hit.Game) r.HGame++; }
            }
            lock (Items) { Items.Add(r); if (Items.Count > 600) Items.RemoveAt(0); }
            try { if (file != null) File.AppendAllText(file, r.Line() + "\r\n", new UTF8Encoding(false)); } catch (Exception) { }
        }
        public void Close(double now) { Finish(now); game = null; }

        public List<Round> Of(string exe, int max) { var l = new List<Round>(); lock (Items) for (int i = Items.Count - 1; i >= 0 && l.Count < max; i--) if (Items[i].Game == exe) l.Insert(0, Items[i]); return l; }

        /// <summary>아무것도 안 바꾼 설정인지. 모르는 값(x)은 기본으로 본다.</summary>
        public static bool Plain(string sig) { for (int i = 1; i < sig.Length; i += 2) if (sig[i] != '0' && sig[i] != 'x') return false; return true; }

        /// <summary>마지막에 한 설정의 판들과 그 앞 설정의 판들(각각 최대 10판). 가운데 값으로 비교한다.</summary>
        public bool Compare(string exe, out Round before, out int nb, out Round after, out int na)
        {
            before = after = null; nb = na = 0; var l = Of(exe, 60); if (l.Count == 0) return false;
            string now = l[l.Count - 1].Sig; var a = new List<Round>(); var b = new List<Round>(); string other = null;
            for (int i = l.Count - 1; i >= 0; i--)
            {
                if (l[i].Sig == now) { if (other == null && a.Count < 10) a.Add(l[i]); else if (other != null) break; }
                else { if (other == null) other = l[i].Sig; if (l[i].Sig != other || b.Count >= 10) break; b.Add(l[i]); }
            }
            na = a.Count; nb = b.Count; if (na < MinRounds || nb < MinRounds) return false;
            before = Median(b); after = Median(a); return true;
        }
        public static Round Median(List<Round> l)
        {
            Func<Func<Round, double>, double> mid = f => { var v = new List<double>(); foreach (var x in l) v.Add(f(x)); v.Sort(); int n = v.Count; return n == 0 ? 0 : n % 2 == 1 ? v[n / 2] : (v[n / 2 - 1] + v[n / 2]) / 2; };
            return new Round { Fps = mid(x => x.Fps), Low = mid(x => x.Low), Fix = mid(x => x.Per10), Max = mid(x => x.Max) };
        }
        public static Round Mean(List<Round> l)
        {
            var r = new Round(); double s = 0; foreach (var x in l) { s += x.Sec; r.Fps += x.Fps * x.Sec; r.Low += x.Low * x.Sec; r.Hitch += x.Hitch; r.HRam += x.HRam; r.HDisk += x.HDisk; r.HCpu += x.HCpu; r.HDrv += x.HDrv; r.HGame += x.HGame; if (x.Max > r.Max) r.Max = x.Max; }
            r.Sec = s; if (s > 0) { r.Fps /= s; r.Low /= s; }
            return r;
        }
    }

    /// <summary>늘어나는 글 파일의 새 줄만 읽는다. 게임이 쓰는 중이어도 막지 않는다.</summary>
    internal sealed class LogTail
    {
        readonly string path; long pos = -1; string rest = "";
        public LogTail(string path) { this.path = path; }
        public List<string> Read()
        {
            var l = new List<string>();
            try
            {
                using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (pos < 0) pos = f.Length;        // 렉 감시를 켜기 전 것은 보지 않는다
                    if (f.Length < pos) { pos = 0; rest = ""; } // 새로 시작한 파일
                    long left = Math.Min(f.Length - pos, 1 << 20); if (left <= 0) return l;
                    if (f.Length - pos > 1 << 20) pos = f.Length - (1 << 20);
                    f.Position = pos; var b = new byte[left]; int n = 0, k; while (n < left && (k = f.Read(b, n, (int)left - n)) > 0) n += k;
                    pos += n; string s = rest + Encoding.UTF8.GetString(b, 0, n); int e = s.LastIndexOf('\n'); if (e < 0) { rest = s.Length > 65536 ? "" : s; return l; }
                    rest = s.Substring(e + 1); foreach (var line in s.Substring(0, e).Split('\n')) l.Add(line.TrimEnd('\r'));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return l;
        }
    }
}
