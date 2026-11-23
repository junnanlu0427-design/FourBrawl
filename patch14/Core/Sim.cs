// 权威模拟：本地对战时直接运行；联机时只在房主机器上运行，朋友端只显示快照。
using System;
using System.Collections.Generic;
using static Brawl.Core.Defs;

namespace Brawl.Core
{
    public sealed class SlotConfig { public SlotMode Mode; public CharId Ch; public string Label = ""; }

    public sealed partial class Sim
    {
        public readonly Random Rng;
        public byte[] Base = new byte[Defs.COLS * Defs.ROWS];
        public byte[] Tiles = new byte[Defs.COLS * Defs.ROWS];
        // 当前地图的实际尺寸（大地图比标准图大）；遮住 Defs 里的同名常量
        public int COLS = Defs.COLS, ROWS = Defs.ROWS, W = Defs.W, H = Defs.H;
        public int TileVersion;
        public int CurMap = -1;

        public GameState State = GameState.Menu;
        public SlotConfig[] Slots = new SlotConfig[MAX_P];
        public List<Player> Players = new List<Player>();
        public List<Proj> Projs = new List<Proj>();
        public List<Clone> Clones = new List<Clone>();
        public List<Tentacle> Tentacles = new List<Tentacle>();
        public List<Hole> Holes = new List<Hole>();
        public List<Item> ItemsList = new List<Item>();
        public int[] Wins = new int[MAX_P];
        public int RoundNo, WinnerSlot = -1;
        public float Timer, RoundT, LavaY = Defs.H + 40, ItemTimer = 6, SlowT, HitStop, TimeStopT;
        public Player TimeStopper;
        public string Banner = "";
        public int MapPick = -1;   // 开局前选的地图；-1 = 每局随机轮换
        public int WinnerTeam = -1; // 团队模式本局获胜的队伍

        // 特效事件
        public readonly List<Fx> FxOut = new List<Fx>();
        public int FxSeq;
        public Action<Fx> OnFx; // 视图层订阅

        // 输入：每个槽位一个
        public readonly PInput[] Inputs = new PInput[MAX_P];

        public Sim(int seed)
        {
            Rng = new Random(seed);
            for (int i = 0; i < MAX_P; i++) Slots[i] = new SlotConfig { Mode = i < 2 ? SlotMode.Human : (i < 4 ? SlotMode.Cpu : SlotMode.Off), Ch = Order[i % Order.Length] };
            BuildMap(0); Array.Copy(Base, Tiles, Tiles.Length);
        }

        public float Rnd(float a, float b) => a + (float)Rng.NextDouble() * (b - a);

        // ================= 地形 =================
        public void BuildMap(int i)
        {
            var m = Maps.All[i]; COLS = m.Cols; ROWS = m.Rows; W = m.W; H = m.H;
            if (Base.Length != COLS * ROWS) { Base = new byte[COLS * ROWS]; Tiles = new byte[COLS * ROWS]; }
            Maps.Build(Base, i); CurMap = i;
        }
        public bool Solid(int c, int r) => c >= 0 && c < COLS && r >= 0 && r < ROWS && Tiles[r * COLS + c] > 0;
        public bool SolidPx(float x, float y) => Solid((int)Math.Floor(x / T), (int)Math.Floor(y / T));
        public bool BoxHit(float x, float y, float w, float h)
        {
            int c0 = (int)Math.Floor(x / T), c1 = (int)Math.Floor((x + w - .01f) / T), r0 = (int)Math.Floor(y / T), r1 = (int)Math.Floor((y + h - .01f) / T);
            for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++) if (Solid(c, r)) return true;
            return false;
        }
        public struct HitInfo { public bool Hx, Hy; }
        public HitInfo MoveBox(ref float x, ref float y, float w, float h, float dx, float dy)
        {
            var hi = new HitInfo();
            int n = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)) / 8));
            float sx = dx / n, sy = dy / n;
            for (int i = 0; i < n; i++)
            {
                if (!hi.Hx) { if (BoxHit(x + sx, y, w, h)) hi.Hx = true; else x += sx; }
                if (!hi.Hy) { if (BoxHit(x, y + sy, w, h)) hi.Hy = true; else y += sy; }
            }
            return hi;
        }
        bool GroundBelow(float x, float y)
        {
            int c = (int)Math.Floor(x / T);
            for (int r = Math.Max(0, (int)Math.Floor(y / T)); r < ROWS; r++) if (Solid(c, r)) return true;
            return false;
        }
        /// <summary>破坏地形。确定性：朋友端收到 Dig 事件后会在本地以同样参数重放。</summary>
        public int DestroyAt(float x, float y, float rad, bool emit = true)
        {
            int n = DestroyAtRaw(Tiles, COLS, ROWS, x, y, rad);
            if (n > 0) { TileVersion++; if (emit) Emit(new Fx { Type = FxType.Dig, X = x, Y = y, A = rad, C = RoundNo }); }
            return n;
        }
        public static int DestroyAtRaw(byte[] tiles, int COLS, int ROWS, float x, float y, float rad)
        {
            int n = 0; int c0 = (int)Math.Floor((x - rad) / T), c1 = (int)Math.Floor((x + rad) / T), r0 = (int)Math.Floor((y - rad) / T), r1 = (int)Math.Floor((y + rad) / T);
            for (int r = r0; r <= r1; r++) for (int c = c0; c <= c1; c++)
                {
                    if (c < 0 || c >= COLS || r < 0 || r >= ROWS) continue; int i = r * COLS + c;
                    if (tiles[i] != 1 && tiles[i] != 3) continue;
                    float dx = c * T + T / 2f - x, dy = r * T + T / 2f - y;
                    if (dx * dx + dy * dy <= rad * rad + T * T / 4f) { tiles[i] = 0; n++; }
                }
            return n;
        }

        /// <summary>造地（ccld 创世）：在空地上填一块土。确定性，朋友端收到 Raise 事件后重放。</summary>
        public bool RaiseAt(int c0, int r0, int w, int h, byte v)
        {
            if (c0 < 1 || r0 < 3 || c0 + w >= COLS - 1 || r0 + h >= ROWS - 2) return false;
            float x = c0 * T, y = r0 * T, pw = w * T, ph = h * T;
            foreach (var p in Players) if (p.Alive && Overlap(x - 10, y - 30, pw + 20, ph + 40, p.X, p.Y, p.W, p.H)) return false;
            foreach (var it in ItemsList) if (Overlap(x, y, pw, ph, it.X, it.Y, it.W, it.H)) return false;
            if (RaiseRaw(Tiles, COLS, ROWS, c0, r0, w, h, v) == 0) return false;
            TileVersion++; Emit(new Fx { Type = FxType.Raise, X = c0, Y = r0, A = w, B = h, C = RoundNo, Color = v }); return true;
        }
        public static int RaiseRaw(byte[] tiles, int COLS, int ROWS, int c0, int r0, int w, int h, byte v)
        {
            int n = 0;
            for (int r = r0; r < r0 + h; r++) for (int c = c0; c < c0 + w; c++)
                { if (c < 0 || c >= COLS || r < 0 || r >= ROWS) continue; int i = r * COLS + c; if (tiles[i] == 0) { tiles[i] = v; n++; } }
            return n;
        }

        // ================= 特效 =================
        void Emit(Fx f) { f.Id = ++FxSeq; FxOut.Add(f); OnFx?.Invoke(f); }
        void Burst(float x, float y, uint col, int n, float spd, float size) => Emit(new Fx { Type = FxType.Burst, X = x, Y = y, Color = col, A = n, B = spd, C = size });
        void Text(float x, float y, string t, uint col, bool big) => Emit(new Fx { Type = FxType.Text, X = x, Y = y, Text = t, Color = col, Big = big });
        void Ring(float x, float y, float r0, float r1, uint col, float life = .35f, float w = 4) => Emit(new Fx { Type = FxType.Ring, X = x, Y = y, A = r0, B = r1, C = life, Color = col, Text = null, Big = w > 6 });
        void Shake(float a) => Emit(new Fx { Type = FxType.Shake, A = a });
        void Flash(float a) => Emit(new Fx { Type = FxType.Flash, A = a });
        void Sound(Sfx s, float p = 0) => Emit(new Fx { Type = FxType.Sound, A = (float)s, B = p });
        void Zap(float x1, float y1, float x2, float y2, uint col, float width = 1) => Emit(new Fx { Type = FxType.Zap, X = x1, Y = y1, A = x2, B = y2, C = width, Color = col });
        void HitSpark(float x, float y, float dir, float dmg) => Emit(new Fx { Type = FxType.Hit, X = x, Y = y, A = dir, B = dmg });

        // ================= 对局流程 =================
        public void StartMatch() { Array.Clear(Wins, 0, Wins.Length); RoundNo = 0; ShuffleMaps(); StartRound(); }
        public void StartRound()
        {
            RoundNo++; BuildMap(NextMap()); Array.Copy(Base, Tiles, Tiles.Length); TileVersion++;
            Players.Clear();
            PrepareBossSeat();
            for (int i = 0; i < MAX_P; i++) if (Slots[i].Mode != SlotMode.Off) { var pl = MakePlayer(i); if (IsBoss(pl)) MakeBoss(pl); Players.Add(pl); }
            Projs.Clear(); Clones.Clear(); Tentacles.Clear(); Holes.Clear(); ItemsList.Clear(); Gadgets.Clear(); contactT.Clear();
            TimeStopT = 0; TimeStopper = null; ItemTimer = 6; RoundT = 0; LavaY = H + 40; SlowT = 0; HitStop = 0;
            State = GameState.Countdown; Timer = 2.2f; Banner = ""; ResetExtras();
        }
        Player MakePlayer(int i)
        {
            var s = Slots[i]; var c = Char(s.Ch); var sps = Maps.All[CurMap].Spawn; var sp = sps[i % sps.Length];
            if (i >= sps.Length) sp = new[] { sp[0] + (i % 2 == 0 ? -40 : 40), sp[1] };
            var p = new Player { Slot = i, Ch = s.Ch, C = c, Kind = s.Mode, Label = s.Label, X = sp[0] - c.W / 2f, Y = sp[1] - c.H, W = c.W, H = c.H, Hp = c.Hp * HP_MULT, MaxHp = c.Hp * HP_MULT, Face = sp[0] < W / 2 ? 1 : -1, Hue = Rnd(0, 360) };
            Unstick(p); return p;
        }

        IEnumerable<Player> Enemies(Player p) { foreach (var e in Players) if (e != p && e.Alive && !Ally(p, e)) yield return e; }
        public int TeamOf(Player p) => Mode == GameMode.Boss ? (p.Slot == BOSS_SLOT ? 1 : 0) : Mode == GameMode.Team ? p.Slot % 2 : p.Slot;
        public int TeamOfSlot(int slot) => Mode == GameMode.Boss ? (slot == BOSS_SLOT ? 1 : 0) : Mode == GameMode.Team ? slot % 2 : slot;
        /// <summary>团队模式下同队（不能互相伤害）</summary>
        public bool Ally(Player a, Player b) => a != null && b != null && a != b && (Mode == GameMode.Team ? a.Slot % 2 == b.Slot % 2 : Mode == GameMode.Boss && a.Slot != BOSS_SLOT && b.Slot != BOSS_SLOT);
        Player Nearest(Player p, float fx, float fy)
        {
            Player b = null; float bd = 1e9f;
            foreach (var e in Players) { if (e == p || !e.Alive || Ally(p, e)) continue; float d = Hypot(e.Cx - fx, e.Cy - fy); if (d < bd) { bd = d; b = e; } }
            return b;
        }
        Player Nearest(Player p) => Nearest(p, p.Cx, p.Cy);
        static bool Overlap(float ax, float ay, float aw, float ah, float bx, float by, float bw, float bh) => ax < bx + bw && ax + aw > bx && ay < by + bh && ay + ah > by;
        static bool Overlap(Player a, Player b) => Overlap(a.X, a.Y, a.W, a.H, b.X, b.Y, b.W, b.H);
        bool Frozen(Player p) => TimeStopT > 0 && p != TimeStopper;
        void Unstick(Player p) { for (int k = 0; k < 16 && BoxHit(p.X, p.Y, p.W, p.H); k++) p.Y -= T / 2f; }

        void SizeTo(Player p, float s)
        {
            if (IsBoss(p)) s *= BOSS_SCALE; // Boss 一直保持巨人体型
            int nw = (int)Math.Round(p.C.W * s), nh = (int)Math.Round(p.C.H * s);
            if (nw == (int)p.W && nh == (int)p.H) return;
            float ox = p.X - (nw - p.W) / 2, oy = p.Y - (nh - p.H);
            if (!BoxHit(ox, oy, nw, nh)) { p.X = ox; p.Y = oy; p.W = nw; p.H = nh; }
        }

        // ================= 伤害 =================
        void Credit(Player t, Player src) { if (src != null && src != t) t.Hitters[src.Slot] = RoundT; }
        void Hurt(Player t, float amt, Player src)
        {
            if (!t.Alive || t.Inv > 0 || Ally(t, src) || t.HeldBy != null || t.SubT > 0) return;
            amt *= DMG_MULT * TakenMulNew(t); if (t.Ch == CharId.XP) amt *= .8f; if (t.God) amt *= .8f; if (Eroded(t)) amt *= 1.15f;
            if (t.BwShield > 0) { float a = Math.Min(t.BwShield, amt); t.BwShield -= a; amt -= a; }
            t.Hp -= amt; Credit(t, src); t.DmgAcc += amt; if (src != null) src.DamageDealt += amt; TrRecord(t, src, amt);
            if (t.DmgAcc >= 5) { Text(t.Cx + Rnd(-12, 12), t.Y - 14, "-" + Math.Round(t.DmgAcc), 0xd68cff, false); t.DmgAcc = 0; }
            if (t.Hp <= 0) Ko(t, "K.O.", src);
        }
        void Damage(Player t, float amt, Player src, float kx, float ky)
        {
            if (!t.Alive || t.Inv > 0 || Ally(t, src)) return;
            if (DamageBlockNew(t, src)) return;
            if (t.Bubble > 0 && amt > 0) { t.Bubble = 0; Ring(t.Cx, t.Cy, t.H, t.H + 30, 0xa8fff0, .3f); Burst(t.Cx, t.Cy, 0xa8fff0, 16, 260, 4); Text(t.Cx, t.Y - 30, "泡泡挡下!", 0xa8fff0, false); Sound(Sfx.Absorb); return; }
            amt *= DMG_MULT * TakenMulNew(t); kx *= KB_MULT; ky *= KB_MULT;
            if (t.Ch == CharId.CCLD2 && !t.Divine) t.Energy = Math.Min(100, t.Energy + 3);
            if (src != null && src.PowT > 0) amt *= 1.5f;
            if (src != null && src == TimeStopper && TimeStopT > 0) amt *= .55f; // 超频期间伤害降低
            if (src != null && src.GiantT > 0) amt *= 1.3f;
            if (src != null && src.Ch == CharId.DZY && src.Stk > 0) amt *= 1 + src.Stk * .07f; // 触手怪进化加伤
            if (Eroded(t)) amt *= 1.15f; // bw 侵蚀：身上有坍缩层/燃烧时受到的伤害 +15%
            if (t.GiantT > 0) { kx *= .6f; ky *= .6f; }
            if (t.Ch == CharId.XP)
            {
                amt *= .8f; kx = 0; ky = 0; t.HitsTaken++;
                if (t.HitsTaken >= 3 && !t.Rage) { t.HitsTaken = 0; t.Rage = true; Text(t.Cx, t.Y - 34, "越挨揍越疼!", 0x4cff8a, false); }
            }
            if (t.God) { amt *= .8f; kx *= .5f; ky *= .5f; }
            else if (t.Ch == CharId.CCLD && LoneK(t) >= 3) { amt *= LONE_DR; kx *= .8f; ky *= .8f; } // 孤高剑心：单挑时受伤减少
            if (t.Ch == CharId.DZY && NearOwnTentacle(t)) { amt *= .75f; kx *= .7f; ky *= .7f; } // 触手护体
            if (t.Ch == CharId.CCLD && !t.God) t.Energy = Math.Min(100, t.Energy + amt * .35f);
            if (src != null && src.Ch == CharId.CCLD && !src.God) src.Energy = Math.Min(100, src.Energy + amt * .6f);
            BossTaken(t, src, ref amt, ref kx, ref ky); float rawAmt = amt;
            if (t.Shield > 0) { amt *= .3f; kx *= .3f; ky *= .3f; Ring(t.Cx, t.Cy, t.H, t.H + 20, 0xc77dff, .2f); }
            if (t.BwShield > 0) { float a = Math.Min(t.BwShield, amt); t.BwShield -= a; amt -= a; Ring(t.Cx, t.Cy, t.H / 2, t.H / 2 + 14, 0xc77dff, .15f); }
            float mult = 1 + (1 - t.Hp / t.MaxHp) * 1.1f;
            amt = (float)Math.Round(amt); t.Hp -= amt; t.Vx += kx * mult; t.Vy += ky * mult;
            if (kx != 0 || ky != 0) { t.OnGround = false; if (!(t.Armor > 0)) t.Stun = .25f; }
            t.Flash = .12f; Credit(t, src); if (src != null) src.DamageDealt += amt; TrRecord(t, src, amt);
            Burst(t.Cx, t.Cy, t.C.Color, 8, 220, 4);
            if (amt > 0)
            {
                Text(t.Cx + Rnd(-10, 10), t.Y - 14, "-" + amt, src != null ? src.C.Color : 0xffffff, false);
                HitSpark(t.Cx, t.Cy, kx != 0 ? Sign(kx) : (src != null ? Sign(t.Cx - src.Cx) : 1), amt);
                Sound(amt >= 14 ? Sfx.Heavy : Sfx.Hit, amt);
            }
            if (Mode == GameMode.Crown && CrownHolder == t.Slot) { t.CrownDmg += Math.Max(amt, (float)Math.Round(rawAmt * .7f)); if (t.CrownDmg >= 25 && t.Hp > 0) DropCrown(t); }
            Shake(Math.Min(14, amt * .35f));
            if (amt >= 14) HitStop = Math.Max(HitStop, Math.Min(.11f, amt / 300));
            if (t.Hp <= 0) Ko(t, "K.O.", src);
        }
        void Ko(Player t, string txt, Player killer = null)
        {
            if (!t.Alive) return; if (TrSaveFromKo(t)) return; t.Hp = 0; t.Alive = false;
            float x = Clamp(t.Cx, 20, W - 20), y = Clamp(t.Cy, 100, H - 20);
            Burst(x, y, t.C.Color, 40, 420, 7); Burst(x, y, 0xffffff, 16, 300, 4); Shake(16); Sound(Sfx.KO);
            Ring(x, y, 10, 120, t.C.Color, .5f, 4); Text(x, y - 30, txt, 0xff5464, true);
            Emit(new Fx { Type = FxType.Zoom, X = x, Y = y });
            Player best = killer; float bt = -1;
            foreach (var kv in t.Hitters) if (RoundT - kv.Value < 6 && kv.Value > bt) { bt = kv.Value; foreach (var q in Players) if (q.Slot == kv.Key && best == null) best = q; }
            if (best != null && best != t)
            {
                best.Kills++;
                if (Leader() == t.Slot && best.Alive) { best.Hp = Math.Min(best.MaxHp, best.Hp + 30); best.PowT = Math.Max(best.PowT, 5); Text(best.Cx, best.Y - 46, "赏金! 击倒领先者", 0xffd84a, true); Ring(best.Cx, best.Cy, 10, 90, 0xffd84a, .4f); }
            }
            OnKoNew(t, best != t ? best : null);
            if (Mode == GameMode.Crown) { DropCrown(t); t.RespawnT = 3; } else if (!BossHeroDown(t)) MakeGhost(t);
            if (t.DotMark != null && t.DotMark != t) { var dm = t.DotMark; t.DotMark = null; t.Dots = 0; t.BurnT = 0; CollapseBoom(x, y, t, dm, true); }
            foreach (var p in Players)
                if (p.Alive && p.Ch == CharId.BW && t.Hitters.TryGetValue(p.Slot, out float ht) && RoundT - ht < 6) { p.Mass += 5; Text(p.Cx, p.Y - 36, "吞噬! 变大", 0xc77dff, true); }
            if (t.Ch == CharId.BW && t.Y < H && Mode != GameMode.Crown) Holes.Add(new Hole { X = Clamp(t.Cx, 40, W - 40), Y = Clamp(t.Cy, 120, H - 40), Delay = 1.2f, Dur = Mode == GameMode.Crown ? 3.5f : 7, Rmax = Mode == GameMode.Crown ? 220 : 430, Owner = t, R = 24 });
            foreach (var c in Clones) if (c.Owner == t) { c.T = -1; Burst(c.Cx, c.Cy, 0x9097c2, 14, 200, 6); }
            foreach (var tc in Tentacles) if (tc.Owner == t) { tc.T = -1; Burst(tc.X, tc.Y - 20, 0xe0457b, 10, 200, 5); }
            foreach (var p in Players) if (p.Latch != null && p.Latch.Owner == t) p.Latch = null;
            if (TimeStopper == t) { TimeStopT = 0; TimeStopper = null; }
            if (Mode != GameMode.Crown && State == GameState.Play && TeamsAlive() <= 1 && !(BossMode && HeroesLeft() && !IsBoss(t))) { SlowT = 1.3f; Flash(.35f); Text(W / 2, 250, "终 结 !", 0xffffff, true); }
        }
        int AliveCount() { int n = 0; foreach (var p in Players) if (p.Alive) n++; return n; }
        /// <summary>还有人活着的队伍数（非团队模式下每人一队）</summary>
        int TeamsAlive() { int mask = 0, n = 0; foreach (var p in Players) if (p.Alive) { int b = 1 << TeamOf(p); if ((mask & b) == 0) { mask |= b; n++; } } return n; }

        // ================= 攻击 / 技能 =================
        void AimVec(Player p, PInput inp, out float ax, out float ay)
        {
            float bx = p.Face, by = 0;
            if (inp.U && !inp.D) { bx = p.Face * .8f; by = -.6f; } else if (inp.D) { bx = p.Face * .8f; by = .6f; }
            float th = p.Kind == SlotMode.Cpu ? .5f : .9f; float bd = 1e9f; bool found = false; ax = 0; ay = 0;
            float bl = Hypot(bx, by);
            foreach (var e in Enemies(p))
            {
                float dx = e.Cx - p.Cx, dy = e.Cy - p.Cy, d = Hypot(dx, dy); if (d < 1) d = 1; if (d > 900) continue;
                if ((dx * bx + dy * by) / d / bl > th && d < bd) { bd = d; ax = dx / d; ay = dy / d; found = true; }
            }
            if (!found) { ax = bx / bl; ay = by / bl; }
        }
        Proj Shoot(Player p, float vx, float vy, float speed, float r, float dmg, ProjKind kind = ProjKind.Bolt)
        {
            var b = new Proj { X = p.Cx + vx * (p.W / 2 + 6), Y = p.Cy + vy * (p.H / 2 + 2), Vx = vx * speed, Vy = vy * speed, R = r, Dmg = dmg, Owner = p, Color = p.C.Color, Kind = kind };
            Projs.Add(b); return b;
        }
        void MeleeBox(Player p, float w, float h, float dmg, float kb, float up, float dig)
        {
            float bx = p.Face > 0 ? p.X + p.W - 4 : p.X - w + 4, by = p.Cy - h / 2;
            foreach (var e in new List<Player>(Enemies(p))) if (Overlap(bx, by, w, h, e.X, e.Y, e.W, e.H)) Damage(e, dmg, p, p.Face * kb, -up);
            HitClonesBox(p, bx, by, w, h, dmg);
            if (dig > 0) DestroyAt(bx + w / 2, by + h / 2, dig);
        }
        bool Eroded(Player t) => t.Dots > 0 || t.BurnT > 0;
        public static float BwScale(Player p) => 1 + Math.Min(p.Mass, 25) * .03f; // 体型越大，弹幕和范围越大
        void AddDot(Player t, Player src, int n)
        {
            if (!t.Alive || t == src || Ally(t, src)) return; t.Dots += n; t.DotT = 3; t.DotOwner = src; t.BurnT = 3; t.BurnSrc = src;
            if (t.Dots >= 6) { t.Dots = 0; CollapseBoom(t.Cx, t.Cy, t, src, false); }
        }
        /// <summary>坍缩爆炸：满 6 层或者中过 DOT 的人死掉时触发。炸周围的人，并向场上所有其他人射出高速黑洞弹。</summary>
        void CollapseBoom(float x, float y, Player t, Player src, bool death)
        {
            float k = src != null ? BwScale(src) : 1, R = 170 * k;
            Ring(x, y, 10, R, 0xc77dff, .45f, 10); Ring(x, y, 10, R * .6f, 0x2a0a40, .35f, 8); Burst(x, y, 0x7b2cff, 40, 420, 7); Burst(x, y, 0x1a0628, 24, 320, 7); Shake(12); Sound(Sfx.Boom, 12);
            Text(x, y - 40, death ? "死亡坍缩!" : "坍缩爆炸!", 0xc77dff, true);
            if (!death) { t.Latch = null; t.LatchCd = 1.2f; Damage(t, 9, src, 0, -260); } // 满层后解除吸附减速
            foreach (var e in new List<Player>(Players)) if (e.Alive && e != src && e != t && !Ally(e, src) && Hypot(e.Cx - x, e.Cy - y) < R) { Damage(e, 6, src, Sign(e.Cx - x == 0 ? 1 : e.Cx - x) * 300, -200); AddDot(e, src, 1); }
            if (src == null) return;
            foreach (var e in Players)
            {
                if (!e.Alive || e == src || e == t || Ally(e, src)) continue;
                float dx = e.Cx - x, dy = e.Cy - y, d = Hypot(dx, dy); if (d < 1) d = 1;
                Projs.Add(new Proj { X = x + dx / d * 26, Y = y + dy / d * 26, Vx = dx / d * 680, Vy = dy / d * 680, R = 7 * k, Dmg = 3, Owner = src, Color = 0x9b4dff, Kind = ProjKind.Hole, Life = 1.8f, Dig = 6, Home = 1.4f, HitSet = new HashSet<Player> { t } });
            }
        }
        void Explode(float x, float y, Player owner)
        {
            DestroyAt(x, y, 64); Shake(18); Flash(.25f); Sound(Sfx.Boom, 18);
            Burst(x, y, 0xff9a3c, 50, 520, 8); Burst(x, y, 0xffe27a, 30, 380, 6); Ring(x, y, 10, 130, 0xff5464, .4f, 10);
            foreach (var p in new List<Player>(Players))
            {
                if (!p.Alive) continue; float dx = p.Cx - x, dy = p.Cy - y, d = Hypot(dx, dy); if (d < 1) d = 1;
                if (d < 100) { float k = 1 - d / 130; Damage(p, 26 * k + 6, p == owner ? null : owner, dx / d * 650 * k + Sign(dx == 0 ? 1 : dx) * 100, -420 * k - 100); }
            }
        }

        void Attack(Player p, PInput inp)
        {
            p.AtkCd = p.C.AtkCd;
            float ax, ay;
            if (p.IceBomb)
            {
                p.IceBomb = false; AimVec(p, inp, out ax, out ay);
                var ib = Shoot(p, ax, Math.Min(ay, 0) - .55f, 520, 9, 0, ProjKind.IceBomb); ib.Life = 1.8f; ib.Color = 0x7fd6ff;
                Text(p.Cx, p.Y - 20, "冰雷!", 0x7fd6ff, false); Sound(Sfx.Swing); return;
            }
            if (p.Bomb)
            {
                p.Bomb = false; AimVec(p, inp, out ax, out ay);
                var b = Shoot(p, ax, Math.Min(ay, 0) - .55f, 520, 9, 0, ProjKind.Bomb); b.Life = 1.8f; b.Color = 0x222222;
                Text(p.Cx, p.Y - 20, "扔!", 0xff5464, false); Sound(Sfx.Swing); return;
            }
            if (AttackV9(p, inp)) return;
            if (AttackNew(p, inp)) return;
            switch (p.Ch)
            {
                case CharId.DVD:
                    if (p.CloneT > 0) { p.AtkCd = .1f; return; }
                    AimVec(p, inp, out ax, out ay);
                    if (p.Stk >= 6)
                    {
                        p.AtkCd = .11f; var b = Shoot(p, ax + Rnd(-.06f, .06f), ay + Rnd(-.06f, .06f), 920, 5, 3); b.Life = .9f; b.Fast = true; b.Color = 0xff9be8; Sound(Sfx.Shoot);
                    }
                    else { p.AtkCd = .7f; var b = Shoot(p, ax, ay, 340, 13, 16, ProjKind.Disc); b.Life = 5f; b.Slow = true; b.Bounces = 4; Sound(Sfx.Disc); } // 光盘会像屏保一样到处反弹
                    break;
                case CharId.XP:
                    p.AtkCd = .6f; p.Bump = .22f; p.Armor = .32f; p.BumpHit.Clear(); p.RageNow = p.Rage; p.Rage = false; p.Vx = p.Face * 720; p.Swing = .22f;
                    Burst(p.Cx, p.Y + p.H, 0x9097c2, 8, 160, 4); Sound(Sfx.Swing); break;
                case CharId.BW:
                    {
                        if (p.Shield > 0) { p.AtkCd = .05f; return; } // 吞噬盾期间不能攻击
                        float s = BwScale(p);
                        if (inp.D)
                        {
                            // 近战吞噬：大范围，把范围内的人猛地吸过来一次，伤害低但叠层
                            p.AtkCd = .7f; p.Swing = .2f; float R = 230 * s; Ring(p.Cx, p.Cy, R, 10, 0x7b2cff, .35f, 10); Ring(p.Cx, p.Cy, R * .7f, 10, 0x2a0a40, .3f, 6); Sound(Sfx.Hole);
                            foreach (var e in new List<Player>(Enemies(p)))
                            {
                                float dx = p.Cx - e.Cx, dy = p.Cy - e.Cy, d = Hypot(dx, dy); if (d < 1) d = 1;
                                if (d < R) { e.Vx = dx / d * Math.Min(950, d * 5); e.Vy = dy / d * Math.Min(700, d * 4) - 140; e.OnGround = false; Damage(e, 3, p, 0, 0); AddDot(e, p, 1); Burst(e.Cx, e.Cy, 0x7b2cff, 6, 160, 4); }
                            }
                        }
                        else { p.AtkCd = .36f; AimVec(p, inp, out ax, out ay); var b = Shoot(p, ax, ay, 300, (float)Math.Round(7 * s), 2, ProjKind.Hole); b.Life = 2.4f; b.Color = 0x7b2cff; b.Dig = 8; b.Home = 1.1f; Sound(Sfx.Hole); }
                        break;
                    }
                case CharId.CCLD:
                    if (p.God)
                    {
                        p.AtkCd = .3f; AimVec(p, inp, out ax, out ay);
                        var b = Shoot(p, ax, ay, 1000, 16, (float)Math.Round(6 * p.Mult), ProjKind.Wave); b.Life = 1.6f; b.Pierce = true; b.HitSet = new HashSet<Player>(); b.Hh = 46; b.Color = 0xfff2a8; b.Ang = (float)Math.Atan2(ay, ax);
                        int before = (int)Math.Floor(p.Mult); p.Mult = Math.Min(3, p.Mult + .06f);
                        if ((int)Math.Floor(p.Mult) > before) Text(p.Cx, p.Y - 50, "剑意 ×" + (int)Math.Floor(p.Mult), 0xfff2a8, true);
                        Sound(Sfx.Wave);
                    }
                    else
                    {   // 剑客三连：9 / 9 / 15，第 3 段上挑
                        if (p.ComboT <= 0) p.ComboN = 0; p.ComboN++; p.ComboT = .6f; int st = (p.ComboN - 1) % 3 + 1;
                        p.Swing = .16f; p.AtkCd = .4f; Sound(Sfx.Swing);
                        if (st < 3) MeleeBox(p, 58, 52, 10, 420, 260, 10);
                        else { MeleeBox(p, 64, 62, 16, 300, 520, 10); Ring(p.Cx + p.Face * 40, p.Cy, 10, 50, 0xfff2a8, .25f, 5); }
                    }
                    break;
                case CharId.KAI:
                    {
                        // 掌心雷：瞬发短程闪电（约 220 像素），打中第一个敌人；连续第 3 下变成连锁闪电
                        p.AtkCd = .26f; p.Swing = .12f;
                        if (p.ComboT <= 0) p.ComboN = 0; p.ComboN++; p.ComboT = .9f;
                        bool chain = p.ComboN % 3 == 0;
                        AimVec(p, inp, out ax, out ay);
                        float sx = p.Cx + ax * (p.W / 2), sy = p.Cy + ay * (p.H / 2), ex = sx, ey = sy; Player first = null;
                        for (int k = 1; k <= 22 && first == null; k++)
                        {
                            float qx = sx + ax * k * 10, qy = sy + ay * k * 10;
                            if (SolidPx(qx, qy)) { DestroyAt(qx, qy, chain ? 12 : 6); break; }
                            ex = qx; ey = qy;
                            foreach (var e in Enemies(p)) if (qx > e.X - 8 && qx < e.X + e.W + 8 && qy > e.Y - 8 && qy < e.Y + e.H + 8) { first = e; break; }
                        }
                        if (first != null) { ex = first.Cx; ey = first.Cy; }
                        Zap(sx, sy, ex, ey, chain ? 0xffffffu : 0x3cf0ffu, chain ? 2 : 1); Sound(chain ? Sfx.Wave : Sfx.Shoot, 4);
                        if (first != null)
                        {
                            float km = 1 + p.Stk * .12f; // 电荷越多伤害越高（最多 +60%）
                            Damage(first, (float)Math.Round((chain ? 11 : 8) * km), p, ax * (chain ? 360 : 160), chain ? -220 : -100);
                            if (p.Stk < 5) { p.Stk++; if (p.Stk == 5) Text(p.Cx, p.Y - 36, "电荷满! 雷遁带天雷", 0x3cf0ff, true); }
                            p.StkT = 6;
                            if (chain)
                            {
                                Text(first.Cx, first.Y - 40, "连锁闪电!", 0x3cf0ff, false); first.Stun = Math.Max(first.Stun, .2f);
                                var hit = new HashSet<Player> { first }; var from = first;
                                for (int k = 0; k < 2; k++)
                                {
                                    Player nb = null; float nd = 280;
                                    foreach (var e in Enemies(p)) if (!hit.Contains(e)) { float d = Hypot(e.Cx - from.Cx, e.Cy - from.Cy); if (d < nd) { nd = d; nb = e; } }
                                    if (nb == null) break;
                                    Zap(from.Cx, from.Cy, nb.Cx, nb.Cy, 0x9ff8ff); hit.Add(nb); Damage(nb, (float)Math.Round(8 * km), p, 0, -120); nb.Stun = Math.Max(nb.Stun, .25f); from = nb;
                                }
                                Burst(first.Cx, first.Cy, 0x3cf0ff, 14, 320, 4);
                            }
                        }
                        break;
                    }
                case CharId.DZY:
                    if (p.Stk > 0)
                    {
                        // 触手鞭：向前甩出 3 条触手，越进化越长
                        p.AtkCd = .64f; p.Swing = .22f; AimVec(p, inp, out ax, out ay); float len = 120 + p.Stk * 8; Sound(Sfx.Swing);
                        var hitW = new HashSet<Player>();
                        for (int k = -1; k <= 1; k++)
                        {
                            float a0 = (float)Math.Atan2(ay, ax) + k * .32f, ux = (float)Math.Cos(a0), uy = (float)Math.Sin(a0);
                            float ex = p.Cx + ux * len, ey = p.Cy + uy * len;
                            Emit(new Fx { Type = FxType.Zap, X = p.Cx, Y = p.Cy, A = ex, B = ey, C = 3, Color = 0xe0457b, Big = true });
                            for (int q = 2; q <= 10; q++)
                            {
                                float qx = p.Cx + ux * len * q / 10, qy = p.Cy + uy * len * q / 10;
                                foreach (var e in Enemies(p)) if (!hitW.Contains(e) && qx > e.X - 10 && qx < e.X + e.W + 10 && qy > e.Y - 10 && qy < e.Y + e.H + 10) hitW.Add(e);
                            }
                        }
                        foreach (var e in hitW) Damage(e, 9, p, Sign(e.Cx - p.Cx == 0 ? p.Face : e.Cx - p.Cx) * 300, -200);
                        DestroyAt(p.Cx + ax * len * .8f, p.Cy + ay * len * .8f, 10);
                        p.WhipN++; if (p.WhipN % 4 == 0) PlantTentacle(p);
                    }
                    else { p.AtkCd = 3.85f; PlantTentacle(p); }
                    break;
                case CharId.GARY:
                    {
                        p.AtkCd = .28f; AimVec(p, inp, out ax, out ay);
                        var b = Shoot(p, ax, ay, 1050, 8, 14, ProjKind.Kunai); b.Life = .8f; b.Color = 0xff8a3c; b.Dig = 6; b.Ang = (float)Math.Atan2(ay, ax); Sound(Sfx.Shoot);
                        break;
                    }
            }
        }
        void BecomeGod(Player p)
        {
            p.God = true; p.Energy = 0; p.Hp = Math.Min(p.MaxHp, p.Hp + 40); p.SkCd = .6f; p.Vy = -300; p.Mult = 1;
            TimeStopT = 5; TimeStopper = p; Sound(Sfx.God); Sound(Sfx.TimeStop);
            Flash(.9f); Shake(22); Ring(p.Cx, p.Cy, 10, 700, 0xffc531, .9f, 16); Ring(p.Cx, p.Cy, 10, 420, 0xffffff, .6f, 10); Ring(p.Cx, p.Cy, 10, 240, 0xff9a3c, .5f, 8);
            Burst(p.Cx, p.Cy, 0xffe27a, 90, 700, 9); Burst(p.Cx, p.Cy, 0xffffff, 40, 500, 6); Text(p.Cx, p.Y - 50, "创 世 神 降 临 !", 0xffc531, true); Text(W / 2, 200, "超 频 !", 0x7ff3ff, true);
            Genesis(p, 2);
        }
        void Skill(Player p, PInput inp)
        {
            p.SkCd = p.C.SkCd;
            if (p.Ult) { UltBlast(p); return; }
            if (SkillV9(p, inp)) return;
            if (SkillNew(p, inp)) return;
            switch (p.Ch)
            {
                case CharId.DVD:
                    {
                        if (p.Stk < DVD_ULT) { p.SkCd = 0; return; }
                        // 十重分身：真身 + 9 个分身散开，分身分头追不同的敌人，碰到就小爆炸
                        p.Stk = 0; p.CloneT = 5; var offs = new List<float>(); for (int i = 0; i < 10; i++) offs.Add((i - 4.5f) * 40);
                        for (int i = offs.Count - 1; i > 0; i--) { int j = Rng.Next(i + 1); var t = offs[i]; offs[i] = offs[j]; offs[j] = t; }
                        float bx = p.X;
                        Func<float, float> tryX = x => { x = Clamp(x, 0, W - p.W); return BoxHit(x, p.Y, p.W, p.H) ? bx : x; };
                        p.X = tryX(bx + offs[0]);
                        var foes = new List<Player>(Enemies(p));
                        for (int i = 1; i < offs.Count; i++) Clones.Add(new Clone { Owner = p, X = tryX(bx + offs[i]), Y = p.Y, W = p.W, H = p.H, Face = p.Face, T = 3.5f + (float)Rng.NextDouble() * 1.5f, Tgt = foes.Count > 0 ? foes[i % foes.Count].Slot : -1, Vy = -300 - (float)Rng.NextDouble() * 300 });
                        foreach (var o in offs) Burst(bx + p.W / 2 + o, p.Cy, 0xc7cbee, 8, 200, 7);
                        Text(bx + p.W / 2, p.Y - 40, "十重分身!", 0xff4fd8, true); Sound(Sfx.Clone);
                        break;
                    }
                case CharId.XP:
                    p.Slam = true; p.Hung = false; p.Hang = 0; p.Vy = -840; p.Vx = p.Face * 260; p.OnGround = false; p.Inv = 9;
                    // 起跳时把周围的人吸过来，方便落地砸中
                    Ring(p.Cx, p.Cy, 240, 20, 0x4cff8a, .35f, 8);
                    foreach (var e in new List<Player>(Enemies(p)))
                    {
                        float dx = p.Cx - e.Cx, dy = p.Cy - e.Cy, d = Hypot(dx, dy); if (d < 1) d = 1;
                        if (d < 240 && !(e.Ch == CharId.XP)) { e.Vx += dx / d * 620; e.Vy += Math.Min(0, dy / d * 300) - 220; e.OnGround = false; Burst(e.Cx, e.Cy, 0x4cff8a, 6, 160, 3); }
                    }
                    Text(p.Cx, p.Y - 30, "泰山压顶!", 0x4cff8a, true); Sound(Sfx.Jump, 1); break;
                case CharId.BW:
                    p.Shield = Mode == GameMode.Crown && CrownHolder == p.Slot ? .8f : 1.8f; p.Absorbs.Clear(); Ring(p.Cx, p.Cy, p.H / 2, p.H / 2 + 44, 0xc77dff, .3f, 6); Sound(Sfx.Absorb); break;
                case CharId.CCLD:
                    {
                        if (!p.God) { if (p.Energy >= 100) BecomeGod(p); else FlashStep(p); return; }
                        // 创世：星落（满天剑雨，吃倍率）+ 在空中造出新的浮岛
                        p.SkCd = 7; Text(p.Cx, p.Y - 46, "创 世 · 星 落 !", 0xe0f4ff, true); Shake(8); Sound(Sfx.Wave, 1);
                        Ring(p.Cx, p.Cy, 10, 360, 0xfff2a8, .6f, 12); Burst(p.Cx, p.Cy, 0xfff2a8, 40, 520, 6);
                        var xs = new List<float>();
                        foreach (var e in Enemies(p)) foreach (var o in new float[] { -34, 0, 34 }) xs.Add(e.Cx + o);
                        for (int i = 0; i < 6; i++) xs.Add(Rnd(60, W - 60));
                        for (int i = 0; i < xs.Count; i++)
                            Projs.Add(new Proj { X = Clamp(xs[i], 10, W - 10), Y = -40 - i * 45, Vx = 0, Vy = 1100, R = 8, Dmg = (float)Math.Round(6 * p.Mult), Owner = p, Kind = ProjKind.Sword, Pierce = true, HitSet = new HashSet<Player>(), Life = 2.5f, Color = 0xe0f4ff, Dig = 16 });
                        Genesis(p, 3);
                        p.Hp = Math.Min(p.MaxHp, p.Hp + 5);
                        break;
                    }
                case CharId.KAI:
                    {
                        float ax, ay; AimVec(p, inp, out ax, out ay);
                        float x0 = p.X, y0 = p.Y, sx0 = p.Cx, sy0 = p.Cy; float nx = x0, ny = y0;
                        for (int k = 1; k <= 28; k++)
                        {
                            float tx = Clamp(x0 + ax * k * 10, 0, W - p.W), ty = Clamp(y0 + ay * k * 10, 0, H - p.H);
                            if (BoxHit(tx, ty, p.W, p.H)) break; nx = tx; ny = ty;
                        }
                        p.X = nx; p.Y = ny; p.Vx = ax * 260; p.Vy = Math.Min(p.Vy, 0) * .3f - 120; p.OnGround = false; p.Inv = Math.Max(p.Inv, .25f); p.Jumps = Math.Max(p.Jumps, 1);
                        float ex = p.Cx, ey = p.Cy; Zap(sx0, sy0, ex, ey, 0x3cf0ff, 2); Burst(sx0, sy0, 0x3cf0ff, 12, 260, 4); Burst(ex, ey, 0xe6ffff, 16, 320, 4); Sound(Sfx.Wave, 8);
                        float lx = ex - sx0, ly = ey - sy0, ll = lx * lx + ly * ly; float skm = 1 + p.Stk * .12f;
                        foreach (var e in new List<Player>(Enemies(p)))
                        {
                            float tt = ll < 1 ? 0 : Clamp(((e.Cx - sx0) * lx + (e.Cy - sy0) * ly) / ll, 0, 1);
                            float qx = sx0 + lx * tt, qy = sy0 + ly * tt;
                            if (Hypot(e.Cx - qx, e.Cy - qy) < 26 + e.W / 2) { Damage(e, (float)Math.Round(9 * skm), p, Sign(lx == 0 ? p.Face : lx) * 120, -200); e.Stun = Math.Max(e.Stun, .45f); Burst(e.Cx, e.Cy, 0x9ff8ff, 10, 260, 3); }
                        }
                        if (p.Stk >= 5)
                        {
                            p.Stk = 0; Zap(ex + Rnd(-30, 30), -20, ex, ey, 0xffffff, 4); Zap(ex + Rnd(-60, 60), -20, ex, ey, 0x3cf0ff, 3);
                            Flash(.3f); Shake(16); Sound(Sfx.Boom, 14); Ring(ex, ey, 10, 150, 0x3cf0ff, .4f, 10); Burst(ex, ey, 0x9ff8ff, 40, 480, 6); Text(ex, ey - 50, "天 雷 !", 0x3cf0ff, true);
                            DestroyAt(ex, ey + 10, 40);
                            foreach (var e in new List<Player>(Enemies(p)))
                            {
                                float dx = e.Cx - ex, dy = e.Cy - ey, d = Hypot(dx, dy); if (d < 1) d = 1;
                                if (d < 150) { Damage(e, 28, p, dx / d * 520, -360); e.Stun = Math.Max(e.Stun, .5f); }
                            }
                        }
                        break;
                    }
                case CharId.DZY:
                    {
                        int n = 0; foreach (var tc in Tentacles) if (tc.Owner == p && tc.T > 0) n++;
                        if (n == 0) { p.SkCd = 0; return; }
                        foreach (var tc in Tentacles)
                        {
                            if (tc.Owner != p || tc.T <= 0) continue; tc.T = -1; float x = tc.X, y = tc.Y - 26;
                            Ring(x, y, 8, 90, 0xe0457b, .35f, 8); Burst(x, y, 0xe0457b, 18, 320, 6); Burst(x, y, 0x5a0a28, 10, 220, 5);
                            Emit(new Fx { Type = FxType.Zap, X = x, Y = y, A = p.Cx, B = p.Cy, C = 2, Color = 0xff7aa0 });
                            foreach (var e in new List<Player>(Enemies(p))) if (Hypot(e.Cx - x, e.Cy - y) < 100 + e.W / 2) Damage(e, 11, p, Sign(e.Cx - x == 0 ? 1 : e.Cx - x) * 280, -240);
                        }
                        bool first = p.Stk == 0; int add = Math.Min(n, 12 - p.Stk); p.Stk += add;
                        p.MaxHp += 10 * add; p.Hp = Math.Min(p.MaxHp, p.Hp + 15 * add + 10);
                        Shake(10 + n * 2); Sound(Sfx.God); Ring(p.Cx, p.Cy, 10, 160 + n * 20, 0xe0457b, .5f, 12); Burst(p.Cx, p.Cy, 0xe0457b, 40, 480, 7);
                        Text(p.Cx, p.Y - 50, first ? "进 化 · 触 手 怪 !" : "献祭 ×" + n + " → " + p.Stk + " 级", 0xff7aa0, true);
                        break;
                    }
                case CharId.GARY:
                    {
                        float ax, ay; AimVec(p, inp, out ax, out ay);
                        var b = Shoot(p, ax, ay, 1400, 6, 0, ProjKind.Hook); b.Life = .42f; b.Color = 0xffd27a; b.Dig = 0; b.Ang = (float)Math.Atan2(ay, ax); Sound(Sfx.Swing);
                        break;
                    }
            }
        }
        /// <summary>创世：在空旷处造出几块会发光的浮岛（普通土，能被打碎）。</summary>
        void Genesis(Player p, int count)
        {
            int made = 0;
            for (int k = 0; k < 60 && made < count; k++)
            {
                int w = 4 + Rng.Next(4), c = 3 + Rng.Next(COLS - w - 6), r = 8 + Rng.Next(ROWS - 18);
                bool clear = true;
                for (int rr = r - 3; rr <= r + 3 && clear; rr++) for (int cc = c - 1; cc <= c + w; cc++) if (Solid(cc, rr)) { clear = false; break; }
                if (!clear || !RaiseAt(c, r, w, 1, 1)) continue;
                made++; float x = (c + w / 2f) * T, y = r * T + T / 2f;
                Ring(x, y, 10, 90, 0xfff2a8, .5f, 8); Burst(x, y, 0xfff2a8, 18, 260, 5); Burst(x, y, 0x9fe7ff, 10, 200, 4);
                Zap(p.Cx, p.Cy, x, y, 0xfff2a8, 1);
            }
            if (made > 0) Text(W / 2, 240, "开天辟地 · 新的大陆!", 0xfff2a8, true);
        }
        bool NearOwnTentacle(Player p) { foreach (var tc in Tentacles) if (tc.Owner == p && tc.T > 0 && Hypot(tc.X - p.Cx, tc.Y - p.Cy) < 220) return true; return false; }
        void PlantTentacle(Player p)
        {
            // 在面前找地面种触手；找不到就种在脚下
            float x = Clamp(p.Cx + p.Face * 34, 10, W - 10), y = -1;
            for (int r = (int)Math.Floor((p.Y) / T); r < ROWS && r < (p.Y + p.H) / T + 10; r++) if (r >= 0 && Solid((int)Math.Floor(x / T), r)) { y = r * T; break; }
            if (y < 0) { x = p.Cx; y = p.Y + p.H; }
            int mine = 0; Tentacle oldest = null;
            foreach (var tc in Tentacles) if (tc.Owner == p && tc.T > 0) { mine++; if (oldest == null || tc.T < oldest.T) oldest = tc; }
            if (mine >= 6 && oldest != null) oldest.T = -1;
            Tentacles.Add(new Tentacle { Owner = p, X = x, Y = y, T = 16, Cd = .3f });
            foreach (var e in new List<Player>(Enemies(p))) if (Math.Abs(e.Cx - x) < 60 && Math.Abs(e.Y + e.H - y) < 60) Damage(e, 8, p, Sign(e.Cx - x == 0 ? 1 : e.Cx - x) * 200, -420); // 破土而出
            Burst(x, y, 0xe0457b, 14, 240, 5); Burst(x, y, 0x5a0a28, 8, 160, 4); Ring(x, y - 10, 6, 50, 0xe0457b, .3f); Sound(Sfx.Hole); Text(x, y - 60, "触手!", 0xff7aa0, false);
        }
        void UpdateTentacles(float dt)
        {
            foreach (var tc in Tentacles)
            {
                if (tc.T <= 0) continue; tc.T -= dt; tc.Cd -= dt; tc.Lash -= dt;
                var o = tc.Owner; if (o == null || !o.Alive) { tc.T = -1; continue; }
                if (!SolidPx(tc.X, tc.Y + 4)) { tc.Y += 600 * dt; if (tc.Y > H + 40) { tc.T = -1; continue; } } // 地面被打穿就往下掉
                if (tc.Cd > 0) continue;
                Player tg = null; float bd = 215;
                foreach (var e in Enemies(o)) { float d = Hypot(e.Cx - tc.X, e.Cy - (tc.Y - 36)); if (d < bd) { bd = d; tg = e; } }
                if (tg == null) continue;
                tc.Cd = .7f; tc.Lash = .25f; tc.LashX = tg.Cx; tc.LashY = tg.Cy;
                Emit(new Fx { Type = FxType.Zap, X = tc.X, Y = tc.Y - 40, A = tg.Cx, B = tg.Cy, C = 2, Color = 0xe0457b, Big = true });
                Damage(tg, 11, o, Sign(tg.Cx - tc.X == 0 ? 1 : tg.Cx - tc.X) * 220, -160);
            }
            Tentacles.RemoveAll(tc => tc.T <= 0);
        }
        void XpLand(Player p)
        {
            p.Slam = false; p.Inv = 0; float x = p.Cx, y = p.Y + p.H;
            DestroyAt(x, y + 12, 62); Shake(14); Sound(Sfx.Slam); Burst(x, y, 0x4cff8a, 30, 380, 6); Burst(x, y, 0x8f7cff, 20, 300, 6); Ring(x, y, 20, 160, 0x4cff8a, .35f, 10);
            foreach (var e in new List<Player>(Enemies(p)))
            {
                float dx = e.Cx - x, dy = e.Cy - y, d = Hypot(dx, dy); if (d < 1) d = 1;
                if (d < 140 + e.W / 2)
                {
                    if (e.Shield > 0 || e.BwShield > 0) { e.Shield = 0; e.BwShield = 0; Text(e.Cx, e.Y - 40, "破盾!", 0x4cff8a, true); }
                    Damage(e, 22, p, Sign(dx == 0 ? 1 : dx) * 200, 260); if (!(e.Armor > 0)) e.Daze = Math.Max(e.Daze, .5f); e.SlowT = 2;
                }
            }
        }
        void CloneBoom(Clone c)
        {
            float x = c.Cx, y = c.Cy; var o = c.Owner; c.T = -1;
            DestroyAt(x, y, 32); Shake(9); Sound(Sfx.Boom, 9); Burst(x, y, 0xff4fd8, 24, 420, 7); Burst(x, y, 0xffffff, 10, 300, 4); Ring(x, y, 10, 110, 0xff4fd8, .35f, 8);
            foreach (var e in new List<Player>(Enemies(o)))
            {
                float dx = e.Cx - x, dy = e.Cy - y, d = Hypot(dx, dy); if (d < 1) d = 1;
                if (d < 100 + e.W / 2) Damage(e, (float)Math.Round(12 * (1 - d / 170) + 4), o, dx / d * 360, -260);
            }
        }
    }
}
