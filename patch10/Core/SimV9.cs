// 第九版：给各角色加「下+攻击 / 下+技能」新招和连携（方案见 proposals/skill_depth.md）。
// 所有新弹道都是直线或固定路线，不追踪；所有「每个敌人」效果最多作用于最近的 5 人（10 人局不刷屏）。
using System;
using System.Collections.Generic;
using static Brawl.Core.Defs;

namespace Brawl.Core
{
    public sealed partial class Sim
    {
        /// <summary>离 p 最近的至多 n 个敌人（活着、不是队友）。</summary>
        List<Player> NearestEnemies(Player p, int n)
        {
            var l = new List<Player>(Enemies(p));
            l.Sort((a, b) => Hypot(a.Cx - p.Cx, a.Cy - p.Cy).CompareTo(Hypot(b.Cx - p.Cx, b.Cy - p.Cy)));
            if (l.Count > n) l.RemoveRange(n, l.Count - n);
            return l;
        }

        // ================= 下+攻击 =================
        bool AttackV9(Player p, PInput inp)
        {
            if (!inp.D) return false;
            float ax, ay;
            switch (p.Ch)
            {
                case CharId.DVD: // 回旋光盘：直线飞出 0.55 秒后沿原路飞回，去回各打一次
                    {
                        if (p.CloneT > 0 || p.Stk >= 6) return false;
                        p.AtkCd = .8f; ax = p.Face; ay = 0;
                        var b = Shoot(p, ax, ay, 690, 10, 9, ProjKind.Bolt); b.Life = 1.25f; b.RetT = .55f; b.Pierce = true; b.HitSet = new HashSet<Player>(); b.Color = 0xff9be8u; b.Dig = 0;
                        Text(p.Cx, p.Y - 26, "回旋!", 0xff9be8u, false); Sound(Sfx.Disc); return true;
                    }
                case CharId.XP: // 空中：屁股坐；地上：跺脚（只打站在地上的人，跳起来能躲）
                    {
                        if (p.V9Cd > 0) return false;
                        p.V9Cd = 1.5f; p.AtkCd = .3f;
                        if (!p.OnGround) { p.ButtT = 1.2f; p.Vy = 1400; p.Vx *= .3f; Text(p.Cx, p.Y - 30, "坐!", 0x9097c2u, false); Sound(Sfx.Swing); return true; }
                        Shake(6); Sound(Sfx.Slam, 6); Burst(p.Cx, p.Y + p.H, 0x9097c2u, 20, 360, 5);
                        Ring(p.Cx - 90, p.Y + p.H, 6, 90, 0xc8ccf0u, .3f, 4); Ring(p.Cx + 90, p.Y + p.H, 6, 90, 0xc8ccf0u, .3f, 4);
                        foreach (var e in new List<Player>(Enemies(p)))
                            if (e.OnGround && Math.Abs(e.Cx - p.Cx) < 180 && Math.Abs(e.Y + e.H - (p.Y + p.H)) < 40 && CanHit(p, e)) { Damage(e, 6, p, Sign(e.Cx - p.Cx) * 200, -260); e.Stun = Math.Max(e.Stun, .3f); }
                        return true;
                    }
                case CharId.GARY: // 扇形三连苦无
                    {
                        p.AtkCd = .9f; AimVec(p, new PInput { L = inp.L, R = inp.R }, out ax, out ay);
                        float a0 = (float)Math.Atan2(ay, ax);
                        for (int k = -1; k <= 1; k++)
                        {
                            float a = a0 + k * .175f, cx = (float)Math.Cos(a), cy = (float)Math.Sin(a);
                            var b = Shoot(p, cx, cy, 1050, 8, 8, ProjKind.Kunai); b.Life = .7f; b.Color = 0xff8a3cu; b.Dig = 4; b.Ang = a;
                        }
                        Sound(Sfx.Shoot); return true;
                    }
                case CharId.DVD2: // 召回：插在地上的碟片沿直线飞回此刻的位置，路上打人
                    {
                        if (p.V9Cd > 0) return false;
                        int n = 0;
                        foreach (var g in Gadgets)
                        {
                            if (g.Dead || g.Owner != p || g.Kind != GKind.StuckDisc) continue;
                            float dx = p.Cx - g.X, dy = p.Cy - g.Y, d = Math.Max(1, Hypot(dx, dy)); const float sp = 1200;
                            Projs.Add(new Proj { X = g.X, Y = g.Y, Vx = dx / d * sp, Vy = dy / d * sp, R = 9, Dmg = 8, Owner = p, Kind = ProjKind.Bolt, Life = d / sp, Pierce = true, HitSet = new HashSet<Player>(), Color = 0x9a7bffu, Dig = 0 });
                            g.Dead = true; n++;
                        }
                        if (n == 0) return false;
                        p.V9Cd = 4; p.AtkCd = .3f; p.Ammo = p.AmmoMax; Text(p.Cx, p.Y - 30, "召回 ×" + n, 0x9a7bffu, true); Sound(Sfx.Wave, 6); return true;
                    }
                case CharId.KAI2: // 反转：直接切换自身极性
                    {
                        if (p.V9Cd > 0) return false;
                        p.V9Cd = 3; p.AtkCd = .2f; p.SelfPol = -p.SelfPol;
                        Text(p.Cx, p.Y - 30, p.SelfPol > 0 ? "极性 ＋" : "极性 －", p.SelfPol > 0 ? 0xff4d5eu : 0x4d8bffu, false);
                        Ring(p.Cx, p.Cy, 10, 60, p.SelfPol > 0 ? 0xff4d5eu : 0x4d8bffu, .3f, 6); Sound(Sfx.Pickup, 6); return true;
                    }
            }
            return false;
        }

        // ================= 下+技能 =================
        bool SkillV9(Player p, PInput inp)
        {
            if (!inp.D) return false;
            switch (p.Ch)
            {
                case CharId.KAI: // 放电：消耗全部电荷，以自己为中心放一圈电
                    {
                        if (p.Stk <= 0) return false;
                        int k = p.Stk; p.Stk = 0; float R = 120 + 20 * k, dmg = 6 + 4 * k, stun = .1f * k;
                        Ring(p.Cx, p.Cy, 10, R, 0x3cf0ffu, .35f, 10); Ring(p.Cx, p.Cy, 10, R * .7f, 0xffffffu, .3f, 6); Burst(p.Cx, p.Cy, 0x9ff8ff, 30, 420, 5); Shake(8); Sound(Sfx.Wave, 6);
                        for (int i = 0; i < 6; i++) { double a = i * Math.PI / 3; Zap(p.Cx, p.Cy, p.Cx + (float)Math.Cos(a) * R, p.Cy + (float)Math.Sin(a) * R, 0x3cf0ffu, 1); }
                        foreach (var e in new List<Player>(Enemies(p)))
                        {
                            float dx = e.Cx - p.Cx, dy = e.Cy - p.Cy, d = Math.Max(1, Hypot(dx, dy));
                            if (d < R + e.W / 2 && CanHit(p, e)) { Damage(e, dmg, p, dx / d * 300, -200); e.Stun = Math.Max(e.Stun, stun); }
                        }
                        Text(p.Cx, p.Y - 40, "放电 ×" + k, 0x3cf0ffu, true); return true;
                    }
                case CharId.DZY: // 单根献祭：只献祭最近的一根触手，+1 级（独立冷却）
                    {
                        if (p.V9SkCd > 0) { p.SkCd = 0; return true; }
                        Tentacle best = null; float bd = 1e9f;
                        foreach (var tc in Tentacles) if (tc.Owner == p && tc.T > 0) { float d = Hypot(tc.X - p.Cx, tc.Y - p.Cy); if (d < bd) { bd = d; best = tc; } }
                        if (best == null) return false;
                        best.T = -1; float x = best.X, y = best.Y - 26;
                        Ring(x, y, 8, 90, 0xe0457bu, .35f, 8); Burst(x, y, 0xe0457bu, 18, 320, 6);
                        Emit(new Fx { Type = FxType.Zap, X = x, Y = y, A = p.Cx, B = p.Cy, C = 2, Color = 0xff7aa0u });
                        foreach (var e in new List<Player>(Enemies(p))) if (Hypot(e.Cx - x, e.Cy - y) < 100 + e.W / 2) Damage(e, 11, p, Sign(e.Cx - x == 0 ? 1 : e.Cx - x) * 280, -240);
                        if (p.Stk < 12) { p.Stk++; p.MaxHp += 10; }
                        p.Hp = Math.Min(p.MaxHp, p.Hp + 15);
                        Text(p.Cx, p.Y - 44, "献祭 ×1 → " + p.Stk + " 级", 0xff7aa0u, false); Sound(Sfx.God, 4);
                        p.V9SkCd = 2.5f; p.SkCd = 0; return true; // 不占用主技能冷却
                    }
                case CharId.BW: // 奇点：前方放下不动的奇点，慢慢把人拉近（能走出去）
                    {
                        if (p.V9SkCd > 0) { p.SkCd = 0; return true; }
                        if (p.Shield > 0) return false;
                        float x = Clamp(p.Cx + p.Face * 160, 20, W - 20), y = p.Cy - 10;
                        AddG(GKind.Singularity, p, x, y, 180, 2.5f, 1);
                        Ring(x, y, 180, 10, 0x7b2cffu, .4f, 10); Text(x, y - 50, "奇点", 0xc77dffu, true); Sound(Sfx.Hole);
                        p.V9SkCd = 9; p.SkCd = 0; return true;
                    }
                case CharId.CCLD2: // 成神后：跳过今天，直接进入下一天
                    {
                        if (!p.Divine) return false;
                        p.Day++; p.SkCd = 1.2f; Text(p.Cx, p.Y - 50, "→ " + DayNames[p.Day % 7], 0xfff2a8u, false); Sound(Sfx.Pickup, 4); return true;
                    }
            }
            return false;
        }

        // ================= 原版 ccld「孤高剑心」：场上敌人越少越强（单挑时神力 3 倍速、受伤减少） =================
        public static float LONE_DR = .8f, LONE_K1 = 3f, LONE_K2 = 1.4f;
        float LoneK(Player p)
        {
            int n = 0; foreach (var e in Players) if (e != p && e.Alive && !Ally(p, e)) n++;
            return n <= 1 ? LONE_K1 : n == 2 ? LONE_K2 : 1;
        }

        /// <summary>原版 ccld 没成神时的技能「瞬步 · 一闪」：向前闪过 260 像素，路上的人都被斩一刀（12 伤害、短暂无敌、加神力）。</summary>
        public static float FLASH_DMG = 8, FLASH_CD = 3.5f;
        void FlashStep(Player p)
        {
            p.SkCd = FLASH_CD; float x0 = p.X, dist = 0;
            for (int i = 0; i < 26; i++) { float nx = p.X + p.Face * 10; if (BoxHit(nx, p.Y, p.W, p.H) || nx < 0 || nx > W - p.W) break; p.X = nx; dist += 10; }
            float bx = Math.Min(x0, p.X) - 6, bw = dist + p.W + 12;
            foreach (var e in new List<Player>(Enemies(p))) if (Overlap(bx, p.Y - 10, bw, p.H + 20, e.X, e.Y, e.W, e.H)) { Damage(e, FLASH_DMG, p, p.Face * 260, -300); e.Stun = Math.Max(e.Stun, .25f); }
            HitClonesBox(p, bx, p.Y - 10, bw, p.H + 20, FLASH_DMG);
            p.Inv = Math.Max(p.Inv, .3f); p.Vx = p.Face * 200; p.Swing = .2f;
            Ring(p.Cx, p.Cy, 6, 70, 0xfff2a8u, .25f, 6); Burst(x0 + p.W / 2, p.Cy, 0xffc531u, 14, 300, 4); Text(p.Cx, p.Y - 40, "一闪!", 0xfff2a8u, false); Sound(Sfx.Swing, 1);
        }

        // ================= 终极球（借鉴大乱斗）：每局第 30 秒在空中出现一次，谁抢到谁下一次技能放终极爆发 =================
        void SpawnUltBall()
        {
            foreach (var p in Players) if (p.Ult) return;
            foreach (var it in ItemsList) if (it.Type == Items.Ult) return;
            ItemsList.Add(new Item { X = W / 2 - 13, Y = H * .22f, Type = Items.Ult, Life = 30, Landed = false });
            Text(W / 2, H * .22f - 50, "终极球出现了!", 0xfff2a8u, true); Sound(Sfx.God, 6); Ring(W / 2, H * .22f, 10, 160, 0xfff2a8u, .6f, 10);
        }
        void UltBlast(Player p)
        {
            p.Ult = false; p.SkCd = 1; p.PowT = Math.Max(p.PowT, 6); p.Inv = Math.Max(p.Inv, .8f);
            Flash(.6f); Shake(26); Sound(Sfx.Boom, 1); Sound(Sfx.God, 2);
            Ring(p.Cx, p.Cy, 10, 460, 0xfff2a8u, .7f, 18); Ring(p.Cx, p.Cy, 10, 300, Char(p.Ch).Color, .55f, 14); Ring(p.Cx, p.Cy, 10, 160, 0xffffffu, .4f, 10);
            Burst(p.Cx, p.Cy, 0xfff2a8u, 80, 900, 9); Burst(p.Cx, p.Cy, Char(p.Ch).Color, 50, 700, 7);
            Text(p.Cx, p.Y - 70, "终 极 爆 发 !", 0xfff2a8u, true);
            DestroyAt(p.Cx, p.Cy, 60);
            foreach (var e in new List<Player>(Enemies(p)))
            {
                float dx = e.Cx - p.Cx, dy = e.Cy - p.Cy, d = Math.Max(1, Hypot(dx, dy));
                if (d < 460 + e.W / 2 && CanHit(p, e)) { Damage(e, 28 * (1 - d / 900), p, dx / d * 1100, -600); e.Stun = Math.Max(e.Stun, .6f); }
            }
        }

        // ================= 每帧 =================
        void UpdateV9(float dt)
        {
            foreach (var p in Players)
            {
                if (p.V9Cd > 0) p.V9Cd -= dt; if (p.V9SkCd > 0) p.V9SkCd -= dt;
                if (p.Ch == CharId.CCLD && p.ComboT > 0) p.ComboT -= dt;
                if (p.ButtT > 0)
                {
                    p.ButtT -= dt;
                    if (!p.Alive) p.ButtT = 0;
                    else if (p.OnGround)
                    {   // 屁股坐落地：半径 80、10 伤害、砸碎脚下的土
                        p.ButtT = 0; float x = p.Cx, y = p.Y + p.H;
                        Shake(10); Sound(Sfx.Slam, 4); Burst(x, y, 0x9097c2u, 26, 420, 6); Ring(x, y, 10, 80, 0xc8ccf0u, .3f, 8); DestroyAt(x, y + 8, 18);
                        foreach (var e in new List<Player>(Enemies(p))) { float dx = e.Cx - x, dy = e.Cy - y, d = Math.Max(1, Hypot(dx, dy)); if (d < 80 + e.W / 2 && CanHit(p, e)) Damage(e, 10, p, dx / d * 380, -380); }
                    }
                }
            }
            // 回旋光盘：时间到就原路返回，返回时可以再打一次
            foreach (var b in Projs)
            {
                if (b.RetT < 0) continue;
                b.RetT -= dt;
                if (b.RetT < 0) { b.Vx = -b.Vx; b.Vy = -b.Vy; b.HitSet?.Clear(); b.Color = 0xffd0f0u; }
            }
            // bw 奇点
            foreach (var g in Gadgets)
            {
                if (g.Dead || g.Kind != GKind.Singularity) continue;
                var o = g.Owner; if (o == null) { g.Dead = true; continue; }
                foreach (var e in Enemies(o))
                {
                    float dx = g.X - e.Cx, dy = g.Y - e.Cy, d = Math.Max(1, Hypot(dx, dy));
                    if (d < g.R && !(e.Ch == CharId.XP && e.Armor > 0)) { e.X += dx / d * 260 * dt; if (!e.OnGround) e.Y += dy / d * 160 * dt; }
                }
                foreach (var b in Projs)
                {
                    if (Hypot(b.X - g.X, b.Y - g.Y) > g.R) continue;
                    if (b.Owner == o && b.Kind == ProjKind.Hole && !b.Sling) { b.Vx *= 1.3f; b.Vy *= 1.3f; b.Sling = true; b.Dmg += 2; } // 引力弹弓
                    else if (b.Owner != o && !Ally(o, b.Owner)) { float a = (float)Math.Atan2(b.Vy, b.Vx), da = (float)Math.Atan2(g.Y - b.Y, g.X - b.X) - a; while (da > Math.PI) da -= (float)(2 * Math.PI); while (da < -Math.PI) da += (float)(2 * Math.PI); a += Clamp(da, -.52f, .52f) * dt * 2; float sp = Hypot(b.Vx, b.Vy); b.Vx = (float)Math.Cos(a) * sp; b.Vy = (float)Math.Sin(a) * sp; }
                }
                if (g.T <= 0)
                {
                    g.Dead = true; Ring(g.X, g.Y, 10, 120, 0x7b2cffu, .4f, 10); Burst(g.X, g.Y, 0x7b2cffu, 30, 420, 6); Shake(8); Sound(Sfx.Boom, 8);
                    foreach (var e in new List<Player>(Enemies(o))) { float dx = e.Cx - g.X, dy = e.Cy - g.Y, d = Math.Max(1, Hypot(dx, dy)); if (d < 120 + e.W / 2 && CanHit(o, e)) { Damage(e, 8, o, dx / d * 300, -300); AddDot(e, o, 1); } }
                }
            }
            // px 拖雷：被钩子拉回来的人经过 px 自己的地雷，地雷立即爆炸 ×1.5
            foreach (var e in Players)
            {
                if (!e.Alive || e.PullT <= 0 || e.PullBy == null || e.PullBy.Ch != CharId.PX) continue;
                var o = e.PullBy;
                foreach (var g in Gadgets)
                {
                    if (g.Dead || g.Kind != GKind.Mine || g.Owner != o || Hypot(e.Cx - g.X, e.Y + e.H - g.Y) > 40 + e.W / 2) continue;
                    g.Dead = true; Ring(g.X, g.Y, 10, 110, 0xff7a3cu, .4f, 12); Burst(g.X, g.Y, 0xffb04au, 34, 480, 7); Shake(11); Sound(Sfx.Boom, 6); DestroyAt(g.X, g.Y, 28);
                    Text(g.X, g.Y - 40, "拖雷!", 0xff7a3cu, true);
                    foreach (var q in new List<Player>(Enemies(o))) { float dx = q.Cx - g.X, dy = q.Cy - g.Y, d = Math.Max(1, Hypot(dx, dy)); if (d < 100 + q.W / 2 && CanHit(o, q)) Damage(q, 21, o, dx / d * 420, -420); }
                }
            }
        }

        // ================= 电脑也会用新招（简单触发） =================
        void ThinkV9(Player p, Player t, float ad, float dy, ref bool a, ref bool s, ref bool dd)
        {
            if (t == null) return;
            if (p.Ult) { s = ad < 380 && Math.Abs(dy) < 300; if (s) { dd = false; return; } }
            // 电脑平时按↓（下平台、趴下）时顺手按了攻击/技能，会误放成新招：有新招的角色先把↓松开，下面按需要再按
            bool v9 = p.Ch == CharId.DVD || p.Ch == CharId.XP || p.Ch == CharId.GARY || p.Ch == CharId.KAI || p.Ch == CharId.DZY || p.Ch == CharId.BW || p.Ch == CharId.DVD2 || p.Ch == CharId.KAI2 || p.Ch == CharId.CCLD2;
            if (v9 && dd && (a || s) && p.Ch != CharId.CCLD2) dd = false;
            double r = Rng.NextDouble();
            switch (p.Ch)
            {
                case CharId.DVD: if (p.AtkCd <= 0 && p.Stk < 6 && p.CloneT <= 0 && Math.Abs(dy) < 40 && ad < 420 && r < .25) { a = true; dd = true; } break;
                case CharId.XP: if (p.V9Cd <= 0 && ((!p.OnGround && ad < 70 && t.Cy > p.Cy) || (p.OnGround && t.OnGround && ad < 170 && Math.Abs(dy) < 30 && r < .3))) { a = true; dd = true; } break;
                case CharId.GARY: if (p.AtkCd <= 0 && ad < 260 && r < .02) { int near = 0; foreach (var e in Enemies(p)) if (Hypot(e.Cx - p.Cx, e.Cy - p.Cy) < 300) near++; if (near >= 2) { a = true; dd = true; } } break;
                case CharId.KAI: if (p.SkCd <= 0 && p.Stk >= 5) { int near = 0; foreach (var e in Enemies(p)) if (Hypot(e.Cx - p.Cx, e.Cy - p.Cy) < 200) near++; if (near >= 2) { s = true; dd = true; } } break;
                case CharId.DZY: if (p.V9SkCd <= 0 && p.SkCd > 0 && r < .05) { int nt = 0; foreach (var tc in Tentacles) if (tc.Owner == p && tc.T > 0) nt++; if (nt >= 2) { s = true; dd = true; } } break;
                case CharId.BW: if (p.V9SkCd <= 0 && ad < 360 && Math.Abs(dy) < 120 && r < .08) { s = true; dd = true; } break;
                case CharId.DVD2: if (p.V9Cd <= 0 && p.Ammo == 0) { a = true; dd = true; } break;
                case CharId.KAI2: if (p.V9Cd <= 0 && t.Pol != 0 && t.Pol == p.SelfPol && ad < 160 && r < .1) { a = true; dd = true; } break;
            }
        }
    }
}
