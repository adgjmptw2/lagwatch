using System;
using System.Collections.Generic;
using static LagWatch.Lang;

namespace LagWatch
{
    /// <summary>그림 저장과 시험에서만 쓰는 가짜 상황</summary>
    internal static class Demo
    {
        /// <summary>고른 묶음과 윈도우만 남긴다. 나머지는 기타로 합쳐진다.</summary>
        public static void Only(Monitor m, string[] keys)
        {
            foreach (var k in new List<string>(m.Apps.Keys)) { string key, label; Names.Group(m.Apps[k], out key, out label); if (key != "win" && Array.IndexOf(keys, key) < 0) m.Apps.Remove(k); }
        }
        public static void Causes(Doctor d, string what)
        {
            var l = new List<Cause>(d.Causes);
            string[] names = { "node", "lghub" };
            if (what == "cpu")
            {
                d.FramesOk = true; d.Fps = 141; d.MaxMs = 86; d.Hitch1m = 14;
                var c = new Cause { Key = "node", Level = 2, Metric = Snap.Cpu, Title = Tx("node가 롤 CPU를 뺏는 중", "node is taking CPU from LoL"), Short = Tx("node가 CPU를 뺏는 중", "node is taking CPU"), Why = Tx("최근 1분 끊김 14번, 최대 멈춤 86ms, node CPU 71%", "14 stutters in last min, max freeze 86ms, node CPU 71%"), ShortWhy = Tx("최근 1분 끊김 14번", "14 stutters in last min") };
                c.Acts.Add(new Act(Tx("코어 2개로 제한", "Limit to 2 cores"), "once:node", true, Tx("코어 제한", "Limit cores"))); c.Acts.Add(new Act(Tx("게임할 때마다 제한", "Every game"), "always:node")); l.RemoveAll(x => x.Key == "node"); l.Insert(0, c);
            }
            else if (what == "leak")
            {
                var c = new Cause { Key = "lghub", Level = 2, Metric = Snap.Vm, Title = Tx("G HUB 가상메모리 급증", "G HUB commit spiking"), Short = Tx("G HUB 메모리 급증", "G HUB spiking"), Why = Tx("최근 3시간 819MB → 15.7GB, 약 3시간 반 뒤 꽉 참", "Last 3h: 819MB → 15.7GB, full in about 3.5h"), ShortWhy = "819MB → 15.7GB" };
                c.Acts.Add(new Act(Tx("재시작", "Restart"), "restart:lghub", true)); c.Acts.Add(new Act(Tx("게임 후 재시작", "Restart after game"), "after:lghub")); l.RemoveAll(x => x.Key == "lghub"); l.Insert(0, c);
            }
            else if (what == "ok")
            {
                d.FramesOk = true; d.Fps = 188; d.MaxMs = 10; d.Hitch1m = 0;
                var c = new Cause { Key = "node", Level = -1, Title = Tx("node 코어 2개, 우선순위 낮음", "node: 2 cores, low priority"), Short = Tx("node 코어 제한", "node limited"), Why = Tx("1분간 끊김 14번 → 0번", "Stutters per min 14 → 0"), ShortWhy = Tx("1분간 끊김 14번 → 0번", "Stutters per min 14 → 0") }; c.Acts.Add(new Act(Tx("되돌리기", "Undo"), "undo:node", true)); l.RemoveAll(x => x.Key == "node"); l.Insert(0, c);
                d.Fix.Rules.Add(new Rule { Key = "node", Label = "node", Cores = 2 }); d.Fix.Queue.Add(new After { Key = "lghub", Label = "G HUB", Restart = true });
            }
            else if (what == "todo")
            {
                d.FramesOk = true; d.Fps = 188; d.MaxMs = 10; d.Hitch1m = 0;
                var c = new Cause { Key = "node", Level = 1, Metric = 0, Title = Tx("게임 중 node CPU 11%", "node CPU 11% in game"), Short = Tx("게임 중 node 11%", "node 11% in game"), Why = Tx("최근 6시간 게임 중 평균 11%, 최대 24%", "Last 6h in game: avg 11%, max 24%"), ShortWhy = Tx("평균 11%, 최대 24%", "avg 11%, max 24%") }; c.Acts.Add(new Act(Tx("게임할 때 코어 2개로 제한", "Limit to 2 cores in game"), "always:node", true, Tx("코어 제한", "Limit cores"))); l.RemoveAll(x => x.Key == "node"); l.Insert(0, c);
                c = new Cause { Key = "lghub", Level = 1, Metric = 2, Title = Tx("G HUB 가상메모리 증가", "G HUB commit growing"), Short = Tx("G HUB 메모리 증가", "G HUB growing"), Why = Tx("최근 3시간 762MB → 852MB", "Last 3h: 762MB → 852MB"), ShortWhy = "762MB → 852MB" }; c.Acts.Add(new Act(Tx("게임 후 재시작", "Restart after game"), "after:lghub", true)); l.RemoveAll(x => x.Key == "lghub"); l.Insert(1, c);
                c = new Cause { Key = "lolclient", Level = 1, Metric = 2, Title = Tx("롤 클라이언트 가상메모리 증가", "LoL Client commit growing"), Short = Tx("롤 클라이언트 메모리 증가", "LoL Client growing"), Why = Tx("최근 3시간 2.4GB → 3.2GB, 게임을 끄면 줄어듦", "Last 3h: 2.4GB → 3.2GB, drops when the game closes"), ShortWhy = "2.4GB → 3.2GB" }; l.RemoveAll(x => x.Key == "lolclient"); l.Insert(2, c);
            }
            d.Causes = l;
        }
    }
}
