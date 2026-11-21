// 第十三版：训练场。本地模式，测角色和伤害：自己和假人都不会死，不涨岩浆，回合不结束。
// 假人 = 训练场里的电脑槽位，三种模式：站桩 / 走动 / 反击（正常电脑）。
using System;
using System.Collections.Generic;

namespace Brawl.Core
{
    /// <summary>一个玩家在训练场里的输出统计。</summary>
    public sealed class TrainStat
    {
        public float Total, Last, ComboDmg, ComboT, BestCombo, Dps, FirstT = -1, LastT = -99; public int ComboN, BestComboN, Hits;
        public readonly List<float[]> Win = new List<float[]>(); // 最近 5 秒的 (时间, 伤害)
        public void Reset() { Total = Last = ComboDmg = ComboT = BestCombo = Dps = 0; FirstT = -1; LastT = -99; ComboN = BestComboN = Hits = 0; Win.Clear(); }
    }

    public partial class Sim
    {
        public static readonly string[] DummyModeNames = { "站桩", "走动", "反击" };
        public int TrDummyMode;            // 0 站桩 1 走动 2 反击
        public bool TrInfEnergy, TrSelfInv, TrDummyRegen = true;
        public readonly TrainStat[] TrStats = MakeStats();
        public readonly float[] TrHitT = new float[Defs.MAX_P];
        static TrainStat[] MakeStats() { var a = new TrainStat[Defs.MAX_P]; for (int i = 0; i < a.Length; i++) a[i] = new TrainStat(); return a; }
        public bool Training => Mode == GameMode.Training;
        const float COMBO_GAP = 1.2f, DPS_WIN = 5f;

        void TrRecord(Player t, Player src, float amt)
        {
            if (!Training || amt <= 0) return;
            TrHitT[t.Slot] = RoundT;
            if (src == null || src == t || src.Kind != SlotMode.Human) return;
            var s = TrStats[src.Slot];
            if (RoundT - s.LastT > COMBO_GAP) { s.ComboN = 0; s.ComboDmg = 0; }
            s.ComboN++; s.ComboDmg += amt; s.Total += amt; s.Last = amt; s.Hits++; s.LastT = RoundT; s.ComboT = COMBO_GAP;
            if (s.FirstT < 0) s.FirstT = RoundT;
            if (s.ComboDmg > s.BestCombo) s.BestCombo = s.ComboDmg; if (s.ComboN > s.BestComboN) s.BestComboN = s.ComboN;
            s.Win.Add(new[] { RoundT, amt });
            if (s.ComboN >= 2) Text(t.Cx, t.Y - 40, s.ComboN + " 连击  " + Math.Round(s.ComboDmg), 0xffe27au, false);
        }
        /// <summary>训练场里被打倒：原地满血复活（不算死）。</summary>
        bool TrSaveFromKo(Player t)
        {
            if (!Training) return false;
            t.Hp = t.MaxHp; t.Inv = Math.Max(t.Inv, .6f);
            Ring(t.Cx, t.Cy, 10, 90, 0x7dffb0u, .4f, 6); Text(t.Cx, t.Y - 56, "击倒! 已满血", 0x7dffb0u, true); Sound(Sfx.KO);
            return true;
        }
        void UpdateTraining(float dt)
        {
            if (!Training) return;
            foreach (var p in Players)
            {
                if (!p.Alive) continue;
                if (p.Kind == SlotMode.Human)
                {
                    if (TrSelfInv) p.Hp = p.MaxHp;
                    if (TrInfEnergy && !p.God && !p.Divine) p.Energy = 100;
                }
                else if (TrDummyRegen && RoundT - TrHitT[p.Slot] > 2.5f && p.Hp < p.MaxHp) { p.Hp = Math.Min(p.MaxHp, p.Hp + p.MaxHp * 2 * dt); }
            }
            foreach (var s in TrStats)
            {
                s.Win.RemoveAll(w => RoundT - w[0] > DPS_WIN);
                float sum = 0; foreach (var w in s.Win) sum += w[1];
                float span = s.Win.Count > 0 ? Math.Max(1, Math.Min(DPS_WIN, RoundT - s.Win[0][0] + .5f)) : 1;
                s.Dps = s.Win.Count > 0 ? sum / span : 0;
                if (s.ComboT > 0) s.ComboT -= dt;
            }
        }
        /// <summary>假人的行为；返回 true 表示已经决定好输入（站桩/走动），false 交给正常电脑。</summary>
        bool TrDummyThink(Player p, out PInput inp)
        {
            inp = default;
            if (!Training || TrDummyMode == 2) return false;
            if (TrDummyMode == 0) return true;
            var ai = p.Ai; ai.T -= 1f / 60;
            if (ai.T <= 0) { ai.T = Rnd(.8f, 1.8f); ai.Jit = Rng.Next(2) == 0 ? -1 : 1; }
            if (p.X < 80) ai.Jit = 1; else if (p.X > W - 80 - p.W) ai.Jit = -1;
            inp = new PInput { L = ai.Jit < 0, R = ai.Jit > 0, U = Rng.NextDouble() < .01 };
            return true;
        }

        // ---------- 训练场按钮（只在本地调用） ----------
        public void TrResetCooldowns()
        {
            foreach (var p in Players) if (p.Kind == SlotMode.Human) { p.AtkCd = 0; p.SkCd = 0; p.V9Cd = 0; p.V9SkCd = 0; }
            Text(W / 2, 200, "冷却已重置", 0x7dffb0u, false);
        }
        public void TrResetForm()
        {
            foreach (var p in Players)
            {
                if (p.Kind != SlotMode.Human) continue;
                p.God = false; p.Divine = false; p.Energy = 0; p.Mult = 1; p.Day = 0; p.Hp = p.MaxHp; p.AtkCd = p.SkCd = p.V9Cd = p.V9SkCd = 0; p.Ult = false;
            }
            if (TimeStopT > 0) { TimeStopT = 0; TimeStopper = null; }
            Text(W / 2, 200, "形态已重置", 0x7dffb0u, false);
        }
        public void TrResetStats() { foreach (var s in TrStats) s.Reset(); Text(W / 2, 200, "统计已清零", 0x7dffb0u, false); }
        public void TrGiveUlt() { foreach (var p in Players) if (p.Kind == SlotMode.Human && p.Alive) p.Ult = true; Text(W / 2, 200, "终极球已给你", 0xfff2a8u, false); }
    }
}
