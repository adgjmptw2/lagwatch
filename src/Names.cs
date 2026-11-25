using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using static LagWatch.Lang;

namespace LagWatch
{
    /// <summary>
    /// 프로세스 이름을 사람이 알아볼 이름으로 바꾼다.
    /// 명령줄은 스크립트 파일 이름만 뽑고 버린다(키·토큰이 들어 있을 수 있어 저장하지 않음).
    /// </summary>
    internal static unsafe class Names
    {
        static readonly HashSet<string> Hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "node", "bun", "deno", "python", "pythonw", "py", "java", "javaw", "powershell", "pwsh", "dotnet", "ruby", "php" };

        static readonly HashSet<string> Generic = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "index", "server", "main", "cli", "app", "start", "run", "worker", "__main__", "bin", "launcher" };

        // 안티치트와 보호 프로세스는 핸들을 열지 않는다
        static readonly string[] Guarded = { "EasyAntiCheat", "EOSAntiCheat", "start_protected_game", "BEService", "vgc", "vgtray", "xigncode", "xhunter", "GameGuard", "GameMon", "nProtect", "NGS", "BlackCipher", "ACE-" };

        static readonly HashSet<string> System_ = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "System", "Registry", "Memory Compression", "MemCompression", "Secure System", "Idle", "smss", "csrss", "wininit", "winlogon",
              "services", "lsass", "LsaIso", "svchost", "dwm", "fontdrvhost", "explorer", "sihost", "taskhostw", "ctfmon", "conhost", "WUDFHost", "spoolsv", "audiodg", "MsMpEng" };

        // 데이터가 깨질 수 있어 여기서 끄지 않는다
        static readonly HashSet<string> Servers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "mysqld", "mariadbd", "postgres", "redis-server", "memurai", "mongod", "sqlservr", "oracle", "cloudflared", "nginx", "httpd", "caddy", "dockerd", "com.docker.backend", "vmmem", "vmmemWSL", "vmwp",
              "query-engine-windows", "schema-engine-windows", "query_engine-windows" };
        public static bool IsServer(string name) { return Servers.Contains(name); }
        static readonly HashSet<string> Dbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mysqld", "mariadbd", "postgres", "redis-server", "memurai", "mongod", "sqlservr", "oracle" };
        public static bool IsDatabase(string name) { return Dbs.Contains(name); }

        public static bool IsGuarded(string name)
        {
            foreach (var g in Guarded) if (name.StartsWith(g, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        public static bool IsSystem(string name) { return System_.Contains(name); }

        // 게임 중 한꺼번에 제한할 때 빼는 것: 음성, 녹화, 입력 장치
        static readonly string[] Live = { "Discord", "obs", "Streamlabs", "XSplit", "TeamSpeak", "ts3client", "nvcontainer", "NVIDIA", "Medal", "voicemeeter", "audiorelay", "lghub", "logi", "iCUE", "Razer", "SteelSeries", "steam", "Spotify" };
        public static bool IsLive(string name) { foreach (var s in Live) if (name.StartsWith(s, StringComparison.OrdinalIgnoreCase)) return true; return false; }

        // 게임과 한 몸인 프로그램. 게임 중에는 램을 비우거나 끄지 않는다
        static readonly string[] Clients = { "LeagueClient", "Riot Client", "RiotClient", "EternalReturnLauncher" };
        public static bool IsGameClient(string name)
        {
            foreach (var c in Clients) if (name.StartsWith(c, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ---- 화면에서 묶어 부르는 이름 ----

        public static string ErName { get { return Tx("이터널 리턴", "Eternal Return"); } }
        public static string LolName { get { return Tx("롤", "LoL"); } }
        static string More { get { return Tx(" 외", " +"); } }

        static readonly Dictionary<string, string> Short = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "League of Legends", LolName }, { "EternalReturn", ErName }, { "chrome", "Chrome" }, { "msedge", "Edge" }, { "discord", "Discord" }, { "kakaotalk", Tx("카카오톡", "KakaoTalk") },
            { "explorer", Tx("탐색기", "Explorer") }, { "claude", "Claude Code" }, { "vgc", "Vanguard" }, { "vgtray", "Vanguard" }, { "dwm", Tx("데스크톱 창 관리자", "Desktop Window Manager") }, { "audiodg", Tx("윈도우 오디오", "Windows Audio") },
            { "MsMpEng", "Windows Defender" }, { "Memory Compression", Tx("메모리 압축", "Memory Compression") }, { "System", Tx("시스템", "System") }, { "msedgewebview2", "WebView2" }, { "SearchApp", Tx("윈도우 검색", "Windows Search") }, { "SearchIndexer", Tx("윈도우 검색", "Windows Search") }
        };
        public static string ShortName(string exe, string fallback) { string s; return Short.TryGetValue(exe, out s) ? s : fallback; }

        /// <summary>여러 프로그램을 한 이름으로 묶는다(롤 클라이언트 셋, node 작업들, 윈도우 자체)</summary>
        public static void Group(App a, out string key, out string label)
        {
            string e = a.Exe ?? "";
            if (a.IsGame) { key = "game|" + e.ToLowerInvariant(); label = ShortName(e, a.Label); return; }
            if (a.Pseudo || a.Service || IsSystem(e)) { key = "win"; label = Tx("윈도우", "Windows"); return; }
            if (e.StartsWith("LeagueClient", StringComparison.OrdinalIgnoreCase)) { key = "lolclient"; label = Tx("롤 클라이언트", "LoL Client"); return; }
            if (e.StartsWith("Riot Client", StringComparison.OrdinalIgnoreCase) || e.StartsWith("RiotClient", StringComparison.OrdinalIgnoreCase)) { key = "riot"; label = "Riot Client"; return; }
            if (e.Equals("node", StringComparison.OrdinalIgnoreCase) || e.StartsWith("query-engine-windows") || e.StartsWith("schema-engine-windows")) { key = "node"; label = "node"; return; }
            int at = a.Key.IndexOf("|@", StringComparison.Ordinal), sl = at > 0 ? a.Key.IndexOf('/', at) : -1; // bun|@scope/tool → tool
            if (sl > 0 && sl < a.Key.Length - 1) { key = label = a.Key.Substring(sl + 1); return; }
            if (e.StartsWith("python", StringComparison.OrdinalIgnoreCase)) { key = "python"; label = "python"; return; }
            if (e.StartsWith("steam", StringComparison.OrdinalIgnoreCase)) { key = "steam"; label = "Steam"; return; }
            if (a.Key == "lghub") { key = "lghub"; label = "G HUB"; return; }
            if (e.StartsWith("vgc") || e.StartsWith("vgtray")) { key = "vanguard"; label = "Vanguard"; return; }
            if (e.StartsWith("lagwatch", StringComparison.OrdinalIgnoreCase)) { key = "lagwatch"; label = Tx("렉 감시", "LagWatch"); return; }
            key = a.Key; label = ShortName(e, a.Label);
        }
        static readonly HashSet<string> Work = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "claude", "code", "cursor", "devenv", "idea64", "pycharm64", "rider64", "webstorm64", "studio64", "notepad++", "sublime_text", "windowsterminal", "cmd", "powershell", "pwsh", "winword", "excel", "powerpnt", "hwp", "photoshop", "blender", "unity" };
        /// <summary>저장하지 않은 것이 있기 쉬운 편집기·터미널·문서. 종료를 권하지 않는다.</summary>
        public static bool IsWork(string name) { return name != null && Work.Contains(name); }
        public static bool IsHost(string name) { return Hosts.Contains(name); }

        public static void Classify(Proc p, out string key, out string label, out bool script, out bool service)
        {
            script = service = false;
            string n = p.Name;
            key = n.ToLowerInvariant(); label = n;
            if (p.IsGame || IsGuarded(n)) return;
            if (key.StartsWith("lghub")) { key = "lghub"; label = "Logitech G HUB"; return; }
            if (key == "svchost")
            {
                string s = ServiceName(p.Pid);
                service = true;
                if (s != null) { key = "svchost|" + s; label = Tx("서비스 · ", "Service · ") + s; }
                return;
            }
            if (Hosts.Contains(n))
            {
                string s = ScriptLabel(n, CommandLine(p.Pid));
                if (s != null) { key = key + "|" + s.ToLowerInvariant(); label = n + " · " + s; script = true; }
                else script = true;
            }
        }

        // ---- 명령줄 → 스크립트 이름 ----

        public static string CommandLine(int pid)
        {
            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                int len = 4096, ret;
                for (int t = 0; t < 2; t++)
                {
                    byte* b = (byte*)Marshal.AllocHGlobal(len);
                    try
                    {
                        uint st = Native.NtQueryInformationProcess(h, 60, b, len, out ret);
                        if (st == 0)
                        {
                            int bytes = *(ushort*)b;
                            return bytes == 0 ? null : new string((char*)*(IntPtr*)(b + 8), 0, bytes / 2);
                        }
                        if (ret <= len) return null;
                        len = ret + 64;
                    }
                    finally { Marshal.FreeHGlobal((IntPtr)b); }
                }
                return null;
            }
            finally { Native.CloseHandle(h); }
        }

        public static List<string> SplitArgs(string cmd)
        {
            var list = new List<string>();
            var sb = new StringBuilder();
            bool q = false, any = false;
            foreach (char c in cmd)
            {
                if (c == '"') { q = !q; any = true; continue; }
                if (!q && (c == ' ' || c == '\t'))
                {
                    if (any || sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); any = false; }
                    continue;
                }
                sb.Append(c);
            }
            if (any || sb.Length > 0) list.Add(sb.ToString());
            return list;
        }

        public static string ScriptLabel(string host, string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return null;
            var a = SplitArgs(cmd);
            string h = host.ToLowerInvariant();
            for (int i = 1; i < a.Count; i++)
            {
                string s = a[i];
                if (s.Length == 0) continue;
                string low = s.ToLowerInvariant();
                if (h.StartsWith("java"))
                {
                    if (low == "-jar" && i + 1 < a.Count) return ShortPath(a[i + 1]);
                    if (low == "-cp" || low == "-classpath" || low == "--module-path" || low == "-p" || low.StartsWith("--add-") && low.IndexOf('=') < 0) { i++; continue; }
                    if (s[0] == '-' || s[0] == '@') continue;
                    int dot = s.LastIndexOf('.');
                    return dot > 0 ? s.Substring(dot + 1) : s;
                }
                if (h == "powershell" || h == "pwsh")
                {
                    if ((low == "-file" || low == "-f") && i + 1 < a.Count) return ShortPath(a[i + 1]);
                    if (low == "-command" || low == "-c" || low.StartsWith("-enc")) return null;
                    if (s[0] == '-') continue;
                    return low.EndsWith(".ps1") ? ShortPath(s) : null;
                }
                if (h.StartsWith("py"))
                {
                    if (low == "-m" && i + 1 < a.Count) return Clean(a[i + 1]);
                    if (low == "-c") return null;
                    if (low == "-x" || low == "-w") { i++; continue; }
                    if (s[0] == '-') continue;
                    return ShortPath(s);
                }
                if (low == "-e" || low == "--eval" || low == "-p" || low == "--print") return null;
                if (low == "-r" || low == "--require" || low == "--import" || low == "--loader" || low == "--env-file" || low == "--conditions" || low == "-c") { i++; continue; }
                if (s[0] == '-') continue;
                if ((h == "bun" || h == "deno" || h == "dotnet") && (low == "run" || low == "x" || low == "exec" || low == "task")) continue;
                return ShortPath(s);
            }
            return null;
        }

        /// <summary>경로를 짧은 이름으로. node_modules 안이면 패키지 이름, 흔한 이름이면 폴더까지.</summary>
        public static string ShortPath(string path)
        {
            string p = Normal(path);
            if (p.Length == 0) return null;
            int nm = p.LastIndexOf("/node_modules/", StringComparison.OrdinalIgnoreCase);
            if (nm >= 0)
            {
                var parts = p.Substring(nm + 14).Split('/');
                string pkg = parts[0];
                if (pkg.StartsWith("@") && parts.Length > 1) pkg += "/" + parts[1];
                if (pkg == ".bin" && parts.Length > 1) pkg = parts[1];
                if (pkg == ".pnpm" && parts.Length > 1) { int at = parts[1].IndexOf('@', 1); pkg = at > 0 ? parts[1].Substring(0, at) : parts[1]; }
                return Clean(pkg);
            }
            var seg = p.Split('/');
            string file = seg[seg.Length - 1];
            string bare = file;
            int dot = file.LastIndexOf('.');
            if (dot > 0) bare = file.Substring(0, dot);
            if (Generic.Contains(bare) && seg.Length > 1 && seg[seg.Length - 2].Length > 0 && !seg[seg.Length - 2].EndsWith(":"))
                return Clean(seg[seg.Length - 2] + "/" + file);
            return Clean(file);
        }

        /// <summary>겹친 빗금과 .. 을 정리</summary>
        static string Normal(string path)
        {
            var o = new List<string>();
            foreach (var x in path.Replace('\\', '/').Split('/'))
            {
                if (x.Length == 0 || x == ".") continue;
                if (x == ".." && o.Count > 0 && o[o.Count - 1] != "..") o.RemoveAt(o.Count - 1); else o.Add(x);
            }
            return (path.StartsWith("/") ? "/" : "") + string.Join("/", o);
        }

        static string Clean(string s)
        {
            if (s.Length == 0) return null;
            if (s.Length > 48) s = s.Substring(0, 47) + "…";
            // 이름이 아니라 값처럼 보이면 쓰지 않는다
            foreach (char c in s) if (c == '=' || c == '?' || c < ' ') return null;
            return s;
        }

        // ---- 서비스 ----

        static Dictionary<int, string> services;
        static int servicesAt;

        static string ServiceName(int pid)
        {
            string s;
            if (services != null && services.TryGetValue(pid, out s)) return s;
            if (services != null && Environment.TickCount - servicesAt < 30000) return null;
            services = LoadServices();
            servicesAt = Environment.TickCount;
            return services.TryGetValue(pid, out s) ? s : null;
        }

        static Dictionary<int, string> LoadServices()
        {
            var map = new Dictionary<int, string>();
            IntPtr scm = Native.OpenSCManagerW(null, null, 0x0004);
            if (scm == IntPtr.Zero) return map;
            try
            {
                int need, count, resume = 0;
                Native.EnumServicesStatusExW(scm, 0, 0x30, 1, null, 0, out need, out count, ref resume, null);
                if (need <= 0) return map;
                int size = need + 8192;
                byte* b = (byte*)Marshal.AllocHGlobal(size);
                try
                {
                    resume = 0;
                    if (!Native.EnumServicesStatusExW(scm, 0, 0x30, 1, b, size, out need, out count, ref resume, null)) return map;
                    // ENUM_SERVICE_STATUS_PROCESSW: 포인터 2개 + SERVICE_STATUS_PROCESS(9*4) → 56바이트
                    for (int i = 0; i < count; i++)
                    {
                        byte* e = b + i * 56;
                        string disp = Marshal.PtrToStringUni(*(IntPtr*)(e + 8));
                        int pid = *(int*)(e + 16 + 28);
                        if (pid == 0 || string.IsNullOrEmpty(disp)) continue;
                        string old;
                        if (!map.TryGetValue(pid, out old)) map[pid] = disp;
                        else if (!old.EndsWith(More, StringComparison.Ordinal)) map[pid] = old + More;
                    }
                }
                finally { Marshal.FreeHGlobal((IntPtr)b); }
            }
            finally { Native.CloseServiceHandle(scm); }
            return map;
        }

        // ---- 파일 정보 ----

        /// <summary>실행 파일 경로. 번호로만 묻기 때문에 게임이나 안티치트의 핸들을 열지 않는다.</summary>
        public static string ImagePath(int pid)
        {
            const int max = 2048;
            byte* b = (byte*)Marshal.AllocHGlobal(24 + max);
            try
            {
                *(long*)b = pid;
                *(ushort*)(b + 8) = 0; *(ushort*)(b + 10) = max; *(IntPtr*)(b + 16) = (IntPtr)(b + 24);
                int ret;
                if (Native.NtQuerySystemInformation(Native.SystemProcessIdInformation, b, 24, out ret) != 0) return null;
                int bytes = *(ushort*)(b + 8);
                return bytes == 0 ? null : DosPath(new string((char*)*(IntPtr*)(b + 16), 0, bytes / 2));
            }
            finally { Marshal.FreeHGlobal((IntPtr)b); }
        }

        static List<KeyValuePair<string, string>> drives;

        static string DosPath(string nt)
        {
            if (drives == null)
            {
                var l = new List<KeyValuePair<string, string>>();
                for (char c = 'A'; c <= 'Z'; c++)
                {
                    var sb = new StringBuilder(512);
                    if (Native.QueryDosDeviceW(c + ":", sb, sb.Capacity) != 0) l.Add(new KeyValuePair<string, string>(sb + "\\", c + ":\\"));
                }
                drives = l;
            }
            foreach (var d in drives) if (nt.StartsWith(d.Key, StringComparison.OrdinalIgnoreCase)) return d.Value + nt.Substring(d.Key.Length);
            return null;
        }

        /// <summary>화면이나 기록에 나올 때 한 번만 파일 설명을 읽는다.</summary>
        public static void FillInfo(App a, int pid)
        {
            if (a.InfoDone) return;
            a.InfoDone = true;
            if (a.Pseudo || pid <= 4) return;
            try
            {
                string path = ImagePath(pid);
                if (path == null) return;
                a.Path = path;
                if (a.Script || a.Service || a.IsGame || a.Key == "lghub") return;
                string d = Sys.Description(path);
                if (!string.IsNullOrEmpty(d))
                {
                    d = d.Trim();
                    if (d.Length >= 2 && d.Length <= 44) a.Label = d;
                }
            }
            catch (Exception) { }
        }
    }
}
