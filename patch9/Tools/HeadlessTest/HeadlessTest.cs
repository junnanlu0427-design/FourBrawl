// 不需要 Unity：用 mono 编译核心代码 + 本文件，跑电脑对电脑的整局比赛和联机回环测试。
// mcs -out:test.exe Assets/Brawl/Scripts/Core/*.cs Tools/HeadlessTest/HeadlessTest.cs && mono test.exe
using System;
using System.Collections.Generic;
using Brawl.Core;

class Loopback : INetTransport
{
    public Loopback Peer; public bool host; public int dropEvery;
    readonly Queue<byte[]> inbox = new Queue<byte[]>(); int sent; bool announced;
    public bool IsHost => host; public bool Connected => true; public string Status => "loop";
    public void Poll(Action<int> c, Action<int, byte[], int> d, Action<int> x)
    {
        if (host && !announced) { announced = true; c(1); }
        while (inbox.Count > 0) { var b = inbox.Dequeue(); d(host ? 1 : 0, b, b.Length); }
    }
    public void Send(int conn, byte[] data, int len)
    {
        sent++; if (dropEvery > 0 && sent % dropEvery == 0) return; // 模拟丢包
        var b = new byte[len]; Array.Copy(data, b, len); Peer.inbox.Enqueue(b);
    }
    public void Close() { }
}

static class HeadlessTest
{
    static int Main()
    {
        int fails = 0;
        // 1) 电脑乱斗：每种角色组合跑多场，统计胜率、检测异常和卡死
        var winsByChar = new int[Defs.Chars.Length]; int matches = 0, rounds = 0, maxSteps = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int m = 0; m < 40; m++)
        {
            var sim = new Sim(1000 + m);
            for (int i = 0; i < Defs.MAX_P; i++) { sim.Slots[i].Mode = SlotMode.Cpu; sim.Slots[i].Ch = Defs.Order[(i + m) % Defs.Order.Length]; }
            sim.StartMatch(); int steps = 0, lastRound = 0, roundSteps = 0;
            while (sim.State != GameState.MatchEnd && steps < 60 * 60 * 20)
            {
                sim.Step(); steps++; roundSteps++;
                if (sim.RoundNo != lastRound) { lastRound = sim.RoundNo; maxSteps = Math.Max(maxSteps, roundSteps); roundSteps = 0; rounds++; }
                sim.FxOut.Clear();
            }
            if (sim.State != GameState.MatchEnd) { Console.WriteLine($"比赛 {m} 没有结束！round={sim.RoundNo}"); fails++; continue; }
            matches++; var wch = sim.Slots[sim.WinnerSlot].Ch; winsByChar[(int)wch]++;
        }
        Console.WriteLine($"电脑对局：{matches} 场比赛 / {rounds} 局，用时 {sw.ElapsedMilliseconds}ms，最长一局 {maxSteps / 60f:F1}s");
        for (int i = 0; i < Defs.Chars.Length; i++) Console.WriteLine($"  {Defs.Chars[i].Name,-5} 夺冠 {winsByChar[i]} 次");
        bool top = true; for (int i = 0; i < Defs.Chars.Length; i++) if (i != (int)CharId.CCLD && winsByChar[i] >= winsByChar[(int)CharId.CCLD]) top = false;
        if (!top)
            Console.WriteLine("  注意：ccld 剑圣不是夺冠最多的（设计要求最强）");

        { var nw = new int[Defs.Chars.Length]; var four = new[] { CharId.DVD, CharId.XP, CharId.BW, CharId.GARY };
          for (int m = 0; m < 40; m++) { var sim = new Sim(500 + m); for (int i = 0; i < 4; i++) { sim.Slots[i].Mode = SlotMode.Cpu; sim.Slots[i].Ch = four[(i + m) % 4]; } sim.Slots[4].Mode = SlotMode.Off;
            sim.StartMatch(); int st = 0; while (sim.State != GameState.MatchEnd && st < 60 * 60 * 20) { sim.Step(); st++; sim.FxOut.Clear(); } if (sim.State == GameState.MatchEnd) nw[(int)sim.Slots[sim.WinnerSlot].Ch]++; }
          Console.WriteLine("无剑圣 4 人经典局（40 场）："); foreach (var c in four) Console.WriteLine($"  {Defs.Chars[(int)c].Name,-5} 夺冠 {nw[(int)c]} 次"); }
        // 2) 联机回环：房主 + 1 位朋友，丢 1/7 的包，检查朋友端状态与房主一致（两种模式各一次）
        foreach (var mode in new[] { GameMode.Classic, GameMode.Crown })
        {
            var hostSim = new Sim(7); var a = new Loopback { host = true }; var b = new Loopback { host = false, dropEvery = 7 }; a.Peer = b; b.Peer = a; a.dropEvery = 7;
            var hs = new HostSession(hostSim, a, CharId.CCLD, "房主");
            var cs = new ClientSession(b, CharId.XP, "小明");
            var fxSeen = 0; cs.Mirror.OnFx += f => fxSeen++;
            for (int f = 0; f < 30; f++) { cs.SendInput(0); hs.Pump(1 / 60f); hs.AfterStep(); cs.Pump(1 / 60f); }
            for (int i = 0; i < Defs.MAX_P; i++) if (hostSim.Slots[i].Ch == CharId.GARY) { }
            if (hs.Seats[1].Kind != SeatKind.Peer || hs.Seats[1].Ch != CharId.XP) { Console.WriteLine("朋友没有进到 P2 / 角色不对"); fails++; }
            hs.Seats[2].Kind = SeatKind.Cpu; hs.Seats[3].Ch = CharId.GARY; hs.Mode = mode; hs.StartMatch();
            int maxLen = 0; int tileMismatch = 0;
            for (int f = 0; f < 60 * 50; f++)
            {
                byte bits = (byte)((f / 20) % 3 == 0 ? 2 : (f % 50 < 3 ? 16 : 0) | (f % 90 < 3 ? 4 : 0));
                cs.SendInput(bits); hs.Pump(1 / 60f);
                hostSim.Step(); hs.AfterStep(); maxLen = Math.Max(maxLen, 0);
                cs.Pump(1 / 60f); cs.Smooth(1 / 60f);
                if (f % 120 == 119 && hostSim.State == GameState.Play && cs.Mirror.RoundNo == hostSim.RoundNo)
                {
                    int diff = 0; for (int i = 0; i < hostSim.Tiles.Length; i++) if ((hostSim.Tiles[i] > 0) != (cs.Mirror.Tiles[i] > 0)) diff++;
                    if (diff > 2) tileMismatch++; // 特效坐标量化会让爆炸边缘偶尔差 1~2 格，下一次地形快照会纠正
                }
            }
            Console.WriteLine($"联机回环[{mode}]：朋友在 P{cs.YourSlot + 1}，收到特效 {fxSeen} 个（房主产生 {hostSim.FxSeq} 个），地形抽查不一致 {tileMismatch} 次，房主状态 {hostSim.State}/第{hostSim.RoundNo}局，朋友状态 {cs.Mirror.State}/第{cs.Mirror.RoundNo}局");
            if (cs.Mirror.Mode != mode) { Console.WriteLine("模式没同步"); fails++; }
            if (cs.YourSlot != 1 || cs.Mirror.Players.Count != hostSim.Players.Count) { Console.WriteLine("朋友端玩家数不一致"); fails++; }
            if (fxSeen < hostSim.FxSeq * 0.9) { Console.WriteLine("特效丢失太多"); fails++; }
            if (tileMismatch > 2) { Console.WriteLine("地形不同步"); fails++; }
            // 快照大小
            var w = new ByteWriter(Proto.MAX_PACKET);
            Snapshot.Encode(w, hs, 1, 1, 0, new List<Fx>(), true);
            Console.WriteLine($"含地形的快照大小 {w.Len} 字节（上限 {Proto.MAX_PACKET}）");
        }
        // 2b) 房间人数上限：设成 3 人，朋友加入后开局应只有 3 名玩家；6 人满房快照不超上限
        {
            var hostSim = new Sim(11); var a = new Loopback { host = true }; var b = new Loopback { host = false }; a.Peer = b; b.Peer = a;
            var hs = new HostSession(hostSim, a, CharId.KAI, "房主"); var cs = new ClientSession(b, CharId.GARY, "小红");
            hs.SetMaxPlayers(3);
            for (int f = 0; f < 30; f++) { cs.SendInput(0); hs.Pump(1 / 60f); hs.AfterStep(); cs.Pump(1 / 60f); }
            hs.StartMatch(); for (int f = 0; f < 120; f++) { cs.SendInput(0); hs.Pump(1 / 60f); hostSim.Step(); hs.AfterStep(); cs.Pump(1 / 60f); }
            Console.WriteLine($"房间上限 3 人：开局玩家 {hostSim.Players.Count} 人，朋友端看到上限 {cs.MaxPlayers}");
            if (hostSim.Players.Count != 3 || cs.MaxPlayers != 3) { Console.WriteLine("房间人数上限不对"); fails++; }
            hs.BackToLobby(); hs.SetMaxPlayers(Defs.MAX_P); hs.StartMatch(); for (int f = 0; f < 60; f++) hostSim.Step();
            var w6 = new ByteWriter(Proto.MAX_PACKET); Snapshot.Encode(w6, hs, 1, 0, 0, new List<Fx>(), true);
            Console.WriteLine($"{Defs.MAX_P} 人局快照（含座位名单 + 地形）{w6.Len} 字节，玩家 {hostSim.Players.Count} 人"); if (w6.Len >= Proto.MAX_PACKET || hostSim.Players.Count != Defs.MAX_P) fails++;
            int maxLen = 0; for (int f = 0; f < 60 * 40; f++) { hostSim.Step(); if (f % 30 == 0) { var wq = new ByteWriter(Proto.MAX_PACKET); Snapshot.Encode(wq, hs, 1, f, 0, new List<Fx>(), f % 90 == 0); maxLen = Math.Max(maxLen, wq.Len); } }
            Console.WriteLine($"{Defs.MAX_P} 人局 40 秒内最大快照 {maxLen} 字节（上限 {Proto.MAX_PACKET}）");
        }
        // 3) 王冠模式电脑对局
        { int done = 0; var cw = new int[Defs.Chars.Length]; for (int m = 0; m < 40; m++) { var sim = new Sim(77 + m); sim.Mode = GameMode.Crown; for (int i = 0; i < Defs.MAX_P; i++) { sim.Slots[i].Mode = SlotMode.Cpu; sim.Slots[i].Ch = Defs.Order[(i + m) % Defs.Order.Length]; }
            sim.StartMatch(); int st = 0; while (sim.State != GameState.MatchEnd && st < 60 * 60 * 60) { sim.Step(); st++; sim.FxOut.Clear(); } if (sim.State == GameState.MatchEnd) { done++; cw[(int)sim.Slots[sim.WinnerSlot].Ch]++; } }
          Console.WriteLine($"王冠模式电脑对局：{done}/40 场正常结束"); for (int i = 0; i < Defs.Chars.Length; i++) Console.WriteLine($"  {Defs.Chars[i].Name,-5} 夺冠 {cw[i]} 次"); if (done < 40) fails++; }
        Console.WriteLine(fails == 0 ? "全部通过" : $"失败 {fails} 项");
        return fails;
    }
}
