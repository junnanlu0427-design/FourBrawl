// 第六版新角色的画面：场上机关（Sim.Gadgets / 绊索）、新子弹（Pellet / Spore / Push）、角色身上的新状态。
// 在 WorldView.DrawWorld 里调用 NewCharView.Draw(sp, Sim, t)：建议放在“角色 + 分身”之后、“子弹”之前。
// 风格：华丽但不刺眼——发光层透明度都压在 0.5 以下，所有脉动都是缓慢的正弦，不做全屏闪白、不做频闪。
using System.Collections.Generic;
using UnityEngine;
using Brawl.Core;

namespace Brawl.View
{
    public static class NewCharView
    {
        /// <summary>联机时“我”的槽位（-1 = 本地同屏）。只有主人能看清 gary-改 的隐形地钉。</summary>
        public static int LocalSlot = -1;

        static SpritePool sp; static Sim S; static float t;
        static readonly Color PxGreen = Art.C(0x8fd14f), PolRed = Art.C(0xff4d5e), PolBlue = Art.C(0x4d8bff), Teal = Art.C(0x3cbf9a), Moss = Art.C(0x6bd94a), Cream = Art.C(0xfff4c8), WhiteBlue = Art.C(0xe8f4ff);

        static SpriteRenderer Put(Sprite s, float x, float y, float w, float h, Color c, int order, float rot = 0, bool add = false) => sp.Put(s, x, y, w, h, c, order, rot, add);
        static Color A(Color c, float a) { c.a = a; return c; }
        static float Hash01(float a, float b) => (Art.Hash((uint)(int)(a * 7.3f + 10000), (uint)(int)(b * 3.1f + 10000)) & 1023) / 1023f;
        static void Line(float x0, float y0, float x1, float y1, float wd, Color c, int order, bool add = true)
        {
            float dx = x1 - x0, dy = y1 - y0, len = Mathf.Sqrt(dx * dx + dy * dy); if (len < .5f) return;
            Put(Art.Pixel, (x0 + x1) / 2, (y0 + y1) / 2, len, wd, c, order, -Mathf.Atan2(dy, dx) * Mathf.Rad2Deg, add);
        }

        public static void Draw(SpritePool pool, Sim sim, float time)
        {
            if (pool == null || sim == null) return;
            NewCharArt.Build(); // 正常已在 Art.Init 里调用过；这里兜底
            sp = pool; S = sim; t = time;
            foreach (var g in sim.Gadgets) if (!g.Dead) DrawGadget(g);
            foreach (var p in sim.Players) if (p.Alive && p.Ch == CharId.GARY2) DrawWires(p);
            foreach (var b in sim.Projs) DrawProj(b);
            foreach (var c in sim.Clones) if (c.Owner != null && c.Owner.Alive) DrawClone(c);
            foreach (var p in sim.Players) if (p.Alive) DrawStates(p);
        }

        // ======================= 机关 =======================
        static void DrawGadget(Gadget g)
        {
            var o = g.Owner; float age = Mathf.Max(0, S.RoundT - g.Born), fade = Mathf.Clamp01(g.T / .6f);
            switch (g.Kind)
            {
                case GKind.Mine:
                    {   // px 地雷：铁壳 + 指示灯，布防前绿灯慢闪，布防后橙红灯越闪越快（缓慢正弦，不是频闪）
                        bool armed = g.Arm <= 0; float rate = armed ? 2.5f + Mathf.Min(age, 4) * 1.2f : 1.5f;
                        float k = .5f + .5f * Mathf.Sin(t * rate * Mathf.PI * 2);
                        var lc = armed ? Art.C(0xff7a3c) : PxGreen;
                        Put(Art.Soft, g.X, g.Y, 34, 8, new Color(0, 0, 0, .35f), 5);
                        Put(NewCharArt.Mine, g.X, g.Y - 6, 26, 13, Color.white, 6);
                        Put(Art.Soft, g.X, g.Y - 12, 22 + 8 * k, 22 + 8 * k, A(lc, .22f + .2f * k), 7, 0, true);
                        Put(Art.Dot, g.X, g.Y - 12, 5, 5, Color.Lerp(lc, Color.white, .35f + .3f * k), 8);
                        if (armed) Put(Art.RingThin, g.X, g.Y - 6, g.R * 2, g.R * .7f, A(lc, .12f + .1f * k), 5, 0, true); // 触发范围
                        else { float u = Mathf.Clamp01(age / .6f); Put(Art.RingThin, g.X, g.Y - 6, 10 + u * 40, 4 + u * 12, A(PxGreen, .4f * (1 - u)), 5, 0, true); }
                        break;
                    }
                case GKind.Spike:
                    {   // gary-改 隐形地钉：主人看得清（青色描边），其他人只看到偶尔一闪的微光
                        bool mine = LocalSlot >= 0 && o != null && o.Slot == LocalSlot;
                        float glint = Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * 1.3f + g.X * .05f)), 24);
                        float a = mine ? .85f : (LocalSlot < 0 ? .16f : .05f) + glint * .3f;
                        if (g.Arm > 0) a = Mathf.Max(a, .6f * Mathf.Clamp01(g.Arm / .5f)); // 埋下去的过程大家都看得到一点
                        if (mine) Put(Art.Soft, g.X, g.Y - 4, 34, 16, A(Teal, .25f), 5, 0, true);
                        Put(NewCharArt.Spike, g.X, g.Y - 6, 22, 14, A(Color.Lerp(Teal, Color.white, .3f), a), 6);
                        if (glint > .05f) Put(Art.Star4, g.X + 4, g.Y - 10, 8 * glint + 2, 8 * glint + 2, new Color(1, 1, 1, .5f * glint), 7, 0, true);
                        break;
                    }
                case GKind.Anchor:
                    {   // 锚点：发绿光的小钉
                        float k = .5f + .5f * Mathf.Sin(t * 3 + g.X * .03f);
                        Put(Art.Soft, g.X, g.Y, 30 + 6 * k, 30 + 6 * k, A(Teal, .28f + .12f * k), 7, 0, true);
                        Put(NewCharArt.Anchor, g.X, g.Y, 11, 11, Color.Lerp(Teal, Color.white, .25f), 8, t * 40);
                        Put(Art.Dot, g.X, g.Y, 4, 4, new Color(.85f, 1, .95f, .9f), 9, 0, true);
                        break;
                    }
                case GKind.StuckDisc:
                    {   // DVD-改 插在地上的碟片：斜插 + 一圈“可以捡”的光圈 + 小箭头
                        var c = Art.C(0x9a7bff); float k = .5f + .5f * Mathf.Sin(t * 4 + g.X);
                        Put(Art.Soft, g.X, g.Y, 50, 50, A(c, .22f + .1f * k), 6, 0, true);
                        Put(Art.Disc, g.X, g.Y, 26, 26, Color.HSVToRGB(Mathf.Repeat(.72f + .05f * Mathf.Sin(t), 1), .35f, 1), 7, 25 + Mathf.Sin(t * 2 + g.X) * 4);
                        Put(Art.RingThin, g.X, g.Y, 40 + 6 * k, 40 + 6 * k, A(c, .35f), 7, 0, true);
                        if (o != null && LocalSlot == o.Slot || LocalSlot < 0) Put(Art.Arrow, g.X, g.Y - 26 - 3 * k, 10, 8, A(Color.Lerp(c, Color.white, .5f), .75f), 8, 180);
                        break;
                    }
                case GKind.BigDisc:
                    {   // 巨型屏保：彩虹残影 + 大碟 + 外圈
                        float hue = Mathf.Repeat(t * .25f + g.X * .0006f, 1);
                        float sp2 = Mathf.Max(1, Mathf.Sqrt(g.Vx * g.Vx + g.Vy * g.Vy));
                        for (int i = 4; i >= 1; i--)
                        {
                            float k = i / 5f, tx = g.X - g.Vx / sp2 * i * 22, ty = g.Y - g.Vy / sp2 * i * 22;
                            Put(Art.Soft, tx, ty, g.R * 2.6f * (1 - k * .3f), g.R * 2.6f * (1 - k * .3f), A(Color.HSVToRGB(Mathf.Repeat(hue - i * .08f, 1), .6f, 1), .16f * (1 - k)), 13, 0, true);
                        }
                        Put(Art.Soft, g.X, g.Y, g.R * 3.4f, g.R * 3.4f, A(Color.HSVToRGB(hue, .55f, 1), .3f), 13, 0, true);
                        Put(Art.Disc, g.X, g.Y, g.R * 2.2f, g.R * 2.2f, Color.HSVToRGB(hue, .3f, 1), 14, -t * 360);
                        Put(Art.Ring, g.X, g.Y, g.R * 2.5f, g.R * 2.5f, A(Color.HSVToRGB(Mathf.Repeat(hue + .5f, 1), .45f, 1), .45f), 15, t * 90, true);
                        for (int i = 0; i < 4; i++) { float a = t * 2 + i * Mathf.PI / 2; Put(Art.Star4, g.X + Mathf.Cos(a) * g.R * 1.3f, g.Y + Mathf.Sin(a) * g.R * 1.3f, 10, 10, new Color(1, 1, 1, .6f), 15, t * 120, true); }
                        break;
                    }
                case GKind.Patch:
                    {   // dzy-改 菌毯：贴着地面的发光绿色涂层，微微起伏，边上冒小泡泡
                        float a = Mathf.Clamp01(g.T / 1.2f) * Mathf.Clamp01(age / .25f + .2f); if (a <= 0) break;
                        float wob = Mathf.Sin(t * 2.2f + g.X * .04f);
                        Put(Art.Soft, g.X, g.Y - 2, g.R * 2.3f, 22, A(Moss, (.16f + .05f * wob) * a), -3, 0, true);
                        Put(Art.Dot, g.X, g.Y + 1, g.R * 2.05f, 9 + wob, A(Art.C(0x2f7a24), .8f * a), -2);
                        Put(Art.Dot, g.X, g.Y, g.R * 1.85f, 6 + wob * .6f, A(Art.C(0x7fe85a), .75f * a), -1);
                        for (int i = 0; i < 3; i++)
                        {
                            float h = Hash01(g.X, g.Y + i * 17), u = Mathf.Repeat(t * (.5f + h * .4f) + h, 1);
                            float bx = g.X + (h * 2 - 1) * g.R * .85f, by = g.Y - 3 - u * 10, s = 3 + 3 * Mathf.Sin(u * Mathf.PI);
                            Put(Art.RingThin, bx, by, s, s, A(Art.C(0xc8ff9a), .55f * a * (1 - u)), 0, 0, true);
                        }
                        break;
                    }
                case GKind.WaterWall:
                    {   // 分开诸水：一堵竖直流动的水墙，朝 Dir 方向推
                        float a = Mathf.Clamp01(g.T / .4f) * Mathf.Clamp01(age / .2f + .1f), H = g.R * 2; var wc = Art.C(0x7fd0ff);
                        Put(Art.Soft, g.X, g.Y, 260, H * 1.15f, A(wc, .3f * a), 16, 0, true);
                        Put(Art.Pixel, g.X, g.Y, 92, H, A(Art.C(0x3a8ad0), .55f * a), 17);
                        Put(Art.Pixel, g.X + g.Dir * 26, g.Y, 34, H, A(Art.C(0xbfeaff), .4f * a), 17, 0, true);
                        Put(Art.Soft, g.X + g.Dir * 40, g.Y - H / 2, 140, 60, A(Color.white, .45f * a), 18, 0, true); // 浪尖白沫
                        for (int i = 0; i < 9; i++)
                        {
                            float h = Hash01(i, g.X), u = Mathf.Repeat(t * (.9f + h * .5f) + h, 1);
                            Put(Art.Spark, g.X + (h - .5f) * 36, g.Y + H / 2 - u * H, 30, 4, A(Color.white, .35f * a * Mathf.Sin(u * Mathf.PI)), 18, 90, true);
                        }
                        for (int i = 0; i < 5; i++) Put(Art.Dot, g.X - 20 + i * 10, g.Y - H / 2 + Mathf.Sin(t * 5 + i) * 3, 12, 9, A(Color.white, .7f * a), 18);
                        for (int i = 0; i < 3; i++) { float u = Mathf.Repeat(t * 1.2f + i / 3f, 1); Put(Art.Wave, g.X + g.Dir * (24 + u * 50), g.Y, 18, H * .5f, A(wc, .35f * a * (1 - u)), 18, g.Dir > 0 ? 0 : 180, true); }
                        break;
                    }
                case GKind.Sun:
                    {   // 日月：会追人的小太阳。范围圈很淡，本体温暖不刺眼
                        float a = Mathf.Clamp01(g.T / .5f), k = .5f + .5f * Mathf.Sin(t * 2.4f);
                        Put(Art.Soft, g.X, g.Y, g.R * 2, g.R * 2, A(Art.C(0xffa030), .12f * a), 16, 0, true);
                        Put(Art.RingThin, g.X, g.Y, g.R * 2, g.R * 2, A(Art.C(0xffc860), .16f * a), 16, t * 10, true);
                        for (int i = 0; i < 10; i++) { float ang = t * .6f + i * Mathf.PI / 5; Put(NewCharArt.Petal, g.X + Mathf.Cos(ang) * 30, g.Y + Mathf.Sin(ang) * 30, 9, 22 + 4 * k, A(Art.C(0xffd060), .45f * a), 17, -ang * Mathf.Rad2Deg + 90, true); }
                        float sz = Mathf.Max(30, g.R * .45f);
                        Put(Art.Soft, g.X, g.Y, sz * 2.4f + 12 * k, sz * 2.4f + 12 * k, A(Art.C(0xffc040), .45f * a), 17, 0, true);
                        for (int i = 0; i < 16; i++) { float ang = -t * .3f + i * Mathf.PI / 8; Put(Art.Spark, g.X + Mathf.Cos(ang) * sz * .95f, g.Y + Mathf.Sin(ang) * sz * .95f, sz * .7f, 6, A(Art.C(0xffb040), .4f * a), 17, -ang * Mathf.Rad2Deg, true); }
                        Put(Art.Dot, g.X, g.Y, sz, sz, A(Art.C(0xffe08a), a), 18);
                        Put(Art.Dot, g.X - sz * .12f, g.Y - sz * .12f, sz * .45f, sz * .45f, A(Art.C(0xfff8e0), .9f * a), 19);
                        break;
                    }
                case GKind.WhiteHole:
                    {   // 事件视界：白色带冷蓝边的光环，内部亮色螺旋向外流（与黑洞的暗色旋涡做镜像区分）
                        float a = Mathf.Clamp01(age / .25f) * Mathf.Clamp01(g.T / .3f + .3f), R = g.R;
                        Put(Art.Soft, g.X, g.Y, R * 2.3f, R * 2.3f, A(Art.C(0xbfe2ff), .2f * a), -6, 0, true);
                        for (int arm = 0; arm < 3; arm++)
                            for (int i = 0; i < 7; i++)
                            {
                                float u = Mathf.Repeat(t * .45f + i / 7f, 1), rr = 18 + u * R * .9f, ang = -t * 1.4f + arm * Mathf.PI * 2 / 3 + u * 2.4f;
                                Put(NewCharArt.Petal, g.X + Mathf.Cos(ang) * rr, g.Y + Mathf.Sin(ang) * rr, 8 + u * 8, 18 + u * 18, A(Color.Lerp(Color.white, WhiteBlue, u), .5f * a * Mathf.Sin(u * Mathf.PI)), -5, -ang * Mathf.Rad2Deg, true);
                            }
                        float wv = Mathf.Repeat(t * .7f, 1);
                        Put(Art.RingThin, g.X, g.Y, R * 2 * (.55f + .5f * wv), R * 2 * (.55f + .5f * wv), A(WhiteBlue, .35f * a * (1 - wv)), -5, 0, true);
                        Put(Art.Ring, g.X, g.Y, R * 2, R * 2, A(Art.C(0x9fd0ff), .38f * a + .06f * Mathf.Sin(t * 3)), -4, t * 20, true);
                        Put(Art.RingThin, g.X, g.Y, R * 1.9f, R * 1.9f, A(Color.white, .5f * a), -4, -t * 30, true);
                        Put(Art.Soft, g.X, g.Y, 90, 90, A(Color.white, .45f * a), -4, 0, true);
                        Put(Art.Dot, g.X, g.Y, 30, 30, A(new Color(.96f, .99f, 1), .95f * a), -3);
                        break;
                    }
                case GKind.Singularity:
                    {   // bw 奇点：不动的紫黑小漩涡，外圈是拉人的范围
                        var pc = Art.C(0x7b2cff);
                        Put(Art.Soft, g.X, g.Y, g.R * 2, g.R * 2, A(pc, .14f), 16, 0, true);
                        Put(Art.RingThin, g.X, g.Y, g.R * 2, g.R * 2, A(Art.C(0xc77dff), .3f), 16, t * 40, true);
                        Put(Art.Swirl, g.X, g.Y, 90, 90, A(Art.C(0xc77dff), .45f), 17, -t * 260, true);
                        Put(Art.HoleSpr, g.X, g.Y, 44, 44, Color.white, 18, t * 200);
                        break;
                    }
                case GKind.Vine:
                    {   // 大地：一丛从地面长出来的藤蔓，缠人的地方会把人卡住
                        float grow = Mathf.Clamp01(age / .35f) * Mathf.Clamp01(g.T / .5f);
                        Put(Art.Soft, g.X, g.Y, g.R * 2.2f, 14, new Color(0, 0, 0, .3f), 9);
                        for (int s = 0; s < 5; s++)
                        {
                            float bx = g.X - g.R * .8f + s * g.R * .4f, hgt = (28 + Hash01(g.X, s) * 22) * grow, ph = s * 1.3f + g.X * .01f; float lx = bx, ly = g.Y;
                            for (int q = 1; q <= 6; q++)
                            {
                                float u = q / 6f, x = bx + Mathf.Sin(t * 1.8f + ph + u * 3) * 6 * u, y = g.Y - hgt * u;
                                Line(lx, ly, x, y, 4.5f - u * 2, Art.C(0x2f8a3a), 10, false); lx = x; ly = y;
                                if (q % 2 == 0) Put(NewCharArt.Leaf, x + (q % 4 == 0 ? 6 : -6), y, 12, 6, Color.white, 11, q % 4 == 0 ? 25 : 155);
                            }
                            Put(Art.Dot, lx, ly, 5, 5, Art.C(0xc8ff9a), 11);
                        }
                        break;
                    }
            }
        }

        // gary-改：锚点之间的绊索——细细的绿光线，一颗光点沿线来回走，让对手看得见
        static void DrawWires(Player o)
        {
            List<System.Tuple<Gadget, Gadget>> ws; try { ws = S.Wires(o); } catch { return; }
            for (int i = 0; i < ws.Count; i++)
            {
                var a = ws[i].Item1; var b = ws[i].Item2; float k = .5f + .5f * Mathf.Sin(t * 2.5f + i);
                Line(a.X, a.Y, b.X, b.Y, 7, A(Teal, .14f + .06f * k), 9);
                Line(a.X, a.Y, b.X, b.Y, 1.8f, A(Color.Lerp(Teal, Color.white, .45f), .85f), 10);
                float u = .5f + .5f * Mathf.Sin(t * 1.6f + i * 2.1f);
                Put(Art.Star4, Mathf.Lerp(a.X, b.X, u), Mathf.Lerp(a.Y, b.Y, u), 10, 10, A(Color.Lerp(Teal, Color.white, .6f), .7f), 11, t * 160, true);
            }
        }

        // ======================= 子弹 =======================
        static void DrawProj(Proj b)
        {
            var c = Art.C(b.Color); float spd = Mathf.Max(1, Mathf.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy)), ang = Mathf.Atan2(-b.Vy, b.Vx) * Mathf.Rad2Deg;
            switch (b.Kind)
            {
                case ProjKind.Pellet:
                    {   // 霰弹弹丸：短亮拖尾 + 暖色弹头
                        var pc = Art.C(0xffd27a);
                        Put(Art.Spark, b.X - b.Vx / spd * 9, b.Y - b.Vy / spd * 9, 22, 4, A(pc, .45f), 13, ang, true);
                        Put(Art.Soft, b.X, b.Y, 14, 14, A(Art.C(0xffa040), .35f), 14, 0, true);
                        Put(Art.Dot, b.X, b.Y, 5, 5, Art.C(0xfff2c8), 15);
                        break;
                    }
                case ProjKind.Spore:
                    {   // 孢子团：滚动的绿团 + 几颗落在后面的孢子粉
                        for (int i = 1; i <= 3; i++) Put(Art.Dot, b.X - b.Vx * .02f * i, b.Y - b.Vy * .02f * i, 6 - i, 6 - i, A(Art.C(0xb8ff8a), .5f - i * .12f), 13, 0, true);
                        Put(Art.Soft, b.X, b.Y, b.R * 4, b.R * 4, A(Moss, .3f), 14, 0, true);
                        Put(NewCharArt.Spore, b.X, b.Y, b.R * 2.4f, b.R * 2.4f, Color.white, 15, -t * 220);
                        break;
                    }
                case ProjKind.Push:
                    {   // 斥力波：白色弧形，后面跟两层冷蓝涟漪
                        float a = b.Ang * Mathf.Rad2Deg, hh = b.Hh > 0 ? b.Hh : 34, fade = Mathf.Clamp01(b.Life / .25f);
                        Put(Art.Soft, b.X, b.Y, 90, hh * 3, A(Art.C(0xbfe2ff), .3f * fade), 13, -a, true);
                        for (int i = 2; i >= 1; i--)
                            Put(Art.Wave, b.X - Mathf.Cos(b.Ang) * 14 * i, b.Y - Mathf.Sin(b.Ang) * 14 * i, 26 - i * 4, hh * (2.4f - i * .35f), A(Art.C(0x9fd0ff), (.4f - i * .1f) * fade), 14, -a, true);
                        Put(Art.Wave, b.X, b.Y, 32, hh * 2.4f, A(Color.white, .95f * fade), 15, -a, true);
                        break;
                    }
                case ProjKind.Disc:
                    if (b.Remix)
                    {   // DVD-改 弹射碟：在原有碟片画法之外加一段彩虹拖尾（每次反弹颜色会随位置变化）
                        for (int i = 1; i <= 4; i++)
                        {
                            float k = i / 5f; var hc = Color.HSVToRGB(Mathf.Repeat(t * .5f + b.X * .002f - i * .07f, 1), .55f, 1);
                            Put(Art.Soft, b.X - b.Vx / spd * i * 12, b.Y - b.Vy / spd * i * 12, b.R * 3 * (1 - k * .4f), b.R * 3 * (1 - k * .4f), A(hc, .3f * (1 - k)), 13, 0, true);
                        }
                    }
                    break;
            }
            // kai-改 极性弹：子弹上叠一个小号＋/－
            if (b.Pol != 0)
            {
                var pc = b.Pol > 0 ? PolRed : PolBlue;
                Put(Art.Soft, b.X, b.Y, b.R * 4.5f + 8, b.R * 4.5f + 8, A(pc, .3f), 16, 0, true);
                Put(b.Pol > 0 ? NewCharArt.Plus : NewCharArt.Minus, b.X, b.Y, 12, 12, Color.Lerp(pc, Color.white, .55f), 17);
            }
        }

        // ======================= 分身 =======================
        static void DrawClone(Clone c)
        {
            var o = c.Owner; float cx = c.X + c.W / 2, cy = c.Y + c.H / 2; int f = c.Face == 0 ? 1 : c.Face;
            switch (c.Kind)
            {
                case 1: // px 假身：和真身一模一样，连手里的枪和钩子层数也画
                    DrawPxGear(o, cx, cy, c.W, c.H, f, 24);
                    break;
                case 2: // ccld-改 泥人：身上糊一层土色，脚下掉泥点
                    {
                        var white = Art.BodyWhite[(int)o.Ch]; if (white == null) break;
                        float grow = o.C != null && o.C.W > 0 ? c.W / o.C.W : 1; var bs = white.bounds.size * grow;
                        var r = Put(white, cx, c.Y + c.H - bs.y / 2, bs.x, bs.y, new Color(.6f, .42f, .24f, .6f), 26); r.flipX = f < 0;
                        for (int i = 0; i < 3; i++) { float u = Mathf.Repeat(t * .8f + i / 3f, 1); Put(Art.Dot, cx + (i - 1) * 8, c.Y + c.H * (.4f + u * .6f), 4, 4, new Color(.45f, .3f, .16f, .8f * (1 - u)), 26); }
                        break;
                    }
                case 3: // dzy-改 小菌怪：小绿团 + 两颗獠牙 + 一只眼
                    {
                        float bob = Mathf.Abs(Mathf.Sin(t * 9 + c.X * .1f)) * 3, by = c.Y + c.H - 12 - bob;
                        Put(Art.Soft, cx, c.Y + c.H, 30, 8, new Color(0, 0, 0, .35f), 25);
                        Put(Art.Soft, cx, by, 44, 44, A(Moss, .25f), 25, 0, true);
                        Put(NewCharArt.Spore, cx, by, 26, 24, Color.white, 26, Mathf.Sin(t * 6) * 8);
                        Put(Art.Dot, cx + f * 4, by - 3, 8, 8, Color.white, 27); Put(Art.Dot, cx + f * 5, by - 3, 4, 4, new Color(.05f, .05f, .1f), 28);
                        Put(NewCharArt.Fang, cx + f * 2, by + 6, 5, 6, Color.white, 28); Put(NewCharArt.Fang, cx + f * 8, by + 6, 5, 6, Color.white, 28);
                        break;
                    }
            }
        }

        // px 的手持霰弹枪 + 钩子层数（真身和假身共用）
        static void DrawPxGear(Player p, float cx, float cy, float w, float h, int f, int order)
        {
            float recoil = p.Swing > 0 ? 4 : 0;
            var r = Put(NewCharArt.Shotgun, cx + f * (w / 2 + 6 - recoil), cy + 6, 26, 9, Color.white, order); r.flipX = f < 0;
            float charge = Mathf.Clamp01((S.RoundT - p.LastAtkT - .9f) / 1.6f); // 越久没开枪越疼：枪口慢慢亮起来
            if (p.LastAtkT > -8 && charge > .05f) Put(Art.Soft, cx + f * (w / 2 + 20), cy + 4, 10 + 12 * charge, 10 + 12 * charge, A(Art.C(0xffb04a), .15f + .25f * charge), order + 1, 0, true);
            int n = Mathf.Clamp(p.HookStk, 0, 3);
            for (int i = 0; i < 3; i++)
            {
                bool on = i < n; float x = cx - 10 + i * 10, y = p.Y - 6 + (cy - p.Cy);
                Put(NewCharArt.Hook, x, y, 9, 9, on ? Color.white : new Color(1, 1, 1, .22f), order + 1);
            }
            if (n >= 3) Put(Art.RingThin, cx, cy, h * 1.9f, h * 1.9f, A(PxGreen, .35f + .12f * Mathf.Sin(t * 4)), order, t * 60, true);
        }

        // ======================= 角色状态 =======================
        static void DrawStates(Player p)
        {
            float w = p.W, h = p.H, cx = p.Cx, cy = p.Cy, foot = p.Y + p.H; int f = p.Face == 0 ? 1 : p.Face;
            // dzy-改 潜菌：人看不见，只剩地面下一道鼓起的波纹在前进
            if (p.Ch == CharId.DZY2 && p.Hidden)
            {
                Put(Art.Soft, cx, foot, 90, 18, A(Moss, .28f), 9, 0, true);
                Put(Art.Dot, cx, foot - 2, 38, 10 + Mathf.Sin(t * 10) * 2, A(Art.C(0x2f7a24), .85f), 10);
                Put(Art.Dot, cx, foot - 4, 26, 6 + Mathf.Sin(t * 10) * 1.5f, A(Art.C(0x8fe86a), .8f), 11);
                for (int i = 0; i < 3; i++) { float u = Mathf.Repeat(t * 1.4f + i / 3f, 1); Put(Art.RingThin, cx - Mathf.Sign(p.Vx + .01f) * u * 30, foot - 1, 30 + u * 60, 6 + u * 8, A(Moss, .45f * (1 - u)), 9, 0, true); }
                return;
            }
            if (p.Hidden) return; // 被 xp-改 吞进肚子里

            // xp-改 肚子里含着人：肚子透出被吞者的颜色
            if (p.Ch == CharId.XP2 && (p.Held != null || p.XVal == 1))
            {
                var hc = p.Held != null ? Art.C(p.Held.C.Color) : Art.C(0xffffff);
                float wob = Mathf.Sin(t * 9) * 3;
                Put(Art.Soft, cx + f * 4 + wob, cy + 4, w * 1.4f, h * 1.2f, A(hc, .35f), 36, 0, true);
            }
            // xp-改 滚球：粉色的球滚起来，身后速度线，贴地时擦出火花
            if (p.BallT > 0)
            {
                float R = Mathf.Max(w, h) * 1.25f, roll = -p.X * 2.2f; var pk = Art.C(0xffb3d1);
                float dir = Mathf.Sign(p.Vx + .01f);
                for (int i = 0; i < 4; i++) { float u = Mathf.Repeat(t * 3 + i * .25f, 1); Put(Art.Spark, cx - dir * (R * .6f + u * 40), cy - R * .3f + i * R * .2f, 30, 4, A(Color.white, .35f * (1 - u)), 36, 0, true); }
                Put(Art.Dot, cx, cy, R + 6, R + 6, new Color(.02f, .02f, .06f, 1), 36);
                Put(Art.Dot, cx, cy, R, R, pk, 37);
                for (int i = 0; i < 3; i++) Put(Art.Pixel, cx, cy, R * .9f, 4, A(Art.C(0xff7aa8), .8f), 37, roll + i * 60);
                Put(Art.Dot, cx - R * .2f, cy - R * .22f, R * .3f, R * .18f, new Color(1, 1, 1, .6f), 37);
                if (p.OnGround) for (int i = 0; i < 2; i++) { float u = Mathf.Repeat(t * 5 + i * .5f, 1); Put(Art.Star4, cx - dir * u * 26, foot - 2 - u * 8, 8 * (1 - u) + 2, 8 * (1 - u) + 2, A(Art.C(0xffe27a), .7f * (1 - u)), 37, 0, true); }
            }
            // ccld-改 神化：奶白金色光晕 + 头顶光环 + 身后半圈「七日」光点（当前这一天最亮）+ 滑翔时展开光羽
            if (p.Divine)
            {   // 神化：头顶通天光柱、身后巨大的金色日轮和七日星盘、三对光翼、双层光环——一眼看出是神
                float k = .5f + .5f * Mathf.Sin(t * 2), sc = 1.35f, top = p.Y + p.H - p.H * sc;
                var gold = Art.C(0xffd84a);
                // 通天光柱（从画面顶上一直照到身上）
                float colH = Mathf.Max(200, top + 40);
                Put(Art.Pixel, cx, top + 20 - colH / 2, 46 + 8 * k, colH, A(Cream, .16f), 4, 0, true);
                Put(Art.Pixel, cx, top + 20 - colH / 2, 16, colH, A(Color.white, .22f), 4, 0, true);
                for (int i = 0; i < 6; i++) { float u = Mathf.Repeat(t * .5f + i / 6f, 1); Put(Art.Dot, cx + Mathf.Sin(i * 2.1f + t) * 18, top - u * Mathf.Min(colH, 600), 5, 5, A(Color.white, .5f * (1 - u)), 5, 0, true); }
                // 巨大日轮
                Put(Art.Soft, cx, cy - 6, 420 + 30 * k, 420 + 30 * k, A(gold, .2f), 6, 0, true);
                Put(Art.Soft, cx, cy - 6, 240 + 16 * k, 240 + 16 * k, A(Cream, .3f), 6, 0, true);
                Put(Art.RingThin, cx, cy - 6, 300, 300, A(gold, .45f), 7, t * 12, true);
                Put(Art.RingThin, cx, cy - 6, 230, 230, A(Cream, .35f), 7, -t * 20, true);
                for (int i = 0; i < 12; i++) { float a = t * .4f + i * Mathf.PI / 6; Put(Art.Spark, cx + Mathf.Cos(a) * 175, cy - 6 + Mathf.Sin(a) * 175, 46, 5, A(gold, .35f), 6, -a * Mathf.Rad2Deg, true); }
                // 七日星盘：七颗星绕身，当天那颗最大最亮
                for (int i = 0; i < 7; i++)
                {
                    float a = -t * .7f + i * Mathf.PI * 2 / 7; bool cur = i == ((p.Day % 7) + 7) % 7;
                    float px = cx + Mathf.Cos(a) * 118, py = cy - 6 + Mathf.Sin(a) * 118;
                    Put(Art.Soft, px, py, cur ? 54 : 24, cur ? 54 : 24, A(Cream, cur ? .5f : .3f), 8, 0, true);
                    Put(Art.Star4, px, py, cur ? 26 + 4 * k : 12, cur ? 26 + 4 * k : 12, A(Color.white, cur ? 1 : .7f), 9, t * (cur ? 90 : 30), true);
                }
                // 双层光环
                Put(Art.Halo, cx, top - 14, 60, 18, A(Cream, .95f), 36, 0, true);
                Put(Art.Halo, cx, top - 24, 84, 24, A(gold, .5f), 35, 0, true);
                // 三对光翼
                bool glide = !p.OnGround && p.Vy > 0 && p.Vy < 400;
                float spread = glide ? 1.15f : .85f;
                for (int s2 = -1; s2 <= 1; s2 += 2)
                    for (int L = 0; L < 3; L++)
                    {
                        float flap = (glide ? 6 : 12) * Mathf.Sin(t * (glide ? 3 : 4.5f) + L * .4f) + L * 22 - 16;
                        float len = (96 - L * 16) * spread;
                        Put(NewCharArt.Feather, cx + s2 * (30 + L * 12 + len * .35f), cy - 14 - L * 6, len, 26 - L * 4, A(L == 0 ? Color.white : L == 1 ? Cream : gold, L == 0 ? .85f : .6f), 9, s2 > 0 ? flap : 180 - flap, true);
                    }
                // 无敌时：金色护体光壳
                if (p.Inv > 0) { Put(Art.Ring, cx, cy - 6, 150 + 10 * k, 150 + 10 * k, A(gold, .45f), 37, t * 60, true); Put(Art.Soft, cx, cy - 6, 160, 160, A(Color.white, .2f), 37, 0, true); }
            }
            // ccld-改 剑客形态手里的刀（神化后照样持刀）
            if (p.Ch == CharId.CCLD2)
            {
                float sw = p.Swing > 0 ? Mathf.Lerp(-110, 50, 1 - p.Swing / .16f) : (p.ParryT > 0 ? 90 : -70);
                Put(Art.Sword, cx + f * (w / 2 + 3), cy + 6, 8, 38, Color.white, 30, f > 0 ? sw : -sw);
            }
            // ccld-改 见切架势：身前一道红白新月 + 收缩的架势圈
            if (p.ParryT > 0)
            {
                float u = p.ParryT <= .3f ? p.ParryT / .3f : .5f + .5f * Mathf.Sin(t * 8); // 朋友端 ParryT 只有开/关，改为呼吸
                Put(Art.Wave, cx + f * (w / 2 + 12), cy, 22, h * 1.6f, A(Color.Lerp(Art.C(0xff4d5e), Color.white, .4f), .75f), 36, f > 0 ? 0 : 180, true);
                Put(Art.RingThin, cx, cy, h * (1.3f + u * 1.1f), h * (1.3f + u * 1.1f), A(Art.C(0xff8a8a), .55f * (1 - u) + .15f), 36, 0, true);
                Put(Art.Star4, cx + f * (w / 2 + 4), cy + 4, 12, 12, new Color(1, 1, 1, .8f), 37, t * 200, true);
            }
            // ccld-改「光」致盲：眼罩 + 头上转的小光点
            if (p.BlindT > 0)
            {
                Put(Art.Soft, cx, p.Y + h * .3f, w * 2.2f, h * 1.2f, new Color(.08f, .06f, .14f, .3f), 36);
                var r = Put(NewCharArt.Blindfold, cx, p.Y + h * .32f, w * 1.15f, 8, Color.white, 37); r.flipX = f < 0;
                for (int i = 0; i < 3; i++) { float a = t * 3 + i * 2.09f; Put(Art.Star4, cx + Mathf.Cos(a) * 16, p.Y - 6 + Mathf.Sin(a) * 4, 7, 7, A(Cream, .8f), 37, 0, true); }
            }
            // kai-改：敌人身上的极性——大号＋/－ + 红蓝描边，远处也看得清
            if (p.Pol != 0)
            {
                var pc = p.Pol > 0 ? PolRed : PolBlue; float a = p.PolT > 0 ? Mathf.Clamp01(p.PolT / .6f) : 1, bob = Mathf.Sin(t * 3 + p.Slot) * 2;
                Put(Art.RingThin, cx, cy, h + 18, h + 18, A(pc, .55f * a), 36, 0, true);
                Put(Art.Soft, cx, p.Y - 26 + bob, 44, 44, A(pc, .32f * a), 36, 0, true);
                Put(p.Pol > 0 ? NewCharArt.Plus : NewCharArt.Minus, cx, p.Y - 26 + bob, 24, 24, A(Color.Lerp(pc, Color.white, .25f), a), 37);
            }
            // kai-改 自身极性：肩头小号符号 + 淡淡的同色光
            if (p.Ch == CharId.KAI2)
            {
                var pc = p.SelfPol > 0 ? PolRed : PolBlue;
                Put(Art.Soft, cx, cy, w * 2.4f, h * 2.2f, A(pc, .14f + .05f * Mathf.Sin(t * 4)), 8, 0, true);
                Put(p.SelfPol > 0 ? NewCharArt.Plus : NewCharArt.Minus, cx - f * (w / 2 + 2), p.Y + 4, 12, 12, Color.Lerp(pc, Color.white, .3f), 36);
            }
            // px 印记：绿色准星慢慢转
            if (p.MarkT > 0)
            {
                float a = Mathf.Clamp01(p.MarkT / .5f), s = h * 1.7f + Mathf.Sin(t * 4) * 3;
                Put(Art.Soft, cx, cy, s * 1.1f, s * 1.1f, A(PxGreen, .14f * a), 36, 0, true);
                Put(NewCharArt.Reticle, cx, cy, s, s, A(Color.Lerp(PxGreen, Color.white, .2f), .85f * a), 37, t * 50);
            }
            // 被钩子 / 收网拉动：从拉人的人身上连一条线
            if (p.PullT > 0 && p.PullBy != null && p.PullBy.Alive)
            {
                var q = p.PullBy;
                if (q.Ch == CharId.PX)
                {   // px 钩子：深色链条 + 钩头挂在被拉的人身上
                    Line(q.Cx, q.Cy, cx, cy, 6, A(PxGreen, .2f), 35);
                    float dx = cx - q.Cx, dy = cy - q.Cy, len = Mathf.Sqrt(dx * dx + dy * dy);
                    for (float k = 8; k < len - 6; k += 9) Put(Art.Dot, q.Cx + dx * k / len, q.Cy + dy * k / len, 4, 4, Art.C(0x4a5a3a), 35);
                    Put(NewCharArt.Hook, cx, cy, 14, 14, Color.white, 36, -Mathf.Atan2(q.Cy - cy, q.Cx - cx) * Mathf.Rad2Deg + 180);
                }
                else
                {   // gary-改 收网（拉向绊索中点）/ kai-改 异极相吸：一条细光线指向落点
                    var lc = q.Ch == CharId.KAI2 ? (q.SelfPol > 0 ? PolRed : PolBlue) : Teal;
                    Line(p.PullX, p.PullY, cx, cy, 6, A(lc, .2f), 35);
                    Line(p.PullX, p.PullY, cx, cy, 2, A(Color.Lerp(lc, Color.white, .4f), .85f), 36);
                    Put(Art.Soft, p.PullX, p.PullY, 30, 30, A(lc, .35f), 36, 0, true);
                }
            }
            // DVD-改：头顶的弹药片数
            if (p.Ch == CharId.DVD2)
            {
                int max = Mathf.Clamp(p.AmmoMax, 0, 8), n = Mathf.Clamp(p.Ammo, 0, max);
                for (int i = 0; i < max; i++) Put(Art.Disc, cx - (max - 1) * 4.5f + i * 9, p.Y - 8, 8, 8, i < n ? Color.HSVToRGB(Mathf.Repeat(.72f + i * .06f, 1), .35f, 1) : new Color(1, 1, 1, .2f), 36, i < n ? -t * 200 : 0);
            }
            // bw-改：身边绕着的能量球（满 3 格下一发是环形波）
            if (p.Ch == CharId.BW2)
            {
                int n = Mathf.Clamp((int)p.Energy, 0, 3);
                for (int i = 0; i < n; i++) { float a = t * 2.4f + i * Mathf.PI * 2 / 3; Put(Art.Soft, cx + Mathf.Cos(a) * w * .9f, cy + Mathf.Sin(a) * h * .55f, 16, 16, A(WhiteBlue, .4f), 36, 0, true); Put(Art.Dot, cx + Mathf.Cos(a) * w * .9f, cy + Mathf.Sin(a) * h * .55f, 6, 6, Color.white, 37); }
                if (n >= 3) Put(Art.RingThin, cx, cy, h * 2.2f, h * 2.2f, A(WhiteBlue, .3f + .1f * Mathf.Sin(t * 4)), 36, -t * 40, true);
            }
            // px：手里的霰弹枪 + 钩子层数
            if (p.Ch == CharId.PX) DrawPxGear(p, cx, cy, w, h, f, 30);
        }
    }
}
