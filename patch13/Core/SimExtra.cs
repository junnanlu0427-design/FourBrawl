// 整体机制（Unity 版自己的设计）：
//  · 两种模式：经典淘汰 / 王冠争夺
//  · 第 25 秒随机事件：低重力、流星雨、道具雨、地震
//  · 三张带机关的新地图：冰川裂谷（滑冰 + 冰锥）、风暴之巅（横风）、传送门工厂（传送门）
//  · 被淘汰的人变成幽灵，可以“吹气”推活人，不会干等
//  · 悬赏：领先者（赢局最多的人）被击杀时，击杀者回血 + 短暂狂暴
//  · 新道具：巨人、冰雷、泡泡、弹簧、骰子
using System;
using System.Collections.Generic;
using static Brawl.Core.Defs;

namespace Brawl.Core
{
    public sealed partial class Sim
    {
        public GameMode Mode = GameMode.Classic;
        public const float CROWN_GOAL = 20, EVENT_AT = 25, EVENT_LEN = 10, CROWN_LAVA_AT = 60;
        public readonly List<Falling> Falls = new List<Falling>();
        public float WindT, WindOn; public int WindDir = 1, WindState; // WindState：0 无风 1 正在刮 2 预警
        float icicleT, meteorT, quakeT;
        public int EventId = -1; public float EventT;
        public float GravK = 1;
        public static readonly string[] EventNames = { "低重力", "流星雨", "道具雨", "地震" };
        public static readonly uint[] EventColors = { 0xbfe9ff, 0xff9a3c, 0x5dff8f, 0xd8a060 };
        public float CrownX, CrownY, CrownVy, CrownVx, CrownLockT; public int CrownHolder = -1, CrownLockSlot = -1; public bool CrownOn;
        int[] mapOrder; float crownIdle;

        public MapDef Map => Maps.All[Math.Max(0, CurMap)];
        public int Leader()
        {
            int best = -1, bw = 0; bool tie = false;
            for (int i = 0; i < MAX_P; i++) { if (Wins[i] > bw) { bw = Wins[i]; best = i; tie = false; } else if (Wins[i] == bw && bw > 0) tie = true; }
            return tie ? -1 : best;
        }

        void ShuffleMaps()
        {
            // 人少（≤3）只抽标准地图；人多时大地图也进轮换；团队专用图只在团队模式出现
            int np = 0; for (int i = 0; i < MAX_P; i++) if (Slots[i].Mode != SlotMode.Off) np++;
            var pool = new List<int>();
            for (int i = 0; i < Maps.All.Length; i++)
            {
                var m = Maps.All[i];
                if (m.Big && np <= 3) continue;
                if (m.TeamOnly && Mode != GameMode.Team) continue;
                pool.Add(i);
                if (m.Big && np >= 7) { pool.Add(i); pool.Add(i); } // 7 人以上：大地图权重 ×3
            }
            mapOrder = pool.ToArray();
            for (int i = mapOrder.Length - 1; i > 0; i--) { int j = Rng.Next(i + 1); int t = mapOrder[i]; mapOrder[i] = mapOrder[j]; mapOrder[j] = t; }
        }
        int NextMap() { if (MapPick >= 0 && MapPick < Maps.All.Length) return MapPick; if (mapOrder == null) ShuffleMaps(); return mapOrder[(RoundNo - 1) % mapOrder.Length]; }

        void ResetExtras()
        {
            Falls.Clear(); WindT = 8; WindOn = 0; WindDir = 1; WindState = 0; icicleT = 3; meteorT = 0; quakeT = 0;
            EventId = -1; EventT = 0; GravK = 1; ResetMapHazards();
            CrownOn = Mode == GameMode.Crown; CrownHolder = -1; crownIdle = 0; ResetCrownPos(); CrownVx = 0; CrownLockT = 0; CrownLockSlot = -1;
        }

        int TileAt(float x, float y)
        {
            int c = (int)Math.Floor(x / T), r = (int)Math.Floor(y / T);
            return c >= 0 && c < COLS && r >= 0 && r < ROWS ? Tiles[r * COLS + c] : 0;
        }
        bool OnIce(Player p) => p.OnGround && (TileAt(p.X + 2, p.Y + p.H + 2) == 3 || TileAt(p.X + p.W - 2, p.Y + p.H + 2) == 3);

        // ================= 每帧额外逻辑 =================
        void UpdateExtras(float dt)
        {
            if (State != GameState.Play) return;
            // ---- 随机事件 ----
            if (EventId < 0 && RoundT >= EVENT_AT && RoundT - dt < EVENT_AT)
            {
                EventId = Rng.Next(EventNames.Length); EventT = EVENT_LEN;
                Text(W / 2, 170, "事件：" + EventNames[EventId] + "!", EventColors[EventId], true); Shake(6); Sound(Sfx.BeepGo);
                if (EventId == 0) GravK = .45f;
                if (EventId == 2) for (int i = 0; i < 5; i++) SpawnItem();
            }
            if (EventId >= 0 && EventT > 0)
            {
                EventT -= dt;
                if (EventId == 1) { meteorT -= dt; if (meteorT <= 0) { meteorT = .5f; Falls.Add(new Falling { X = Rnd(80, W - 80), Y = -20, Warn = .7f, Meteor = true }); } }
                if (EventId == 3)
                {
                    quakeT -= dt; if (quakeT <= 0)
                    {
                        quakeT = .35f; Shake(5); float x = Rnd(40, W - 40);
                        for (int r = 0; r < ROWS; r++) if (Solid((int)(x / T), r)) { DestroyAt(x, r * T + 6, 22); Burst(x, r * T, Map.Dirt, 10, 200, 5); break; }
                    }
                }
                if (EventT <= 0) { if (EventId == 0) GravK = 1; Text(W / 2, 170, EventNames[EventId] + " 结束", 0x9097c2, false); }
            }
            // ---- 地图机关 ----
            var m = Map;
            if (m.Hazard == MapHazard.Ice)
            {
                icicleT -= dt; if (icicleT <= 0) { icicleT = Rnd(2.2f, 3.6f); Falls.Add(new Falling { X = Rnd(80, W - 80), Y = 30, Warn = .8f }); }
            }
            else if (m.Hazard == MapHazard.Wind)
            {
                WindT -= dt;
                if (WindOn > 0)
                {
                    WindOn -= dt;
                    foreach (var p in Players)
                    {
                        if (!p.Alive || (p.Ch == CharId.XP && (p.Armor > 0 || p.Slam))) continue;
                        int c0 = (int)Math.Floor(p.Cx / T), r = (int)Math.Floor(p.Cy / T); bool cover = false;
                        for (int k = 1; k <= 6; k++) if (Solid(c0 - WindDir * k, r)) { cover = true; break; }
                        if (!cover) { p.Vx += WindDir * (p.God ? 500 : 1000) * dt * (p.GiantT > 0 ? .5f : 1); if (!p.OnGround) p.Vx += WindDir * 500 * dt; }
                    }
                    foreach (var b in Projs) if (b.Kind != ProjKind.Sword && b.Kind != ProjKind.Wave) b.Vx += WindDir * 500 * dt;
                    if (WindOn <= 0) { WindT = 12; WindDir *= -1; }
                }
                else if (WindT <= 0) { WindOn = 3; Text(W / 2, 180, WindDir > 0 ? "狂风 →" : "← 狂风", 0xbfe9ff, true); Shake(6); Sound(Sfx.Swing); }
                else if (WindT < 2 && WindT + dt >= 2) Text(W / 2, 180, "起风了…", 0x9097c2, false);
                WindState = WindOn > 0 ? 1 : WindT < 2 ? 2 : 0;
            }
            if (m.Portals != null)
            {
                foreach (var p in Players) if (p.Alive) { if (p.TpCd > 0) p.TpCd -= dt; else if (Teleport(p.Cx, p.Cy, out float qx, out float qy)) { p.X = qx - p.W / 2; p.Y = qy - p.H / 2; p.TpCd = .5f; } }
                foreach (var b in Projs) { if (b.TpCd > 0) b.TpCd -= dt; else if (Teleport(b.X, b.Y, out float qx, out float qy)) { b.X = qx; b.Y = qy; b.TpCd = .5f; } }
                foreach (var it in ItemsList) { if (it.TpCd > 0) it.TpCd -= dt; else if (Teleport(it.X + it.W / 2, it.Y + it.H / 2, out float qx, out float qy)) { it.X = qx - it.W / 2; it.Y = qy - it.H / 2; it.TpCd = .5f; } }
            }
            UpdateMapHazards(dt);
            UpdateFalls(dt);
            UpdateGhosts(dt);
            if (Mode == GameMode.Crown) UpdateCrown(dt);
        }

        bool Teleport(float x, float y, out float qx, out float qy)
        {
            foreach (var pt in Map.Portals)
            {
                if (Math.Abs(x - pt[0]) < 22 && Math.Abs(y - pt[1]) < 34 && pt[3] < LavaY - 40) { qx = pt[2]; qy = pt[3]; Ring(qx, qy, 8, 50, 0xffffff, .25f); Sound(Sfx.Clone); return true; }
                if (Math.Abs(x - pt[2]) < 22 && Math.Abs(y - pt[3]) < 34 && pt[1] < LavaY - 40) { qx = pt[0]; qy = pt[1]; Ring(qx, qy, 8, 50, 0xffffff, .25f); Sound(Sfx.Clone); return true; }
            }
            qx = qy = 0; return false;
        }

        void UpdateFalls(float dt)
        {
            foreach (var s in Falls)
            {
                if (s.Warn > 0) { s.Warn -= dt; continue; }
                s.Vy += (s.Meteor ? 2200 : 1600) * dt; s.Y += s.Vy * dt;
                foreach (var p in new List<Player>(Players))
                    if (p.Alive && !s.Dead && p.Inv <= 0 && Math.Abs(p.Cx - s.X) < p.W / 2 + 8 && Math.Abs(p.Cy - s.Y) < p.H / 2 + 14)
                    { Damage(p, s.Meteor ? 14 : 18, null, Sign(p.Cx - s.X == 0 ? 1 : p.Cx - s.X) * 200, -380); s.Dead = true; }
                if (!s.Dead && (SolidPx(s.X, s.Y + 14) || s.Y > H))
                {
                    s.Dead = true;
                    if (s.Meteor) { DestroyAt(s.X, s.Y + 14, 40); Burst(s.X, s.Y, 0xff9a3c, 26, 360, 6); Shake(6); Sound(Sfx.Boom, 8); }
                    else { DestroyAt(s.X, s.Y + 14, 26); Burst(s.X, s.Y, 0xc9f1ff, 18, 260, 5); Sound(Sfx.Hit, 6); }
                }
            }
            Falls.RemoveAll(s => s.Dead);
        }

        // ================= 幽灵 =================
        void MakeGhost(Player t)
        {
            if (Mode != GameMode.Classic) return;
            t.Ghost = true; t.GhostCd = 1.5f; t.Vx = t.Vy = 0;
            t.X = Clamp(t.X, 20, W - 20 - t.W); t.Y = Clamp(t.Y, 80, H - 80);
        }
        void UpdateGhosts(float dt)
        {
            foreach (var g in Players)
            {
                if (g.Alive || !g.Ghost) continue;
                g.GhostCd -= dt; g.Hue = (g.Hue + dt * 90) % 360;
                PInput inp = g.Kind == SlotMode.Cpu ? GhostThink(g) : Inputs[g.Slot];
                float ix = (inp.R ? 1 : 0) - (inp.L ? 1 : 0), iy = (inp.D ? 1 : 0) - (inp.U ? 1 : 0);
                if (ix != 0) g.Face = (int)ix;
                g.Vx += (ix * 300 - g.Vx) * Math.Min(1, 6 * dt); g.Vy += (iy * 300 - g.Vy) * Math.Min(1, 6 * dt);
                g.X = Clamp(g.X + g.Vx * dt, 0, W - g.W); g.Y = Clamp(g.Y + g.Vy * dt, 40, H - g.H - 10);
                if (inp.A && g.GhostCd <= 0 && TimeStopT <= 0)
                {
                    g.GhostCd = 2.5f; Ring(g.Cx, g.Cy, 10, 110, 0xdfe4ff, .3f); Sound(Sfx.Swing);
                    foreach (var p in Players)
                    {
                        if (!p.Alive) continue; float dx = p.Cx - g.Cx, dy = p.Cy - g.Cy, d = Hypot(dx, dy); if (d < 1) d = 1;
                        if (d < 110 && !(p.Ch == CharId.XP)) { p.Vx += dx / d * 420; p.Vy += dy / d * 200 - 120; p.OnGround = false; }
                    }
                }
            }
        }
        PInput GhostThink(Player g)
        {
            int lead = Leader(); Player t = null;
            foreach (var p in Players) if (p.Alive && p.Slot == lead) t = p;
            if (t == null) t = Nearest(g, g.Cx, g.Cy);
            if (t == null) return default;
            float dx = t.Cx - g.Cx, dy = t.Cy - g.Cy; float d = Hypot(dx, dy);
            return new PInput { L = dx < -30, R = dx > 30, U = dy < -30, D = dy > 30, A = d < 90 && Rng.NextDouble() < .05 };
        }

        // ================= 王冠争夺 =================
        void UpdateCrown(float dt)
        {
            if (CrownLockT > 0) CrownLockT -= dt;
            Player holder = null; foreach (var p in Players) if (p.Slot == CrownHolder) holder = p;
            if (holder != null && !holder.Alive) { DropCrown(holder); holder = null; }
            if (holder != null)
            {
                CrownX = holder.Cx; CrownY = holder.Y - 18; holder.Score += dt;
                if ((int)holder.Score != (int)(holder.Score - dt) && (int)holder.Score % 5 == 0) Text(holder.Cx, holder.Y - 50, (int)holder.Score + " 分", 0xffd84a, false);
            }
            else
            {
                crownIdle += dt;
                if (BoxHit(CrownX - 12, CrownY - 9, 24, 18)) { for (int k = 0; k < 12 && BoxHit(CrownX - 12, CrownY - 9, 24, 18); k++) CrownY -= T; if (BoxHit(CrownX - 12, CrownY - 9, 24, 18)) ResetCrownPos(); }
                if (crownIdle > 12) { crownIdle = 0; ResetCrownPos(); Ring(CrownX, CrownY, 10, 60, 0xffd84a, .4f); }
                CrownVy += GRAV * GravK * .6f * dt; CrownVx *= (float)Math.Pow(.4, dt);
                float x = CrownX - 12, y = CrownY - 9;
                var h = MoveBox(ref x, ref y, 24, 18, CrownVx * dt, CrownVy * dt); if (h.Hy) { CrownVy = 0; CrownVx *= .5f; }
                if (h.Hx) CrownVx = -CrownVx * .5f;
                CrownX = x + 12; CrownY = y + 9;
                if (CrownY > Math.Min(H + 20, LavaY + 10) || CrownX < -40 || CrownX > W + 40) { ResetCrownPos(); Text(W / 2, 140, "王冠重新出现", 0xffd84a, false); }
                foreach (var p in Players)
                    if (p.Alive && !(CrownLockT > 0 && p.Slot == CrownLockSlot) && Overlap(p.X, p.Y, p.W, p.H, CrownX - 12, CrownY - 9, 24, 18))
                    { CrownHolder = p.Slot; p.CrownDmg = 0; crownIdle = 0; Text(p.Cx, p.Y - 40, "戴上王冠!", 0xffd84a, true); Sound(Sfx.Pickup); Ring(p.Cx, p.Cy, 10, 80, 0xffd84a, .4f); break; }
            }
        }
        void ResetCrownPos()
        {
            // 放在一个出生点正上方（出生点一定是空地；有天花板的地图也不会卡住）
            var sps = Maps.All[Math.Max(0, CurMap)].Spawn; var sp = sps[Rng.Next(sps.Length)];
            CrownX = sp[0]; CrownY = sp[1] - 60; CrownVy = 0; CrownVx = 0;
            if (BoxHit(CrownX - 12, CrownY - 9, 24, 18)) CrownY = sp[1] - 20;
        }
        void DropCrown(Player p)
        {
            if (CrownHolder != p.Slot) return;
            CrownHolder = -1; CrownX = Clamp(p.Cx, 30, W - 30); CrownY = Math.Min(p.Y - 10, H - 60); CrownVy = -520; CrownVx = Rnd(-260, 260);
            CrownLockT = .8f; CrownLockSlot = p.Slot; Text(p.Cx, p.Y - 40, "王冠掉了!", 0xffd84a, true); Sound(Sfx.Pickup, 1);
        }
        void Respawn(Player p)
        {
            var sp = Maps.All[CurMap].Spawn[Rng.Next(Maps.All[CurMap].Spawn.Length)];
            p.X = sp[0] - p.W / 2; p.Y = sp[1] - p.H - 40; p.Vx = p.Vy = 0; p.Alive = true; p.Hp = p.MaxHp; p.Inv = 1.5f; Unstick(p);
            p.Latch = null; p.Grap = false; p.Dots = 0; p.BurnT = 0; p.DotMark = null; p.BleedT = 0; p.FrozenT = 0; p.Slam = false; p.Daze = 0; p.Stun = 0; p.CrownDmg = 0; p.Bump = 0;
            Ring(p.Cx, p.Cy, 10, 90, p.C.Color, .4f); Burst(p.Cx, p.Cy, p.C.Color, 20, 300, 5);
        }
        void UpdateRespawns(float dt)
        {
            if (Mode != GameMode.Crown || State != GameState.Play) return;
            foreach (var p in Players) if (!p.Alive && p.RespawnT > 0) { p.RespawnT -= dt; if (p.RespawnT <= 0) Respawn(p); }
        }

        // ================= 道具：新增效果 =================
        void SpawnItem()
        {
            int t; do { t = Rng.Next(Items.Count); } while (t == Items.Dice && Rng.NextDouble() > .35);
            ItemsList.Add(new Item { X = Rnd(W * .14f, W * .86f), Y = -30, Type = t });
        }
        void IceBurst(float x, float y, Player o)
        {
            Burst(x, y, 0xc9f1ff, 40, 420, 6); Ring(x, y, 10, 130, 0x7fd6ff, .4f, 8); Sound(Sfx.Absorb, 1);
            foreach (var p in Players)
                if (p.Alive && p != o && !Ally(p, o) && Hypot(p.Cx - x, p.Cy - y) < 130 && !(p.Ch == CharId.XP && p.Slam) && !p.God)
                { p.FrozenT = 1.5f; Credit(p, o); Text(p.Cx, p.Y - 30, "冻住!", 0x7fd6ff, false); }
        }
        void DiceSwap()
        {
            var al = new List<Player>(); foreach (var q in Players) if (q.Alive) al.Add(q);
            var pos = new List<float[]>(); foreach (var q in al) pos.Add(new[] { q.Cx, q.Y + q.H });
            for (int i = pos.Count - 1; i > 0; i--) { int j = Rng.Next(i + 1); var tmp = pos[i]; pos[i] = pos[j]; pos[j] = tmp; }
            for (int i = 0; i < al.Count; i++) { var q = al[i]; q.X = pos[i][0] - q.W / 2; q.Y = pos[i][1] - q.H; q.Vx = q.Vy = 0; Ring(q.Cx, q.Cy, 6, 60, 0xff6bd6, .35f); }
            Text(W / 2, 200, "乾坤大挪移!", 0xff6bd6, true); Flash(.2f); Sound(Sfx.Clone);
        }
    
        /// <summary>掉出地图：不直接死，扣最大血量的 22%（至少 20 点），从天上掉回来，落地前无敌。血不够扣才死。</summary>
        void FellOff(Player p)
        {
            float pen = Training ? 0 : Math.Max(14, p.MaxHp * .15f) * FallPenaltyMulNew(p); // 第四版：掉下去扣得更少
            if (p.Hp - pen <= 0 || State != GameState.Play) { Ko(p, "坠落!"); return; }
            Player last = null; float lt = -99; foreach (var kv in p.Hitters) if (kv.Value > lt && RoundT - kv.Value < 6) { lt = kv.Value; foreach (var q in Players) if (q.Slot == kv.Key) last = q; }
            p.Hp -= pen; if (last != null) last.DamageDealt += pen;
            if (Mode == GameMode.Crown && CrownHolder == p.Slot) DropCrown(p);
            float bx = W / 2f;
            for (int k = 0; k < 30; k++)
            {
                float x = Rnd(120, W - 120); int c = (int)Math.Floor(x / T); int top = -1;
                for (int r = 0; r < ROWS; r++) if (Solid(c, r)) { top = r; break; }
                if (top > 2 && top * T < LavaY - 40) { bx = x; break; }
            }
            p.X = Clamp(bx - p.W / 2, 0, W - p.W); p.Y = -p.H - 20; p.Vx = 0; p.Vy = 200; p.OnGround = false; p.Jumps = 1;
            p.Grap = false; p.Latch = null; p.Stun = 0; p.Daze = 0; p.Slam = false; p.Bump = 0; p.Inv = Math.Max(p.Inv, 1.6f);
            Text(p.Cx, 60, "-" + Math.Round(pen) + " 从天而降!", 0xff9a3c, true); Sound(Sfx.Heavy, 10); Ring(p.Cx, 30, 6, 60, p.C.Color, .4f);
        }
}
}
