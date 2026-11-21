// 联机逻辑（与具体传输无关）：房主权威，朋友发输入，房主每秒 30 次下发快照。
// 所有消息都走“不可靠”通道：快照本身是全量状态，特效/挖地形事件带编号，朋友确认前房主会一直重发。
using System;
using System.Collections.Generic;
using static Brawl.Core.Defs;

namespace Brawl.Core
{
    /// <summary>传输层接口：Unity Relay（UTP）或局域网 UDP 都实现它。连接号 0 在朋友端代表房主。</summary>
    public interface INetTransport
    {
        bool IsHost { get; }
        bool Connected { get; }
        string Status { get; }
        void Poll(Action<int> onConnect, Action<int, byte[], int> onData, Action<int> onDisconnect);
        void Send(int conn, byte[] data, int len);
        void Close();
    }

    public enum SeatKind : byte { Off = 0, Open = 1, Cpu = 2, Host = 3, Peer = 4 }

    public sealed class Seat { public SeatKind Kind; public CharId Ch; public string Label = ""; public int Conn = -1; public bool WasOpen; }

    public static class Proto
    {
        public const int VERSION = 12;
        public const byte C_INPUT = 1, H_SNAP = 2, H_REJECT = 3;
        public const int MAX_PACKET = 1200; // 低于 Relay/UDP 的单包上限（10 人局）
        public const int SNAP_FLAG_TERRAIN = 1, SNAP_FLAG_SEATS = 2;
    }

    // ===================== 房主 =====================
    public sealed class HostSession
    {
        public readonly Sim Sim; public readonly INetTransport Net;
        public readonly Seat[] Seats = new Seat[MAX_P];
        public bool Started;
        readonly Dictionary<int, int> connSlot = new Dictionary<int, int>();
        readonly Dictionary<int, int> connFxAck = new Dictionary<int, int>();
        readonly Dictionary<int, float> connLastSeen = new Dictionary<int, float>();
        readonly List<Fx> fxLog = new List<Fx>();
        readonly ByteWriter w = new ByteWriter(Proto.MAX_PACKET);
        int snapSeq, frame; float clock;
        public event Action<string> OnLog;

        public HostSession(Sim sim, INetTransport net, CharId hostChar, string hostName)
        {
            Sim = sim; Net = net;
            Seats[0] = new Seat { Kind = SeatKind.Host, Ch = hostChar, Label = hostName };
            for (int i = 1; i < MAX_P; i++) Seats[i] = new Seat { Kind = SeatKind.Open, Ch = Order[i % Order.Length] };
            sim.OnFx += f => { fxLog.Add(f); if (fxLog.Count > 600) fxLog.RemoveRange(0, fxLog.Count - 600); };
            ApplySeatsToSim();
        }

        public int PeerCount => connSlot.Count;

        /// <summary>房间人数上限（2~6，含房主）。超出上限的座位关闭；有朋友坐着的座位不能被关掉。</summary>
        public int MaxPlayers = MAX_P;
        public int MinAllowedPlayers { get { int m = 2; for (int i = 1; i < MAX_P; i++) if (Seats[i].Kind == SeatKind.Peer) m = Math.Max(m, i + 1); return m; } }
        public void SetMaxPlayers(int n)
        {
            n = Math.Max(MinAllowedPlayers, Math.Min(MAX_P, n)); if (Started) return;
            for (int i = 1; i < MAX_P; i++)
            {
                if (i >= n) { if (Seats[i].Kind != SeatKind.Peer) Seats[i].Kind = SeatKind.Off; }
                else if (i >= MaxPlayers && Seats[i].Kind == SeatKind.Off) Seats[i].Kind = SeatKind.Open;
            }
            MaxPlayers = n; ApplySeatsToSim();
        }

        public void CycleSeat(int i)
        {
            if (i <= 0 || i >= MaxPlayers) return; var s = Seats[i];
            if (s.Kind == SeatKind.Peer || s.Kind == SeatKind.Host) return;
            s.Kind = s.Kind == SeatKind.Open ? SeatKind.Cpu : s.Kind == SeatKind.Cpu ? SeatKind.Off : SeatKind.Open;
            ApplySeatsToSim();
        }
        public void CycleSeatChar(int i, int dir)
        {
            var s = Seats[i]; if (s.Kind == SeatKind.Peer) return;
            int nc = Defs.Chars.Length; s.Ch = (CharId)(((int)s.Ch + dir + nc) % nc); ApplySeatsToSim();
        }

        void ApplySeatsToSim()
        {
            for (int i = 0; i < MAX_P; i++)
            {
                var s = Seats[i]; var c = Sim.Slots[i];
                c.Ch = s.Ch; c.Label = s.Kind == SeatKind.Host ? "房主" : s.Kind == SeatKind.Peer ? (s.Label == "" ? "朋友" : s.Label) : s.Kind == SeatKind.Cpu || s.Kind == SeatKind.Open ? "电脑" : "";
                c.Mode = s.Kind == SeatKind.Host ? SlotMode.Human : s.Kind == SeatKind.Peer ? SlotMode.Net : s.Kind == SeatKind.Off ? SlotMode.Off : (Started || s.Kind == SeatKind.Cpu ? SlotMode.Cpu : SlotMode.Off);
            }
        }

        public GameMode Mode = GameMode.Classic;
        public int MapPick = -1; // -1 = 随机轮换
        public void StartMatch()
        {
            Started = true; Sim.Mode = Mode; Sim.MapPick = MapPick;
            for (int i = 1; i < MAX_P; i++) Seats[i].WasOpen = Seats[i].Kind == SeatKind.Open;
            ApplySeatsToSim(); Sim.StartMatch();
        }
        public void BackToLobby() { Started = false; Sim.State = GameState.Menu; ApplySeatsToSim(); }

        /// <summary>每个渲染帧调用一次（推进网络，不推进模拟）。</summary>
        public void Pump(float dt)
        {
            clock += dt;
            Net.Poll(OnConnect, OnData, OnDisconnect);
            foreach (var kv in new List<KeyValuePair<int, float>>(connLastSeen))
                if (clock - kv.Value > 8) { OnLog?.Invoke("朋友超时掉线"); OnDisconnect(kv.Key); }
        }

        void OnConnect(int conn) { connLastSeen[conn] = clock; }

        void OnDisconnect(int conn)
        {
            connLastSeen.Remove(conn); connFxAck.Remove(conn);
            if (!connSlot.TryGetValue(conn, out int slot)) return;
            connSlot.Remove(conn);
            var s = Seats[slot]; s.Conn = -1;
            s.Kind = Started ? SeatKind.Cpu : SeatKind.Open; if (!Started) s.Label = "";
            if (Started) { s.WasOpen = true; foreach (var p in Sim.Players) if (p.Slot == slot) { p.Kind = SlotMode.Cpu; p.Label = "电脑"; } }
            ApplySeatsToSim(); OnLog?.Invoke($"P{slot + 1} 离开了，由电脑接管");
        }

        void OnData(int conn, byte[] data, int len)
        {
            connLastSeen[conn] = clock;
            var r = new ByteReader(data, len);
            if (r.U8() != Proto.C_INPUT) return;
            int ver = r.U8(); if (ver != Proto.VERSION) { Reject(conn, "版本不一致，请双方使用同一个版本"); return; }
            var ch = (CharId)(r.U8() % Defs.Chars.Length); byte bits = (byte)r.U8(); int ack = r.I32(); string name = r.Str();
            if (!r.Ok) return;
            if (!connSlot.TryGetValue(conn, out int slot))
            {
                slot = -1;
                for (int i = 1; i < MAX_P && slot < 0; i++) if (Seats[i].Kind == SeatKind.Open) slot = i;
                if (slot < 0 && Started) for (int i = 1; i < MAX_P && slot < 0; i++) if (Seats[i].Kind == SeatKind.Cpu && Seats[i].WasOpen) slot = i;
                if (slot < 0) { Reject(conn, "房间已满"); return; }
                connSlot[conn] = slot; var s = Seats[slot]; s.Kind = SeatKind.Peer; s.Conn = conn; s.Label = name; if (!Started) s.Ch = ch;
                connFxAck[conn] = Sim.FxSeq; // 新加入的人不补发旧特效
                if (Started) foreach (var p in Sim.Players) if (p.Slot == slot) { p.Kind = SlotMode.Net; p.Label = name; }
                ApplySeatsToSim(); OnLog?.Invoke($"{name} 加入了 P{slot + 1}");
            }
            var seat = Seats[slot];
            if (!Started && seat.Ch != ch) { seat.Ch = ch; ApplySeatsToSim(); }
            if (seat.Label != name && name != "") { seat.Label = name; ApplySeatsToSim(); }
            Sim.Inputs[slot] = PInput.Unpack(bits);
            if (ack > (connFxAck.TryGetValue(conn, out int a0) ? a0 : 0)) connFxAck[conn] = Math.Min(ack, Sim.FxSeq);
        }

        void Reject(int conn, string why) { w.Reset(); w.U8(Proto.H_REJECT); w.Str(why); Net.Send(conn, w.Buf, w.Len); }

        /// <summary>每个模拟步之后调用；每 2 步（30Hz）向每位朋友发一次快照。</summary>
        public void AfterStep()
        {
            frame++;
            if (frame % 2 != 0) return;
            snapSeq++;
            bool terrain = snapSeq % 30 == 1;
            foreach (var kv in connSlot)
            {
                int ack = connFxAck.TryGetValue(kv.Key, out int a) ? a : 0;
                Snapshot.Encode(w, this, kv.Value, snapSeq, ack, fxLog, terrain);
                Net.Send(kv.Key, w.Buf, w.Len);
            }
        }
    }

    /// <summary>地形差异的游程编码：0x00..0x7f = 连续 1~128 个 0 字节；0x80|n-1 后跟 n 个原样字节。</summary>
    public static class TerrainRle
    {
        public static void Write(ByteWriter w, byte[] b)
        {
            int i = 0, n = b.Length;
            while (i < n)
            {
                if (b[i] == 0) { int j = i; while (j < n && b[j] == 0 && j - i < 128) j++; w.U8(j - i - 1); i = j; }
                else { int j = i; while (j < n && j - i < 128 && (b[j] != 0 || (j + 1 < n && b[j + 1] != 0))) j++; if (j == i) j = i + 1; w.U8(0x80 | (j - i - 1)); for (int k = i; k < j; k++) w.U8(b[k]); i = j; }
            }
        }
        public static void Read(ByteReader r, byte[] b)
        {
            int i = 0, n = b.Length;
            while (i < n && r.Ok)
            {
                int t = r.U8();
                if (t < 0x80) { int c = t + 1; for (int k = 0; k < c && i < n; k++) b[i++] = 0; }
                else { int c = (t & 0x7f) + 1; for (int k = 0; k < c && i < n; k++) b[i++] = (byte)r.U8(); }
            }
        }
    }

    // ===================== 快照编解码 =====================
    public static class Snapshot
    {
        [Flags] public enum PF : uint { Alive = 1, God = 2, Shield = 4, Bomb = 8, Slam = 16, Latch = 32, Spd = 64, Pow = 128, Rage = 256, Daze = 512, Flash = 1024, Inv = 2048, Clone = 4096, Bump = 8192, Swing = 16384, Stun = 32768, Slow = 65536, Bleed = 131072, OnGround = 262144, Grap = 524288, Ghost = 1 << 20, Giant = 1 << 21, Bubble = 1 << 22, Spring = 1 << 23, Frozen = 1 << 24, IceBomb = 1 << 25, Burn = 1 << 26, Ult = 1 << 27 }

        public static void Encode(ByteWriter w, HostSession hs, int yourSlot, int seq, int fxAck, List<Fx> fxLog, bool terrain)
        {
            var s = hs.Sim; w.Reset();
            bool seats = !hs.Started || seq % 8 == 0; // 座位名单不常变：开局后每 8 包发一次，省出空间给 10 人
            w.U8(Proto.H_SNAP); w.U16(seq & 0xffff); w.U8(yourSlot); w.U8((terrain ? Proto.SNAP_FLAG_TERRAIN : 0) | (seats ? Proto.SNAP_FLAG_SEATS : 0));
            w.U8(hs.Started ? 1 : 0); w.U8((int)s.State); w.U8(s.CurMap); w.U8(s.RoundNo); w.U8(s.WinnerSlot + 1);
            for (int i = 0; i < MAX_P; i++) w.U8(s.Wins[i]);
            w.I16(s.Timer * 100); w.I16(s.LavaY); w.I16((LAVA_AT - s.RoundT) * 10);
            w.U8(s.TimeStopT > 0 && s.TimeStopper != null ? s.TimeStopper.Slot + 1 : 0);
            w.Str(s.Banner); w.U8(hs.MaxPlayers); w.U8((int)hs.Mode); w.U8(hs.MapPick + 1);
            if (seats) for (int i = 0; i < MAX_P; i++) { var st = hs.Seats[i]; w.U8((int)st.Kind); w.U8((int)st.Ch); w.Str(s.Slots[i].Label); }
            // 玩家
            w.U8(s.Players.Count);
            foreach (var p in s.Players)
            {
                PF f = 0;
                if (p.BurnT > 0 || p.Dots > 0) f |= PF.Burn; if (p.Ult) f |= PF.Ult; if (p.Alive) f |= PF.Alive; if (p.God) f |= PF.God; if (p.Shield > 0) f |= PF.Shield; if (p.Bomb) f |= PF.Bomb; if (p.Slam) f |= PF.Slam;
                if (p.Latch != null) f |= PF.Latch; if (p.SpdT > 0) f |= PF.Spd; if (p.PowT > 0) f |= PF.Pow; if (p.Rage) f |= PF.Rage; if (p.Daze > 0) f |= PF.Daze;
                if (p.Flash > 0) f |= PF.Flash; if (p.Inv > 0) f |= PF.Inv; if (p.CloneT > 0) f |= PF.Clone; if (p.Bump > 0) f |= PF.Bump; if (p.Swing > 0) f |= PF.Swing;
                if (p.Stun > 0) f |= PF.Stun; if (p.SlowT > 0) f |= PF.Slow; if (p.BleedT > 0) f |= PF.Bleed; if (p.OnGround) f |= PF.OnGround; if (p.Grap) f |= PF.Grap; if (p.Ghost) f |= PF.Ghost; if (p.GiantT > 0) f |= PF.Giant; if (p.Bubble > 0) f |= PF.Bubble; if (p.SpringT > 0) f |= PF.Spring; if (p.FrozenT > 0) f |= PF.Frozen; if (p.IceBomb) f |= PF.IceBomb;
                w.U8(p.Slot); w.U8((int)p.Ch); w.U8((int)p.Kind); w.I32((int)f);
                w.I16(p.X * 4); w.I16(p.Y * 4); w.I16(p.Vx); w.I16(p.Vy); w.U8((int)p.W); w.U8((int)p.H); w.I8(p.Face);
                w.U16((int)Math.Ceiling(Math.Max(0, p.Hp))); w.U16((int)p.MaxHp);
                w.U8((int)p.Energy); w.U8(p.Stk); w.U8(p.Dots); w.U8((int)(p.Mult * 50)); w.U8((int)(Clamp(1 - p.SkCd / p.C.SkCd, 0, 1) * 255));
                w.U8((int)Math.Min(255, p.BwShield)); w.U8((int)(p.Hue / 360 * 255)); w.U8((int)(Clamp(p.Swing, 0, 1) * 255));
                w.U8(p.Kills); w.U16((int)p.DamageDealt); w.U16((int)(p.Score * 10)); w.U8((int)(Clamp(p.RespawnT, 0, 25) * 10));
                if (p.Latch != null) { w.I16(p.Latch.X); w.I16(p.Latch.Y); w.U8(p.Latch.Owner.Slot); }
                if (p.Grap) { w.I16(p.GrapX); w.I16(p.GrapY); }
                w.U8(Sim.PackX(p)); w.U8(Sim.XValOf(p));
            }
            // 分身（所有人看起来一样，只有主人知道哪个是真身）
            w.U8(s.Clones.Count);
            foreach (var c in s.Clones) { w.U8(c.Owner.Slot); w.I16(c.X * 4); w.I16(c.Y * 4); w.I8(c.Face); w.U8(c.Kind); w.U8((int)c.W); w.U8((int)c.H); }
            int ntc = Math.Min(24, s.Tentacles.Count); w.U8(ntc);
            for (int k = 0; k < ntc; k++) { var tc = s.Tentacles[k]; w.U8(tc.Owner.Slot); w.I16(tc.X); w.I16(tc.Y); w.U8(tc.Lash > 0 ? 1 : 0); w.I16(tc.LashX); w.I16(tc.LashY); w.U8((int)Math.Min(255, tc.T * 10)); }
            w.U8(s.Holes.Count);
            foreach (var h in s.Holes) { w.I16(h.X); w.I16(h.Y); w.I16(h.R); w.U8(h.Active ? 1 : 0); }
            w.U8(s.ItemsList.Count);
            foreach (var it in s.ItemsList) { w.I16(it.X * 4); w.I16(it.Y * 4); w.U8(it.Type); w.U8(it.Landed ? 1 : 0); }
            w.U8((int)s.Mode); w.U8(s.EventId + 1); w.U8((int)(Clamp(s.EventT, 0, 25) * 10)); w.U8(s.WindState); w.I8(s.WindDir);
            w.U8(s.CrownOn ? 1 : 0); w.I16(s.CrownX); w.I16(s.CrownY); w.U8(s.CrownHolder + 1);
            w.U8(s.HazPhase); w.I8(s.HazSide); w.I16(s.HazT * 100);
            // 新角色的场上物件（地雷、锚点、菌毯……）
            int gpos = w.Len; w.U8(0); int gc = 0;
            foreach (var g in s.Gadgets) { if (gc >= 48 || w.Left < 520) break; w.U8((int)g.Kind); w.U8(g.Owner != null ? g.Owner.Slot : 255); w.I16(g.X); w.I16(g.Y); w.U8((int)Math.Min(255, g.R / 2)); w.I8(g.Dir); gc++; }
            w.Buf[gpos] = (byte)gc;
            int nfall = Math.Min(12, s.Falls.Count); w.U8(nfall);
            for (int i = 0; i < nfall; i++) { var fl = s.Falls[i]; w.I16(fl.X); w.I16(fl.Y); w.U8(fl.Warn > 0 ? 1 : 0); w.U8(fl.Meteor ? 1 : 0); }
            if (terrain)
            {
                w.U16(s.TileVersion);
                // 只发“和初始地形不同”的部分（异或后游程压缩），地图再大也不会撑爆数据包
                var bits = new byte[(s.Tiles.Length + 7) / 8];
                for (int i = 0; i < s.Tiles.Length; i++) if ((s.Tiles[i] > 0) != (s.Base[i] > 0)) bits[i >> 3] |= (byte)(1 << (i & 7));
                TerrainRle.Write(w, bits);
            }
            // 子弹：预留特效空间
            int projCountPos = w.Len; w.U8(0); int pc = 0;
            foreach (var b in s.Projs)
            {
                if (w.Left < 260 + 16) break;
                w.U8((int)b.Kind); w.I16(b.X * 4); w.I16(b.Y * 4); w.I16(b.Vx); w.I16(b.Vy); w.U8((int)Math.Min(255, b.R)); w.Rgb(b.Color);
                w.U8(b.Owner != null ? b.Owner.Slot : 255); pc++;
                if (pc >= 255) break;
            }
            w.Buf[projCountPos] = (byte)pc;
            // 特效：从朋友确认的编号之后开始
            int fxCountPos = w.Len; w.U8(0); int fc = 0;
            int start = 0; while (start < fxLog.Count && fxLog[start].Id <= fxAck) start++;
            for (int i = start; i < fxLog.Count && fc < 255; i++)
            {
                var f = fxLog[i]; int need = 1 + 4 + 4 + 12 + 3 + 1 + ByteWriter.StrSize(f.Text);
                if (w.Left < need) break;
                w.U8((int)f.Type); w.I32(f.Id); w.I16(f.X); w.I16(f.Y); w.F32(f.A); w.F32(f.B); w.F32(f.C); w.Rgb(f.Color); w.U8(f.Big ? 1 : 0); w.Str(f.Text);
                fc++;
            }
            w.Buf[fxCountPos] = (byte)fc;
        }
    }

    // ===================== 朋友端 =====================
    /// <summary>朋友端：把快照解码进一个“镜像” Sim（不推进），视图层照常读取它。</summary>
    public sealed class ClientSession
    {
        public readonly Sim Mirror = new Sim(1);
        public readonly INetTransport Net;
        public readonly Seat[] Seats = new Seat[MAX_P];
        public int YourSlot = -1, LastSeq = -1, LastFx, MaxPlayers = MAX_P;
        public bool Started, GotSnap; public string Rejected; public float SinceSnap;
        public GameMode LobbyMode; public int LobbyMap = -1; // 房间里房主选的模式和地图
        public int TimeStopSlot = -1; public float LavaLeft = 99;
        public CharId MyChar; public string MyName;
        public readonly Dictionary<int, (float x, float y, int owner)> Latches = new Dictionary<int, (float, float, int)>();
        public readonly List<int> ProjOwners = new List<int>();
        readonly ByteWriter w = new ByteWriter(256);
        readonly Dictionary<int, Player> bySlot = new Dictionary<int, Player>();
        int lastRound = -1;

        public ClientSession(INetTransport net, CharId ch, string name)
        {
            Net = net; MyChar = ch; MyName = name; Mirror.State = GameState.Menu;
            for (int i = 0; i < MAX_P; i++) Seats[i] = new Seat();
        }

        public void SendInput(byte bits)
        {
            if (!Net.Connected) return;
            w.Reset(); w.U8(Proto.C_INPUT); w.U8(Proto.VERSION); w.U8((int)MyChar); w.U8(bits); w.I32(LastFx); w.Str(MyName);
            Net.Send(0, w.Buf, w.Len);
        }

        public void Pump(float dt)
        {
            SinceSnap += dt;
            Net.Poll(_ => { }, OnData, _ => { });
            // 两次快照之间按速度外推，画面更顺
            foreach (var b in Mirror.Projs) { b.X += b.Vx * dt; b.Y += b.Vy * dt; }
        }

        void OnData(int conn, byte[] data, int len)
        {
            var r = new ByteReader(data, len);
            int type = r.U8();
            if (type == Proto.H_REJECT) { Rejected = r.Str(); return; }
            if (type != Proto.H_SNAP) return;
            int seq = r.U16();
            if (LastSeq >= 0) { int d = (seq - LastSeq) & 0xffff; if (d == 0 || d > 30000) return; } // 过期的包丢掉
            LastSeq = seq; SinceSnap = 0; GotSnap = true;
            Decode(r);
        }

        void Decode(ByteReader r)
        {
            var s = Mirror;
            YourSlot = r.U8(); int flags = r.U8();
            Started = r.U8() == 1; s.State = (GameState)r.U8(); int map = r.U8(); int round = r.U8(); s.WinnerSlot = r.U8() - 1;
            for (int i = 0; i < MAX_P; i++) s.Wins[i] = r.U8();
            s.Timer = r.I16() / 100f; s.LavaY = r.I16(); LavaLeft = r.I16() / 10f; TimeStopSlot = r.U8() - 1;
            s.Banner = r.Str(); MaxPlayers = r.U8(); LobbyMode = (GameMode)r.U8(); LobbyMap = r.U8() - 1; s.MapPick = LobbyMap;
            if ((flags & Proto.SNAP_FLAG_SEATS) != 0) for (int i = 0; i < MAX_P; i++) { Seats[i].Kind = (SeatKind)r.U8(); Seats[i].Ch = (CharId)(r.U8() % Defs.Chars.Length); Seats[i].Label = r.Str(); s.Slots[i].Label = Seats[i].Label; s.Slots[i].Ch = Seats[i].Ch; }
            if (map != s.CurMap || round != lastRound)
            {
                if (map < Maps.All.Length) { s.BuildMap(map); Array.Copy(s.Base, s.Tiles, s.Tiles.Length); s.TileVersion++; }
                lastRound = round; s.RoundNo = round;
            }
            int np = r.U8(); var seen = new HashSet<int>(); s.Players.Clear(); Latches.Clear();
            for (int k = 0; k < np && r.Ok; k++)
            {
                int slot = r.U8(); var ch = (CharId)(r.U8() % Defs.Chars.Length); var kind = (SlotMode)r.U8(); var f = (Snapshot.PF)(uint)r.I32();
                if (!bySlot.TryGetValue(slot, out var p) || p.Ch != ch) { p = new Player { Slot = slot, Ch = ch, C = Char(ch) }; bySlot[slot] = p; p.X = float.NaN; }
                p.Kind = kind; p.Label = s.Slots[slot % MAX_P].Label;
                float nx = r.I16() / 4f, ny = r.I16() / 4f;
                if (float.IsNaN(p.X) || Hypot(nx - p.X, ny - p.Y) > 160) { p.X = nx; p.Y = ny; }
                p.TargetX = nx; p.TargetY = ny;
                p.Vx = r.I16(); p.Vy = r.I16(); p.W = r.U8(); p.H = r.U8(); p.Face = r.I8(); if (p.Face == 0) p.Face = 1;
                p.Hp = r.U16(); p.MaxHp = r.U16(); p.Energy = r.U8(); p.Stk = r.U8(); p.Dots = r.U8(); p.Mult = r.U8() / 50f;
                p.SkPctNet = r.U8() / 255f; p.BwShield = r.U8(); p.Hue = r.U8() / 255f * 360; p.Swing = r.U8() / 255f;
                p.Kills = r.U8(); p.DamageDealt = r.U16(); p.Score = r.U16() / 10f; p.RespawnT = r.U8() / 10f;
                p.BurnT = (f & Snapshot.PF.Burn) != 0 ? 1 : 0; p.Ult = (f & Snapshot.PF.Ult) != 0; p.Alive = (f & Snapshot.PF.Alive) != 0; p.God = (f & Snapshot.PF.God) != 0; p.Shield = (f & Snapshot.PF.Shield) != 0 ? 1 : 0;
                p.Bomb = (f & Snapshot.PF.Bomb) != 0; p.Slam = (f & Snapshot.PF.Slam) != 0; p.SpdT = (f & Snapshot.PF.Spd) != 0 ? 1 : 0; p.PowT = (f & Snapshot.PF.Pow) != 0 ? 1 : 0;
                p.Rage = (f & Snapshot.PF.Rage) != 0; p.Daze = (f & Snapshot.PF.Daze) != 0 ? 1 : 0; p.Flash = (f & Snapshot.PF.Flash) != 0 ? .05f : 0; p.Inv = (f & Snapshot.PF.Inv) != 0 ? 1 : 0;
                p.CloneT = (f & Snapshot.PF.Clone) != 0 ? 1 : 0; p.Bump = (f & Snapshot.PF.Bump) != 0 ? 1 : 0; p.Stun = (f & Snapshot.PF.Stun) != 0 ? 1 : 0; p.SlowT = (f & Snapshot.PF.Slow) != 0 ? 1 : 0;
                p.BleedT = (f & Snapshot.PF.Bleed) != 0 ? 1 : 0; p.OnGround = (f & Snapshot.PF.OnGround) != 0;
                p.Ghost = (f & Snapshot.PF.Ghost) != 0; p.GiantT = (f & Snapshot.PF.Giant) != 0 ? 1 : 0; p.Bubble = (f & Snapshot.PF.Bubble) != 0 ? 1 : 0; p.SpringT = (f & Snapshot.PF.Spring) != 0 ? 1 : 0; p.FrozenT = (f & Snapshot.PF.Frozen) != 0 ? 1 : 0; p.IceBomb = (f & Snapshot.PF.IceBomb) != 0;
                if ((f & Snapshot.PF.Latch) != 0) { float lx = r.I16(), ly = r.I16(); int lo = r.U8(); Latches[slot] = (lx, ly, lo); }
                p.Grap = (f & Snapshot.PF.Grap) != 0; if (p.Grap) { p.GrapX = r.I16(); p.GrapY = r.I16(); }
                Sim.UnpackX(p, r.U8(), r.U8());
                s.Players.Add(p); seen.Add(slot);
            }
            int nc = r.U8(); s.Clones.Clear();
            for (int k = 0; k < nc && r.Ok; k++)
            {
                int o = r.U8(); float x = r.I16() / 4f, y = r.I16() / 4f; int face = r.I8(); int ck = r.U8(), cw = r.U8(), chh = r.U8();
                bySlot.TryGetValue(o, out var owner);
                if (owner != null) s.Clones.Add(new Clone { Kind = (byte)ck, Owner = owner, X = x, Y = y, W = cw > 0 ? cw : owner.W, H = chh > 0 ? chh : owner.H, Face = face == 0 ? 1 : face, T = 1 });
            }
            int ntc = r.U8(); s.Tentacles.Clear();
            for (int k = 0; k < ntc && r.Ok; k++)
            {
                int o = r.U8(); float x = r.I16(), y = r.I16(); bool lash = r.U8() == 1; float lx = r.I16(), ly = r.I16(); float tl = r.U8() / 10f;
                bySlot.TryGetValue(o, out var owner);
                if (owner != null) s.Tentacles.Add(new Tentacle { Owner = owner, X = x, Y = y, Lash = lash ? .2f : 0, LashX = lx, LashY = ly, T = tl });
            }
            int nh = r.U8(); s.Holes.Clear();
            for (int k = 0; k < nh && r.Ok; k++) { float x = r.I16(), y = r.I16(), rr = r.I16(); bool act = r.U8() == 1; s.Holes.Add(new Hole { X = x, Y = y, R = rr, Delay = 1, T = act ? 2 : 0 }); }
            int ni = r.U8(); s.ItemsList.Clear();
            for (int k = 0; k < ni && r.Ok; k++) { float x = r.I16() / 4f, y = r.I16() / 4f; int t = r.U8(); bool landed = r.U8() == 1; s.ItemsList.Add(new Item { X = x, Y = y, Type = t % Items.Names.Length, Landed = landed }); }
            s.Mode = (GameMode)r.U8(); s.EventId = r.U8() - 1; s.EventT = r.U8() / 10f; s.WindState = r.U8(); s.WindDir = r.I8(); if (s.WindDir == 0) s.WindDir = 1;
            s.CrownOn = r.U8() == 1; s.CrownX = r.I16(); s.CrownY = r.I16(); s.CrownHolder = r.U8() - 1;
            s.HazPhase = r.U8(); s.HazSide = r.I8(); s.HazT = r.I16() / 100f;
            int ng = r.U8(); s.Gadgets.Clear();
            for (int k = 0; k < ng && r.Ok; k++)
            {
                var g = new Gadget { Kind = (GKind)r.U8() }; int o = r.U8(); g.X = r.I16(); g.Y = r.I16(); g.R = r.U8() * 2; g.Dir = r.I8(); g.T = 1;
                bySlot.TryGetValue(o, out g.Owner); s.Gadgets.Add(g);
            }
            int nfl = r.U8(); s.Falls.Clear();
            for (int k = 0; k < nfl && r.Ok; k++) { float x = r.I16(), y = r.I16(); bool warn = r.U8() == 1, met = r.U8() == 1; s.Falls.Add(new Falling { X = x, Y = y, Warn = warn ? 1 : 0, Meteor = met }); }
            if ((flags & Proto.SNAP_FLAG_TERRAIN) != 0)
            {
                r.U16(); var bits = new byte[(s.Tiles.Length + 7) / 8]; TerrainRle.Read(r, bits);
                for (int i = 0; i < s.Tiles.Length; i++) if (s.Base[i] > 0) bits[i >> 3] ^= (byte)(1 << (i & 7));
                if (r.Ok)
                {
                    bool changed = false;
                    for (int i = 0; i < s.Tiles.Length; i++)
                    {
                        byte want = (bits[i >> 3] >> (i & 7) & 1) != 0 ? (s.Base[i] == 0 ? (byte)1 : s.Base[i]) : (byte)0;
                        if (s.Tiles[i] != want) { s.Tiles[i] = want; changed = true; }
                    }
                    if (changed) s.TileVersion++;
                }
            }
            int npj = r.U8(); s.Projs.Clear(); ProjOwners.Clear();
            for (int k = 0; k < npj && r.Ok; k++)
            {
                var b = new Proj { Kind = (ProjKind)r.U8(), X = r.I16() / 4f, Y = r.I16() / 4f, Vx = r.I16(), Vy = r.I16(), R = r.U8(), Color = r.Rgb() };
                int o = r.U8(); bySlot.TryGetValue(o, out b.Owner); b.Ang = (float)Math.Atan2(b.Vy, b.Vx);
                s.Projs.Add(b);
            }
            int nf = r.U8();
            for (int k = 0; k < nf && r.Ok; k++)
            {
                var f = new Fx { Type = (FxType)r.U8(), Id = r.I32(), X = r.I16(), Y = r.I16(), A = r.F32(), B = r.F32(), C = r.F32(), Color = r.Rgb(), Big = r.U8() == 1, Text = r.Str() };
                if (!r.Ok) break;
                if (f.Id <= LastFx) continue;
                LastFx = f.Id;
                if (f.Type == FxType.Dig && (int)f.C == s.RoundNo) { if (Sim.DestroyAtRaw(s.Tiles, s.COLS, s.ROWS, f.X, f.Y, f.A) > 0) s.TileVersion++; }
                if (f.Type == FxType.Raise && (int)f.C == s.RoundNo) { if (Sim.RaiseRaw(s.Tiles, s.COLS, s.ROWS, (int)f.X, (int)f.Y, (int)f.A, (int)f.B, (byte)f.Color) > 0) s.TileVersion++; }
                s.OnFx?.Invoke(f);
            }
        }

        /// <summary>每帧把玩家位置平滑地拉向最新快照位置。</summary>
        public void Smooth(float dt)
        {
            float k = Math.Min(1, dt * 18);
            foreach (var p in Mirror.Players)
            {
                float tx = p.TargetX + p.Vx * Math.Min(SinceSnap, .1f), ty = p.TargetY + p.Vy * Math.Min(SinceSnap, .1f) * .5f;
                p.X += (tx - p.X) * k; p.Y += (ty - p.Y) * k;
            }
        }
    }
}
