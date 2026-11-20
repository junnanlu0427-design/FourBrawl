// 第五版大地图的机关：列车隧道 / 车厢脱钩、弹跳蘑菇、空间站低重力 + 气闸、锻炉传送带 + 液压锤。
using System;
using System.Collections.Generic;
using static Brawl.Core.Defs;

namespace Brawl.Core
{
    public sealed partial class Sim
    {
        public float HazT; public int HazPhase, HazSide = 1; // 机关计时、阶段（0 平静 1 预警 2 发动）、方向
        bool carDropped; readonly HashSet<Player> hazHit = new HashSet<Player>();
        public const int TRAIN_ROOF = 50; // 列车车顶所在行
        // 液压锤所在的列范围（锻炉）
        public static readonly int[][] Hammers = { new[] { 36, 44 }, new[] { 100, 108 } };
        public const int HAMMER_TOP = 12, HAMMER_BOTTOM = 54;

        void ResetMapHazards()
        {
            var m = Map; carDropped = false; hazHit.Clear(); HazPhase = 0; HazSide = 1;
            HazT = m.Hazard == MapHazard.Train ? 20 : m.Hazard == MapHazard.Space ? 20 : m.Hazard == MapHazard.Forge ? 8 : 0;
        }

        /// <summary>重力倍率（空间站的低重力舱）。</summary>
        float LocalGrav(Player p) { var m = Map; return m.LowGravX0 >= 0 && p.Cx >= m.LowGravX0 && p.Cx < m.LowGravX1 ? .55f : 1; }

        /// <summary>脚下的特殊格子：弹跳蘑菇、传送带。</summary>
        void FootTiles(Player p, float dt)
        {
            if (!p.OnGround) return;
            int a = TileAt(p.X + 3, p.Y + p.H + 2), b = TileAt(p.X + p.W - 3, p.Y + p.H + 2);
            if (a == 4 || b == 4)
            {
                p.Vy = -1380; p.OnGround = false; p.Jumps = 1; p.Coyote = 0; p.JumpCut = false;
                Burst(p.Cx, p.Y + p.H, 0xff5a6a, 10, 260, 5); Ring(p.Cx, p.Y + p.H, 6, 50, 0xffb0c0, .25f); Sound(Sfx.Jump, 6);
            }
            else if (a == 5 || b == 5 || a == 6 || b == 6)
            {
                int dir = (a == 5 || b == 5) ? 1 : -1;
                MoveBox(ref p.X, ref p.Y, p.W, p.H, dir * 170 * dt, 0);
            }
        }

        void UpdateMapHazards(float dt)
        {
            var m = Map;
            if (m.Hazard == MapHazard.Train)
            {
                if (!carDropped && RoundT >= 50)
                {   // 最后一节车厢脱钩
                    carDropped = true; int c0 = 4 + 5 * 29;
                    for (int r = TRAIN_ROOF; r <= 66; r++) for (int c = c0; c <= c0 + 26 && c < COLS; c++) Tiles[r * COLS + c] = 0;
                    TileVersion++; Text(W - 500, 820, "最后一节车厢脱钩了!", 0xff7a5a, true); Shake(10); Sound(Sfx.Boom, 12);
                    Burst((c0 + 13) * T, TRAIN_ROOF * T, m.Stone, 40, 400, 7);
                }
                HazT -= dt;
                if (HazPhase == 0 && HazT <= 3) { HazPhase = 1; Text(W / 2, 760, "隧道来了! 按住 ↓ 趴下或躲进车厢", 0xff5464, true); Sound(Sfx.Beep); }
                if (HazPhase == 1 && HazT <= 0) { HazPhase = 2; HazT = 1.1f; hazHit.Clear(); Shake(10); Sound(Sfx.Heavy, 14); }
                else if (HazPhase == 2)
                {
                    float roof = TRAIN_ROOF * T;
                    foreach (var p in Players)
                    {
                        if (!p.Alive || hazHit.Contains(p) || p.Ghost) continue;
                        bool duck = p.OnGround && Inputs[p.Slot].D;
                        if (p.Y + p.H <= roof + 4 && p.Y + p.H > roof - 6 * T && !duck)
                        { hazHit.Add(p); Damage(p, 17, null, -950, -380); Text(p.Cx, p.Y - 40, "撞上隧道!", 0xff5464, false); }
                    }
                    foreach (var b in Projs) if (b.Y < roof) b.Vx -= 400 * dt;
                    if (HazT <= 0) { HazPhase = 0; HazT = 20; }
                }
            }
            else if (m.Hazard == MapHazard.Space)
            {
                HazT -= dt;
                if (HazPhase == 0 && HazT <= 3) { HazPhase = 1; Text(W / 2, 360, HazSide < 0 ? "← 左侧气闸要开了!" : "右侧气闸要开了! →", 0x7ff3ff, true); Sound(Sfx.Beep); }
                if (HazPhase == 1 && HazT <= 0) { HazPhase = 2; HazT = 3; Shake(6); Sound(Sfx.Wave); }
                else if (HazPhase == 2)
                {
                    float doorX = HazSide < 0 ? 11 * T : 133 * T;
                    foreach (var p in Players)
                    {
                        if (!p.Alive || p.Ghost) continue;
                        float dx = Math.Abs(p.Cx - doorX);
                        if (dx < 820 && p.Cy > 40 * T && p.Cy < 67 * T) { float k = 1 - dx / 820; p.Vx += HazSide * (p.OnGround ? 900 : 1500) * k * dt * (p.GiantT > 0 ? .5f : 1); }
                    }
                    foreach (var b in Projs) if (Math.Abs(b.X - doorX) < 820) b.Vx += HazSide * 600 * dt;
                    if (Rng.NextDouble() < .5) Burst(doorX - HazSide * 40, Rnd(53, 65) * T, 0xd8ffff, 2, 300, 3);
                    if (HazT <= 0) { HazPhase = 0; HazT = 20; HazSide = -HazSide; }
                }
            }
            else if (m.Hazard == MapHazard.Forge)
            {
                HazT -= dt;
                if (HazPhase == 0 && HazT <= 1.2f) { HazPhase = 1; Sound(Sfx.Beep); }
                if (HazPhase == 1 && HazT <= 0)
                {
                    HazPhase = 2; HazT = .35f; Shake(12); Sound(Sfx.Slam, 16);
                    foreach (var hm in Hammers)
                    {
                        float x0 = hm[0] * T, x1 = (hm[1] + 1) * T, y0 = HAMMER_TOP * T, y1 = HAMMER_BOTTOM * T;
                        Burst((x0 + x1) / 2, y1, 0xff8a3c, 24, 380, 6);
                        foreach (var p in Players)
                            if (p.Alive && !p.Ghost && p.Cx > x0 - 6 && p.Cx < x1 + 6 && p.Cy > y0 && p.Cy < y1)
                            { Damage(p, 20, null, 0, 700); p.SlowT = Math.Max(p.SlowT, 2); Text(p.Cx, p.Y - 40, "压扁!", 0xff8a3c, true); }
                    }
                }
                else if (HazPhase == 2 && HazT <= 0) { HazPhase = 0; HazT = 8; }
            }
        }
    }
}
