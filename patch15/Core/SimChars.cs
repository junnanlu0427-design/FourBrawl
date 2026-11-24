// 第六版新角色：px（原版新人）+ 7 个「-改」角色，以及通用的双击闪避、连杀播报。
// 所有数字是倍率前的原始值（伤害会再乘 DMG_MULT），和 Defs 里的描述同一口径。
using System;
using System.Collections.Generic;
using static Brawl.Core.Defs;

namespace Brawl.Core
{
    public sealed partial class Sim
    {
        public readonly List<Gadget> Gadgets = new List<Gadget>();
        readonly Dictionary<int, float> contactT = new Dictionary<int, float>(); // 滚球 / 踩头的接触冷却
        public static bool IsNew(CharId c) => c >= CharId.PX;
        public static readonly string[] DayNames = { "第一日 · 要有光", "第二日 · 分开诸水", "第三日 · 大地与草木", "第四日 · 日月星辰", "第五日 · 飞鸟与游鱼", "第六日 · 造人", "第七日 · 安息" };

        Gadget AddG(GKind k, Player o, float x, float y, float r, float t, int max = 99)
        {
            var g = new Gadget { Kind = k, Owner = o, X = x, Y = y, R = r, T = t, Born = RoundT };
            Gadgets.Add(g);
            int n = 0; for (int i = Gadgets.Count - 1; i >= 0; i--) if (Gadgets[i].Kind == k && Gadgets[i].Owner == o && !Gadgets[i].Dead) { n++; if (n > max) Gadgets[i].Dead = true; }
            return g;
        }
        bool CanHit(Player o, Player e) => e.Alive && e != o && !Ally(o, e) && e.Inv <= 0 && !e.Hidden;
        bool Contact(Player a, Player b, float gap)
        {
            int key = a.Slot * 16 + b.Slot; if (contactT.TryGetValue(key, out float t0) && RoundT - t0 < gap) return false;
            contactT[key] = RoundT; return true;
        }

        // ================= 攻击 =================
        bool AttackNew(Player p, PInput inp)
        {
            if (!IsNew(p.Ch)) return false;
            float ax, ay;
            switch (p.Ch)
            {
                case CharId.PX:
                    {
                        DecoyBoomOf(p);
                        if (inp.D)
                        {   // 地雷
                            p.AtkCd = .6f; var g = AddG(GKind.Mine, p, p.Cx, p.Y + p.H - 5, 30, 4, 3); g.Arm = .6f;
                            Burst(p.Cx, p.Y + p.H, 0x8fd14fu, 6, 120, 3); Sound(Sfx.Pickup); Text(p.Cx, p.Y - 20, "地雷", 0x8fd14fu, false); return true;
                        }
                        float charge = Clamp((RoundT - p.LastAtkT - .9f) / 1.6f, 0, 1); p.LastAtkT = RoundT;
                        AimVec(p, inp, out ax, out ay); float a0 = (float)Math.Atan2(ay, ax);
                        for (int i = 0; i < 6; i++)
                        {
                            float a = a0 + (i - 2.5f) * .11f + Rnd(-.03f, .03f), sp = 950 * Rnd(.9f, 1.05f);
                            var b = Shoot(p, (float)Math.Cos(a), (float)Math.Sin(a), sp, 4, 4.0f * (1 + .6f * charge), ProjKind.Pellet);
                            b.Life = b.Born = .32f; b.Dig = 4; b.Color = charge > .9f ? 0xfff27au : 0xc8ff8au; b.Remix = true;
                        }
                        p.Vx -= p.Face * 140; p.Swing = .14f; Shake(3 + charge * 4); Sound(Sfx.Shoot, 10);
                        Burst(p.Cx + ax * 24, p.Cy + ay * 24, 0xfff2a8u, 8, 260, 3);
                        if (charge > .9f) Text(p.Cx, p.Y - 26, "满蓄!", 0xfff27au, false);
                        return true;
                    }
                case CharId.CCLD2:
                    {
                        if (p.Divine) { GodCombo(p, inp); return true; }
                        if (p.ComboT3 <= 0) p.Combo3 = 0;
                        p.Combo3 = p.Combo3 % 3 + 1; p.ComboT3 = 1; int st = p.Combo3;
                        p.Swing = .16f; p.AtkCd = p.Divine ? .34f : .4f; Sound(Sfx.Swing);
                        float reach = st == 3 ? 100 : 80;
                        if (st == 3) { p.Vx = p.Face * 620; p.Bump = 0; }
                        Slash(p, reach, 58, st == 3 ? 17 : 12, st == 3 ? 520 : 300, st == 3 ? 300 : 200, true);
                        if (st == 3 || p.Divine)
                        {
                            AimVec(p, inp, out ax, out ay);
                            var b = Shoot(p, ax, ay, 900, 14, p.Divine ? 10 : 8, ProjKind.Wave); b.Life = p.Divine ? .34f : .25f; b.Pierce = true; b.HitSet = new HashSet<Player>(); b.Hh = 30;
                            b.Color = p.Divine ? 0xfff2a8u : 0xff6a5au; b.Ang = (float)Math.Atan2(ay, ax); b.Remix = true;
                        }
                        return true;
                    }
                case CharId.DVD2:
                    {
                        if (p.Ammo <= 0) { p.AtkCd = .4f; p.Swing = .16f; Sound(Sfx.Swing); Slash(p, 60, 44, 7, 260, 160, false); return true; }
                        p.Ammo--; AimVec(p, inp, out ax, out ay);
                        var b = Shoot(p, ax, ay, 640, 11, 12, ProjKind.Disc); b.Life = 6; b.Bounces = 4; b.Pierce = true; b.HitSet = new HashSet<Player>(); b.Remix = true; b.Color = 0x9a7bffu;
                        Sound(Sfx.Disc); return true;
                    }
                case CharId.XP2:
                    {
                        if (p.Held != null) { Spit(p); return true; }
                        if (p.StoredDmg > 0)
                        {
                            AimVec(p, inp, out ax, out ay); var b = Shoot(p, ax, ay, 820, 9, (float)Math.Round(p.StoredDmg * 1.1f)); b.Life = 1.2f; b.Color = 0xffb3d1u; b.Remix = true;
                            p.StoredDmg = 0; Text(p.Cx, p.Y - 24, "噗!", 0xffb3d1u, false); Sound(Sfx.Shoot); return true;
                        }
                        p.Swing = .3f; Sound(Sfx.Absorb);
                        float bx = p.Face > 0 ? p.X + p.W - 6 : p.X - 110 + 6, by = p.Cy - 38;
                        Ring(p.Cx + p.Face * 60, p.Cy, 70, 10, 0xffb3d1u, .3f, 6);
                        int got = 0;
                        foreach (var b in Projs) if (b.Owner != p && !Ally(p, b.Owner) && b.Kind != ProjKind.Sword && b.Kind != ProjKind.Hook && Overlap(bx, by, 110, 76, b.X - 4, b.Y - 4, 8, 8)) { p.StoredDmg = Math.Max(p.StoredDmg, b.Dmg); b.Life = 0; got++; }
                        Player vic = null; float bd = 1e9f;
                        foreach (var e in Enemies(p)) if (CanHit(p, e) && e.Held == null && Overlap(bx, by, 110, 76, e.X, e.Y, e.W, e.H)) { float d = Math.Abs(e.Cx - p.Cx); if (d < bd) { bd = d; vic = e; } }
                        if (vic != null && vic.Bubble <= 0)
                        {
                            vic.HeldBy = p; p.Held = vic; p.HoldT = 1.2f; vic.Latch = null; vic.Grap = false; vic.PullT = 0; vic.Vx = vic.Vy = 0;
                            Text(p.Cx, p.Y - 40, "吞!", 0xffb3d1u, true); Burst(p.Cx, p.Cy, 0xffb3d1u, 14, 220, 5); Sound(Sfx.Absorb, 8); p.AtkCd = .3f;
                        }
                        else if (got > 0) Text(p.Cx, p.Y - 30, "吞下子弹", 0xffb3d1u, false);
                        else Slash(p, 60, 50, 8, 300, 160, false);
                        return true;
                    }
                case CharId.BW2:
                    {
                        if (p.Energy >= 3)
                        {
                            p.Energy = 0; p.AtkCd = .7f; Text(p.Cx, p.Y - 40, "质量守恒 · 环形斥力!", 0xe8f4ffu, true); Ring(p.Cx, p.Cy, 10, 200, 0xe8f4ffu, .4f, 10); Sound(Sfx.Wave, 6);
                            for (int i = 0; i < 10; i++) { float a = i * (float)Math.PI / 5; PushWave(p, (float)Math.Cos(a), (float)Math.Sin(a)); }
                            return true;
                        }
                        if (inp.D)
                        {
                            if (p.BurstCd > 0) { p.AtkCd = .1f; return true; }
                            p.BurstCd = 3; p.AtkCd = .6f; Ring(p.Cx, p.Cy, 10, 160, 0xe8f4ffu, .3f, 10); Ring(p.Cx, p.Cy, 10, 120, 0x9fd0ffu, .25f, 6); Sound(Sfx.Wave, 8); Shake(6);
                            foreach (var e in new List<Player>(Enemies(p)))
                            {
                                float dx = e.Cx - p.Cx, dy = e.Cy - p.Cy, d = Hypot(dx, dy); if (d < 1) d = 1;
                                if (d < 160 + e.W / 2 && CanHit(p, e)) { Damage(e, 4, p, dx / d * 650, Math.Min(dy / d * 400, 0) - 260); e.PushT = .6f; e.PushBy = p; }
                            }
                            foreach (var b in Projs) if (b.Owner != p && !Ally(p, b.Owner) && Hypot(b.X - p.Cx, b.Y - p.Cy) < 160) { b.Vx = -b.Vx; b.Vy = -b.Vy; b.Owner = p; b.Color = 0xe8f4ffu; b.Home = 0; if (b.HitSet != null) b.HitSet.Clear(); }
                            return true;
                        }
                        AimVec(p, inp, out ax, out ay); PushWave(p, ax, ay); Sound(Sfx.Wave, 3); return true;
                    }
                case CharId.GARY2:
                    {
                        if (inp.D)
                        {
                            p.AtkCd = .6f; var g = AddG(GKind.Spike, p, p.Cx, p.Y + p.H - 4, 22, 30, 2); g.Arm = .5f;
                            Burst(p.Cx, p.Y + p.H, 0x3cbf9au, 5, 100, 3); Sound(Sfx.Pickup); return true;
                        }
                        AimVec(p, inp, out ax, out ay);
                        var b = Shoot(p, ax, ay, 1000, 6, 15, ProjKind.Kunai); b.Life = .7f; b.Color = 0x3cbf9au; b.Dig = 0; b.Ang = (float)Math.Atan2(ay, ax); b.Remix = true; Sound(Sfx.Shoot);
                        return true;
                    }
                case CharId.KAI2:
                    {
                        AimVec(p, inp, out ax, out ay); int pol = (p.Combo3++ % 2 == 0) ? 1 : -1;
                        var b = Shoot(p, ax, ay, 1150, 8, 13); b.Life = .32f; b.Pol = pol; b.Remix = true; b.Color = pol > 0 ? 0xff4d5eu : 0x3d9bffu; b.Dig = 5;
                        Sound(Sfx.Shoot, 3); return true;
                    }
                case CharId.DZY2:
                    {
                        if (p.SubT > 0) { p.AtkCd = .1f; return true; }
                        AimVec(p, inp, out ax, out ay);
                        var b = Shoot(p, ax, ay, 1, 9, 10, ProjKind.Spore); b.Vx = ax * 560; b.Vy = Math.Min(ay, 0) * 560 - 320; b.Life = 2.5f; b.Color = 0x6bd94au; b.Remix = true;
                        Sound(Sfx.Swing); return true;
                    }
            }
            return false;
        }

        void PushWave(Player p, float ax, float ay)
        {
            var b = Shoot(p, ax, ay, 380, 16, 6, ProjKind.Push); b.Life = .8f; b.Pierce = true; b.HitSet = new HashSet<Player>(); b.Hh = 34; b.Color = 0xe8f4ffu; b.Ang = (float)Math.Atan2(ay, ax); b.Dig = 0; b.Remix = true;
        }

        /// <summary>近身斩击（带剑尖判定）。顺便打假身/泥人。</summary>
        void Slash(Player p, float reach, float h, float dmg, float kb, float up, bool tipper)
        {
            float bx = p.Face > 0 ? p.X + p.W - 4 : p.X - reach + 4, by = p.Cy - h / 2;
            foreach (var e in new List<Player>(Enemies(p)))
            {
                if (!Overlap(bx, by, reach, h, e.X, e.Y, e.W, e.H) || e.Hidden) continue;
                bool tip = tipper && Math.Abs(e.Cx - p.Cx) - p.W / 2 > reach * .55f;
                if (tip) { Text(e.Cx, e.Y - 44, "剑尖!", 0xff4d5eu, false); Zap(p.Cx, p.Cy, e.Cx, e.Cy, 0xff4d5eu, 1); Sound(Sfx.Pickup, 6); }
                Damage(e, (float)Math.Round(dmg * (tip ? 1.4f : 1)), p, p.Face * kb, -up);
                if (p.Ch == CharId.CCLD2 && !p.Divine) p.Energy = Math.Min(100, p.Energy + (tip ? 8 : 4));
            }
            HitClonesBox(p, bx, by, reach, h, dmg);
            DestroyAt(bx + reach / 2, by + h / 2, 8);
        }
        void HitClonesBox(Player p, float x, float y, float w, float h, float dmg)
        {
            foreach (var c in new List<Clone>(Clones)) if (c.Kind != 0 && c.T > 0 && c.Owner != p && !Ally(c.Owner, p) && Overlap(x, y, w, h, c.X, c.Y, c.W, c.H)) CloneHurt(c, dmg);
        }

        // ================= 技能 =================
        bool SkillNew(Player p, PInput inp)
        {
            if (!IsNew(p.Ch)) return false;
            float ax, ay;
            switch (p.Ch)
            {
                case CharId.PX:
                    {
                        if (inp.D)
                        {
                            foreach (var c in Clones) if (c.Owner == p && c.Kind == 1) c.T = -1;
                            Clones.Add(new Clone { Kind = 1, Owner = p, X = p.X, Y = p.Y, W = p.W, H = p.H, Face = p.Face, T = 10, Hp = 3 });
                            p.SkCd = 5; Burst(p.Cx, p.Cy, 0xc7cbeeu, 10, 200, 6); Sound(Sfx.Clone); Text(p.Cx, p.Y - 30, "假身", 0x8fd14fu, false); return true;
                        }
                        DecoyBoomOf(p);
                        bool all = p.HookStk >= 3; var tg = new List<Player>();
                        foreach (var e in Enemies(p))
                        {
                            float dx = e.Cx - p.Cx, dy = e.Cy - p.Cy;
                            if (dx * p.Face > -20 && Hypot(dx, dy) < 560 && Math.Abs(dy) < 280 && CanHit(p, e)) tg.Add(e);
                        }
                        tg.Sort((a, b) => Hypot(a.Cx - p.Cx, a.Cy - p.Cy).CompareTo(Hypot(b.Cx - p.Cx, b.Cy - p.Cy)));
                        if (!all && tg.Count > 1) tg.RemoveRange(1, tg.Count - 1);
                        if (tg.Count > 5) tg.RemoveRange(5, tg.Count - 5); // 10 人局最多勾 5 人
                        if (tg.Count == 0) { Zap(p.Cx, p.Cy, p.Cx + p.Face * 420, p.Cy, 0x8fd14fu, 2); p.SkCd = 1.2f; Sound(Sfx.Swing); return true; }
                        if (all) { p.HookStk = 0; Text(p.Cx, p.Y - 48, "全部勾住!", 0x8fd14fu, true); Shake(8); }
                        foreach (var e in tg)
                        {
                            Zap(p.Cx, p.Cy, e.Cx, e.Cy, 0x8fd14fu, 2); Damage(e, 8, p, 0, 0); if (!e.Alive) continue;
                            e.MarkT = 6; e.MarkSrc = p; StartPull(e, p.Cx + p.Face * (p.W / 2 + e.W / 2 + 6), p.Cy, p, 0, .4f);
                            if (!all) { p.HookStk = Math.Min(3, p.HookStk + 1); if (p.HookStk == 3) Text(p.Cx, p.Y - 44, "钩子满层! 下一钩勾全部", 0x8fd14fu, false); }
                        }
                        Sound(Sfx.Hit, 8); return true;
                    }
                case CharId.CCLD2:
                    {
                        if (!p.Divine)
                        {
                            if (p.Energy >= 100) { Descend(p); return true; }
                            p.ParryT = .3f; p.SkCd = 3; Ring(p.Cx, p.Cy, p.H, p.H + 10, 0xff4d5eu, .3f, 4); Sound(Sfx.Swing, 2); return true;
                        }
                        int day = p.Day % 7; p.Day++; p.SkCd = 3.8f;
                        Text(p.Cx, p.Y - 50, DayNames[day], 0xfff2a8u, true); Sound(Sfx.Wave, 2);
                        CastDay(p, day, inp); return true;
                    }
                case CharId.DVD2:
                    {
                        var g = AddG(GKind.BigDisc, p, p.Cx + p.Face * 60, p.Cy - 30, 45, 6, 1); g.Vx = p.Face * 380; g.Vy = -380;
                        p.AmmoMax = 5; p.Ammo = Math.Min(5, p.Ammo + 2);
                        Text(p.Cx, p.Y - 46, "巨型屏保!", 0x9a7bffu, true); Ring(g.X, g.Y, 10, 80, 0x9a7bffu, .3f, 8); Sound(Sfx.Disc, 8); return true;
                    }
                case CharId.XP2:
                    p.BallT = 2.5f; p.Vx = p.Face * 560; p.Vy = Math.Min(p.Vy, -200); p.OnGround = false;
                    Text(p.Cx, p.Y - 40, "滚滚!", 0xffb3d1u, true); Burst(p.Cx, p.Y + p.H, 0xffb3d1u, 12, 260, 4); Sound(Sfx.Jump, 4); return true;
                case CharId.BW2:
                    {
                        AimVec(p, inp, out ax, out ay);
                        float x = Clamp(p.Cx + ax * 200, 40, W - 40), y = Clamp(p.Cy + ay * 200, 60, H - 60);
                        AddG(GKind.WhiteHole, p, x, y, 170, 3, 1); Text(x, y - 60, "事件视界", 0xe8f4ffu, true); Ring(x, y, 10, 170, 0xe8f4ffu, .4f, 10); Sound(Sfx.Hole, 6); return true;
                    }
                case CharId.GARY2:
                    {
                        var ws = Wires(p);
                        if (ws.Count == 0)
                        {
                            AimVec(p, inp, out ax, out ay); p.Vx = ax * 950; p.Vy = Math.Min(ay * 700, 0) - 260; p.OnGround = false; p.Inv = Math.Max(p.Inv, .2f); p.SkCd = 2.5f;
                            Zap(p.Cx, p.Cy, p.Cx + ax * 200, p.Cy + ay * 200, 0x3cbf9au, 1); Sound(Sfx.Swing); return true;
                        }
                        var caught = new Dictionary<Player, int>(); var counts = new int[ws.Count];
                        foreach (var e in Enemies(p))
                        {
                            if (!CanHit(p, e)) continue;
                            for (int i = 0; i < ws.Count; i++) if (SegDist(e.Cx, e.Cy, ws[i].Item1.X, ws[i].Item1.Y, ws[i].Item2.X, ws[i].Item2.Y) < 60 + e.W / 2) { caught[e] = i; counts[i]++; break; }
                        }
                        foreach (var w in ws) { Zap(w.Item1.X, w.Item1.Y, w.Item2.X, w.Item2.Y, 0x7fffd0u, 2); Ring((w.Item1.X + w.Item2.X) / 2, (w.Item1.Y + w.Item2.Y) / 2, 60, 6, 0x3cbf9au, .3f, 6); }
                        foreach (var kv in caught) { var w = ws[kv.Value]; StartPull(kv.Key, (w.Item1.X + w.Item2.X) / 2, (w.Item1.Y + w.Item2.Y) / 2, p, 12 + 5 * (counts[kv.Value] - 1), .45f); }
                        foreach (var g in Gadgets) if (g.Owner == p && g.Kind == GKind.Anchor) g.Dead = true;
                        Text(p.Cx, p.Y - 44, caught.Count > 0 ? "收网!" : "收网（空）", 0x3cbf9au, true); Sound(Sfx.Wave, 6); Shake(caught.Count > 0 ? 8 : 2); return true;
                    }
                case CharId.KAI2:
                    {
                        var M = new List<Player>(); foreach (var e in Enemies(p)) if (e.Pol != 0 && e.PolT > 0 && CanHit(p, e)) M.Add(e);
                        if (M.Count == 0) { p.SkCd = .4f; return true; }
                        Text(p.Cx, p.Y - 46, "磁 暴 !", 0xff6a8au, true); Ring(p.Cx, p.Cy, 10, 220, 0xff4d5eu, .35f, 8); Ring(p.Cx, p.Cy, 10, 180, 0x3d9bffu, .35f, 8); Sound(Sfx.Boom, 8); Shake(8);
                        if (M.Count == 1)
                        {
                            var e = M[0]; float dx = e.Cx - p.Cx, dy = e.Cy - p.Cy, d = Math.Max(1, Hypot(dx, dy));
                            Zap(p.Cx, p.Cy, e.Cx, e.Cy, e.Pol > 0 ? 0xff4d5eu : 0x3d9bffu, 2);
                            if (e.Pol != p.SelfPol) StartPull(e, p.Cx + p.Face * (p.W / 2 + e.W / 2 + 8), p.Cy, p, 10, .35f);
                            else Damage(e, 10, p, dx / d * 760, -320);
                        }
                        else
                            foreach (var e in M)
                            {
                                Player opp = null, same = null; float od = 1e9f, sd = 1e9f;
                                foreach (var q in M) { if (q == e) continue; float d = Hypot(q.Cx - e.Cx, q.Cy - e.Cy); if (q.Pol != e.Pol) { if (d < od) { od = d; opp = q; } } else if (d < sd) { sd = d; same = q; } }
                                if (opp != null) { Zap(e.Cx, e.Cy, opp.Cx, opp.Cy, 0xffffffu, 2); StartPull(e, (e.Cx + opp.Cx) / 2, (e.Cy + opp.Cy) / 2, p, 18, .4f); }
                                else if (same != null) { float dx = e.Cx - same.Cx, dy = e.Cy - same.Cy, d = Math.Max(1, Hypot(dx, dy)); Zap(e.Cx, e.Cy, same.Cx, same.Cy, e.Pol > 0 ? 0xff4d5eu : 0x3d9bffu, 1); Damage(e, 8, p, dx / d * 820, -360); }
                            }
                        foreach (var e in M) { e.Pol = 0; e.PolT = 0; }
                        p.SelfPol = -p.SelfPol; Text(p.Cx, p.Y - 26, p.SelfPol > 0 ? "自身极性 ＋" : "自身极性 －", p.SelfPol > 0 ? 0xff4d5eu : 0x3d9bffu, false);
                        return true;
                    }
                case CharId.DZY2:
                    {
                        if (p.SubT > 0) { Emerge(p); return true; }
                        if (!OnCarpet(p, p)) AddG(GKind.Patch, p, p.Cx, p.Y + p.H, 50, 14, 28);
                        p.SubT = 2; p.SkCd = .25f; p.Inv = Math.Max(p.Inv, .1f); p.Latch = null;
                        Burst(p.Cx, p.Y + p.H, 0x6bd94au, 16, 220, 5); Sound(Sfx.Hole, 4); return true;
                    }
            }
            return false;
        }

        void Descend(Player p)
        {
            // 神降：天上劈下光柱，全场震荡，2.5 秒无敌——一眼看出来是神
            p.Divine = true; p.Energy = 0; p.Hp = Math.Min(p.MaxHp, p.Hp + 40); p.SkCd = .8f; p.Day = 0; p.Inv = Math.Max(p.Inv, 2.5f);
            Flash(.7f); Shake(24); Sound(Sfx.God); Sound(Sfx.Boom, 2);
            for (int i = 0; i < 4; i++) Emit(new Fx { Type = FxType.Zap, X = p.Cx + (i - 1.5f) * 14, Y = -60, A = p.Cx, B = p.Cy, C = 8, Color = i % 2 == 0 ? 0xfff2a8u : 0xffffffu });
            Ring(p.Cx, p.Cy, 10, 620, 0xfff2a8u, .8f, 18); Ring(p.Cx, p.Cy, 10, 420, 0xffffffu, .6f, 14); Ring(p.Cx, p.Cy, 10, 220, 0xffd84au, .45f, 10);
            Burst(p.Cx, p.Cy, 0xfff2a8u, 90, 900, 9); Burst(p.Cx, p.Cy, 0xffffffu, 40, 600, 7);
            Text(p.Cx, p.Y - 70, "七 日 创 世 · 神 降 !", 0xfff2a8u, true);
            foreach (var e in new List<Player>(Enemies(p)))
            {
                float dx = e.Cx - p.Cx, dy = e.Cy - p.Cy, d = Math.Max(1, Hypot(dx, dy));
                if (d < 560 + e.W / 2) { Damage(e, 18, p, dx / d * 900, -480); e.Stun = Math.Max(e.Stun, .5f); }
            }
        }

        /// <summary>神之一击：从天而降的光矛（七日里多次用到）。</summary>
        void LightSpear(Player p, float x, float delay, float dmg)
        {
            var b = new Proj { X = Clamp(x, 10, W - 10), Y = -60 - delay * 900, Vx = 0, Vy = 1500, R = 12, Dmg = dmg, Owner = p, Kind = ProjKind.Sword, Pierce = true, HitSet = new HashSet<Player>(), Life = 2.5f, Color = 0xfff2a8u, Dig = 18 };
            Projs.Add(b);
        }

        void CastDay(Player p, int day, PInput inp)
        {
            switch (day)
            {
                case 0: // 第一日 · 要有光：全场致盲 + 每个敌人头上落下光矛
                    Flash(.75f); Shake(12); Sound(Sfx.God, 2);
                    Ring(p.Cx, p.Cy, 10, 900, 0xffffffu, .7f, 20); Ring(p.Cx, p.Cy, 10, 520, 0xfff2a8u, .5f, 14); Burst(p.Cx, p.Cy, 0xffffffu, 70, 900, 8);
                    foreach (var e in NearestEnemies(p, 5))
                    {
                        if (!CanHit(p, e)) continue;
                        Damage(e, 12, p, 0, 0); e.BlindT = 2f;
                        for (int k = -1; k <= 1; k++) LightSpear(p, e.Cx + k * 36, Math.Abs(k) * .12f, 8);
                    }
                    break;
                case 1: // 第二日 · 分开诸水：两道巨浪从你身边向左右两边扫过整张地图
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var g = AddG(GKind.WaterWall, p, p.Cx + s * 40, p.Cy - 120, 300, 2.6f, 2); g.Dir = s; g.Vx = s * 760;
                        Burst(g.X, g.Y + 200, 0x7fd0ffu, 40, 500, 7);
                    }
                    Shake(14); Sound(Sfx.Wave, 2); Sound(Sfx.Slam, 4); Ring(p.Cx, p.Cy, 10, 300, 0x7fd0ffu, .5f, 12);
                    break;
                case 2: // 第三日 · 大地与草木：每个敌人脚下同时拔起石柱，落地被藤蔓缠住
                    {
                        int made = 0;
                        foreach (var tg in NearestEnemies(p, 5))
                        {
                            if (Hypot(tg.Cx - p.Cx, tg.Cy - p.Cy) > 1100) continue;
                            float x = tg.Cx, footY = tg.Y + tg.H;
                            int c = (int)Math.Floor(x / T), r0 = -1;
                            for (int r = Math.Max(0, (int)Math.Floor((footY - 10) / T)); r < ROWS; r++) if (Solid(c, r)) { r0 = r; break; }
                            if (r0 < 0) r0 = Math.Min(ROWS - 2, (int)Math.Floor(footY / T) + 1);
                            int top = Math.Max(3, r0 - 9);
                            int n = RaiseRaw(Tiles, COLS, ROWS, c - 2, top, 5, r0 - top, 2);
                            if (n > 0) { TileVersion++; Emit(new Fx { Type = FxType.Raise, X = c - 2, Y = top, A = 5, B = r0 - top, C = RoundNo, Color = 2 }); }
                            foreach (var q in Players) if (q.Alive && BoxHit(q.X, q.Y, q.W, q.H)) { Unstick(q); if (q != p && !Ally(p, q) && CanHit(p, q)) { q.Vy = -1100; Damage(q, 14, p, 0, -400); } }
                            AddG(GKind.Vine, p, c * T, top * T, 90, 6, 6);
                            Burst(c * T, top * T, Map.Dirt, 36, 480, 8); Burst(c * T, top * T, 0x7dff8au, 16, 300, 5); made++;
                        }
                        Shake(16); Sound(Sfx.Slam, 2); Ring(p.Cx, p.Y + p.H, 10, 260, 0x7dff8au, .5f, 10);
                        if (made == 0) p.SkCd = .3f;
                        break;
                    }
                case 3: // 第四日 · 日月星辰：一颗巨大的太阳追着敌人烧，同时天降流星
                    AddG(GKind.Sun, p, p.Cx, p.Y - 120, 210, 8, 1); p.SpringT = 8;
                    foreach (var e in NearestEnemies(p, 5)) LightSpear(p, e.Cx, .3f, 10);
                    Ring(p.Cx, p.Y - 120, 10, 260, 0xffa030u, .6f, 12); Burst(p.Cx, p.Y - 120, 0xffd060u, 50, 600, 7); Sound(Sfx.God, 2); Shake(10);
                    break;
                case 4: // 第五日 · 飞鸟与游鱼：12 只光鸟锁定此刻敌人的位置，地上先亮预警圈，再直线俯冲（不追踪，能躲）
                    {
                        var tgs = NearestEnemies(p, 5); var pts = new List<float[]>();
                        for (int i = 0; i < 12; i++)
                        {
                            float tx, ty;
                            if (tgs.Count > 0 && i < tgs.Count * 2 + 2) { var e = tgs[i % tgs.Count]; tx = e.Cx + (i / tgs.Count - 1) * 26; ty = e.Cy; }
                            else { tx = Rnd(80, W - 80); ty = p.Cy + Rnd(-200, 200); }
                            pts.Add(new[] { tx, ty });
                        }
                        for (int i = 0; i < pts.Count; i++)
                        {
                            float tx = pts[i][0], ty = pts[i][1], side = i % 2 == 0 ? -1 : 1;
                            float sx = tx + side * 380, sy = ty - 520 - i * 18, dx = tx - sx, dy = ty - sy, d = Math.Max(1, Hypot(dx, dy));
                            const float sp = 1100; // 飞到落点约 0.6 秒，地上的预警圈足够看清
                            var b = new Proj { X = sx, Y = sy, Vx = dx / d * sp, Vy = dy / d * sp, R = 7, Dmg = 7, Owner = p, Kind = ProjKind.Bolt, Life = d / sp + .25f, Color = 0xffd84au, Remix = true, Dig = 6 };
                            Projs.Add(b); Ring(tx, ty, 6, 40, 0xff5a4au, d / sp, 4);
                        }
                        Ring(p.Cx, p.Cy, 10, 200, 0xffd84au, .5f, 10); Burst(p.Cx, p.Cy, 0xffd84au, 40, 500, 6); Sound(Sfx.Wave, 2);
                        break;
                    }
                case 5: // 第六日 · 造人：三个泥人一起上
                    for (int i = 0; i < 3; i++)
                    {
                        float x = Clamp(p.X + (i - 1) * 60 - p.Face * 30, 0, W - p.W); if (BoxHit(x, p.Y, p.W, p.H)) x = p.X;
                        Clones.Add(new Clone { Kind = 2, Owner = p, X = x, Y = p.Y, W = p.W, H = p.H, Face = p.Face, T = 10, Hp = 70, Cd = .4f + i * .2f });
                        Burst(x + p.W / 2, p.Y + p.H, Map.Dirt, 26, 360, 6);
                    }
                    Sound(Sfx.Clone); Shake(8); Ring(p.Cx, p.Y + p.H, 10, 180, 0xc89058u, .5f, 10);
                    break;
                default: // 第七日 · 安息：神的领域——3 秒无敌回血，全场敌人挨一次审判、技能冷却 +3 秒
                    p.Inv = Math.Max(p.Inv, 3f); p.Hp = Math.Min(p.MaxHp, p.Hp + 40);
                    Flash(.5f); Shake(14); Sound(Sfx.God, 1);
                    Ring(p.Cx, p.Cy, 10, 1000, 0xffd84au, 1.2f, 22); Ring(p.Cx, p.Cy, 10, 600, 0xfff2a8u, .9f, 16); Burst(p.Cx, p.Cy, 0xffd84au, 60, 700, 8);
                    foreach (var e in NearestEnemies(p, 5))
                    {
                        if (!CanHit(p, e)) continue;
                        e.SkCd += 3; Text(e.Cx, e.Y - 30, "技能 +3 秒", 0xffd84au, false);
                        LightSpear(p, e.Cx, 0, 16);
                    }
                    break;
            }
        }

        void Emerge(Player p)
        {
            p.SubT = 0; p.SkCd = p.C.SkCd; p.Inv = Math.Max(p.Inv, .2f); p.Vy = -420; p.OnGround = false;
            Ring(p.Cx, p.Cy, 10, 90, 0x6bd94au, .35f, 10); Burst(p.Cx, p.Y + p.H, 0x6bd94au, 30, 380, 6); Burst(p.Cx, p.Y + p.H, 0x2a5a1au, 16, 300, 5); Text(p.Cx, p.Y - 40, "咬!", 0x6bd94au, true); Sound(Sfx.Slam, 8); Shake(8);
            foreach (var e in new List<Player>(Enemies(p)))
            {
                float dx = p.Cx - e.Cx, dy = p.Cy - e.Cy, d = Math.Max(1, Hypot(dx, dy));
                if (d < 90 + e.W / 2 && CanHit(p, e)) { Damage(e, 18, p, 0, -200); e.Vx = dx / d * 500; e.OnGround = false; }
            }
        }

        void StartPull(Player e, float x, float y, Player by, int dmg, float t)
        {
            if (!e.Alive || e.HeldBy != null) return;
            e.PullT = t; e.PullX = x; e.PullY = y; e.PullBy = by; e.PullDmg = dmg; e.Grap = false; e.Latch = null; e.Stun = Math.Max(e.Stun, .3f);
        }

        void Spit(Player p)
        {
            var e = p.Held; p.Held = null; if (e == null) return; e.HeldBy = null;
            e.X = p.Face > 0 ? p.X + p.W + 2 : p.X - e.W - 2; e.Y = p.Cy - e.H / 2;
            if (BoxHit(e.X, e.Y, e.W, e.H)) { e.X = p.Cx - e.W / 2; e.Y = p.Y - e.H - 2; Unstick(e); }
            e.Vx = p.Face * 1150; e.Vy = -140; e.SpatT = .7f; e.SpatBy = p; e.Stun = .5f; e.OnGround = false;
            Damage(e, 10, p, 0, 0); e.Vx = p.Face * 1150;
            p.AtkCd = .5f; Text(p.Cx, p.Y - 34, "噗!", 0xffb3d1u, true); Burst(p.Cx + p.Face * 20, p.Cy, 0xffffffu, 10, 300, 3); Sound(Sfx.Shoot, 10); Shake(6);
        }

        void DecoyBoomOf(Player p) { foreach (var c in Clones.ToArray()) if (c.Owner == p && c.Kind == 1 && c.T > 0) DecoyBoom(c); }
        void DecoyBoom(Clone c)
        {
            var o = c.Owner; c.T = -1; float x = c.Cx, y = c.Cy;
            Ring(x, y, 10, 110, 0x8fd14fu, .35f, 10); Burst(x, y, 0x8fd14fu, 28, 420, 6); Burst(x, y, 0xffffffu, 10, 300, 4); Shake(9); Sound(Sfx.Boom, 9); Text(x, y - 40, "假身爆炸!", 0x8fd14fu, false);
            foreach (var e in new List<Player>(Enemies(o)))
            {
                float dx = e.Cx - x, dy = e.Cy - y, d = Math.Max(1, Hypot(dx, dy));
                if (d < 110 + e.W / 2 && CanHit(o, e)) { Damage(e, 12, o, dx / d * 380, -280); e.MarkT = 6; e.MarkSrc = o; }
            }
        }
        void CloneHurt(Clone c, float dmg)
        {
            if (c.T <= 0) return;
            if (c.Kind == 1) { c.Hits++; Burst(c.Cx, c.Cy, 0xc7cbeeu, 6, 160, 4); if (c.Hits >= 3) DecoyBoom(c); }
            else { c.Hp -= dmg * DMG_MULT; Burst(c.Cx, c.Cy, c.Kind == 2 ? Map.Dirt : 0x6bd94au, 6, 160, 4); if (c.Hp <= 0) CloneDie(c); }
        }
        void CloneDie(Clone c)
        {
            var o = c.Owner; c.T = -1; float x = c.Cx, y = c.Cy; float R = c.Kind == 2 ? 80 : 80, dmg = c.Kind == 2 ? 8 : 12;
            Burst(x, y, c.Kind == 2 ? Map.Dirt : 0x6bd94au, 24, 380, 6); Ring(x, y, 10, R, c.Kind == 2 ? 0xc8a070u : 0x6bd94au, .3f, 8); Sound(Sfx.Boom, 6); Shake(6);
            foreach (var e in new List<Player>(Enemies(o))) { float dx = e.Cx - x, dy = e.Cy - y, d = Math.Max(1, Hypot(dx, dy)); if (d < R + e.W / 2 && CanHit(o, e)) Damage(e, dmg, o, dx / d * 320, -240); }
        }

        // ================= 子弹 =================
        /// <summary>子弹打中人之前：见切格挡。返回 true = 子弹被挡掉。</summary>
        bool ProjBlockNew(Proj b, Player p)
        {
            if (p.Ch == CharId.CCLD2 && p.ParryT > 0 && b.Owner != null) { ParrySuccess(p, b.Owner); Burst(b.X, b.Y, 0xff4d5eu, 10, 260, 3); return true; }
            return false;
        }
        bool ProjHitNew(Proj b, Player p)
        {
            if (!b.Remix) return false; var o = b.Owner;
            float l = Math.Max(1, Hypot(b.Vx, b.Vy));
            switch (b.Kind)
            {
                case ProjKind.Pellet:
                    {
                        float k = .6f + (b.Born > 0 ? b.Life / b.Born : 1); // 越近越疼
                        Damage(p, (float)Math.Round(b.Dmg * k, 1), o, b.Vx / l * 110, -70);
                        if (o != null && p.MarkT > 0 && p.MarkSrc == o && p.Alive)
                        {
                            p.MarkT = 0; Damage(p, 10, o, 0, 0); if (o.Alive) o.Hp = Math.Min(o.MaxHp, o.Hp + 8);
                            Text(p.Cx, p.Y - 44, "引爆印记!", 0x8fd14fu, true); Ring(p.Cx, p.Cy, 6, 60, 0x8fd14fu, .3f, 8); Sound(Sfx.Heavy, 10);
                        }
                        return true;
                    }
                case ProjKind.Push:
                    Damage(p, b.Dmg, o, b.Vx / l * 600, -260); p.PushT = .6f; p.PushBy = o; return true;
                case ProjKind.Spore:
                    Damage(p, b.Dmg, o, b.Vx / l * 200, -160); if (o != null) AddG(GKind.Patch, o, p.Cx, p.Y + p.H, 60, 14, 28); return true;
            }
            if (o != null && o.Ch == CharId.KAI2 && b.Pol != 0)
            {
                Damage(p, (float)Math.Round(b.Dmg * (p.Pol == o.SelfPol ? 1.15f : 1)), o, b.Vx / l * 160, -100);
                p.Pol = b.Pol; p.PolT = 5; return true;
            }
            return false;
        }
        /// <summary>子弹撞到地形。返回 true = 已处理。</summary>
        bool ProjWallNew(Proj b, float px, float py, int c, int r)
        {
            if (!b.Remix) return false; var o = b.Owner;
            if (b.Kind == ProjKind.Disc && b.Bounces <= 0) { b.Life = 0; b.Boomed = true; if (o != null) AddG(GKind.StuckDisc, o, px, py, 16, 6); Sound(Sfx.Hit, 3); return true; }
            if (b.Kind == ProjKind.Kunai && o != null && o.Ch == CharId.GARY2) { b.Life = 0; AddG(GKind.Anchor, o, px, py, 6, 40, 4); Ring(px, py, 4, 20, 0x3cbf9au, .2f); Sound(Sfx.Hit, 2); return true; }
            if (b.Kind == ProjKind.Spore) { b.Life = 0; if (o != null) AddG(GKind.Patch, o, px, r * T, 60, 14, 28); Burst(px, py, 0x6bd94au, 14, 220, 5); Sound(Sfx.Hit, 3); return true; }
            if (b.Kind == ProjKind.Push) { b.Life = 0; Burst(px, py, 0xe8f4ffu, 8, 200, 4); return true; }
            return false;
        }
        void Disc2Bounce(Proj b)
        {
            b.Dmg = (float)Math.Round(b.Dmg * 1.25f, 1); b.Vx *= 1.1f; b.Vy *= 1.1f; b.HitSet?.Clear();
            uint[] cols = { 0x9a7bffu, 0x5ab4ffu, 0x7dffb0u, 0xffd84au, 0xff6b6bu, 0xff9ad5u }; b.Color = cols[(4 - b.Bounces + 6) % 6];
            bool corner = b.BounceT2 == RoundT || RoundT - b.BounceT < .12f; b.BounceT = RoundT;
            if (corner && b.Owner != null)
            {
                var o = b.Owner; b.BounceT = -9;
                Text(b.X, b.Y - 24, "PERFECT!", 0xffd84au, true); Ring(b.X, b.Y, 10, 70, 0xffd84au, .35f, 10); Burst(b.X, b.Y, 0xffd84au, 24, 380, 6); Shake(6); Sound(Sfx.Boom, 8);
                foreach (var e in new List<Player>(Enemies(o))) if (Hypot(e.Cx - b.X, e.Cy - b.Y) < 70 + e.W / 2 && CanHit(o, e)) Damage(e, 18, o, Sign(e.Cx - b.X) * 300, -260);
                if (o.Alive) o.Ammo = Math.Min(o.AmmoMax, o.Ammo + 1);
            }
            else Ring(b.X, b.Y, 4, 24, b.Color, .2f);
        }
        void ProjCleanupNew()
        {
            foreach (var b in Projs)
                if (b.Life <= 0 && !b.Boomed && b.Remix && b.Kind == ProjKind.Disc) { b.Boomed = true; if (b.Owner != null) b.Owner.Ammo = Math.Min(b.Owner.AmmoMax, b.Owner.Ammo + 1); }
            // 假身 / 泥人 / 小菌怪挨子弹
            foreach (var c in Clones.ToArray())
            {
                if (c.Kind == 0 || c.T <= 0) continue;
                foreach (var b in Projs.ToArray())
                {
                    if (b.Life <= 0 || b.Owner == null || b.Owner == c.Owner || Ally(b.Owner, c.Owner) || b.Kind == ProjKind.Bomb || b.Kind == ProjKind.IceBomb) continue;
                    if (Math.Abs(b.X - c.Cx) < b.R + c.W / 2 && Math.Abs(b.Y - c.Cy) < b.R + c.H / 2)
                    {
                        if (b.Pierce) { if (RoundT - c.Cd < .25f && c.Kind == 1) continue; if (c.Kind == 1) c.Cd = RoundT; } else b.Life = 0;
                        CloneHurt(c, b.Dmg); if (c.T <= 0) break;
                    }
                }
            }
        }

        // ================= 伤害修正 =================
        bool DamageBlockNew(Player t, Player src)
        {
            if (t.HeldBy != null || t.SubT > 0) return true;
            if (t.Ch == CharId.CCLD2 && t.ParryT > 0 && src != null && src != t) { ParrySuccess(t, src); return true; }
            return false;
        }
        float TakenMulNew(Player t)
        {
            float k = 1;
            if (t.Ch == CharId.CCLD2 && t.Divine) k *= .75f;
            if (t.Ch == CharId.XP2 && t.BallT > 0) k *= .6f;
            if (t.Ch == CharId.DZY2 && OnCarpet(t, t)) k *= .85f;
            if (t.Ch == CharId.BW2) foreach (var g in Gadgets) if (g.Kind == GKind.WhiteHole && g.Owner == t && Hypot(g.X - t.Cx, g.Y - t.Cy) < g.R) { k *= .7f; break; }
            return k;
        }
        void ParrySuccess(Player p, Player src)
        {
            p.ParryT = 0; p.SkCd = 0; p.Inv = Math.Max(p.Inv, .35f); SlowT = Math.Max(SlowT, .6f);
            if (!p.Divine) p.Energy = Math.Min(100, p.Energy + 20);
            Text(p.Cx, p.Y - 50, "见 切 !", 0xff4d5eu, true); Flash(.2f); Sound(Sfx.Heavy, 14); Ring(p.Cx, p.Cy, 10, 90, 0xff4d5eu, .3f, 8);
            if (src != null && src.Alive && Hypot(src.Cx - p.Cx, src.Cy - p.Cy) < 700)
            {
                float nx = src.Cx - src.Face * (src.W / 2 + p.W / 2 + 10) - p.W / 2, ny = src.Y + src.H - p.H;
                if (!BoxHit(nx, ny, p.W, p.H)) { Zap(p.Cx, p.Cy, nx + p.W / 2, ny + p.H / 2, 0xff4d5eu, 2); p.X = nx; p.Y = ny; }
                p.Face = Sign(src.Cx - p.Cx) == 0 ? p.Face : Sign(src.Cx - p.Cx);
                if (!Ally(p, src) && src.Inv <= 0) Damage(src, 22, p, p.Face * 420, -260);
            }
        }

        // ================= 每帧 =================
        /// <summary>每帧在读输入之后调用。返回 true = 这一帧不走正常移动（被吞 / 被拉）。</summary>
        bool PreMoveNew(Player p, ref PInput inp, float dt)
        {
            p.MarkT -= dt; p.PolT -= dt; if (p.PolT <= 0) p.Pol = 0; p.BlindT -= dt; p.BurstCd -= dt; p.PushT -= dt; p.SpatT -= dt; p.ComboT3 -= dt; p.TrailT -= dt; p.DodgeT -= dt; p.DodgeCd -= dt;
            if (p.BallT > 0) { p.BallT -= dt; if (p.BallT <= 0) { Burst(p.Cx, p.Cy, 0xffb3d1u, 10, 200, 4); } }
            if (p.ParryT > 0) { p.ParryT -= dt; if (p.ParryT <= 0) { p.Stun = Math.Max(p.Stun, .35f); Text(p.Cx, p.Y - 26, "没格到", 0x9097c2u, false); } }
            if (p.Ch == CharId.CCLD2 && !p.Divine && State == GameState.Play) p.Energy = Math.Min(100, p.Energy + 1.1f * dt);
            if (p.Ch == CharId.CCLD2 && p.Divine) p.Hp = Math.Min(p.MaxHp, p.Hp + .6f * dt);
            if (p.Ch == CharId.DVD2 && p.AmmoMax > 3 && !HasG(GKind.BigDisc, p)) { p.AmmoMax = 3; p.Ammo = Math.Min(3, p.Ammo); }
            // 被吞：藏在 xp-改 肚子里，乱按可以早点出来
            if (p.HeldBy != null)
            {
                var h = p.HeldBy;
                if (!h.Alive || h.Held != p) { p.HeldBy = null; if (h.Held == p) h.Held = null; return false; }
                p.X = h.Cx - p.W / 2; p.Y = h.Cy - p.H / 2; p.Vx = p.Vy = 0; p.OnGround = false;
                var ri = Inputs[p.Slot]; bool mash = p.Kind == SlotMode.Cpu ? Rng.NextDouble() < .3 : (ri.L || ri.R || ri.U || ri.A || ri.S);
                if (mash) h.HoldT -= dt * .6f;
                return true;
            }
            if (p.Held != null) { p.HoldT -= dt; if (p.HoldT <= 0) Spit(p); }
            // 被钩子 / 收网 / 磁暴拉
            if (p.PullT > 0)
            {
                p.PullT -= dt; float dx = p.PullX - p.Cx, dy = p.PullY - p.Cy, d = Hypot(dx, dy);
                float step = 1600 * dt;
                if (d <= step + 4 || p.PullT <= 0)
                {
                    if (d <= step + 4) { p.X = p.PullX - p.W / 2; p.Y = p.PullY - p.H / 2; }
                    p.PullT = 0; Unstick(p); p.Vx = 0; p.Vy = -80;
                    if (p.PullDmg > 0) { Damage(p, p.PullDmg, p.PullBy, 0, -140); p.Stun = Math.Max(p.Stun, .55f); Burst(p.Cx, p.Cy, 0xffffffu, 12, 300, 4); p.PullDmg = 0; }
                }
                else { p.X += dx / d * step; p.Y += dy / d * step; p.Vx = dx / d * 600; p.Vy = dy / d * 600; }
                p.OnGround = false; return true;
            }
            // 通用闪避：快速双击左或右
            if (State == GameState.Play && p.Kind != SlotMode.Cpu && !p.God)
            {
                if (inp.L && !p.PrevL) { if (RoundT - p.TapL < .26f) Dodge(p, -1); p.TapL = RoundT; }
                if (inp.R && !p.PrevR) { if (RoundT - p.TapR < .26f) Dodge(p, 1); p.TapR = RoundT; }
            }
            p.PrevL = inp.L; p.PrevR = inp.R;
            switch (p.Ch)
            {
                case CharId.DZY2:
                    if (p.SubT > 0) { p.SubT -= dt; inp.A = false; if (p.SubT <= 0) Emerge(p); }
                    if (p.OnGround && p.TrailT <= 0 && p.SubT <= 0 && State == GameState.Play) { p.TrailT = .35f; AddG(GKind.Patch, p, p.Cx, p.Y + p.H, 24, 10, 28); }
                    if (OnCarpet(p, p)) p.Hp = Math.Min(p.MaxHp, p.Hp + 3 * dt);
                    break;
                case CharId.KAI2:
                    foreach (var e in Enemies(p)) if (e.Pol != 0 && e.Pol != p.SelfPol && !e.Hidden) { float dx = p.Cx - e.Cx, dy = p.Cy - e.Cy, d = Hypot(dx, dy); if (d < 140 && d > 30) { e.Vx += dx / d * 700 * dt; } }
                    break;
            }
            // 敌人的菌毯：变慢 + 掉血
            if (p.OnGround) foreach (var q in Players) if (q.Ch == CharId.DZY2 && q != p && !Ally(p, q) && OnCarpet(p, q)) { Hurt(p, 2 * dt, q); break; }
            return false;
        }
        public void Dodge(Player p, int dir)
        {
            if (p.DodgeCd > 0 || p.Stun > 0 || p.FrozenT > 0 || p.Daze > 0) return;
            p.DodgeT = .25f; p.DodgeCd = 1.2f; p.Inv = Math.Max(p.Inv, .25f); p.Face = dir;
            if (p.OnGround) p.Vx = dir * 900; else { p.Vx = dir * 720; p.Vy = Math.Min(p.Vy, 0) * .3f; }
            Burst(p.Cx, p.Y + p.H, 0xdfe4ffu, 8, 200, 4); Ring(p.Cx, p.Cy, p.H / 2, p.H, 0xdfe4ffu, .2f, 4); Sound(Sfx.Swing, 1);
        }
        float SpeedMulNew(Player p)
        {
            float k = 1;
            if (p.Ch == CharId.XP2) { if (p.BallT > 0) k *= 2.24f; if (p.Held != null) k *= .7f; }
            if (p.Ch == CharId.DZY2) { if (p.SubT > 0) k *= 1.85f; else if (OnCarpet(p, p)) k *= 1.3f; }
            if (p.OnGround) foreach (var q in Players) if (q.Ch == CharId.DZY2 && q != p && !Ally(p, q) && OnCarpet(p, q)) { k *= .8f; break; }
            return k;
        }
        /// <summary>移动之后：撞墙伤害（bw-改）、被吐出去撞人/撞墙、滚球撞人、踩头。</summary>
        void PostMoveNew(Player p, HitInfo h, float vxb, float dt)
        {
            if (p.PushT > 0 && h.Hx && Math.Abs(vxb) > 180)
            {
                var by = p.PushBy; p.PushT = 0;
                Damage(p, Math.Min(16, 8 + Math.Abs(vxb) * .02f), by, 0, 0); p.Daze = Math.Max(p.Daze, .4f);
                float wx = p.Cx + Sign(vxb) * (p.W / 2 + 6); DestroyAt(wx, p.Cy, 12);
                Text(p.Cx, p.Y - 34, "撞墙!", 0xe8f4ffu, true); Ring(wx, p.Cy, 6, 60, 0xe8f4ffu, .3f, 8); Shake(10); Sound(Sfx.Heavy, 12);
                if (by != null && by.Alive) by.Energy = Math.Min(3, by.Energy + 1);
            }
            if (p.SpatT > 0 && p.SpatBy != null)
            {
                var o = p.SpatBy; bool hit = h.Hx;
                foreach (var q in new List<Player>(Players)) if (q != p && q != o && q.Alive && !Ally(o, q) && !q.Hidden && Overlap(p, q)) { Damage(q, 16, o, Sign(p.Vx) * 520, -300); hit = true; }
                if (hit) { p.SpatT = 0; Damage(p, 16, o, 0, -200); Shake(10); Sound(Sfx.Heavy, 14); Burst(p.Cx, p.Cy, 0xffb3d1u, 16, 320, 5); }
                else p.Vx = Sign(p.Vx == 0 ? 1 : p.Vx) * Math.Max(Math.Abs(p.Vx), 900);
            }
            if (p.Ch == CharId.XP2)
            {
                if (p.BallT > 0)
                {
                    if (h.Hx) { p.Vx = -vxb; p.Face = -p.Face; Burst(p.Cx + Sign(vxb) * p.W / 2, p.Cy, 0xffffffu, 6, 200, 3); Sound(Sfx.Hit, 4); }
                    foreach (var e in new List<Player>(Enemies(p)))
                        if (CanHit(p, e) && Overlap(p.X - 6, p.Y, p.W + 12, p.H, e.X, e.Y, e.W, e.H) && Contact(p, e, .5f))
                            Damage(e, (float)Math.Round(4 + Math.Abs(p.Vx) * .012f), p, Sign(p.Vx == 0 ? p.Face : p.Vx) * 520, -300);
                    if (Rng.NextDouble() < .5 && p.OnGround) Burst(p.Cx, p.Y + p.H, 0xffe0a0u, 2, 160, 3);
                }
                if (p.Vy > 120)
                    foreach (var e in new List<Player>(Enemies(p)))
                        if (CanHit(p, e) && p.X + p.W > e.X + 4 && p.X < e.X + e.W - 4 && p.Y + p.H >= e.Y - 6 && p.Y + p.H <= e.Y + 16 && Contact(p, e, .3f))
                        { Damage(e, 9, p, 0, 200); p.Vy = -780; p.Jumps = 1; Text(e.Cx, e.Y - 30, "踩!", 0xffb3d1u, false); Sound(Sfx.Jump, 8); }
            }
        }

        bool HasG(GKind k, Player o) { foreach (var g in Gadgets) if (g.Kind == k && g.Owner == o && !g.Dead) return true; return false; }
        /// <summary>p 是否站在 owner 的菌毯上。</summary>
        bool OnCarpet(Player p, Player owner)
        {
            if (!p.OnGround && p.SubT <= 0) return false;
            float fy = p.Y + p.H;
            foreach (var g in Gadgets) if (g.Kind == GKind.Patch && g.Owner == owner && !g.Dead && Math.Abs(p.Cx - g.X) < g.R && Math.Abs(fy - g.Y) < 26) return true;
            return false;
        }
        static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float lx = bx - ax, ly = by - ay, ll = lx * lx + ly * ly; float t = ll < 1 ? 0 : Clamp(((px - ax) * lx + (py - ay) * ly) / ll, 0, 1);
            return Hypot(px - (ax + lx * t), py - (ay + ly * t));
        }
        bool LineClear(float ax, float ay, float bx, float by)
        {
            for (int i = 2; i <= 18; i++) { float k = i / 20f; if (SolidPx(ax + (bx - ax) * k, ay + (by - ay) * k)) return false; }
            return true;
        }
        /// <summary>gary-改 的绊索：距离 ≤ 320、中间无遮挡的锚点两两相连，最多 3 根。朋友端也用同样的规则画。</summary>
        public List<Tuple<Gadget, Gadget>> Wires(Player o)
        {
            var an = new List<Gadget>(); foreach (var g in Gadgets) if (g.Kind == GKind.Anchor && g.Owner == o && !g.Dead) an.Add(g);
            var pairs = new List<Tuple<Gadget, Gadget>>();
            for (int i = 0; i < an.Count; i++) for (int j = i + 1; j < an.Count; j++) { var a = an[i]; var b = an[j]; float d = Hypot(a.X - b.X, a.Y - b.Y); if (d <= 320 && d > 20 && LineClear(a.X, a.Y, b.X, b.Y)) pairs.Add(Tuple.Create(a, b)); }
            pairs.Sort((x, y) => Hypot(x.Item1.X - x.Item2.X, x.Item1.Y - x.Item2.Y).CompareTo(Hypot(y.Item1.X - y.Item2.X, y.Item1.Y - y.Item2.Y)));
            if (pairs.Count > 3) pairs.RemoveRange(3, pairs.Count - 3);
            return pairs;
        }

        void UpdateGadgets(float dt)
        {
            foreach (var g in Gadgets.ToArray())
            {
                if (g.Dead) continue; var o = g.Owner;
                if (o == null || !o.Alive) { if (g.Kind != GKind.Patch) g.Dead = true; }
                g.T -= dt; g.Arm -= dt; g.Tick -= dt;
                switch (g.Kind)
                {
                    case GKind.Mine:
                        {
                            bool boom = g.T <= 0;
                            if (g.Arm <= 0) foreach (var e in Players) if (o != null && CanHit(o, e) && Math.Abs(e.Cx - g.X) < g.R + e.W / 2 && Math.Abs(e.Y + e.H - g.Y) < 30) boom = true;
                            if (boom && o != null)
                            {
                                g.Dead = true; Ring(g.X, g.Y, 10, 90, 0x8fd14fu, .35f, 10); Burst(g.X, g.Y, 0xffb04au, 26, 420, 6); Shake(9); Sound(Sfx.Boom, 10); DestroyAt(g.X, g.Y, 24);
                                foreach (var e in new List<Player>(Enemies(o))) { float dx = e.Cx - g.X, dy = e.Cy - g.Y, d = Math.Max(1, Hypot(dx, dy)); if (d < 90 + e.W / 2 && CanHit(o, e)) Damage(e, 14, o, dx / d * 420, -420); }
                            }
                            break;
                        }
                    case GKind.Spike:
                        if (g.Arm <= 0 && o != null) foreach (var e in new List<Player>(Players))
                                if (CanHit(o, e) && Math.Abs(e.Cx - g.X) < 18 + e.W / 2 && Math.Abs(e.Y + e.H - g.Y) < 18) { g.Dead = true; Damage(e, 10, o, 0, -900); e.Vy = -900; Text(e.Cx, e.Y - 30, "地钉!", 0x3cbf9au, false); Burst(g.X, g.Y, 0x3cbf9au, 12, 300, 4); Sound(Sfx.Heavy, 8); break; }
                        if (g.T <= 0) g.Dead = true;
                        break;
                    case GKind.Anchor:
                        if (g.T <= 0 || !SolidPx(g.X + Sign(0) * 0, g.Y) && !SolidPx(g.X - 3, g.Y) && !SolidPx(g.X + 3, g.Y) && !SolidPx(g.X, g.Y - 3) && !SolidPx(g.X, g.Y + 3)) g.Dead = true; // 锚点所在的地形被打掉了
                        break;
                    case GKind.StuckDisc:
                        if (o != null && Hypot(o.Cx - g.X, o.Cy - g.Y) < 36) { g.Dead = true; o.Ammo = Math.Min(o.AmmoMax, o.Ammo + 1); o.AtkCd = 0; Ring(g.X, g.Y, 6, 30, 0x9a7bffu, .2f); Sound(Sfx.Pickup); }
                        else if (g.T <= 0) { g.Dead = true; if (o != null) { o.Ammo = Math.Min(o.AmmoMax, o.Ammo + 1); Zap(g.X, g.Y, o.Cx, o.Cy, 0x9a7bffu, 1); } }
                        break;
                    case GKind.BigDisc:
                        {
                            if (g.T <= 0 || o == null) { g.Dead = true; Burst(g.X, g.Y, 0x9a7bffu, 20, 300, 6); break; }
                            bool fx = false, fy = false; float nx = g.X + g.Vx * dt, ny = g.Y + g.Vy * dt;
                            if (nx - g.R < 0 || nx + g.R > W || SolidPx(nx + Sign(g.Vx) * g.R, g.Y)) { g.Vx = -g.Vx; fx = true; } else g.X = nx;
                            if (ny - g.R < 0 || ny + g.R > H || SolidPx(g.X, ny + Sign(g.Vy) * g.R)) { g.Vy = -g.Vy; fy = true; } else g.Y = ny;
                            if (fx || fy) { Sound(Sfx.Heavy, 4); Ring(g.X + (fx ? Sign(-g.Vx) * g.R : 0), g.Y + (fy ? Sign(-g.Vy) * g.R : 0), 4, 30, 0xffffffu, .2f); }
                            if ((fx && fy) || ((fx || fy) && RoundT - g.Tick < .2f && g.Tick > -99))
                            {   // 完美角落
                                Text(g.X, g.Y - 50, "PERFECT!", 0xffd84au, true); Ring(g.X, g.Y, 10, 120, 0xffd84au, .4f, 12); Burst(g.X, g.Y, 0xffd84au, 30, 420, 7); Shake(10); Sound(Sfx.Boom, 10);
                                foreach (var e in new List<Player>(Enemies(o))) if (Hypot(e.Cx - g.X, e.Cy - g.Y) < 110 + e.W / 2 && CanHit(o, e)) Damage(e, 18, o, Sign(e.Cx - g.X) * 360, -300);
                                g.Tick = -999;
                            }
                            else if (fx || fy) g.Tick = RoundT;
                            foreach (var e in new List<Player>(Enemies(o)))
                                if (CanHit(o, e) && Hypot(e.Cx - g.X, e.Cy - g.Y) < g.R + e.W / 2)
                                {
                                    if (!g.HitT.TryGetValue(e.Slot, out float ht) || RoundT - ht > .6f) { g.HitT[e.Slot] = RoundT; Damage(e, 14, o, 0, 0); }
                                    e.Vx = g.Vx * 1.1f; e.Vy = g.Vy * 1.1f; e.OnGround = false;
                                }
                            break;
                        }
                    case GKind.Patch:
                        if (g.T <= 0 || !SolidPx(g.X, g.Y + 4)) g.Dead = true; // 地形碎了菌毯一起没
                        break;
                    case GKind.WaterWall:
                        if (g.T <= 0 || o == null) { g.Dead = true; break; }
                        g.X += g.Vx * dt; if (g.X < -80 || g.X > W + 80) { g.Dead = true; break; }
                        foreach (var e in new List<Player>(Enemies(o))) if (Math.Abs(e.Cx - g.X) < 46 + e.W / 2 && Math.Abs(e.Cy - g.Y) < g.R)
                        {
                            e.Vx = g.Dir * 900; e.Vy = Math.Min(e.Vy, -260); e.OnGround = false;
                            if (CanHit(o, e) && !g.HitT.ContainsKey(e.Slot)) { g.HitT[e.Slot] = RoundT; Damage(e, 14, o, 0, 0); e.Vx = g.Dir * 900; Burst(e.Cx, e.Cy, 0x7fd0ffu, 14, 300, 5); }
                        }
                        foreach (var b in Projs) if (b.Owner != o && !Ally(o, b.Owner) && Math.Abs(b.X - g.X) < 50 && Math.Abs(b.Y - g.Y) < g.R) { b.Life = 0; Burst(b.X, b.Y, 0x7fd0ffu, 4, 120, 3); }
                        break;
                    case GKind.Sun:
                        {
                            if (g.T <= 0 || o == null) { g.Dead = true; break; }
                            var t = Nearest(o, g.X, g.Y);
                            if (t != null) { float dx = t.Cx - g.X, dy = t.Cy - 40 - g.Y, d = Math.Max(1, Hypot(dx, dy)); g.X += dx / d * 150 * dt; g.Y += dy / d * 150 * dt; }
                            if (g.Tick <= 0) { g.Tick = .5f; foreach (var e in new List<Player>(Enemies(o))) if (CanHit(o, e) && Hypot(e.Cx - g.X, e.Cy - g.Y) < g.R) { Damage(e, 5, o, 0, 0); Zap(g.X, g.Y, e.Cx, e.Cy, 0xffa030u, 1); } }
                            break;
                        }
                    case GKind.WhiteHole:
                        {
                            if (o == null) { g.Dead = true; break; }
                            foreach (var b in Projs) if (b.Owner != o && !Ally(o, b.Owner) && b.Kind != ProjKind.Sword && Hypot(b.X - g.X, b.Y - g.Y) < g.R) { b.Vx = -b.Vx; b.Vy = -b.Vy; b.Owner = o; b.Color = 0xe8f4ffu; b.Home = 0; b.HitSet?.Clear(); }
                            foreach (var e in Enemies(o))
                            {
                                float dx = e.Cx - g.X, dy = e.Cy - g.Y, d = Math.Max(1, Hypot(dx, dy));
                                if (d < g.R && !e.Hidden) { float s = (1 - d / g.R) * 2200 + 500; e.Vx += dx / d * s * dt; e.Vy += dy / d * s * dt * .6f; e.OnGround = false; e.PushT = Math.Max(e.PushT, .3f); e.PushBy = o; }
                            }
                            if (g.T <= 0)
                            {
                                g.Dead = true; Ring(g.X, g.Y, 10, g.R + 30, 0xe8f4ffu, .4f, 12); Burst(g.X, g.Y, 0xe8f4ffu, 30, 420, 6); Shake(10); Sound(Sfx.Boom, 10);
                                foreach (var e in new List<Player>(Enemies(o))) { float dx = e.Cx - g.X, dy = e.Cy - g.Y, d = Math.Max(1, Hypot(dx, dy)); if (d < g.R + 30 && CanHit(o, e)) { Damage(e, 12, o, dx / d * 520, -300); e.PushT = .6f; e.PushBy = o; } }
                            }
                            break;
                        }
                    case GKind.Vine:
                        if (g.T <= 0 || o == null) { g.Dead = true; break; }
                        foreach (var e in Enemies(o)) if (CanHit(o, e) && Math.Abs(e.Cx - g.X) < g.R && Math.Abs(e.Y + e.H - g.Y) < 40 && !g.HitT.ContainsKey(e.Slot)) { g.HitT[e.Slot] = RoundT; e.Stun = Math.Max(e.Stun, 1.1f); e.Vx = 0; Text(e.Cx, e.Y - 30, "被藤蔓缠住!", 0x7dff8au, false); }
                        break;
                }
            }
            // gary-改：敌人碰到绊索
            foreach (var o in Players)
            {
                if (o.Ch != CharId.GARY2 || !o.Alive) continue;
                foreach (var w in Wires(o))
                    foreach (var e in new List<Player>(Enemies(o)))
                        if (CanHit(o, e) && e.Stun <= 0 && SegDist(e.Cx, e.Cy, w.Item1.X, w.Item1.Y, w.Item2.X, w.Item2.Y) < e.W / 2 + 3)
                        {
                            Damage(e, 7, o, 0, -220); e.Stun = Math.Max(e.Stun, .5f); e.Vx *= .2f; Text(e.Cx, e.Y - 30, "绊倒!", 0x3cbf9au, false); Sound(Sfx.Hit, 6);
                            (Hypot(w.Item1.X - e.Cx, w.Item1.Y - e.Cy) < Hypot(w.Item2.X - e.Cx, w.Item2.Y - e.Cy) ? w.Item1 : w.Item2).Dead = true; break;
                        }
            }
            Gadgets.RemoveAll(g => g.Dead);
        }

        void UpdateCloneNew(Clone c, float dt)
        {
            var o = c.Owner;
            if (c.Kind == 1)
            {   // px 假身：原地站着（会掉落），时间到就消失
                c.Vy += GRAV * GravK * dt; var h = MoveBox(ref c.X, ref c.Y, c.W, c.H, 0, c.Vy * dt); if (h.Hy) c.Vy = 0;
                if (c.T <= 0 || c.Y > H) { c.T = -1; Burst(c.Cx, c.Cy, 0xc7cbeeu, 10, 160, 5); }
                return;
            }
            // 泥人 / 小菌怪：追最近的敌人
            var t = Nearest(o, c.Cx, c.Cy); int dir = t != null ? Sign(t.Cx - c.Cx) : 0; float ad = t != null ? Math.Abs(t.Cx - c.Cx) : 0;
            if (c.Kind == 2 && ad < 180) dir = 0;
            if (dir != 0) c.Face = dir; else if (t != null) c.Face = Sign(t.Cx - c.Cx) == 0 ? c.Face : Sign(t.Cx - c.Cx);
            float sp = c.Kind == 3 ? 300 : 260;
            c.Vx += (dir * sp - c.Vx) * Math.Min(1, 10 * dt); c.Vy += GRAV * GravK * dt; if (c.Vy > 1300) c.Vy = 1300;
            if (c.OnGround && t != null && (t.Cy < c.Y - 40 || SolidPx(c.Cx + dir * (c.W / 2 + 6), c.Y + c.H - 10))) { c.Vy = -760; c.OnGround = false; }
            var hh = MoveBox(ref c.X, ref c.Y, c.W, c.H, c.Vx * dt, c.Vy * dt); c.OnGround = false;
            if (hh.Hy) { if (c.Vy > 0) c.OnGround = true; c.Vy = 0; }
            if (hh.Hx) c.Vx = 0;
            if (c.Kind == 2)
            {
                c.Cd -= dt;
                if (c.Cd <= 0 && t != null && Math.Abs(t.Cy - c.Cy) < 120 && ad < 420)
                {
                    c.Cd = .8f; float dx = t.Cx - c.Cx, dy = t.Cy - c.Cy, d = Math.Max(1, Hypot(dx, dy));
                    var b = new Proj { X = c.Cx, Y = c.Cy, Vx = dx / d * 900, Vy = dy / d * 900, R = 12, Dmg = 5, Owner = o, Kind = ProjKind.Wave, Life = .34f, Pierce = true, HitSet = new HashSet<Player>(), Hh = 26, Color = 0xd8b080u, Ang = (float)Math.Atan2(dy, dx), Remix = true };
                    Projs.Add(b); Sound(Sfx.Swing);
                }
            }
            else if (t != null && Overlap(c.X, c.Y, c.W, c.H, t.X, t.Y, t.W, t.H)) { CloneDie(c); return; }
            if (c.T <= 0 || c.Y > H) CloneDie(c);
        }

        // ================= 死亡 / 掉落 =================
        void OnKoNew(Player t, Player killer)
        {
            if (t.Held != null) { var v = t.Held; t.Held = null; v.HeldBy = null; v.X = t.Cx - v.W / 2; v.Y = t.Y - v.H; Unstick(v); }
            if (t.HeldBy != null) { if (t.HeldBy.Held == t) t.HeldBy.Held = null; t.HeldBy = null; }
            t.PullT = 0; t.SubT = 0; t.BallT = 0;
            foreach (var g in Gadgets) if (g.Owner == t && g.Kind != GKind.Patch) g.Dead = true;
            // 寄生：死在 dzy-改 菌毯上冒出小菌怪
            foreach (var q in Players) if (q.Alive && q.Ch == CharId.DZY2 && !Ally(q, t) && q != t)
                {
                    bool on = false; float fy = t.Y + t.H; foreach (var g in Gadgets) if (g.Kind == GKind.Patch && g.Owner == q && !g.Dead && Math.Abs(t.Cx - g.X) < g.R && Math.Abs(fy - g.Y) < 40) on = true;
                    if (on) { Clones.Add(new Clone { Kind = 3, Owner = q, X = t.Cx - 10, Y = t.Y + t.H - 20, W = 20, H = 18, Face = 1, T = 10, Hp = 25 }); Text(t.Cx, t.Y - 20, "寄生!", 0x6bd94au, false); break; }
                }
            // 连杀播报
            if (killer != null && killer != t)
            {
                if (t.Streak >= 2) { Text(t.Cx, t.Y - 70, "终结者!", 0xff5464u, true); Sound(Sfx.God, 3); }
                killer.Streak = RoundT - killer.StreakT < 10 ? killer.Streak + 1 : 1; killer.StreakT = RoundT;
                string s = killer.Streak == 2 ? "双 杀 !" : killer.Streak == 3 ? "三 杀 !" : killer.Streak >= 4 ? "超 神 !" : null;
                if (s != null) { Text(W / 2f, 300, "P" + (killer.Slot + 1) + " " + killer.C.Name + "  " + s, killer.C.Color, true); Sound(Sfx.God, 4); Shake(8); }
            }
            t.Streak = 0;
        }
        float FallPenaltyMulNew(Player p)
        {
            if (p.PushT > 0 && p.PushBy != null && p.PushBy.Alive) p.PushBy.Energy = Math.Min(3, p.PushBy.Energy + 1);
            if (p.Held != null) { var v = p.Held; p.Held = null; v.HeldBy = null; }
            p.PullT = 0; p.SubT = 0;
            return p.Ch == CharId.XP2 ? .5f : 1;
        }

        // ================= 电脑 =================
        void ThinkNew(Player p, Player t, float ad, float dy, ref bool a, ref bool s, ref bool dd, ref int dir)
        {
            a = false; s = false; dd = false; bool ok = p.SkCd <= 0;
            switch (p.Ch)
            {
                case CharId.PX:
                    {
                        bool decoy = false; foreach (var c in Clones) if (c.Owner == p && c.Kind == 1) decoy = true;
                        if (ad < 300 && Math.Abs(dy) < 110) a = true;
                        if (ok && ad > 160 && ad < 520 && Math.Abs(dy) < 240) s = true;
                        if (ok && !decoy && p.Hp < p.MaxHp * .5f && ad < 260 && Rng.NextDouble() < .05) { s = true; dd = true; }
                        if (!s && ad < 140 && p.OnGround && Rng.NextDouble() < .03) { a = true; dd = true; }
                        break;
                    }
                case CharId.CCLD2:
                    if (ad < 110 && Math.Abs(dy) < 60) a = true;
                    if (ok)
                    {
                        if (!p.Divine) { if (p.Energy >= 100) s = true; else if (ad < 130 && Math.Abs(dy) < 80 && Rng.NextDouble() < .06) s = true; }
                        else if (ad < 500) s = true;
                    }
                    if (p.Divine && ad > 140 && ad < 330 && Math.Abs(dy) < 100) a = true;
                    break;
                case CharId.DVD2:
                    {
                        Gadget near = null; float nd = 340; foreach (var g in Gadgets) if (g.Kind == GKind.StuckDisc && g.Owner == p) { float d = Math.Abs(g.X - p.Cx); if (d < nd && Math.Abs(g.Y - p.Cy) < 160) { nd = d; near = g; } }
                        if (near != null && p.Ammo < 2) dir = Sign(near.X - p.Cx);
                        if (p.Ammo > 0 ? ad < 700 && Math.Abs(dy) < 160 : ad < 90 && Math.Abs(dy) < 50) a = true;
                        if (ok && ad < 700) s = true;
                        break;
                    }
                case CharId.XP2:
                    if (p.Held != null) a = p.HoldT < .9f && Rng.NextDouble() < .2;
                    else if (ad < 120 && Math.Abs(dy) < 50) a = true;
                    if (ok && ad > 140 && ad < 420 && Math.Abs(dy) < 80 && p.Held == null) s = true;
                    break;
                case CharId.BW2:
                    if (p.BurstCd <= 0 && ad < 130 && Math.Abs(dy) < 90) { a = true; dd = true; }
                    else if (ad < 360 && Math.Abs(dy) < 100) a = true;
                    if (ok && ad < 420 && Math.Abs(dy) < 200) s = true;
                    break;
                case CharId.GARY2:
                    {
                        if (ad < 600 && Math.Abs(dy) < 130) a = true;
                        int nw = Wires(p).Count;
                        if (ok && nw > 0) foreach (var w in Wires(p)) if (SegDist(t.Cx, t.Cy, w.Item1.X, w.Item1.Y, w.Item2.X, w.Item2.Y) < 70) s = true;
                        if (!a && ad < 200 && p.OnGround && Rng.NextDouble() < .05) { a = true; dd = true; }
                        if (a && !dd && Rng.NextDouble() < .35) { /* 偶尔朝地形射，布锚点 */ dd = false; }
                        break;
                    }
                case CharId.KAI2:
                    {
                        if (ad < 360 && Math.Abs(dy) < 90) a = true;
                        int m = 0; foreach (var e in Enemies(p)) if (e.Pol != 0 && e.PolT > 0) m++;
                        if (ok && (m >= 2 || (m == 1 && ad < 420))) s = true;
                        break;
                    }
                case CharId.DZY2:
                    if (p.SubT > 0) { dir = Sign(t.Cx - p.Cx); if (ad < 60) s = true; break; }
                    if (ad < 520 && Math.Abs(dy) < 200) a = true;
                    if (ok && ad < 360 && ad > 80 && Math.Abs(dy) < 120) s = true;
                    break;
            }
            // 有子弹快打到自己：偶尔闪避
            if (p.DodgeCd <= 0 && p.Stun <= 0) foreach (var b in Projs) if (b.Owner != p && !Ally(p, b.Owner) && Hypot(b.X - p.Cx, b.Y - p.Cy) < 90 && Rng.NextDouble() < .05) { Dodge(p, -Sign(b.Vx == 0 ? 1 : b.Vx) * -1); break; }
        }
        static bool RangedNew(CharId c) => c == CharId.PX || c == CharId.DVD2 || c == CharId.BW2 || c == CharId.GARY2 || c == CharId.KAI2 || c == CharId.DZY2;
        static float PrefNew(Player p) => p.Ch == CharId.PX ? 170 : p.Ch == CharId.DVD2 ? 300 : p.Ch == CharId.BW2 ? 200 : p.Ch == CharId.GARY2 ? 240 : p.Ch == CharId.KAI2 ? 230 : p.Ch == CharId.DZY2 ? 240 : 30;
    }
}
namespace Brawl.Core
{
    public sealed partial class Sim
    {
        // 快照里的新角色状态：一个字节的标志 + 一个字节的数值
        public static int PackX(Player p)
        {
            int f = 0;
            if (p.Pol > 0 && p.PolT > 0) f |= 1; if (p.Pol < 0 && p.PolT > 0) f |= 2; if (p.MarkT > 0) f |= 4; if (p.BlindT > 0) f |= 8;
            if (p.Hidden) f |= 16; if (p.BallT > 0) f |= 32; if (p.ParryT > 0) f |= 64; if (p.Divine) f |= 128;
            return f;
        }
        public static int XValOf(Player p)
        {
            switch (p.Ch)
            {
                case CharId.PX: return p.HookStk;
                case CharId.CCLD2: return p.Day % 7;
                case CharId.DVD2: return p.Ammo | (p.AmmoMax << 4);
                case CharId.BW2: return (int)p.Energy;
                case CharId.KAI2: return p.SelfPol > 0 ? 1 : 0;
                case CharId.XP2: return p.Held != null ? 1 : 0;
                default: return 0;
            }
        }
        public static void UnpackX(Player p, int f, int v)
        {
            p.XFlags = f; p.XVal = v;
            p.Pol = (f & 1) != 0 ? 1 : (f & 2) != 0 ? -1 : 0; p.PolT = p.Pol != 0 ? 1 : 0;
            p.MarkT = (f & 4) != 0 ? 1 : 0; p.BlindT = (f & 8) != 0 ? 1 : 0; p.SubT = (f & 16) != 0 ? 1 : 0;
            p.BallT = (f & 32) != 0 ? 1 : 0; p.ParryT = (f & 64) != 0 ? 1 : 0; p.Divine = (f & 128) != 0;
            switch (p.Ch)
            {
                case CharId.PX: p.HookStk = v; break;
                case CharId.CCLD2: p.Day = v; break;
                case CharId.DVD2: p.Ammo = v & 15; p.AmmoMax = v >> 4; break;
                case CharId.BW2: p.Energy = v; break;
                case CharId.KAI2: p.SelfPol = v == 1 ? 1 : -1; break;
            }
        }
    }
}
