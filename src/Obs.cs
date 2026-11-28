using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LagWatch
{
    /// <summary>OBS 웹소켓 설정 파일(%APPDATA%\obs-studio\plugin_config\obs-websocket\config.json). 비밀번호는 밖으로 내보내지 않는다.</summary>
    internal sealed class ObsConfig
    {
        public bool Found, Enabled, Auth;
        public int Port = 4455;
        public string Password = "";
        /// <summary>OBS 설정 폴더. 시험에서는 휴대용 OBS 폴더로 바꾼다(--obsdir).</summary>
        public static string ObsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio");
        public static string FilePath { get { return Path.Combine(ObsDir, @"plugin_config\obs-websocket\config.json"); } }

        public static ObsConfig Read() { try { return File.Exists(FilePath) ? Parse(File.ReadAllText(FilePath)) : new ObsConfig(); } catch (Exception) { return new ObsConfig(); } }
        public static ObsConfig Parse(string text)
        {
            var o = Json.Parse(text); var c = new ObsConfig { Found = true };
            c.Enabled = Json.Bool(o, "server_enabled"); c.Auth = Json.Bool(o, "auth_required");
            double p = Json.Num(o, "server_port"); if (p > 0 && p < 65536) c.Port = (int)p;
            c.Password = Json.Str(o, "server_password") ?? "";
            return c;
        }
    }

    /// <summary>1초 표본. 끊김 수는 OBS가 쌓은 누적값 그대로다.</summary>
    internal sealed class ObsStats
    {
        public bool Streaming, Recording, Reconnecting;
        public long RenderSkipped, RenderTotal, EncSkipped, EncTotal, NetSkipped, NetTotal, Bytes, RecBytes;
        public double AvgRenderMs, Fps, ObsCpu, Congestion;
    }

    /// <summary>방송 인코더 설정. 단순 모드는 basic.ini(GetProfileParameter), 고급 모드는 프로필 폴더의 streamEncoder.json.</summary>
    internal sealed class ObsEncoder
    {
        public string Mode = "", Encoder = "", Preset = "";
        public int Kbps;
        public bool Hardware { get { return Encoder.Length > 0 && Encoder.IndexOf("x264", StringComparison.OrdinalIgnoreCase) < 0; } }
    }

    /// <summary>obs-websocket v5 클라이언트. 한 스레드에서만 쓴다.</summary>
    internal sealed class Obs : IDisposable
    {
        ClientWebSocket ws;
        Task<WebSocketReceiveResult> pending;
        readonly byte[] rbuf = new byte[64 * 1024];
        readonly MemoryStream acc = new MemoryStream();
        int nextId;
        public string WsVersion = "", ObsVersion = "", Fail = "";
        /// <summary>방송·녹화 상태가 바뀌었다는 이벤트를 받으면 켠다. 읽는 쪽이 끈다.</summary>
        public bool OutputChanged;

        public bool Open { get { return ws != null && ws.State == WebSocketState.Open; } }

        public static string Auth(string pass, string salt, string challenge)
        {
            using (var h = SHA256.Create())
            {
                string secret = Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(pass + salt)));
                return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(secret + challenge)));
            }
        }

        /// <summary>연결하고 인증까지. 실패하면 Fail에 이유를 남긴다("연결", "인증", "응답").</summary>
        public bool Connect(ObsConfig c, int ms)
        {
            Close(); Fail = "";
            try
            {
                ws = new ClientWebSocket();
                var t = ws.ConnectAsync(new Uri("ws://127.0.0.1:" + c.Port), CancellationToken.None);
                if (!t.Wait(ms) || ws.State != WebSocketState.Open) { Fail = "연결"; Close(); return false; }
                var hello = Json.Parse(Recv(ms) ?? "{}");
                var hd = Json.Obj(hello, "d"); WsVersion = Json.Str(hd, "obsWebSocketVersion") ?? "";
                var au = Json.Obj(hd, "authentication");
                string auth = au != null ? Auth(c.Password, Json.Str(au, "salt") ?? "", Json.Str(au, "challenge") ?? "") : null;
                Send("{\"op\":1,\"d\":{\"rpcVersion\":1," + (auth != null ? "\"authentication\":" + Json.Quote(auth) + "," : "") + "\"eventSubscriptions\":64}}"); // 64 = Outputs
                var idf = Recv(ms);
                if (idf == null || Json.Num(Json.Parse(idf), "op") != 2) { Fail = "인증"; Close(); return false; }
                var v = Request("GetVersion", null, ms); ObsVersion = Json.Str(v, "obsVersion") ?? "";
                return true;
            }
            catch (Exception) { if (Fail.Length == 0) Fail = Open ? "응답" : "인증"; Close(); return false; } // 비밀번호가 틀리면 OBS가 바로 닫는다
        }

        public void Close()
        {
            var w = ws; ws = null; pending = null; acc.SetLength(0);
            if (w == null) return;
            try { if (w.State == WebSocketState.Open) w.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait(300); } catch (Exception) { }
            try { w.Dispose(); } catch (Exception) { }
        }
        public void Dispose() { Close(); }

        void Send(string s) { var b = Encoding.UTF8.GetBytes(s); if (!ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, CancellationToken.None).Wait(3000)) throw new IOException("보내기 시간 초과"); }

        /// <summary>메시지 하나를 ms까지 기다린다. 없으면 null. 받다 만 메시지는 다음 호출이 이어 받는다.</summary>
        public string Recv(int ms)
        {
            var until = Environment.TickCount + ms;
            while (true)
            {
                if (pending == null) pending = ws.ReceiveAsync(new ArraySegment<byte>(rbuf), CancellationToken.None);
                int left = until - Environment.TickCount;
                if (!pending.Wait(Math.Max(0, left))) return null;
                var r = pending.Result; pending = null;
                if (r.MessageType == WebSocketMessageType.Close) throw new IOException("OBS가 연결을 닫음");
                acc.Write(rbuf, 0, r.Count);
                if (!r.EndOfMessage) continue;
                string s = Encoding.UTF8.GetString(acc.GetBuffer(), 0, (int)acc.Length); acc.SetLength(0);
                return s;
            }
        }

        /// <summary>요청 하나. 응답 데이터(responseData)를 돌려주고, 실패하면 null.</summary>
        public object Request(string type, string data, int ms)
        {
            string id = "q" + (++nextId);
            Send("{\"op\":6,\"d\":{\"requestType\":" + Json.Quote(type) + ",\"requestId\":\"" + id + "\"" + (data != null ? ",\"requestData\":" + data : "") + "}}");
            var d = Wait(7, id, ms); if (d == null) return null;
            return Json.Bool(Json.Obj(d, "requestStatus"), "result") ? Json.Obj(d, "responseData") : null;
        }

        /// <summary>여러 요청을 한 번에(RequestBatch). 순서대로 responseData, 실패한 자리는 null.</summary>
        public object[] Batch(string[] types, int ms)
        {
            string id = "b" + (++nextId); var sb = new StringBuilder("{\"op\":8,\"d\":{\"requestId\":\"" + id + "\",\"haltOnFailure\":false,\"requests\":[");
            for (int i = 0; i < types.Length; i++) sb.Append(i > 0 ? "," : "").Append("{\"requestType\":").Append(Json.Quote(types[i])).Append(",\"requestId\":\"").Append(i).Append("\"}");
            Send(sb.Append("]}}").ToString());
            var d = Wait(9, id, ms); var res = Json.Arr(d, "results"); var o = new object[types.Length];
            if (res != null) foreach (var x in res) { int k; if (int.TryParse(Json.Str(x, "requestId"), out k) && k >= 0 && k < o.Length && Json.Bool(Json.Obj(x, "requestStatus"), "result")) o[k] = Json.Obj(x, "responseData"); }
            return o;
        }

        /// <summary>그 응답이 올 때까지 받는다. 사이에 온 이벤트는 처리한다.</summary>
        Dictionary<string, object> Wait(int op, string id, int ms)
        {
            var until = Environment.TickCount + ms;
            while (true)
            {
                int left = until - Environment.TickCount; if (left <= 0) throw new IOException("응답 시간 초과");
                var s = Recv(left); if (s == null) continue;
                var m = Json.Parse(s); int o = (int)Json.Num(m, "op", -1); var d = Json.Obj(m, "d");
                if (o == 5) { Event(d); continue; }
                if (o == op && Json.Str(d, "requestId") == id) return d;
            }
        }

        /// <summary>이벤트만 기다린다(방송하지 않을 때). ms 동안 받은 것을 처리한다.</summary>
        public void Pump(int ms)
        {
            var until = Environment.TickCount + ms;
            while (true)
            {
                int left = until - Environment.TickCount; if (left <= 0) return;
                var s = Recv(left); if (s == null) return;
                var m = Json.Parse(s); if ((int)Json.Num(m, "op", -1) == 5) Event(Json.Obj(m, "d"));
            }
        }

        void Event(Dictionary<string, object> d)
        {
            string t = Json.Str(d, "eventType");
            if (t == "StreamStateChanged" || t == "RecordStateChanged") OutputChanged = true;
            else if (t == "ExitStarted") throw new IOException("OBS 종료");
        }

        public static ObsStats Stats(object st, object ss, object rs)
        {
            var s = new ObsStats();
            s.RenderSkipped = (long)Json.Num(st, "renderSkippedFrames", 0); s.RenderTotal = (long)Json.Num(st, "renderTotalFrames", 0);
            s.EncSkipped = (long)Json.Num(st, "outputSkippedFrames", 0); s.EncTotal = (long)Json.Num(st, "outputTotalFrames", 0);
            s.AvgRenderMs = Json.Num(st, "averageFrameRenderTime", 0); s.Fps = Json.Num(st, "activeFps", 0); s.ObsCpu = Json.Num(st, "cpuUsage", 0);
            s.Streaming = Json.Bool(ss, "outputActive"); s.Reconnecting = Json.Bool(ss, "outputReconnecting");
            s.NetSkipped = (long)Json.Num(ss, "outputSkippedFrames", 0); s.NetTotal = (long)Json.Num(ss, "outputTotalFrames", 0);
            s.Bytes = (long)Json.Num(ss, "outputBytes", 0); s.Congestion = Json.Num(ss, "outputCongestion", 0);
            s.Recording = Json.Bool(rs, "outputActive"); s.RecBytes = (long)Json.Num(rs, "outputBytes", 0);
            return s;
        }

        /// <summary>지금 프로필의 방송 인코더 설정</summary>
        public ObsEncoder Encoder(int ms)
        {
            var e = new ObsEncoder();
            Func<string, string, string> p = (cat, name) => Json.Str(Request("GetProfileParameter", "{\"parameterCategory\":" + Json.Quote(cat) + ",\"parameterName\":" + Json.Quote(name) + "}", ms), "parameterValue");
            e.Mode = p("Output", "Mode") ?? "";
            if (e.Mode == "Advanced")
            {
                e.Encoder = p("AdvOut", "Encoder") ?? "";
                var prof = Json.Str(Request("GetProfileList", null, ms), "currentProfileName");
                var se = StreamEncoderJson(prof);
                if (se != null) { e.Kbps = (int)Json.Num(se, "bitrate", 0); e.Preset = Json.Str(se, "preset2") ?? Json.Str(se, "preset") ?? ""; }
            }
            else
            {
                e.Encoder = p("SimpleOutput", "StreamEncoder") ?? "";
                int k; if (int.TryParse(p("SimpleOutput", "VBitrate"), out k)) e.Kbps = k;
                bool nv = e.Encoder.IndexOf("nvenc", StringComparison.OrdinalIgnoreCase) >= 0;
                e.Preset = (nv ? p("SimpleOutput", "NVENCPreset2") : !e.Hardware ? p("SimpleOutput", "Preset") : null) ?? ""; // 다른 하드웨어 인코더 프리셋은 아직 안 읽는다
            }
            return e;
        }

        /// <summary>프로필 이름 → 폴더(user.ini 또는 global.ini의 [Basic] Profile/ProfileDir) → streamEncoder.json</summary>
        static object StreamEncoderJson(string profile)
        {
            try
            {
                string dir = null;
                foreach (var ini in new[] { "user.ini", "global.ini" })
                {
                    var f = Path.Combine(ObsConfig.ObsDir, ini); if (!File.Exists(f)) continue;
                    string name = null, pdir = null;
                    foreach (var l in File.ReadAllLines(f, Encoding.UTF8)) { if (l.StartsWith("Profile=")) name = l.Substring(8); else if (l.StartsWith("ProfileDir=")) pdir = l.Substring(11); }
                    if (pdir != null && (profile == null || name == profile)) { dir = pdir; break; }
                }
                if (dir == null) return null;
                var j = Path.Combine(ObsConfig.ObsDir, @"basic\profiles", dir, "streamEncoder.json");
                return File.Exists(j) ? Json.Parse(File.ReadAllText(j, Encoding.UTF8)) : null;
            }
            catch (Exception) { return null; }
        }
    }
}
