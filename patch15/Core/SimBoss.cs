// 第十四版：Boss 讨伐模式。P10 座位是 Boss（坐了人就由那个人来当 Boss，空着就是电脑），其余所有人一队一起打它。
// Boss = 放大 2.4 倍的随机角色，血量按讨伐人数算，几乎不吃击退，每隔几秒放一个有预警的 Boss 大招；血量低于 40% 狂暴。
using System;
using System.Collections.Generic;
using static Brawl.Core.Defs;

namespace Brawl.Core
{
    public partial class Sim
    {
        public const int BOSS_SLOT = MAX_P - 1;
        public const float BOSS_SCALE = 2.4f;
        public bool BossMode => Mode == GameMode.Boss;
        public const int BOSS_LIVES = 2; // 讨伐者每局额外 2 条命
        public readonly int[] BossLives = new int[MAX_P];
        /// <summary>讨伐者被打倒：还有命就 4 秒后复活（返回 true），没命了才真的出局。</summary>
        bool BossHeroDown(Player t)
        {
            if (!BossMode || IsBoss(t) || BossLives[t.Slot] <= 0) return false;
            BossLives[t.Slot]--; t.RespawnT = 4; Text(t.Cx, t.Y - 60, "还剩 " + BossLives[t.Slot] + " 条命", 0xffd0a0u, true);
            return true;
        }
        bool BossAlive() { foreach (var p in Players) if (IsBoss(p) && p.Alive) return true; return false; }
        bool HeroesLeft() { foreach (var p in Players) if (!IsBoss(p) && (p.Alive || p.RespawnT > 0)) return true; return false; }
        public bool IsBoss(Player p) => p != null && Mode == GameMode.Boss && p.Slot == BOSS_SLOT;
        public static readonly string[] BossMoveNames = { "震 地", "陨 石 雨", "冲 锋", "引 力 漩 涡" };
        float bossCd = 5, bossWarn = -1; int bossMove = -1, bossMoveIx; bool bossRage; float bossDashT, bossDmgK = 1, bossIdleT;

        /// <summary>开局时：Boss 座位空着就放一个电脑 Boss，角色随机。</summary>
        void PrepareBossSeat()
        {
            if (!BossMode) return;
            for (int i = 0; i < BossLives.Length; i++) BossLives[i] = BOSS_LIVES;
            var s = Slots[BOSS_SLOT];
            if (s.Mode == SlotMode.Off) { s.Mode = SlotMode.Cpu; s.Ch = Defs.Order[Rng.Next(Defs.Order.Length)]; s.Label = "Boss"; }
        }
        void MakeBoss(Player p)
        {
            int heroes = 0; foreach (var q in Slots) if (q.Mode != SlotMode.Off) heroes++; heroes = Math.Max(1, heroes - 1);
            float cx = W / 2f, by = p.Y + p.H;
            p.W = (float)Math.Round(p.C.W * BOSS_SCALE); p.H = (float)Math.Round(p.C.H * BOSS_SCALE);
            p.X = cx - p.W / 2; p.Y = Math.Max(0, by - p.H - 40);
            p.MaxHp = p.Hp = 400 + 560 * heroes; Unstick(p);
            bossDmgK = heroes <= 1 ? .75f : heroes == 2 ? .9f : 1; bossIdleT = 0;
            bossCd = 6; bossWarn = -1; bossMove = -1; bossMoveIx = Rng.Next(4); bossRage = false; bossDashT = 0;
        }
        /// <summary>Boss 受伤：击退只吃 25%，不会被打硬直。</summary>
        void BossTaken(Player t, Player src, ref float amt, ref float kx, ref float ky)
        {
            if (IsBoss(src)) amt *= bossDmgK;
            if (!IsBoss(t)) return; kx *= .25f; ky *= .25f; bossIdleT = 0; if (RoundT > 150) amt *= 1.5f; // 打太久：Boss 破防
        }

        void UpdateBoss(float dt)
        {
            if (!BossMode || State != GameState.Play) return;
            Player b = null; foreach (var p in Players) if (IsBoss(p)) b = p;
            if (b == null || !b.Alive) return;
            b.Stun = 0; b.Daze = 0;
            bossIdleT += dt;
            if (bossIdleT > 15) // 15 秒没人打到 Boss（可能卡在打不到的地方）：Boss 瞬移回地图中间上空
            {
                bossIdleT = 0; Ring(b.Cx, b.Cy, 10, 120, 0xff4a4au, .4f, 8);
                b.X = W / 2 - b.W / 2; b.Y = H * .15f; b.Vx = b.Vy = 0; Unstick(b); Text(b.Cx, b.Y - 40, "Boss 降临!", 0xff6a4au, true);
            }
            if (!bossRage && b.Hp < b.MaxHp * .4f)
            {
                bossRage = true; bossCd = Math.Min(bossCd, 1.5f); Text(b.Cx, b.Y - 60, "Boss 狂 暴 !", 0xff4a4au, true); Shake(14); Sound(Sfx.God, 2);
                Ring(b.Cx, b.Cy, 20, 420, 0xff4a4au, .6f, 14);
            }
            if (bossRage) { b.PowT = Math.Max(b.PowT, .2f); b.SpdT = Math.Max(b.SpdT, .2f); }
            if (RoundT > 180) // 打满 3 分钟：Boss 力竭持续掉血，一定能分出胜负
            {
                if (RoundT - dt <= 180) { Text(W / 2, 230, "Boss 力 竭 ! 持续掉血", 0xff6a4au, true); Shake(8); }
                b.Hp -= b.MaxHp * .015f * dt; if (b.Hp <= 0) { b.Hp = 0; Ko(b, "力竭!"); return; }
            }
            if (bossDashT > 0)
            {
                bossDashT -= dt; b.Vx = b.Face * 1500; b.Inv = Math.Max(b.Inv, .05f);
                foreach (var e in new List<Player>(Enemies(b))) if (!b.BumpHit.Contains(e) && Overlap(b.X - 10, b.Y, b.W + 20, b.H, e.X, e.Y, e.W, e.H)) { b.BumpHit.Add(e); Damage(e, bossRage ? 18 : 14, b, b.Face * 900, -420); }
                if (Rng.NextDouble() < .6) Burst(b.Cx - b.Face * b.W / 2, b.Cy, b.C.Color, 3, 200, 6);
            }
            if (bossWarn > 0)
            {
                bossWarn -= dt;
                if (bossMove == 0 && b.OnGround && bossWarn < .55f && bossWarn + dt >= .55f) { b.Vy = -900; b.OnGround = false; }
                if (bossWarn <= 0) DoBossMove(b);
                return;
            }
            bossCd -= dt * (bossRage ? 1.6f : 1);
            if (bossCd > 0) return;
            if (b.Kind == SlotMode.Human) { var bi = Inputs[b.Slot]; if (!(bi.D && bi.S)) return; } // 人当 Boss：↓ + 技能 放大招
            StartBossMove(b, (bossMoveIx++) % 4);
        }
        void StartBossMove(Player b, int m)
        {
            bossMove = m; bossWarn = m == 1 ? .4f : 1.1f; bossCd = Rnd(6, 8.5f);
            Text(b.Cx, b.Y - 50, BossMoveNames[m] + " !", 0xff6a4au, true); Sound(Sfx.Beep, 2);
            switch (m)
            {
                case 0: Ring(b.Cx, b.Y + b.H, 30, 620, 0xff4a4au, 1.1f, 6); break;
                case 1: foreach (var e in Enemies(b)) Ring(e.Cx, e.Y + e.H, 10, 70, 0xff8a3cu, 1.1f, 6); break;
                case 2: { var t = Nearest(b); if (t != null) b.Face = Sign(t.Cx - b.Cx == 0 ? 1 : t.Cx - b.Cx); Zap(b.Cx, b.Cy, b.Cx + b.Face * 900, b.Cy, 0xff4a4au, 2); break; }
                case 3: Ring(b.Cx, b.Cy, 520, 40, 0x9a7bffu, 1.1f, 8); break;
            }
        }
        void DoBossMove(Player b)
        {
            float k = bossRage ? 1.25f : 1;
            switch (bossMove)
            {
                case 0: // 震地：落地冲击波，地上 620 内的人都挨打并被震飞
                    Shake(22); Sound(Sfx.Slam, 1); Ring(b.Cx, b.Y + b.H, 20, 640, 0xff6a4au, .5f, 14); Burst(b.Cx, b.Y + b.H, 0xffb07a, 40, 600, 7); DestroyAt(b.Cx, b.Y + b.H, 40);
                    foreach (var e in new List<Player>(Enemies(b)))
                    {
                        float dx = e.Cx - b.Cx; if (Math.Abs(dx) < 620 && Math.Abs(e.Y + e.H - (b.Y + b.H)) < 140) { Damage(e, (float)Math.Round(16 * k), b, Sign(dx == 0 ? 1 : dx) * 500, -760); e.Stun = Math.Max(e.Stun, .4f); }
                    }
                    break;
                case 1: // 陨石雨：每个讨伐者头顶落 3 颗陨石，再加几颗随机的
                    foreach (var e in Enemies(b)) foreach (var o in new[] { -40f, 0, 40f }) Falls.Add(new Falling { X = Clamp(e.Cx + o, 20, W - 20), Y = Math.Max(-20, e.Y - 640), Warn = .9f, Meteor = true });
                    for (int i = 0; i < (bossRage ? 8 : 4); i++) Falls.Add(new Falling { X = Rnd(80, W - 80), Y = -20, Warn = .9f, Meteor = true });
                    break;
                case 2: // 冲锋：横冲直撞 0.6 秒
                    bossDashT = .6f; b.BumpHit.Clear(); Sound(Sfx.Heavy, 1); Shake(10); break;
                case 3: // 引力漩涡：把所有人吸到身边，然后爆开
                    foreach (var e in new List<Player>(Enemies(b)))
                    {
                        float dx = b.Cx - e.Cx, dy = b.Cy - e.Cy, d = Math.Max(1, Hypot(dx, dy));
                        if (d < 900) { e.Vx += dx / d * 900; e.Vy += dy / d * 700 - 200; }
                        if (d < 260) Damage(e, (float)Math.Round(12 * k), b, -dx / d * 700, -500);
                    }
                    Ring(b.Cx, b.Cy, 10, 300, 0x9a7bffu, .5f, 14); Burst(b.Cx, b.Cy, 0x9a7bffu, 40, 520, 7); Sound(Sfx.Hole, 1); Shake(12);
                    break;
            }
            bossMove = -1;
        }
    }
}
