// 第十四版：角色造型（头、身体、四肢、武器）——纯计算部分。
// 只用 UnityEngine 的 Color / Vector2 / Mathf，不碰 Texture、Sprite、Sim：
//   · RBitmap：用有符号距离场画抗锯齿的部件贴图（世界坐标，原点在贴图中心，y 向上），也能把部件合成成一张图（头像、预览）。
//   · RigLook：每个角色的造型参数（身材比例、配色、发型、武器……），全部程序生成，不用外部素材。
//   · RigArt：按 RigLook 画出每个部件（自带深色描边）。
//   · RigPose：按状态（待机、跑、跳、落、贴墙、普攻各段、技能、挨打、眩晕、神化漂浮）摆出骨骼，输出从后到前的绘制列表。
// Unity 侧的 CharRig.cs 把这些部件做成 Sprite，在 WorldView.DrawPlayer 里按列表画出来。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Brawl.View
{
    // ======================================================================
    // 位图
    // ======================================================================
    public sealed class RBitmap
    {
        public readonly int W, H; public readonly float Ppu; public readonly float[] P; // RGBA（非预乘），第 0 行在最下面
        public float WW => W / Ppu; public float WH => H / Ppu;
        public RBitmap(float worldW, float worldH, float ppu)
        {
            Ppu = ppu; W = Mathf.Max(2, Mathf.CeilToInt(worldW * ppu)); H = Mathf.Max(2, Mathf.CeilToInt(worldH * ppu)); P = new float[W * H * 4];
        }
        public delegate float Sdf(float x, float y);
        public delegate Color Shade(float x, float y);
        /// <summary>按距离场填色（坐标：世界单位，原点在贴图中心，y 向上）。</summary>
        public void Fill(Sdf f, Shade s, float soft = 1)
        {
            float fe = 1.1f / Ppu * soft, hw = W / Ppu / 2, hh = H / Ppu / 2;
            for (int j = 0; j < H; j++)
            {
                float y = (j + .5f) / Ppu - hh;
                for (int i = 0; i < W; i++)
                {
                    float x = (i + .5f) / Ppu - hw, d = f(x, y);
                    if (d > fe) continue;
                    float a = Mathf.Clamp01(.5f - d / fe); if (a <= 0) continue;
                    var c = s(x, y); c.a *= a; Blend(i, j, c);
                }
            }
        }
        public void Fill(Sdf f, Color c, float soft = 1) => Fill(f, (x, y) => c, soft);
        public void Blend(int i, int j, Color c)
        {
            int k = (j * W + i) * 4; float oa = P[k + 3], a = c.a + oa * (1 - c.a);
            if (a <= 1e-5f) return;
            P[k] = (c.r * c.a + P[k] * oa * (1 - c.a)) / a; P[k + 1] = (c.g * c.a + P[k + 1] * oa * (1 - c.a)) / a; P[k + 2] = (c.b * c.a + P[k + 2] * oa * (1 - c.a)) / a; P[k + 3] = a;
        }
        public RBitmap Silhouette()
        {
            var b = new RBitmap(WW, WH, Ppu);
            for (int k = 0; k < P.Length; k += 4) { b.P[k] = b.P[k + 1] = b.P[k + 2] = 1; b.P[k + 3] = P[k + 3]; }
            return b;
        }
        public Color Get(float u, float v) // 双线性取样，u,v 为像素坐标
        {
            u -= .5f; v -= .5f; int i0 = Mathf.FloorToInt(u), j0 = Mathf.FloorToInt(v); float fu = u - i0, fv = v - j0;
            Color c00 = Px(i0, j0), c10 = Px(i0 + 1, j0), c01 = Px(i0, j0 + 1), c11 = Px(i0 + 1, j0 + 1);
            // 按 alpha 加权，避免透明边缘发黑
            float w00 = (1 - fu) * (1 - fv) * c00.a, w10 = fu * (1 - fv) * c10.a, w01 = (1 - fu) * fv * c01.a, w11 = fu * fv * c11.a, ws = w00 + w10 + w01 + w11;
            float a = (1 - fu) * (1 - fv) * c00.a + fu * (1 - fv) * c10.a + (1 - fu) * fv * c01.a + fu * fv * c11.a;
            if (ws <= 1e-6f) return new Color(0, 0, 0, 0);
            return new Color((c00.r * w00 + c10.r * w10 + c01.r * w01 + c11.r * w11) / ws, (c00.g * w00 + c10.g * w10 + c01.g * w01 + c11.g * w11) / ws, (c00.b * w00 + c10.b * w10 + c01.b * w01 + c11.b * w11) / ws, a);
        }
        Color Px(int i, int j) { if (i < 0 || j < 0 || i >= W || j >= H) return new Color(0, 0, 0, 0); int k = (j * W + i) * 4; return new Color(P[k], P[k + 1], P[k + 2], P[k + 3]); }
        /// <summary>把 src 画到本图上：中心 (cx,cy)、尺寸 (w,h)（世界单位，本图坐标系：原点在中心，y 向上）、逆时针旋转 rot 度，可水平翻转，乘色 tint。</summary>
        public void Composite(RBitmap src, float cx, float cy, float w, float h, float rotDeg, Color tint, bool flipX)
        {
            float hw = W / Ppu / 2, hh = H / Ppu / 2, ca = Mathf.Cos(rotDeg * Mathf.Deg2Rad), sa = Mathf.Sin(rotDeg * Mathf.Deg2Rad);
            float ext = (Mathf.Abs(w * ca) + Mathf.Abs(h * sa)) / 2 + 1, eyt = (Mathf.Abs(w * sa) + Mathf.Abs(h * ca)) / 2 + 1;
            int i0 = Mathf.Max(0, Mathf.FloorToInt((cx - ext + hw) * Ppu)), i1 = Mathf.Min(W - 1, Mathf.CeilToInt((cx + ext + hw) * Ppu));
            int j0 = Mathf.Max(0, Mathf.FloorToInt((cy - eyt + hh) * Ppu)), j1 = Mathf.Min(H - 1, Mathf.CeilToInt((cy + eyt + hh) * Ppu));
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    float x = (i + .5f) / Ppu - hw - cx, y = (j + .5f) / Ppu - hh - cy;
                    float lx = x * ca + y * sa, ly = -x * sa + y * ca; // 逆旋转到贴图坐标
                    float u = lx / w + .5f, v = ly / h + .5f; if (u < 0 || v < 0 || u > 1 || v > 1) continue;
                    if (flipX) u = 1 - u;
                    var c = src.Get(u * src.W, v * src.H); if (c.a <= .002f) continue;
                    Blend(i, j, new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a * tint.a));
                }
        }
    }

    /// <summary>距离场小工具（世界单位）。</summary>
    public static class RSd
    {
        public static float Circ(float x, float y, float cx, float cy, float r) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;
        public static float Ell(float x, float y, float cx, float cy, float rx, float ry)
        { float dx = (x - cx) / rx, dy = (y - cy) / ry; float k = Mathf.Sqrt(dx * dx + dy * dy); return (k - 1) * Mathf.Min(rx, ry); }
        public static float Box(float x, float y, float cx, float cy, float hw, float hh, float r = 0)
        {
            float qx = Mathf.Abs(x - cx) - hw + r, qy = Mathf.Abs(y - cy) - hh + r;
            return Mathf.Sqrt(Mathf.Max(qx, 0) * Mathf.Max(qx, 0) + Mathf.Max(qy, 0) * Mathf.Max(qy, 0)) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
        }
        /// <summary>线段胶囊，半径从 ra 渐变到 rb。</summary>
        public static float Seg(float x, float y, float ax, float ay, float bx, float by, float ra, float rb = -1)
        {
            if (rb < 0) rb = ra; float px = x - ax, py = y - ay, dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
            float t = l2 > 0 ? Mathf.Clamp01((px * dx + py * dy) / l2) : 0; float ex = px - dx * t, ey = py - dy * t;
            return Mathf.Sqrt(ex * ex + ey * ey) - Mathf.Lerp(ra, rb, t);
        }
        /// <summary>凸多边形（逆时针给点），近似距离。</summary>
        public static float Poly(float x, float y, params float[] p)
        {
            float d = -1e9f; int n = p.Length / 2; float area = 0;
            for (int i = 0; i < n; i++) area += p[i * 2] * p[(i + 1) % n * 2 + 1] - p[(i + 1) % n * 2] * p[i * 2 + 1];
            float sg = area >= 0 ? 1 : -1; // 顺时针给点也行
            for (int i = 0; i < n; i++)
            {
                float ax = p[i * 2], ay = p[i * 2 + 1], bx = p[(i + 1) % n * 2], by = p[(i + 1) % n * 2 + 1];
                float ex = bx - ax, ey = by - ay, l = Mathf.Sqrt(ex * ex + ey * ey); if (l < 1e-5f) continue;
                d = Mathf.Max(d, sg * ((x - ax) * ey - (y - ay) * ex) / l);
            }
            return d;
        }
        public static float U(float a, float b) => Mathf.Min(a, b);
        public static float U(float a, float b, float c) => Mathf.Min(a, Mathf.Min(b, c));
        public static float Sub(float a, float b) => Mathf.Max(a, -b);
        public static float I(float a, float b) => Mathf.Max(a, b);
        public static float SU(float a, float b, float k) { float h = Mathf.Clamp01(.5f + .5f * (b - a) / k); return Mathf.Lerp(b, a, h) - k * h * (1 - h); }
    }

    // ======================================================================
    // 造型参数
    // ======================================================================
    public enum RWeapon { None, Sword, GodSword, Katana, Disc, Staff, Kunai, Shotgun, Wrench, Fists, Spore }
    public enum RArms { Normal, Tentacle, Big }
    public enum RBack { None, Cape, Scarf, Ponytail, HoodTail }

    public sealed class RigLook
    {
        public int Id; public string Name;
        public float Scale = 1, Head = 1, TorsoW = 1, TorsoH = 1, Arm = 1, Leg = 1, LimbT = 1, HandS = 1;
        public Color Skin = H(0xffd8b0), Main, Main2, Dark, Pants, Shoe = H(0x2a2236), Accent, Hair, Sleeve, Glove;
        public bool ShortSleeve, Robe, Belly, NoNeckSkin;
        public RWeapon Weapon; public RArms Arms; public RBack Back; public Color BackC;
        public float Crouch; // 待机时压低重心（刺客）
        public static Color H(uint rgb, float a = 1) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
    }

    public static class RigLooks
    {
        public const int GOD = 15; // 原版 ccld 成神形态
        public const int COUNT = 16;
        static readonly RigLook[] cache = new RigLook[COUNT];
        public static RigLook Get(int id) { if (id < 0 || id >= COUNT) id = 0; return cache[id] ?? (cache[id] = Make(id)); }
        static Color H(uint c) => RigLook.H(c);
        static RigLook Make(int id)
        {
            var L = new RigLook { Id = id };
            switch (id)
            {
                case 0: // DVD：戴耳机的连帽衫少年，手里一张彩虹光盘
                    L.Name = "DVD"; L.Main = H(0xff4fd8); L.Main2 = H(0xc42aa8); L.Sleeve = L.Main; L.Dark = H(0x5a1a6a); L.Pants = H(0x3a2f6e); L.Shoe = H(0xf4f4ff); L.Accent = H(0x7ff3ff); L.Hair = H(0x3a1d4a);
                    L.Weapon = RWeapon.Disc; L.Head = 1.08f; L.Leg = .88f; break;
                case 1: // xp：壮汉，大肚子 + 背心 + 大拳头
                    L.Name = "xp"; L.Main = H(0x4cff8a); L.Main2 = H(0x1fae55); L.Sleeve = L.Skin = H(0xe8b48a); L.ShortSleeve = true; L.Dark = H(0x1a5a34); L.Pants = H(0x2c4a7a); L.Shoe = H(0x3a2618); L.Accent = H(0xffffff); L.Hair = H(0x1a3a24);
                    L.Weapon = RWeapon.Fists; L.Arms = RArms.Big; L.TorsoW = 1.45f; L.TorsoH = 1.05f; L.Belly = true; L.LimbT = 1.3f; L.HandS = 1.45f; L.Head = .92f; L.Leg = .85f; break;
                case 2: // bw：黑洞法师，兜帽长袍，脸是一片黑暗只露紫色眼睛，法杖顶着黑洞
                    L.Name = "bw"; L.Main = H(0x7a3ad8); L.Main2 = H(0x3f1a7a); L.Sleeve = L.Main; L.Dark = H(0x1a0a2e); L.Pants = H(0x2a1446); L.Shoe = H(0x1a0a2e); L.Accent = H(0xc77dff); L.Hair = H(0x1a0a2e); L.Glove = H(0x2a1446);
                    L.Weapon = RWeapon.Staff; L.Robe = true; L.Back = RBack.HoodTail; L.BackC = H(0x5a2aa8); break;
                case 3: // ccld：剑客，黑色刺发 + 头巾 + 红披风 + 长剑
                    L.Name = "ccld"; L.Main = H(0xffc531); L.Main2 = H(0xd9901c); L.Sleeve = H(0xf0b02a); L.Dark = H(0x3a1d12); L.Pants = H(0x4a2a1a); L.Shoe = H(0x2a1810); L.Accent = H(0xc8302a); L.Hair = H(0x1e1a24);
                    L.Weapon = RWeapon.Sword; L.Back = RBack.Cape; L.BackC = H(0xb02a2a); break;
                case 4: // gary：钩锁刺客，深色头套 + 橙色夜行衣 + 红围巾 + 苦无
                    L.Name = "gary"; L.Main = H(0xff8a3c); L.Main2 = H(0xc4561c); L.Sleeve = H(0x2a2236); L.Dark = H(0x2a2236); L.Pants = H(0x2a2236); L.Shoe = H(0x15121e); L.Accent = H(0xe0283c); L.Hair = H(0x2a2236); L.Glove = H(0x2a2236);
                    L.Weapon = RWeapon.Kunai; L.Back = RBack.Scarf; L.BackC = H(0xe0283c); L.Crouch = .14f; L.Leg = 1.02f; break;
                case 5: // kai：雷霆拳师，深蓝刺发 + 白头带 + 青色道服 + 绑手
                    L.Name = "kai"; L.Main = H(0x3cf0ff); L.Main2 = H(0x1a9fd0); L.Sleeve = L.Skin; L.ShortSleeve = true; L.Dark = H(0x123a6a); L.Pants = H(0x1d3a8a); L.Shoe = H(0x101a3a); L.Accent = H(0xffe14a); L.Hair = H(0x1d3a8a); L.Glove = H(0xf4fbff);
                    L.Weapon = RWeapon.Fists; L.HandS = 1.25f; break;
                case 6: // dzy：触手怪，粉色章鱼头 + 嘴边触须 + 触手手臂
                    L.Name = "dzy"; L.Main = H(0xe0457b); L.Main2 = H(0x9a2a5a); L.Skin = H(0xff8ab0); L.Sleeve = H(0xff8ab0); L.Dark = H(0x5a0a28); L.Pants = H(0x7a1a44); L.Shoe = H(0x5a0a28); L.Accent = H(0xffd0de); L.Hair = H(0xe0457b);
                    L.Weapon = RWeapon.None; L.Arms = RArms.Tentacle; L.Arm = 1.25f; L.Head = 1.12f; L.NoNeckSkin = true; break;
                case 7: // px：霰弹猎手，鸭舌帽 + 护目镜 + 橄榄夹克 + 子弹带 + 霰弹枪
                    L.Name = "px"; L.Main = H(0x8fd14f); L.Main2 = H(0x5a8a2a); L.Sleeve = H(0x6f9e3a); L.Dark = H(0x2f3a1e); L.Pants = H(0x4a4030); L.Shoe = H(0x2a2016); L.Accent = H(0xffb04a); L.Hair = H(0x5a3a1e); L.Glove = H(0x3a2a1a);
                    L.Weapon = RWeapon.Shotgun; break;
                case 8: // ccld-改：白金居合剑客，银白长发马尾 + 白色和服金边 + 黑腰带 + 武士刀
                    L.Name = "ccld-改"; L.Main = H(0xf8f4e4); L.Main2 = H(0xd8cfae); L.Sleeve = H(0xf8f4e4); L.Dark = H(0x1a1620); L.Pants = H(0x2a2438); L.Shoe = H(0x1a1620); L.Accent = H(0xe8b84a); L.Hair = H(0xeef2ff);
                    L.Weapon = RWeapon.Katana; L.Back = RBack.Ponytail; L.BackC = H(0xeef2ff); L.Robe = true; break;
                case 9: // DVD-改：屏保弹射手，墨镜 + 紫色科技夹克 + 胸口光盘标
                    L.Name = "DVD-改"; L.Main = H(0x9a7bff); L.Main2 = H(0x5a3ad0); L.Sleeve = H(0x9a7bff); L.Dark = H(0x241a4a); L.Pants = H(0x241a4a); L.Shoe = H(0x7ff3ff); L.Accent = H(0x7ff3ff); L.Hair = H(0x2a1a5a); L.Glove = H(0x241a4a);
                    L.Weapon = RWeapon.Disc; L.Head = 1.04f; L.Leg = .9f; break;
                case 10: // xp-改：吞吞滚球，圆滚滚的粉色胖子，大嘴 + 背带裤
                    L.Name = "xp-改"; L.Main = H(0xffb3d1); L.Main2 = H(0xe07aa8); L.Skin = H(0xffc8dc); L.Sleeve = H(0xffc8dc); L.ShortSleeve = true; L.Dark = H(0x8a2a5a); L.Pants = H(0x4a6ad8); L.Shoe = H(0x8a2a5a); L.Accent = H(0xffffff); L.Hair = H(0xe07aa8);
                    L.Weapon = RWeapon.Fists; L.Arms = RArms.Big; L.TorsoW = 1.5f; L.Belly = true; L.LimbT = 1.25f; L.HandS = 1.35f; L.Head = 1.02f; L.Leg = .8f; break;
                case 11: // bw-改：白洞法师，白色长袍冷蓝镶边，法杖顶着白光
                    L.Name = "bw-改"; L.Main = H(0xf2f8ff); L.Main2 = H(0xb8d4f0); L.Sleeve = L.Main; L.Dark = H(0x2a4a7a); L.Pants = H(0x9ab8dc); L.Shoe = H(0x2a4a7a); L.Accent = H(0x6ab8ff); L.Hair = H(0x2a4a7a); L.Glove = H(0xdce8f8);
                    L.Weapon = RWeapon.Staff; L.Robe = true; L.Back = RBack.HoodTail; L.BackC = H(0xcfe2f8); break;
                case 12: // gary-改：绊索机关师，护目镜 + 乱发 + 青绿工装 + 扳手
                    L.Name = "gary-改"; L.Main = H(0x3cbf9a); L.Main2 = H(0x23866a); L.Sleeve = H(0x3cbf9a); L.Dark = H(0x173a32); L.Pants = H(0x3a3a46); L.Shoe = H(0x241a14); L.Accent = H(0xffd23c); L.Hair = H(0x6a3a1e); L.Glove = H(0xe8a03c);
                    L.Weapon = RWeapon.Wrench; L.Back = RBack.Scarf; L.BackC = H(0x2a6a5a); break;
                case 13: // kai-改：极性磁暴，桃红刺发 + 红蓝分色头带 + 红蓝磁力拳套
                    L.Name = "kai-改"; L.Main = H(0xff6a8a); L.Main2 = H(0xc43a5a); L.Sleeve = H(0x2a2a3a); L.Dark = H(0x2a2a3a); L.Pants = H(0x2a2a3a); L.Shoe = H(0x15151e); L.Accent = H(0x4a8aff); L.Hair = H(0xff4a7a); L.Glove = H(0xff3a4a);
                    L.Weapon = RWeapon.Fists; L.HandS = 1.4f; break;
                case 14: // dzy-改：蘑菇菌人，绿色菌盖带亮斑 + 苔藓身体 + 孢子团
                    L.Name = "dzy-改"; L.Main = H(0x6bd94a); L.Main2 = H(0x2f7a24); L.Skin = H(0xf0ead0); L.Sleeve = H(0x8fe86a); L.Dark = H(0x1e4a18); L.Pants = H(0x3a6a2a); L.Shoe = H(0x2a3a1a); L.Accent = H(0xd8ff8a); L.Hair = H(0x6bd94a); L.Glove = H(0xf0ead0);
                    L.Weapon = RWeapon.Spore; L.Head = 1.1f; L.Leg = .9f; break;
                default: // 原版 ccld 成神：金甲剑神，光辉长剑
                    L.Name = "ccld·神"; L.Main = H(0xfff2a8); L.Main2 = H(0xffa514); L.Sleeve = H(0xffd860); L.Dark = H(0x7a4a0a); L.Pants = H(0xd89a2a); L.Shoe = H(0x7a4a0a); L.Accent = H(0xffffff); L.Hair = H(0xffc81e); L.Glove = H(0xffe27a); L.Skin = H(0xffe6c4);
                    L.Weapon = RWeapon.GodSword; L.Back = RBack.Cape; L.BackC = H(0xfff2c0); L.Scale = 1.12f; break;
            }
            if (L.Glove.a <= 0) L.Glove = L.Skin;
            return L;
        }
    }

    // ======================================================================
    // 部件
    // ======================================================================
    public static class RP
    {
        public const int Head = 0, HeadHurt = 1, Torso = 2, UArm = 3, FArm = 4, Hand = 5, Thigh = 6, Shin = 7, Foot = 8, Weapon = 9, Back = 10, Skirt = 11, Extra = 12, Hand2 = 13, Count = 14;
    }

    /// <summary>一个角色的全部部件 + 骨骼尺寸（世界单位，按角色定义的碰撞盒大小算出来）。</summary>
    public sealed class RigParts
    {
        public RigLook L; public float S; // 尺寸单位
        public RBitmap[] Fill = new RBitmap[RP.Count], Sil = new RBitmap[RP.Count];
        public float ArmU, ArmF, ArmT, LegU, LegF, LegT, TorsoW, TorsoH, HeadR, FootL, FootH, HandR;
        public float WeaponLen, WeaponGrip, WeaponGrip2; // 武器沿长度方向的握点（0..1，从左端算）
        public float BackLen;
    }

    public static class RigArt
    {
        const float PPU = 4f, OL = 1.0f; // 每世界单位像素数；描边粗细（世界单位）
        static readonly Color Ink = new Color(.07f, .05f, .1f, 1);
        static Color Mul(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
        static Color Lt(Color c, float k) => Color.Lerp(c, Color.white, k);

        /// <summary>按角色定义的碰撞盒（w,h）生成部件。之后不管放大缩小都按比例缩放。</summary>
        public static RigParts Build(RigLook L, float defW, float defH)
        {
            var P = new RigParts { L = L };
            float s = Mathf.Sqrt(defW * defH) / 32f * L.Scale; P.S = s;
            P.ArmU = 6.4f * s * L.Arm; P.ArmF = 6.0f * s * L.Arm; P.ArmT = 4.0f * s * L.LimbT;
            P.LegU = 6.8f * s * L.Leg; P.LegF = 6.6f * s * L.Leg; P.LegT = 4.6f * s * L.LimbT;
            P.TorsoW = 11.5f * s * L.TorsoW; P.TorsoH = 12.5f * s * L.TorsoH; P.HeadR = 10.5f * s * L.Head;
            P.FootL = 6.6f * s * (L.Arms == RArms.Big ? 1.15f : 1); P.FootH = 3.6f * s; P.HandR = 2.5f * s * L.HandS;
            if (L.Arms == RArms.Tentacle) P.ArmT = 3.6f * s;
            Head(P, false); Head(P, true); Torso(P); Limbs(P); Weapon(P); Back(P); if (L.Robe) Skirt(P); Extra(P);
            for (int i = 0; i < RP.Count; i++) if (P.Fill[i] != null) P.Sil[i] = P.Fill[i].Silhouette();
            return P;
        }

        static RBitmap New(float w, float h) => new RBitmap(w + 2 * OL + 1.2f, h + 2 * OL + 1.2f, PPU);

        // ------------------------------ 头 ------------------------------
        static void Eye(RBitmap b, float x, float y, float r, Color iris, bool hurt, bool glow = false)
        {
            if (hurt)
            {   // ＞＜ 眯眼
                b.Fill((px, py) => U2(RSd.Seg(px, py, x - r, y + r * .7f, x + r * .6f, y, r * .32f), RSd.Seg(px, py, x + r * .6f, y, x - r, y - r * .7f, r * .32f)), Ink);
                return;
            }
            if (glow) { b.Fill((px, py) => RSd.Ell(px, py, x, y, r * 1.5f, r * 1.2f), new Color(iris.r, iris.g, iris.b, .35f), 3); b.Fill((px, py) => RSd.Ell(px, py, x, y, r * .75f, r * .55f), Lt(iris, .6f)); return; }
            b.Fill((px, py) => RSd.Ell(px, py, x, y, r, r * 1.28f), Ink);
            b.Fill((px, py) => RSd.Ell(px, py, x + r * .08f, y, r * .8f, r * 1.08f), Color.white);
            b.Fill((px, py) => RSd.Ell(px, py, x + r * .28f, y - r * .05f, r * .55f, r * .8f), iris);
            b.Fill((px, py) => RSd.Ell(px, py, x + r * .32f, y - r * .1f, r * .3f, r * .48f), Ink);
            b.Fill((px, py) => RSd.Circ(px, py, x + r * .12f, y + r * .42f, r * .24f), Color.white);
        }
        static float U2(float a, float b) => Mathf.Min(a, b);

        static void Head(RigParts P, bool hurt)
        {
            var L = P.L; float R = P.HeadR, s = P.S; var b = New(R * 3.2f, R * 3.0f);
            Color skin = L.Skin, hair = L.Hair, ink = Ink;
            float ex = R * .3f, ey = R * .05f, er = R * .2f; // 近处那只眼睛
            Color iris = Color.Lerp(L.Main, Ink, .25f);
            RBitmap.Sdf face = (x, y) => RSd.Ell(x, y, 0, -R * .04f, R * .96f, R * .92f);
            switch (L.Id)
            {
                case 2: case 11: // 兜帽法师：兜帽包住头，脸是一团黑暗，眼睛发光
                    {
                        Color hood = L.Main, hood2 = L.Main2, voidC = L.Id == 2 ? H(0x08020f) : H(0x0c1a33), glow = L.Id == 2 ? H(0xd8a0ff) : H(0xffffff);
                        RBitmap.Sdf hd = (x, y) => RSd.SU(RSd.Ell(x, y, -R * .06f, R * .02f, R * 1.05f, R * 1.0f), RSd.Poly(x, y, -R * .2f, R * .7f, -R * 1.25f, R * 1.25f, -R * .9f, R * .2f), R * .3f);
                        b.Fill((x, y) => hd(x, y) - OL, ink);
                        b.Fill(hd, (x, y) => Color.Lerp(hood2, hood, Mathf.Clamp01(.5f + y / R * .6f - x / R * .2f)));
                        b.Fill((x, y) => RSd.Ell(x, y, R * .3f, -R * .1f, R * .62f, R * .68f), voidC);
                        b.Fill((x, y) => Mathf.Abs(RSd.Ell(x, y, R * .3f, -R * .1f, R * .66f, R * .72f)) - R * .06f, L.Accent);
                        if (hurt) { Eye(b, R * .22f, -R * .02f, er * .8f, glow, true); }
                        else { Eye(b, R * .14f, -R * .02f, er * .8f, glow, false, true); Eye(b, R * .5f, -R * .04f, er * .7f, glow, false, true); }
                        break;
                    }
                case 6: // dzy：章鱼头（更高的圆顶）+ 嘴边三条小触须
                    {
                        RBitmap.Sdf hd = (x, y) => RSd.U(RSd.Ell(x, y, 0, R * .08f, R * .95f, R * 1.08f), RSd.Seg(x, y, R * .15f, -R * .7f, R * .1f, -R * 1.25f, R * .16f, R * .07f), RSd.U(RSd.Seg(x, y, R * .5f, -R * .62f, R * .62f, -R * 1.12f, R * .14f, R * .06f), RSd.Seg(x, y, -R * .22f, -R * .72f, -R * .38f, -R * 1.15f, R * .14f, R * .06f)));
                        b.Fill((x, y) => hd(x, y) - OL, ink);
                        b.Fill(hd, (x, y) => Color.Lerp(L.Main2, L.Skin, Mathf.Clamp01(.55f + y / R * .5f)));
                        for (int i = 0; i < 4; i++) { float cx = -R * (.55f - i * .12f), cy = R * (.55f - i * .25f); b.Fill((x, y) => RSd.Circ(x, y, cx, cy, R * .09f), L.Accent); }
                        b.Fill((x, y) => RSd.Ell(x, y, -R * .2f, R * .62f, R * .32f, R * .12f), new Color(1, 1, 1, .5f), 2);
                        Eye(b, R * .22f, R * .1f, er * 1.2f, H(0x2a0a1a), hurt); Eye(b, R * .62f, R * .06f, er * .9f, H(0x2a0a1a), hurt);
                        break;
                    }
                case 14: // dzy-改：蘑菇菌盖 + 苍白小脸
                    {
                        RBitmap.Sdf cap = (x, y) => RSd.Sub(RSd.Ell(x, y, 0, R * .2f, R * 1.38f, R * 1.0f), RSd.Box(x, y, 0, -R * .9f, R * 2, R * .62f));
                        RBitmap.Sdf fc = (x, y) => RSd.Ell(x, y, R * .05f, -R * .38f, R * .7f, R * .62f);
                        b.Fill((x, y) => fc(x, y) - OL, ink); b.Fill(fc, (x, y) => Color.Lerp(Mul(skin, .85f), skin, Mathf.Clamp01(.5f + x / R)));
                        Eye(b, R * .22f, -R * .38f, er * .85f, H(0x1e3a18), hurt); Eye(b, R * .55f, -R * .4f, er * .7f, H(0x1e3a18), hurt);
                        b.Fill((x, y) => cap(x, y) - OL, ink);
                        b.Fill(cap, (x, y) => Color.Lerp(L.Main2, L.Main, Mathf.Clamp01(.3f + (y + R * .3f) / R * .7f)));
                        float[] sp = { -.7f, .55f, .2f, .75f, .75f, .45f, -.15f, .25f, .45f, .05f, -.85f, .02f };
                        for (int i = 0; i < sp.Length; i += 2) { float cx = sp[i] * R, cy = sp[i + 1] * R; b.Fill((x, y) => RSd.I(RSd.Circ(x, y, cx, cy, R * .14f), cap(x, y)), L.Accent); }
                        b.Fill((x, y) => RSd.I(RSd.Ell(x, y, -R * .35f, R * .85f, R * .45f, R * .14f), cap(x, y)), new Color(1, 1, 1, .45f), 2);
                        break;
                    }
                default:
                    {
                        // 头后面的头发（长发 / 刺发外轮廓）
                        RBitmap.Sdf back = null;
                        if (L.Id == 3) back = (x, y) => RSd.U(RSd.Ell(x, y, -R * .12f, R * .2f, R * 1.0f, R * .9f), RSd.Poly(x, y, -R * .1f, R * .9f, -R * .9f, R * 1.35f, -R * .6f, R * .5f), RSd.U(RSd.Poly(x, y, -R * .6f, R * .7f, -R * 1.35f, R * .65f, -R * .9f, R * .1f), RSd.Poly(x, y, R * .3f, R * .8f, -R * .2f, R * 1.4f, -R * .3f, R * .7f)));
                        if (L.Id == 5 || L.Id == 13) back = (x, y) => RSd.U(RSd.Ell(x, y, -R * .1f, R * .2f, R * 1.0f, R * .88f), RSd.Poly(x, y, R * .45f, R * .7f, R * .1f, R * 1.45f, -R * .2f, R * .8f), RSd.U(RSd.Poly(x, y, -R * .2f, R * .8f, -R * .75f, R * 1.45f, -R * .7f, R * .6f), RSd.Poly(x, y, -R * .6f, R * .6f, -R * 1.45f, R * .75f, -R * .9f, R * .05f)));
                        if (L.Id == 8) back = (x, y) => RSd.U(RSd.Ell(x, y, -R * .1f, R * .1f, R * 1.04f, R * .98f), RSd.Box(x, y, -R * .55f, -R * .45f, R * .5f, R * .55f, R * .3f));
                        if (L.Id == 9) back = (x, y) => RSd.U(RSd.Ell(x, y, -R * .1f, R * .2f, R * 1.0f, R * .88f), RSd.Poly(x, y, -R * .1f, R * .85f, -R * .5f, R * 1.3f, -R * .6f, R * .6f), RSd.Poly(x, y, -R * .5f, R * .7f, -R * 1.25f, R * 1.0f, -R * .85f, R * .3f));
                        if (L.Id == 12) back = (x, y) => RSd.U(RSd.Ell(x, y, -R * .1f, R * .25f, R * 1.06f, R * .9f), RSd.Circ(x, y, -R * .8f, R * .55f, R * .35f), RSd.U(RSd.Circ(x, y, -R * .3f, R * .95f, R * .35f), RSd.Circ(x, y, R * .3f, R * .9f, R * .32f)));
                        if (back != null) { b.Fill((x, y) => back(x, y) - OL, ink); b.Fill(back, (x, y) => Color.Lerp(Mul(hair, .8f), Lt(hair, .12f), Mathf.Clamp01(.5f + y / R * .5f))); }
                        if (L.Id == 4) // gary 头套：整颗头包住，只露眼缝
                        {
                            RBitmap.Sdf hd = (x, y) => RSd.U(face(x, y), RSd.Poly(x, y, -R * .6f, R * .5f, -R * 1.3f, R * .9f, -R * .9f, R * .1f));
                            b.Fill((x, y) => hd(x, y) - OL, ink); b.Fill(hd, (x, y) => Color.Lerp(H(0x15121e), H(0x3a3050), Mathf.Clamp01(.5f + y / R * .5f)));
                            b.Fill((x, y) => RSd.Box(x, y, R * .38f, R * .02f, R * .58f, R * .2f, R * .15f), H(0xffd8b0));
                            Eye(b, R * .3f, R * .03f, er * .75f, H(0x3a2a1a), hurt); Eye(b, R * .7f, R * .02f, er * .6f, H(0x3a2a1a), hurt);
                            b.Fill((x, y) => RSd.Box(x, y, -R * .1f, R * .55f, R * .9f, R * .1f, R * .05f), new Color(L.Main.r, L.Main.g, L.Main.b, .85f)); // 橙色头套带
                            break;
                        }
                        // 脸
                        Color sk = skin;
                        RBitmap.Sdf fc = face;
                        if (L.Id == 1) fc = (x, y) => RSd.U(RSd.Ell(x, y, 0, -R * .02f, R * .96f, R * .9f), RSd.Box(x, y, R * .15f, -R * .55f, R * .78f, R * .42f, R * .35f)); // 大下巴
                        if (L.Id == 10) fc = (x, y) => RSd.Ell(x, y, 0, -R * .06f, R * 1.02f, R * .95f);
                        b.Fill((x, y) => fc(x, y) - OL, ink);
                        b.Fill(fc, (x, y) => Color.Lerp(Mul(sk, .86f), sk, Mathf.Clamp01(.55f + x / R * .6f + y / R * .2f)));
                        // 头发盖住头顶（后脑多、额头少）
                        if (L.Id == 3 || L.Id == 5 || L.Id == 8 || L.Id == 9 || L.Id == 12 || L.Id == 13 || L.Id == 15)
                            b.Fill((x, y) => RSd.I(fc(x, y) - R * .03f, R * (L.Id == 8 ? .28f : .36f) - y + x * .42f), (x, y) => Color.Lerp(Mul(hair, .8f), Lt(hair, .15f), Mathf.Clamp01(.5f + y / R * .6f)));
                        if (L.Id == 8) b.Fill((x, y) => RSd.Seg(x, y, -R * .1f, R * .2f, -R * .25f, -R * .75f, R * .32f, R * .18f), (x, y) => Color.Lerp(H(0x9aa4c8), hair, .5f)); // 鬓发
                        b.Fill((x, y) => RSd.Ell(x, y, -R * .55f, -R * .05f, R * .2f, R * .26f), L.Id == 8 ? Color.Lerp(H(0x9aa4c8), hair, .5f) : Mul(sk, .85f)); // 耳朵
                        // 腮红
                        if (L.Id != 1 && L.Id != 12) b.Fill((x, y) => RSd.Ell(x, y, R * .55f, -R * .36f, R * .17f, R * .09f), new Color(1, .45f, .5f, .45f), 2);
                        // 眼睛
                        bool small = L.Id == 1 || L.Id == 10;
                        Eye(b, ex, ey, small ? er * .7f : er, iris, hurt); Eye(b, ex + R * .38f, ey - R * .02f, (small ? er * .7f : er) * .82f, iris, hurt);
                        // 眉毛
                        if (L.Id == 1 || L.Id == 3 || L.Id == 5 || L.Id == 13) b.Fill((x, y) => RSd.Seg(x, y, ex - er, ey + er * 1.7f, ex + R * .55f, ey + er * (hurt ? 1.2f : 1.95f), R * .07f), Mul(hair, .8f));
                        // 嘴
                        if (L.Id == 10 && !hurt) // 大嘴
                        {
                            b.Fill((x, y) => RSd.Sub(RSd.Ell(x, y, R * .5f, -R * .4f, R * .4f, R * .32f), RSd.Box(x, y, R * .5f, -R * .05f, R * .6f, R * .2f)), H(0x5a0a28));
                            b.Fill((x, y) => RSd.I(RSd.Ell(x, y, R * .52f, -R * .58f, R * .24f, R * .12f), RSd.Ell(x, y, R * .5f, -R * .4f, R * .4f, R * .32f)), H(0xff6a8a));
                        }
                        else if (hurt) b.Fill((x, y) => RSd.Ell(x, y, R * .52f, -R * .45f, R * .12f, R * .1f), H(0x5a0a28));
                        else if (L.Id == 1) b.Fill((x, y) => RSd.Sub(RSd.Ell(x, y, R * .5f, -R * .4f, R * .3f, R * .16f), RSd.Box(x, y, R * .5f, -R * .2f, R * .4f, R * .12f)), H(0x5a2a1a));
                        else b.Fill((x, y) => RSd.Seg(x, y, R * .38f, -R * .43f, R * .62f, -R * .4f, R * .05f), Mul(sk, .45f));
                        // 前面的头发 / 帽子 / 饰品
                        switch (L.Id)
                        {
                            case 0: // DVD：连帽 + 大耳机
                                {
                                    RBitmap.Sdf hood = (x, y) => RSd.Sub(RSd.Ell(x, y, -R * .12f, R * .06f, R * 1.12f, R * 1.06f), RSd.Ell(x, y, R * .32f, -R * .1f, R * .82f, R * .78f));
                                    b.Fill((x, y) => hood(x, y) - OL, ink); b.Fill(hood, (x, y) => Color.Lerp(L.Main2, L.Main, Mathf.Clamp01(.5f + y / R * .5f)));
                                    b.Fill((x, y) => RSd.Poly(x, y, R * .4f, R * .7f, -R * .1f, R * .95f, -R * .1f, R * .45f), hair); // 刘海
                                    b.Fill((x, y) => RSd.Seg(x, y, -R * .25f, R * .95f, -R * .25f, R * .05f, R * .12f), ink); // 耳机头梁
                                    b.Fill((x, y) => RSd.Ell(x, y, -R * .3f, -R * .1f, R * .32f, R * .4f) - OL * .6f, ink);
                                    b.Fill((x, y) => RSd.Ell(x, y, -R * .3f, -R * .1f, R * .32f, R * .4f), H(0x2a2a3a));
                                    b.Fill((x, y) => RSd.Ell(x, y, -R * .26f, -R * .1f, R * .16f, R * .22f), L.Accent);
                                    break;
                                }
                            case 1: // xp：寸头
                                b.Fill((x, y) => RSd.I(face(x, y) + R * .02f, R * .38f - y + x * .25f), Color.Lerp(hair, L.Main2, .35f));
                                break;
                            case 3: // ccld：刘海 + 头巾 + 飘带
                                b.Fill((x, y) => RSd.U(RSd.Poly(x, y, R * .85f, R * .4f, R * .4f, R * .95f, R * .1f, R * .3f), RSd.Poly(x, y, R * .35f, R * .6f, -R * .1f, R * .95f, -R * .2f, R * .35f)), hair);
                                b.Fill((x, y) => RSd.Box(x, y, 0, R * .42f, R * .98f, R * .14f, R * .08f) - OL * .5f, ink);
                                b.Fill((x, y) => RSd.Box(x, y, 0, R * .42f, R * .98f, R * .14f, R * .08f), L.Dark);
                                b.Fill((x, y) => RSd.U(RSd.Seg(x, y, -R * .9f, R * .42f, -R * 1.5f, R * .2f, R * .1f, R * .06f), RSd.Seg(x, y, -R * .9f, R * .38f, -R * 1.45f, -R * .1f, R * .1f, R * .05f)), L.Dark);
                                break;
                            case 5: case 13: // kai / kai-改：刘海刺 + 头带
                                {
                                    var band = L.Id == 5 ? H(0xf4fbff) : H(0xff3a4a);
                                    b.Fill((x, y) => RSd.U(RSd.Poly(x, y, R * .95f, R * .35f, R * .45f, R * .95f, R * .2f, R * .35f), RSd.Poly(x, y, R * .45f, R * .5f, R * .05f, R * 1.05f, -R * .2f, R * .4f)), hair);
                                    b.Fill((x, y) => RSd.Box(x, y, 0, R * .4f, R * .98f, R * .13f, R * .06f) - OL * .5f, ink);
                                    b.Fill((x, y) => RSd.Box(x, y, 0, R * .4f, R * .98f, R * .13f, R * .06f), (x, y) => L.Id == 13 && x < 0 ? H(0x3a7aff) : band);
                                    b.Fill((x, y) => RSd.U(RSd.Seg(x, y, -R * .9f, R * .4f, -R * 1.45f, R * .1f, R * .09f, R * .05f), RSd.Seg(x, y, -R * .9f, R * .36f, -R * 1.35f, -R * .2f, R * .09f, R * .05f)), L.Id == 13 ? H(0x3a7aff) : band);
                                    if (L.Id == 5) b.Fill((x, y) => RSd.Poly(x, y, R * .05f, R * .5f, -R * .12f, R * .4f, -R * .02f, R * .4f, -R * .12f, R * .3f, R * .1f, R * .42f, R * 0, R * .42f), L.Accent);
                                    break;
                                }
                            case 7: // px：鸭舌帽 + 护目镜
                                {
                                    RBitmap.Sdf cap = (x, y) => RSd.U(RSd.Sub(RSd.Ell(x, y, -R * .05f, R * .25f, R * 1.0f, R * .82f), RSd.Box(x, y, 0, -R * .4f, R * 2, R * .5f)), RSd.Box(x, y, R * .85f, R * .18f, R * .55f, R * .1f, R * .08f));
                                    b.Fill((x, y) => cap(x, y) - OL, ink); b.Fill(cap, (x, y) => Color.Lerp(L.Main2, L.Main, Mathf.Clamp01(.5f + y / R * .6f)));
                                    b.Fill((x, y) => RSd.Box(x, y, -R * .1f, R * .38f, R * .9f, R * .08f, R * .04f), H(0x3a2a1a));
                                    for (int k = 0; k < 2; k++) { float gx = R * (.05f + k * .42f); b.Fill((x, y) => RSd.Circ(x, y, gx, R * .45f, R * .2f), ink); b.Fill((x, y) => RSd.Circ(x, y, gx, R * .45f, R * .14f), (x, y) => Color.Lerp(L.Accent, Color.white, Mathf.Clamp01((y - R * .45f) / R * 3 + .3f))); }
                                    break;
                                }
                            case 8: // ccld-改：银白长发 + 金色发饰 + 红色眼影
                                b.Fill((x, y) => RSd.U(RSd.Seg(x, y, R * .75f, R * .55f, R * .92f, -R * .45f, R * .13f, R * .05f), RSd.Poly(x, y, R * .95f, R * .3f, R * .55f, R * .85f, R * .2f, R * .5f)) - OL * .4f, H(0x6a7498));
                                b.Fill((x, y) => RSd.U(RSd.Seg(x, y, R * .75f, R * .55f, R * .92f, -R * .45f, R * .13f, R * .05f), RSd.Poly(x, y, R * .95f, R * .3f, R * .55f, R * .85f, R * .2f, R * .5f)), (x, y) => Color.Lerp(H(0xb8c0dc), hair, Mathf.Clamp01(.5f + y / R)));
                                b.Fill((x, y) => RSd.Seg(x, y, -R * .2f, R * .82f, -R * .55f, R * .55f, R * .09f), L.Accent);
                                b.Fill((x, y) => RSd.Circ(x, y, -R * .2f, R * .82f, R * .12f), H(0xff4d5e));
                                if (!hurt) b.Fill((x, y) => RSd.Seg(x, y, ex + er * .3f, ey - er * 1.5f, ex + er * 1.2f, ey - er * 1.3f, R * .04f), new Color(1, .3f, .35f, .8f));
                                break;
                            case 9: // DVD-改：短刺发 + 青紫墨镜
                                b.Fill((x, y) => RSd.U(RSd.Poly(x, y, R * .85f, R * .45f, R * .35f, R * .95f, R * .1f, R * .35f), RSd.Poly(x, y, R * .3f, R * .6f, -R * .25f, R * 1.0f, -R * .3f, R * .35f)), hair);
                                b.Fill((x, y) => RSd.Box(x, y, R * .45f, R * .05f, R * .58f, R * .2f, R * .14f) - OL * .5f, ink);
                                b.Fill((x, y) => RSd.Box(x, y, R * .45f, R * .05f, R * .58f, R * .2f, R * .14f), (x, y) => Color.Lerp(H(0x7ff3ff), H(0x9a3aff), Mathf.Clamp01(.5f + (x - y) / R)));
                                b.Fill((x, y) => RSd.Seg(x, y, R * .1f, R * .16f, R * .5f, R * .16f, R * .04f), new Color(1, 1, 1, .7f));
                                b.Fill((x, y) => RSd.Seg(x, y, -R * .55f, R * .1f, -R * .1f, R * .05f, R * .06f), ink);
                                break;
                            case 10: // xp-改：头顶一撮呆毛
                                b.Fill((x, y) => RSd.Seg(x, y, 0, R * .85f, R * .25f, R * 1.25f, R * .1f, R * .04f) - OL * .5f, ink);
                                b.Fill((x, y) => RSd.Seg(x, y, 0, R * .85f, R * .25f, R * 1.25f, R * .1f, R * .04f), hair);
                                break;
                            case 12: // gary-改：乱发 + 额头上的大护目镜
                                b.Fill((x, y) => RSd.U(RSd.Poly(x, y, R * .9f, R * .4f, R * .4f, R * .95f, R * .05f, R * .35f), RSd.Poly(x, y, R * .4f, R * .7f, -R * .3f, R * 1.0f, -R * .3f, R * .4f)), hair);
                                b.Fill((x, y) => RSd.Box(x, y, -R * .1f, R * .5f, R * .95f, R * .08f, R * .04f), H(0x3a2a1a));
                                for (int k = 0; k < 2; k++) { float gx = R * (.1f + k * .45f); b.Fill((x, y) => RSd.Circ(x, y, gx, R * .52f, R * .24f), ink); b.Fill((x, y) => RSd.Circ(x, y, gx, R * .52f, R * .18f), (x, y) => Color.Lerp(H(0x2a8a7a), H(0xaaffee), Mathf.Clamp01((y - R * .52f) / R * 3 + .4f))); b.Fill((x, y) => RSd.Circ(x, y, gx + R * .06f, R * .58f, R * .05f), Color.white); }
                                break;
                            case 15: // 剑神：金发 + 光冠
                                b.Fill((x, y) => RSd.U(RSd.Poly(x, y, R * .9f, R * .4f, R * .4f, R * .98f, R * .05f, R * .3f), RSd.Poly(x, y, R * .35f, R * .65f, -R * .2f, R * 1.05f, -R * .3f, R * .3f)), hair);
                                b.Fill((x, y) => RSd.Poly(x, y, -R * .5f, R * .7f, R * .5f, R * .7f, R * .35f, R * .95f, R * .15f, R * .8f, 0, R * 1.15f, -R * .15f, R * .8f, -R * .35f, R * .95f) - OL * .4f, ink);
                                b.Fill((x, y) => RSd.Poly(x, y, -R * .5f, R * .7f, R * .5f, R * .7f, R * .35f, R * .95f, R * .15f, R * .8f, 0, R * 1.15f, -R * .15f, R * .8f, -R * .35f, R * .95f), H(0xffd860));
                                break;
                        }
                        break;
                    }
            }
            b.Fill((x, y) => RSd.Ell(x, y, -R * .2f, R * .62f, R * .35f, R * .12f), new Color(1, 1, 1, .18f), 3); // 高光
            if (hurt) P.Fill[RP.HeadHurt] = b; else P.Fill[RP.Head] = b;
        }
        static Color H(uint c) => RigLook.H(c);

        // ------------------------------ 身体 ------------------------------
        static void Torso(RigParts P)
        {
            var L = P.L; float W = P.TorsoW, Ht = P.TorsoH, hh = Ht / 2; var b = New(W * 1.3f, Ht * 1.15f);
            float shoulder = W / 2, waist = W / 2 * (L.Belly ? 1.05f : .84f);
            RBitmap.Sdf sh = (x, y) => { float k = Mathf.Clamp01((y + hh) / Ht); float hw = Mathf.Lerp(waist, shoulder, k); return RSd.Box(x, y, 0, 0, hw, hh, Mathf.Min(hw, hh) * .45f); };
            if (L.Belly) { var s0 = sh; sh = (x, y) => RSd.SU(s0(x, y), RSd.Ell(x, y, W * .12f, -Ht * .1f, W * .55f, Ht * .45f), 2f * P.S); }
            b.Fill((x, y) => sh(x, y) - OL, Ink);
            Color m = L.Main, m2 = L.Main2;
            b.Fill(sh, (x, y) => Color.Lerp(m2, m, Mathf.Clamp01(.55f + x / W * .8f + y / Ht * .3f)));
            switch (L.Id)
            {
                case 0: // 连帽衫：口袋 + 胸口小光盘
                    b.Fill((x, y) => RSd.Box(x, y, W * .08f, -Ht * .25f, W * .3f, Ht * .14f, P.S), Mul(m, .82f));
                    b.Fill((x, y) => RSd.Circ(x, y, W * .15f, Ht * .15f, Ht * .14f), Color.white); b.Fill((x, y) => RSd.Circ(x, y, W * .15f, Ht * .15f, Ht * .05f), m2);
                    b.Fill((x, y) => RSd.Seg(x, y, -W * .05f, hh, -W * .1f, Ht * .05f, P.S * .35f), Color.white); break;
                case 1: case 10: // 胖子：背心 / 背带裤 + 浅色肚皮
                    b.Fill((x, y) => RSd.I(RSd.Ell(x, y, W * .2f, -Ht * .12f, W * .36f, Ht * .32f), sh(x, y)), Lt(L.Skin, .25f));
                    if (L.Id == 1) b.Fill((x, y) => RSd.I(RSd.Box(x, y, 0, hh - P.S * .6f, W, P.S * .9f), sh(x, y)), m2);
                    else { b.Fill((x, y) => RSd.I(RSd.Box(x, y, 0, -hh + Ht * .22f, W, Ht * .24f), sh(x, y)), L.Pants); b.Fill((x, y) => RSd.U(RSd.Seg(x, y, -W * .25f, -hh + Ht * .3f, -W * .3f, hh, P.S * .7f), RSd.Seg(x, y, W * .25f, -hh + Ht * .3f, W * .3f, hh, P.S * .7f)), L.Pants); b.Fill((x, y) => RSd.Circ(x, y, W * .26f, -hh + Ht * .38f, P.S * .7f), H(0xffd23c)); }
                    b.Fill((x, y) => RSd.Circ(x, y, W * .3f, -Ht * .15f, P.S * .45f), Mul(L.Skin, .7f)); break;
                case 2: case 11: // 长袍：中缝金边 + 胸口符号
                    b.Fill((x, y) => RSd.Box(x, y, W * .12f, 0, P.S * .7f, hh), L.Accent);
                    b.Fill((x, y) => Mathf.Abs(RSd.Circ(x, y, W * .12f, Ht * .12f, Ht * .17f)) - P.S * .35f, L.Id == 2 ? H(0x08020f) : L.Accent);
                    b.Fill((x, y) => RSd.Circ(x, y, W * .12f, Ht * .12f, Ht * .07f), L.Id == 2 ? L.Accent : Color.white);
                    b.Fill((x, y) => RSd.Box(x, y, 0, -hh + Ht * .1f, W, P.S * .8f), L.Dark); break;
                case 3: case 15: // 剑客：交叉衣领 + 腰带 + 红色腰带结
                    b.Fill((x, y) => RSd.U(RSd.Seg(x, y, -W * .2f, hh, W * .2f, Ht * .05f, P.S * .7f), RSd.Seg(x, y, W * .25f, hh, W * .05f, Ht * .15f, P.S * .55f)), L.Id == 3 ? H(0xfff2c0) : Color.white);
                    b.Fill((x, y) => RSd.I(RSd.Box(x, y, 0, -hh + Ht * .2f, W, P.S * 1.3f), sh(x, y)), L.Dark);
                    b.Fill((x, y) => RSd.Box(x, y, W * .2f, -hh + Ht * .2f, P.S * 1.1f, P.S * 1.1f, P.S * .3f), L.Accent);
                    if (L.Id == 15) { b.Fill((x, y) => RSd.I(RSd.Box(x, y, 0, hh - P.S, W, P.S * 1.4f), sh(x, y)), H(0xffe27a)); b.Fill((x, y) => RSd.Circ(x, y, W * .12f, Ht * .1f, P.S * 1.3f), H(0xff6a1a)); }
                    break;
                case 4: // 刺客：X 形绑带 + 红围巾
                    b.Fill((x, y) => RSd.I(RSd.U(RSd.Seg(x, y, -W * .5f, hh, W * .5f, -hh, P.S * .55f), RSd.Seg(x, y, W * .5f, hh, -W * .5f, -hh, P.S * .55f)), sh(x, y)), L.Dark);
                    b.Fill((x, y) => RSd.I(RSd.Box(x, y, 0, hh - P.S * .9f, W, P.S * 1.4f, P.S * .5f), sh(x, y) - OL), L.Accent);
                    b.Fill((x, y) => RSd.I(RSd.Box(x, y, 0, -hh + Ht * .12f, W, P.S * .8f), sh(x, y)), L.Dark); break;
                case 5: // 道服：白边交领 + 黑带
                    b.Fill((x, y) => RSd.U(RSd.Seg(x, y, -W * .25f, hh, W * .22f, -Ht * .05f, P.S * .7f), RSd.Seg(x, y, W * .3f, hh, W * .1f, Ht * .1f, P.S * .55f)), Color.white);
                    b.Fill((x, y) => RSd.Poly(x, y, -W * .02f, Ht * .2f, -W * .22f, -Ht * .02f, -W * .1f, -Ht * .02f, -W * .25f, -Ht * .25f, W * .02f, Ht * .02f, -W * .08f, Ht * .02f), L.Accent);
                    b.Fill((x, y) => RSd.I(RSd.Box(x, y, 0, -hh + Ht * .15f, W, P.S * 1.0f), sh(x, y)), L.Dark); break;
                case 6: // 触手怪身体：吸盘
                    for (int i = 0; i < 4; i++) { float cx = W * (.28f - (i % 2) * .22f), cy = Ht * (.25f - i * .17f); b.Fill((x, y) => RSd.Circ(x, y, cx, cy, P.S * .9f), L.Accent); b.Fill((x, y) => RSd.Circ(x, y, cx, cy, P.S * .4f), L.Main2); }
                    break;
                case 7: // 猎人夹克：子弹带 + 拉链
                    b.Fill((x, y) => RSd.I(RSd.Seg(x, y, -W * .5f, hh, W * .5f, -hh * .7f, P.S * 1.0f), sh(x, y)), H(0x6a4a2a));
                    for (int i = 0; i < 4; i++) { float u = (i + .5f) / 4f, cx = Mathf.Lerp(-W * .4f, W * .4f, u), cy = Mathf.Lerp(hh * .85f, -hh * .55f, u); b.Fill((x, y) => RSd.Box(x, y, cx, cy, P.S * .35f, P.S * .7f, P.S * .2f), H(0xd84a2a)); }
                    b.Fill((x, y) => RSd.Box(x, y, W * .3f, 0, P.S * .2f, hh), Mul(m, .7f));
                    b.Fill((x, y) => RSd.I(RSd.Box(x, y, 0, -hh + Ht * .1f, W, P.S * .8f), sh(x, y)), H(0x3a2a1a)); break;
                case 8: // ccld-改：和服 V 领金边 + 黑腰带
                    b.Fill((x, y) => RSd.U(RSd.Seg(x, y, -W * .3f, hh, W * .18f, -Ht * .05f, P.S * .7f), RSd.Seg(x, y, W * .3f, hh, W * .05f, Ht * .12f, P.S * .55f)), L.Accent);
                    b.Fill((x, y) => RSd.I(RSd.Box(x, y, 0, -hh + Ht * .18f, W, P.S * 1.4f), sh(x, y)), L.Dark);
                    b.Fill((x, y) => RSd.I(RSd.Box(x, y, 0, -hh + Ht * .18f, W, P.S * .25f), sh(x, y)), L.Accent); break;
                case 9: // DVD-改：科技夹克 + 胸口 DVD 椭圆标
                    b.Fill((x, y) => RSd.Box(x, y, W * .28f, 0, P.S * .25f, hh), L.Accent);
                    b.Fill((x, y) => RSd.Ell(x, y, W * .05f, Ht * .12f, W * .22f, Ht * .1f), Color.white);
                    b.Fill((x, y) => RSd.Ell(x, y, W * .05f, Ht * .12f, W * .1f, Ht * .035f), m2);
                    b.Fill((x, y) => RSd.I(RSd.Box(x, y, 0, hh - P.S * .5f, W, P.S * .7f), sh(x, y)), L.Accent); break;
                case 12: // 机关师工装：工具腰带
                    b.Fill((x, y) => RSd.Box(x, y, W * .2f, Ht * .1f, W * .18f, Ht * .14f, P.S * .3f), Mul(m, .8f));
                    b.Fill((x, y) => RSd.I(RSd.Box(x, y, 0, -hh + Ht * .18f, W, P.S * 1.1f), sh(x, y)), H(0x4a3a2a));
                    b.Fill((x, y) => RSd.Box(x, y, W * .1f, -hh + Ht * .18f, P.S * .9f, P.S * .9f, P.S * .2f), L.Accent);
                    b.Fill((x, y) => RSd.Box(x, y, -W * .22f, -hh + Ht * .1f, P.S * .7f, P.S * 1.4f, P.S * .2f), H(0xc8c8d8)); break;
                case 13: // kai-改：磁铁胸标（红蓝）
                    b.Fill((x, y) => RSd.U(RSd.Seg(x, y, -W * .25f, hh, W * .2f, -Ht * .05f, P.S * .65f), RSd.Seg(x, y, W * .3f, hh, W * .1f, Ht * .1f, P.S * .5f)), L.Main);
                    b.Fill((x, y) => RSd.Sub(RSd.Circ(x, y, W * .14f, -Ht * .02f, Ht * .2f), RSd.Box(x, y, W * .14f, Ht * .15f, Ht * .12f, Ht * .2f)), (x, y) => x < W * .14f ? H(0xff3a4a) : H(0x3a7aff));
                    b.Fill((x, y) => RSd.I(RSd.Box(x, y, 0, -hh + Ht * .12f, W, P.S * .9f), sh(x, y)), H(0x15151e)); break;
                case 14: // 蘑菇人：苔藓斑
                    for (int i = 0; i < 5; i++) { float cx = W * (.3f - (i % 3) * .25f), cy = Ht * (.3f - i * .14f); b.Fill((x, y) => RSd.I(RSd.Circ(x, y, cx, cy, P.S * 1.1f), sh(x, y)), L.Accent); }
                    break;
            }
            b.Fill((x, y) => RSd.I(RSd.Ell(x, y, W * .2f, hh * .55f, W * .25f, Ht * .1f), sh(x, y)), new Color(1, 1, 1, .2f), 2);
            P.Fill[RP.Torso] = b;
        }

        // ------------------------------ 四肢 ------------------------------
        static RBitmap Capsule(float len, float t, Color c, Color c2, float taper = 1, Color? cuff = null, float cuffAt = -1)
        {
            var b = New(len + t, t); float r0 = t / 2, r1 = t / 2 * taper, ax = -len / 2, bx = len / 2;
            b.Fill((x, y) => RSd.Seg(x, y, ax, 0, bx, 0, r0, r1) - OL, Ink);
            b.Fill((x, y) => RSd.Seg(x, y, ax, 0, bx, 0, r0, r1), (x, y) => Color.Lerp(c2, c, Mathf.Clamp01(.5f + y / t * 1.4f)));
            if (cuff.HasValue) { var cc = cuff.Value; float cx = Mathf.Lerp(ax, bx, cuffAt); b.Fill((x, y) => RSd.I(RSd.Seg(x, y, ax, 0, bx, 0, r0, r1), Mathf.Abs(x - cx) - t * .22f), cc); }
            b.Fill((x, y) => RSd.Seg(x, y, ax + t * .2f, t * .22f, bx - t * .2f, t * .22f, t * .08f), new Color(1, 1, 1, .22f), 2);
            return b;
        }
        static void Limbs(RigParts P)
        {
            var L = P.L; float s = P.S;
            Color sl = L.Sleeve, sl2 = Mul(sl, .78f), arm = L.ShortSleeve ? L.Skin : sl;
            if (L.Arms == RArms.Tentacle)
            {
                P.Fill[RP.UArm] = Capsule(P.ArmU, P.ArmT, L.Skin, L.Main2, .85f);
                var f = Capsule(P.ArmF, P.ArmT * .85f, L.Skin, L.Main2, .6f);
                for (int i = 0; i < 3; i++) { float cx = -P.ArmF / 2 + P.ArmF * (.2f + i * .28f); f.Fill((x, y) => RSd.Circ(x, y, cx, -P.ArmT * .18f, P.ArmT * .16f), L.Accent); }
                P.Fill[RP.FArm] = f;
                var h = New(P.HandR * 2.4f, P.HandR * 2.4f); float R = P.HandR;
                h.Fill((x, y) => Mathf.Abs(RSd.Circ(x, y, 0, 0, R * .7f)) - R * .32f - OL, Ink);
                h.Fill((x, y) => RSd.Sub(Mathf.Abs(RSd.Circ(x, y, 0, 0, R * .7f)) - R * .32f, RSd.Poly(x, y, 0, 0, R * 2, -R * .2f, R * 2, -R * 2)), L.Skin);
                P.Fill[RP.Hand] = h;
            }
            else
            {
                P.Fill[RP.UArm] = Capsule(P.ArmU, P.ArmT, L.ShortSleeve ? L.Skin : sl, L.ShortSleeve ? Mul(L.Skin, .8f) : sl2, 1, L.ShortSleeve ? sl : (Color?)null, .1f);
                P.Fill[RP.FArm] = Capsule(P.ArmF, P.ArmT * .92f, arm, Mul(arm, .8f), .95f, L.Id == 5 ? L.Glove : (Color?)null, .9f);
                P.Fill[RP.Hand] = HandBmp(P, L.Id == 13 ? H(0xff3a4a) : L.Glove, L.Id == 5 || L.Id == 13);
                if (L.Id == 13) P.Fill[RP.Hand2] = HandBmp(P, H(0x3a7aff), true);
            }
            Color pc = L.Pants, pc2 = Mul(pc, .75f);
            P.Fill[RP.Thigh] = Capsule(P.LegU, P.LegT, pc, pc2);
            P.Fill[RP.Shin] = Capsule(P.LegF, P.LegT * .9f, pc, pc2, .9f, L.Id == 3 || L.Id == 8 ? (Color?)H(0xf4f0e0) : null, .75f);
            // 鞋：朝 +x，鞋底平
            var fb = New(P.FootL, P.FootH); float fl = P.FootL, fh = P.FootH;
            RBitmap.Sdf ft = (x, y) => RSd.U(RSd.Box(x, y, 0, -fh * .15f, fl / 2, fh * .35f, fh * .3f), RSd.Ell(x, y, fl * .12f, fh * .05f, fl * .38f, fh * .48f));
            fb.Fill((x, y) => ft(x, y) - OL, Ink);
            fb.Fill(ft, (x, y) => Color.Lerp(Mul(L.Shoe, .75f), L.Shoe, Mathf.Clamp01(.5f + y / fh)));
            fb.Fill((x, y) => RSd.I(RSd.Box(x, y, 0, -fh * .38f, fl, fh * .12f), ft(x, y)), Color.Lerp(L.Shoe, Color.white, .55f));
            P.Fill[RP.Foot] = fb;
        }
        static RBitmap HandBmp(RigParts P, Color g, bool wrap)
        {
            float R = P.HandR; var h = New(R * 2.3f, R * 2.1f);
            RBitmap.Sdf hs = (x, y) => RSd.U(RSd.Box(x, y, R * .05f, 0, R * .85f, R * .78f, R * .55f), RSd.Ell(x, y, -R * .35f, R * .45f, R * .35f, R * .28f));
            h.Fill((x, y) => hs(x, y) - OL, Ink);
            h.Fill(hs, (x, y) => Color.Lerp(Mul(g, .8f), g, Mathf.Clamp01(.5f + y / R * .7f)));
            h.Fill((x, y) => RSd.Seg(x, y, R * .25f, R * .05f, R * .25f, -R * .5f, R * .05f), Mul(g, .65f));
            if (wrap) h.Fill((x, y) => RSd.I(hs(x, y), Mathf.Abs(x + R * .45f) - R * .18f), Mul(g, .8f));
            return h;
        }

        // ------------------------------ 武器 ------------------------------
        static void Weapon(RigParts P)
        {
            var L = P.L; float s = P.S; RBitmap b = null;
            switch (L.Weapon)
            {
                case RWeapon.Sword: case RWeapon.GodSword:
                    {
                        bool god = L.Weapon == RWeapon.GodSword; float len = (god ? 32 : 26) * s, t = (god ? 6.5f : 5) * s; b = New(len, t * 1.6f); float x0 = -len / 2;
                        float gx = x0 + len * .17f; // 护手位置
                        RBitmap.Sdf blade = (x, y) => RSd.Poly(x, y, gx, -t * .28f, x0 + len * .9f, -t * .28f, x0 + len, 0, x0 + len * .9f, t * .28f, gx, t * .28f);
                        RBitmap.Sdf hilt = (x, y) => RSd.U(RSd.Box(x, y, x0 + len * .08f, 0, len * .08f, t * .14f, t * .1f), RSd.Box(x, y, gx, 0, t * .14f, t * .55f, t * .08f), RSd.Circ(x, y, x0 + t * .1f, 0, t * .2f));
                        b.Fill((x, y) => RSd.U(blade(x, y), hilt(x, y)) - OL, Ink);
                        b.Fill(blade, (x, y) => god ? Color.Lerp(H(0xffd860), H(0xfffbe8), Mathf.Clamp01(.5f + y / t * 2)) : Color.Lerp(H(0xa8b4c8), H(0xf4f8ff), Mathf.Clamp01(.5f + y / t * 2.2f)));
                        b.Fill((x, y) => RSd.Seg(x, y, gx + t * .4f, 0, x0 + len * .9f, 0, s * .25f), god ? new Color(1, 1, 1, .9f) : H(0x7a8aa0));
                        b.Fill(hilt, (x, y) => x < gx - t * .2f ? H(0x5a2a1a) : (god ? H(0xff9a1a) : H(0xe8b84a)));
                        P.WeaponLen = len; P.WeaponGrip = .1f; break;
                    }
                case RWeapon.Katana:
                    {
                        float len = 28 * s, t = 4.2f * s; b = New(len, t * 2.2f); float x0 = -len / 2, gx = x0 + len * .2f;
                        // 微微上弯的刀身
                        RBitmap.Sdf blade = (x, y) => { float u = Mathf.Clamp01((x - gx) / (len * .8f)); float c = u * u * t * .9f; float w = Mathf.Lerp(t * .3f, t * .05f, Mathf.Pow(u, 6)); return Mathf.Max(Mathf.Abs(y - c) - w, Mathf.Max(gx - x, x - (x0 + len))); };
                        RBitmap.Sdf hilt = (x, y) => RSd.U(RSd.Box(x, y, x0 + len * .1f, 0, len * .1f, t * .2f, t * .1f), RSd.Ell(x, y, gx, 0, t * .18f, t * .5f));
                        b.Fill((x, y) => RSd.U(blade(x, y), hilt(x, y)) - OL * .9f, Ink);
                        b.Fill(blade, (x, y) => Color.Lerp(H(0xb8c4d8), H(0xffffff), Mathf.Clamp01(.6f + (y - (Mathf.Clamp01((x - gx) / (len * .8f)) * Mathf.Clamp01((x - gx) / (len * .8f)) * t * .9f)) / t * 3)));
                        b.Fill(hilt, (x, y) => x < gx - t * .25f ? ((Mathf.Repeat(x / (s * 1.4f), 1) < .5f) ? H(0x1a1620) : H(0xf4f0e0)) : H(0xe8b84a));
                        P.WeaponLen = len; P.WeaponGrip = .12f; break;
                    }
                case RWeapon.Disc: case RWeapon.Spore:
                    {
                        float r = (L.Weapon == RWeapon.Spore ? 4.6f : 5.6f) * s; b = New(r * 2, r * 2);
                        if (L.Weapon == RWeapon.Spore)
                        {
                            b.Fill((x, y) => RSd.Circ(x, y, 0, 0, r) - OL, Ink);
                            b.Fill((x, y) => RSd.Circ(x, y, 0, 0, r), (x, y) => Color.Lerp(H(0x3a8a2a), H(0xd8ff8a), Mathf.Clamp01(.5f + (x + y) / r * .5f)));
                            for (int i = 0; i < 5; i++) { float a = i * 1.26f; b.Fill((x, y) => RSd.Circ(x, y, Mathf.Cos(a) * r * .5f, Mathf.Sin(a) * r * .5f, r * .16f), H(0xf4ffd0)); }
                        }
                        else
                        {
                            b.Fill((x, y) => RSd.Circ(x, y, 0, 0, r) - OL, Ink);
                            b.Fill((x, y) => RSd.Circ(x, y, 0, 0, r), (x, y) => { float a = Mathf.Atan2(y, x); return Color.Lerp(Color.white, new Color(.75f + .25f * Mathf.Sin(a * 2), .75f + .25f * Mathf.Sin(a * 2 + 2), .75f + .25f * Mathf.Sin(a * 2 + 4)), .6f); });
                            b.Fill((x, y) => Mathf.Abs(RSd.Circ(x, y, 0, 0, r * .62f)) - s * .2f, new Color(.55f, .55f, .65f, .6f));
                            b.Fill((x, y) => RSd.Circ(x, y, 0, 0, r * .22f), Ink);
                            b.Fill((x, y) => RSd.Circ(x, y, 0, 0, r * .12f), new Color(.2f, .2f, .25f));
                        }
                        P.WeaponLen = r * 2; P.WeaponGrip = .5f; break;
                    }
                case RWeapon.Staff:
                    {
                        bool white = L.Id == 11; float len = 30 * s, t = 2.4f * s, R = 4.2f * s; b = New(len + R, R * 2.4f); float x0 = -(len + R) / 2, x1 = x0 + len;
                        RBitmap.Sdf shaft = (x, y) => RSd.Seg(x, y, x0 + t, 0, x1 - R * .5f, 0, t / 2);
                        RBitmap.Sdf claw = (x, y) => RSd.U(RSd.Seg(x, y, x1 - R * 1.2f, 0, x1, R * .95f, t * .4f, t * .2f), RSd.Seg(x, y, x1 - R * 1.2f, 0, x1, -R * .95f, t * .4f, t * .2f));
                        b.Fill((x, y) => RSd.U(shaft(x, y), claw(x, y)) - OL, Ink);
                        b.Fill(shaft, (x, y) => white ? Color.Lerp(H(0xa8b8d0), H(0xf4f8ff), Mathf.Clamp01(.5f + y / t)) : Color.Lerp(H(0x2a1a14), H(0x6a4a3a), Mathf.Clamp01(.5f + y / t)));
                        b.Fill(claw, white ? H(0x6ab8ff) : H(0xc77dff));
                        float ox = x1 + R * .05f;
                        b.Fill((x, y) => RSd.Circ(x, y, ox, 0, R * 1.05f), white ? new Color(.7f, .85f, 1, .35f) : new Color(.6f, .3f, 1, .35f), 3);
                        b.Fill((x, y) => RSd.Circ(x, y, ox, 0, R * .78f) - OL * .7f, white ? H(0x6ab8ff) : H(0xc77dff));
                        b.Fill((x, y) => RSd.Circ(x, y, ox, 0, R * .78f), (x, y) => white ? Color.Lerp(H(0xffffff), H(0xcfe6ff), Mathf.Clamp01(RSd.Circ(x, y, ox, 0, 0) / R)) : Color.Lerp(H(0x000000), H(0x3a0a5a), Mathf.Clamp01(RSd.Circ(x, y, ox, 0, 0) / R * 1.3f)));
                        b.Fill((x, y) => Mathf.Abs(RSd.Circ(x, y, ox, 0, R * .45f)) - s * .25f, white ? new Color(.6f, .8f, 1, .8f) : new Color(.78f, .5f, 1, .7f));
                        P.WeaponLen = len + R; P.WeaponGrip = .42f; break;
                    }
                case RWeapon.Kunai:
                    {
                        float len = 11 * s, t = 3.4f * s; b = New(len, t); float x0 = -len / 2;
                        RBitmap.Sdf bl = (x, y) => RSd.Poly(x, y, x0 + len * .38f, -t * .45f, x0 + len, 0, x0 + len * .38f, t * .45f);
                        RBitmap.Sdf hd = (x, y) => RSd.U(RSd.Box(x, y, x0 + len * .2f, 0, len * .17f, t * .15f), Mathf.Abs(RSd.Circ(x, y, x0 + t * .25f, 0, t * .2f)) - s * .25f);
                        b.Fill((x, y) => RSd.U(bl(x, y), hd(x, y)) - OL * .8f, Ink);
                        b.Fill(bl, (x, y) => Color.Lerp(H(0x9aa4b8), H(0xf0f4ff), Mathf.Clamp01(.5f + y / t * 2)));
                        b.Fill(hd, L.Main);
                        P.WeaponLen = len; P.WeaponGrip = .2f; break;
                    }
                case RWeapon.Shotgun:
                    {
                        float len = 25 * s, t = 5.2f * s; b = New(len, t * 1.6f); float x0 = -len / 2;
                        RBitmap.Sdf stock = (x, y) => RSd.Poly(x, y, x0, -t * .55f, x0 + len * .3f, -t * .2f, x0 + len * .3f, t * .3f, x0, t * .1f);
                        RBitmap.Sdf body = (x, y) => RSd.Box(x, y, x0 + len * .42f, t * .05f, len * .14f, t * .32f, s * .4f);
                        RBitmap.Sdf barrel = (x, y) => RSd.U(RSd.Box(x, y, x0 + len * .74f, t * .18f, len * .26f, t * .14f), RSd.Box(x, y, x0 + len * .7f, -t * .1f, len * .2f, t * .13f));
                        RBitmap.Sdf pump = (x, y) => RSd.Box(x, y, x0 + len * .68f, -t * .12f, len * .09f, t * .2f, s * .4f);
                        b.Fill((x, y) => RSd.U(stock(x, y), body(x, y), RSd.U(barrel(x, y), pump(x, y))) - OL, Ink);
                        b.Fill(stock, H(0x8a5a2a)); b.Fill(barrel, (x, y) => Color.Lerp(H(0x3a3e48), H(0x8a92a4), Mathf.Clamp01(.5f + (y - t * .1f) / t * 2))); b.Fill(body, H(0x4a4e58)); b.Fill(pump, H(0x6a4a2a));
                        b.Fill((x, y) => RSd.Box(x, y, x0 + len * .94f, t * .36f, s * .4f, s * .5f), L.Accent);
                        P.WeaponLen = len; P.WeaponGrip = .32f; P.WeaponGrip2 = .68f; break;
                    }
                case RWeapon.Wrench:
                    {
                        float len = 15 * s, t = 2.6f * s, R = 3.0f * s; b = New(len + R, R * 2.2f); float x0 = -(len + R) / 2, x1 = x0 + len;
                        RBitmap.Sdf w = (x, y) => RSd.U(RSd.Seg(x, y, x0 + t, 0, x1, 0, t / 2), RSd.Sub(RSd.Circ(x, y, x1 + R * .3f, 0, R), RSd.Box(x, y, x1 + R * 1.1f, 0, R * .7f, R * .38f)));
                        b.Fill((x, y) => w(x, y) - OL, Ink);
                        b.Fill(w, (x, y) => Color.Lerp(H(0x8a92a4), H(0xe8ecf4), Mathf.Clamp01(.5f + y / R)));
                        b.Fill((x, y) => RSd.Box(x, y, x0 + len * .25f, 0, len * .22f, t * .55f, t * .3f), H(0xffd23c));
                        P.WeaponLen = len + R; P.WeaponGrip = .2f; break;
                    }
            }
            P.Fill[RP.Weapon] = b;
        }

        // ------------------------------ 背后：披风 / 围巾 / 马尾 ------------------------------
        static void Back(RigParts P)
        {
            var L = P.L; float s = P.S; RBitmap b = null; Color c = L.BackC, c2 = Mul(L.BackC, .72f);
            switch (L.Back)
            {
                case RBack.Cape: // 竖向贴图，上端是系绳处
                    {
                        float len = 22 * s * L.Scale, w = 16 * s; b = New(w * 1.3f, len); P.BackLen = len;
                        RBitmap.Sdf cp = (x, y) => RSd.Sub(RSd.Poly(x, y, -w * .3f, len / 2, w * .3f, len / 2, w * .62f, -len / 2 + s, -w * .62f, -len / 2 + s), RSd.Circ(x, y, 0, -len / 2 - s * 1.2f, w * .22f));
                        b.Fill((x, y) => cp(x, y) - OL, Ink);
                        b.Fill(cp, (x, y) => Color.Lerp(c2, c, Mathf.Clamp01(.5f + x / w)));
                        b.Fill((x, y) => RSd.Seg(x, y, w * .1f, len * .4f, w * .2f, -len * .4f, s * .3f), Mul(c, .8f));
                        if (L.Id == 15) b.Fill((x, y) => RSd.I(RSd.Box(x, y, 0, -len / 2 + s * 1.2f, w, s * .6f), cp(x, y) + s), H(0xffd860));
                        break;
                    }
                case RBack.Scarf: // 横向贴图，右端系在脖子上，左端飘
                    {
                        float len = 15 * s, t = 3.2f * s; b = New(len, t * 1.6f); P.BackLen = len;
                        RBitmap.Sdf sc = (x, y) => { float u = (x + len / 2) / len; float w = Mathf.Lerp(t * .35f, t * .5f, u); float cy = Mathf.Sin(u * 7) * t * .25f * (1 - u); return Mathf.Max(Mathf.Abs(y - cy) - w, Mathf.Abs(x) - len / 2); };
                        b.Fill((x, y) => sc(x, y) - OL, Ink); b.Fill(sc, (x, y) => Color.Lerp(c2, c, Mathf.Clamp01(.5f + y / t)));
                        break;
                    }
                case RBack.Ponytail: // 竖向，上端系在后脑
                    {
                        float len = 16 * s, w = 5.4f * s; b = New(w * 1.4f, len); P.BackLen = len;
                        RBitmap.Sdf pt = (x, y) => { float u = (len / 2 - y) / len; float ww = Mathf.Lerp(w * .32f, w * .5f, Mathf.Sin(Mathf.Clamp01(u) * 2.6f)) * (1 - .6f * u * u); float cx = Mathf.Sin(u * 3.5f) * w * .2f; return Mathf.Max(Mathf.Abs(x - cx) - ww, Mathf.Abs(y) - len / 2); };
                        b.Fill((x, y) => pt(x, y) - OL, Ink); b.Fill(pt, (x, y) => Color.Lerp(H(0xb8c0dc), c, Mathf.Clamp01(.6f + x / w)));
                        b.Fill((x, y) => RSd.Box(x, y, 0, len / 2 - s * 1.2f, w * .4f, s * .6f, s * .3f), L.Accent);
                        break;
                    }
                case RBack.HoodTail: // 法师兜帽后摆（竖向）
                    {
                        float len = 11 * s, w = 8 * s; b = New(w, len); P.BackLen = len;
                        RBitmap.Sdf ht = (x, y) => RSd.Poly(x, y, -w * .35f, len / 2, w * .35f, len / 2, w * .1f, -len / 2, -w * .3f, -len / 2 + s);
                        b.Fill((x, y) => ht(x, y) - OL, Ink); b.Fill(ht, (x, y) => Color.Lerp(c2, c, Mathf.Clamp01(.5f + x / w)));
                        break;
                    }
            }
            P.Fill[RP.Back] = b;
        }
        static void Skirt(RigParts P) // 长袍 / 袴：挂在腰上，盖住大腿
        {
            var L = P.L; float s = P.S, w = P.TorsoW * (L.Id == 8 ? 1.0f : 1.05f), len = P.LegU * (L.Id == 8 ? 1.15f : 1.35f); var b = New(w * 1.4f, len);
            RBitmap.Sdf sk = (x, y) => RSd.Poly(x, y, -w * .42f, len / 2, w * .42f, len / 2, w * .62f, -len / 2 + s * .5f, -w * .62f, -len / 2 + s * .5f);
            b.Fill((x, y) => sk(x, y) - OL, Ink);
            Color c = L.Id == 8 ? L.Pants : L.Main, c2 = Mul(c, .72f);
            b.Fill(sk, (x, y) => Color.Lerp(c2, c, Mathf.Clamp01(.5f + x / w)));
            b.Fill((x, y) => RSd.I(RSd.Box(x, y, 0, -len / 2 + s * 1.1f, w, s * .5f), sk(x, y)), L.Accent);
            if (L.Id == 8) b.Fill((x, y) => RSd.Seg(x, y, 0, len / 2, 0, -len / 2, s * .25f), Mul(c, .6f));
            P.Fill[RP.Skirt] = b;
        }
        static void Extra(RigParts P) // ccld-改 腰间刀鞘
        {
            var L = P.L; float s = P.S;
            if (L.Id == 8)
            {
                float len = 20 * s, t = 2.6f * s; var b = New(len, t);
                RBitmap.Sdf sc = (x, y) => RSd.Seg(x, y, -len / 2 + t / 2, 0, len / 2 - t / 2, 0, t / 2);
                b.Fill((x, y) => sc(x, y) - OL * .8f, Ink); b.Fill(sc, (x, y) => Color.Lerp(H(0x1a1620), H(0x4a3a5a), Mathf.Clamp01(.5f + y / t)));
                b.Fill((x, y) => RSd.I(sc(x, y), Mathf.Abs(x - len * .3f) - s * .5f), L.Accent);
                b.Fill((x, y) => RSd.I(sc(x, y), Mathf.Abs(x + len * .4f) - s * .5f), L.Accent);
                P.Fill[RP.Extra] = b;
            }
        }
    }

    // ======================================================================
    // 姿势
    // ======================================================================
    public struct RigIn
    {
        public float T, Vx, Vy;       // Vx：相对朝向（向前为正）；Vy：向上为正
        public bool Ground;
        public float Run;             // 跑步相位（弧度，外部累加）
        public float Swing;           // 普攻进度 0..1，<0 表示没在出手
        public int Step;              // 第几下（交替挥砍 / 左右拳）
        public int Combo;             // ccld-改 神化 5 段连招（1..5），0 = 普通
        public float Cast;            // 技能进度 0..1，<0 表示没有
        public float Hurt;            // 挨打 0..1
        public bool Stun;
        public int Wall;              // 贴墙滑：墙在前面 +1 / 后面 -1 / 0 无
        public bool Float;            // 神化漂浮
        public bool HideWeapon;       // 光盘已经扔出去了
        public float Hue;             // DVD 彩虹
    }
    public struct RigDraw { public int Part; public float X, Y, W, H, Rot; public bool Back; public bool FlipY; }

    public static class RigPose
    {
        static Vector2 V(float x, float y) => new Vector2(x, y);
        static Vector2 Dir(float deg) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
        static float Ease(float k) { k = Mathf.Clamp01(k); return k * k * (3 - 2 * k); }
        static float EaseOut(float k) { k = Mathf.Clamp01(k); return 1 - (1 - k) * (1 - k) * (1 - k); }

        struct Pose
        {
            public Vector2 Hip; public float Lean, Tilt, Spin;
            public Vector2 HandF, HandB, FootF, FootB; // 手：相对各自肩膀；脚：绝对（脚踝）
            public float WAng; public bool Hurt;
            public float FootAngF, FootAngB;
        }
        static Pose Lerp(Pose a, Pose b, float k)
        {
            if (k <= 0) return a; if (k >= 1) { b.Hurt = b.Hurt || a.Hurt; return b; }
            var r = a; r.Hip = Vector2.Lerp(a.Hip, b.Hip, k); r.Lean = Mathf.Lerp(a.Lean, b.Lean, k); r.Tilt = Mathf.Lerp(a.Tilt, b.Tilt, k); r.Spin = Mathf.Lerp(a.Spin, b.Spin, k);
            r.HandF = Vector2.Lerp(a.HandF, b.HandF, k); r.HandB = Vector2.Lerp(a.HandB, b.HandB, k); r.FootF = Vector2.Lerp(a.FootF, b.FootF, k); r.FootB = Vector2.Lerp(a.FootB, b.FootB, k);
            r.WAng = Mathf.LerpAngle(a.WAng, b.WAng, k); r.FootAngF = Mathf.Lerp(a.FootAngF, b.FootAngF, k); r.FootAngB = Mathf.Lerp(a.FootAngB, b.FootAngB, k); r.Hurt = a.Hurt || (b.Hurt && k > .4f);
            return r;
        }

        /// <summary>两段 IK：从 root 伸到 target，返回关节位置（bend=+1 关节朝前 / -1 朝后）。</summary>
        static Vector2 IK(Vector2 root, ref Vector2 target, float a, float b, float bend)
        {
            Vector2 d = target - root; float len = d.magnitude; float max = (a + b) * .999f, min = Mathf.Abs(a - b) + .01f;
            if (len < 1e-4f) { d = V(0, -1); len = min; }
            float L = Mathf.Clamp(len, min, max); Vector2 n = d / len; target = root + n * L;
            float c = Mathf.Clamp((a * a + L * L - b * b) / (2 * a * L), -1, 1), ang = Mathf.Acos(c), baseA = Mathf.Atan2(n.y, n.x);
            // bend>0：关节在 root→target 方向的「前面」（+x 一侧）
            float a1 = baseA + ang, a2 = baseA - ang; Vector2 j1 = root + V(Mathf.Cos(a1), Mathf.Sin(a1)) * a, j2 = root + V(Mathf.Cos(a2), Mathf.Sin(a2)) * a;
            return (bend > 0 ? (j1.x >= j2.x ? j1 : j2) : (j1.x < j2.x ? j1 : j2));
        }

        /// <summary>摆出姿势。输出从后到前的部件列表（坐标：脚底中心为原点，x 朝前，y 向上，旋转为逆时针角度）。</summary>
        public static void Build(RigParts P, RigIn s, List<RigDraw> outList)
        {
            outList.Clear();
            var L = P.L; float S = P.S;
            float ankleY = P.FootH * .55f, legLen = P.LegU + P.LegF, armLen = P.ArmU + P.ArmF;
            float hipY0 = ankleY + legLen * (.94f - L.Crouch * .5f);
            var w = L.Weapon;
            // ---------------- 基础：站立 / 待机 ----------------
            var b = new Pose { Hip = V(0, hipY0), Lean = .04f + L.Crouch, Tilt = 0 };
            float br = Mathf.Sin(s.T * 2.3f); b.Hip.y += br * .35f * S; b.Tilt = br * .02f;
            b.FootF = V(3.0f * S, ankleY); b.FootB = V(-2.8f * S, ankleY);
            float r = armLen;
            switch (w)
            {
                case RWeapon.Sword: case RWeapon.GodSword: b.HandF = V(.45f * r, -.62f * r); b.WAng = 38; b.HandB = V(-.15f * r, -.9f * r); break;
                case RWeapon.Katana: b.HandF = V(.3f * r, -.78f * r); b.WAng = -28; b.HandB = V(.1f * r, -.85f * r); break;
                case RWeapon.Disc: case RWeapon.Spore: b.HandF = V(.42f * r, -.6f * r); b.WAng = 0; b.HandB = V(-.12f * r, -.92f * r); break;
                case RWeapon.Staff: b.HandF = V(.45f * r, -.45f * r); b.WAng = 96; b.HandB = V(-.1f * r, -.9f * r); break;
                case RWeapon.Kunai: b.HandF = V(.5f * r, -.5f * r); b.WAng = -8; b.HandB = V(-.35f * r, -.7f * r); break;
                case RWeapon.Shotgun: b.WAng = 4; b.HandF = V(.62f * r, -.42f * r); b.HandB = V(.2f * r, -.5f * r); break;
                case RWeapon.Wrench: b.HandF = V(.4f * r, -.65f * r); b.WAng = 55; b.HandB = V(-.15f * r, -.9f * r); break;
                case RWeapon.Fists: if (L.Arms == RArms.Big) { b.HandF = V(.6f * r, -.35f * r); b.HandB = V(.45f * r, -.2f * r); } else { b.HandF = V(.55f * r, .05f * r); b.HandB = V(.4f * r, .18f * r); } b.Lean += .06f; break;
                default: b.HandF = V(.3f * r, -.8f * r); b.HandB = V(-.2f * r, -.85f * r); break; // 触手
            }
            if (L.Arms == RArms.Tentacle) { b.HandF += V(Mathf.Sin(s.T * 3.1f) * .12f * r, Mathf.Sin(s.T * 2.3f) * .1f * r); b.HandB += V(Mathf.Sin(s.T * 2.7f + 1) * .12f * r, 0); }
            else { b.HandF.y += br * .03f * r; b.HandB.y += br * .03f * r; }
            var pose = b;
            // ---------------- 移动 ----------------
            float spd = Mathf.Abs(s.Vx);
            if (s.Ground && spd > 30)
            {
                float k = Mathf.Clamp01(spd / 260f), ph = s.Run, dir = s.Vx >= 0 ? 1 : -1, stride = 6.2f * S * k * dir, lift = 4.2f * S * k;
                pose.FootF = V(Mathf.Cos(ph) * stride + .4f * S, ankleY + lift * Mathf.Max(0, Mathf.Sin(ph)));
                pose.FootB = V(Mathf.Cos(ph + Mathf.PI) * stride - .4f * S, ankleY + lift * Mathf.Max(0, Mathf.Sin(ph + Mathf.PI)));
                pose.FootAngF = Mathf.Sin(ph) > 0 ? -18 * Mathf.Cos(ph) * k : 0; pose.FootAngB = Mathf.Sin(ph + Mathf.PI) > 0 ? 18 * Mathf.Cos(ph) * k : 0;
                pose.Hip.y = hipY0 - 1.0f * S * k + Mathf.Abs(Mathf.Cos(ph)) * 1.1f * S * k;
                pose.Lean += .16f * k * dir;
                float sw = Mathf.Cos(ph) * k;
                // 空着的手前后摆；拿武器的手幅度小一点
                pose.HandB = Vector2.Lerp(pose.HandB, V(-sw * .55f * r, -.8f * r), .75f);
                if (w == RWeapon.Fists) { pose.HandF += V(sw * .12f * r, 0); pose.HandB = b.HandB + V(-sw * .12f * r, 0); }
                else if (w != RWeapon.Shotgun && w != RWeapon.Staff) pose.HandF += V(sw * .18f * r, 0);
            }
            else if (!s.Ground && !s.Float)
            {
                if (s.Wall != 0 && s.Vy < 0)
                {   // 贴墙滑：一只手扒墙，脚蹬墙，身子往外仰
                    float side = s.Wall;
                    pose.FootF = V(side * 4.5f * S, hipY0 - legLen * .7f); pose.FootB = V(side * 3.0f * S, hipY0 - legLen * .9f);
                    pose.HandB = V(side * .85f * r, .35f * r); pose.Lean = -side * .14f; pose.FootAngF = pose.FootAngB = side > 0 ? 70 : -70;
                }
                else if (s.Vy > 60)
                {   // 起跳：收腿，手往上带
                    float k = Mathf.Clamp01(s.Vy / 500f);
                    pose.FootF = V(3.2f * S, hipY0 - legLen * Mathf.Lerp(.8f, .55f, k)); pose.FootB = V(-2.2f * S, hipY0 - legLen * Mathf.Lerp(.85f, .7f, k));
                    pose.FootAngF = -25; pose.FootAngB = -35;
                    pose.HandB = V(-.35f * r, .55f * r); pose.Lean = .1f;
                }
                else
                {   // 下落：腿伸直微张，手张开
                    float k = Mathf.Clamp01(-s.Vy / 600f), fl = Mathf.Sin(s.T * 9) * .08f * k;
                    pose.FootF = V(2.6f * S, hipY0 - legLen * .93f); pose.FootB = V(-3.0f * S, hipY0 - legLen * .86f);
                    pose.FootAngF = -15; pose.FootAngB = -30;
                    pose.HandB = V(-.55f * r, (.25f + fl) * r); if (w == RWeapon.Fists) pose.HandF = V(.5f * r, (.25f - fl) * r);
                    pose.Lean = -.04f;
                }
            }
            if (s.Float)
            {   // 神化漂浮：双腿并拢、脚尖朝下，手舒展，刀尖朝下
                pose.Hip = V(0, hipY0 + Mathf.Sin(s.T * 1.6f) * 1.2f * S);
                pose.FootF = V(1.2f * S, pose.Hip.y - legLen * .97f); pose.FootB = V(-.6f * S, pose.Hip.y - legLen * .9f);
                pose.FootAngF = -60; pose.FootAngB = -70; pose.Lean = .05f + Mathf.Clamp(s.Vx / 2000f, -.15f, .15f);
                pose.HandB = V(-.5f * r, -.55f * r); if (w == RWeapon.Katana) { pose.HandF = V(.5f * r, -.6f * r); pose.WAng = -70; }
            }
            // ---------------- 普攻 ----------------
            if (s.Swing >= 0)
            {
                float u = s.Swing, e = EaseOut(u); var a = pose; float lunge = 0;
                bool alt = (s.Step & 1) == 1;
                switch (w)
                {
                    case RWeapon.Sword: case RWeapon.GodSword: case RWeapon.Katana: case RWeapon.Wrench: case RWeapon.None:
                        {
                            float sa, ea; // 手臂方向（相对肩膀的角度，0 = 正前方，90 = 正上方）
                            if (s.Combo > 0)
                            {
                                switch (s.Combo)
                                {
                                    case 1: sa = 175; ea = -5; a.Lean = Mathf.Lerp(-.05f, .15f, e); break;                            // 天幕横扫
                                    case 2: sa = 125; ea = -85; a.Lean = Mathf.Lerp(-.2f, .45f, e); lunge = 2 * S * e; break;      // 裁决下劈
                                    case 3: sa = 0; ea = 0; a.Spin = -360 * Ease(u); break;                                       // 星环回旋
                                    case 4: { sa = ea = 0; float j = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 3)); a.HandF = V((.45f + .55f * j) * r, .02f * r); a.WAng = 0; a.Lean = .15f + .1f * j; break; } // 光矛三连
                                    default: sa = 10; ea = -4; a.Lean = .5f * e; lunge = 6 * S * e; a.FootF = V(7 * S * e + 3 * S, ankleY); a.FootB = V(-8 * S * e - 2 * S, ankleY); a.Hip.y -= 2.2f * S * e; break; // 创世一击
                                }
                                if (s.Combo != 4) { float ang = Mathf.Lerp(sa, ea, e); a.HandF = Dir(ang) * (s.Combo == 5 ? 1.0f : .95f) * r; a.WAng = ang - (s.Combo == 1 ? 0 : 12); }
                                if (s.Combo == 3) { a.HandF = V(.95f * r, 0); a.WAng = 0; }
                            }
                            else
                            {
                                float sa0 = alt ? -65 : 118, ea0 = alt ? 95 : -58; float ang = Mathf.Lerp(sa0, ea0, e);
                                a.HandF = Dir(ang) * .95f * r; a.WAng = ang + (alt ? 12 : -12); a.Lean = Mathf.Lerp(-.08f, .26f, e); lunge = 1.5f * S * e;
                                if (w == RWeapon.None) { a.WAng = 0; } // 触手鞭打
                            }
                            a.HandB = Vector2.Lerp(pose.HandB, V(-.5f * r, -.35f * r), Mathf.Sin(u * Mathf.PI));
                            break;
                        }
                    case RWeapon.Fists:
                        {
                            float ext = u < .3f ? EaseOut(u / .3f) : 1 - Ease((u - .3f) / .7f) * .95f; var tgt = V(1.02f * r, .1f * r);
                            if (L.Arms == RArms.Big && alt) { a.HandF = Vector2.Lerp(V(-.2f * r, .9f * r), V(.95f * r, -.25f * r), e); a.Lean = Mathf.Lerp(-.15f, .35f, e); } // 胖子：抡圆了砸
                            else if (alt) { a.HandB = Vector2.Lerp(pose.HandB, tgt + V(.08f * r, 0), ext); a.HandF = pose.HandF + V(-.15f * r, -.05f * r) * ext; }
                            else { a.HandF = Vector2.Lerp(pose.HandF, tgt, ext); a.HandB = pose.HandB + V(-.12f * r, 0) * ext; }
                            a.Lean = Mathf.Max(a.Lean, pose.Lean + .2f * ext); lunge = 1.6f * S * ext;
                            break;
                        }
                    case RWeapon.Disc: case RWeapon.Spore:
                        {
                            float ang = Mathf.Lerp(-150, 15, e); a.HandF = Dir(ang) * .92f * r; a.Lean = Mathf.Lerp(-.12f, .3f, e); a.WAng = 0; lunge = 1.2f * S * e;
                            a.HandB = V(-.4f * r, .1f * r); break;
                        }
                    case RWeapon.Staff:
                        {
                            float k = Mathf.Sin(Mathf.Min(1, u * 1.4f) * Mathf.PI); a.HandF = Vector2.Lerp(pose.HandF, V(.85f * r, .05f * r), k); a.WAng = Mathf.Lerp(96, 18, k);
                            a.HandB = Vector2.Lerp(pose.HandB, V(.7f * r, .25f * r), k); a.Lean = pose.Lean + .18f * k; break;
                        }
                    case RWeapon.Kunai:
                        {
                            float ext = u < .25f ? EaseOut(u / .25f) : 1 - Ease((u - .25f) / .75f); a.HandF = Vector2.Lerp(pose.HandF, V(1.0f * r, -.05f * r), ext); a.WAng = Mathf.Lerp(pose.WAng, 0, ext);
                            a.Lean = pose.Lean + .2f * ext; lunge = 2 * S * ext; break;
                        }
                    case RWeapon.Shotgun:
                        {
                            float rc = u < .12f ? u / .12f : 1 - Ease((u - .12f) / .88f); a.WAng = pose.WAng + 24 * rc; a.HandF = pose.HandF + V(-.12f * r, .1f * r) * rc; a.HandB = pose.HandB + V(-.12f * r, .04f * r) * rc; a.Lean = pose.Lean - .12f * rc; break;
                        }
                }
                a.Hip.x += lunge; a.FootF.x += lunge * .6f;
                pose = a;
            }
            // ---------------- 技能 ----------------
            if (s.Cast >= 0)
            {
                float k = Mathf.Sin(Mathf.Clamp01(s.Cast) * Mathf.PI); var c = pose;
                c.HandF = V(.25f * r, .92f * r); c.HandB = V(-.25f * r, .85f * r); c.Lean = -.12f; c.Tilt = -.12f;
                switch (w)
                {
                    case RWeapon.Sword: case RWeapon.GodSword: case RWeapon.Katana: case RWeapon.Wrench: c.WAng = 88; break;
                    case RWeapon.Staff: c.HandF = V(.5f * r, .6f * r); c.WAng = 100; break;
                    case RWeapon.Shotgun: c.HandF = V(.55f * r, .2f * r); c.HandB = V(.25f * r, .05f * r); c.WAng = 40; c.Lean = -.05f; break;
                    case RWeapon.Fists: c.HandF = V(.7f * r, .5f * r); c.HandB = V(.5f * r, .6f * r); c.Lean = .1f; break;
                }
                pose = Lerp(pose, c, Ease(k * 1.4f));
            }
            // ---------------- 挨打 / 眩晕 ----------------
            if (s.Stun)
            {
                var c = pose; float wb = Mathf.Sin(s.T * 3.2f);
                c.Lean = .22f + wb * .05f; c.Tilt = wb * .28f; c.HandF = V(.15f * r, -.95f * r); c.HandB = V(-.1f * r, -.95f * r); c.Hip.y -= 1.5f * S; c.Hurt = true;
                if (w == RWeapon.Shotgun) { c.HandF = V(.3f * r, -.8f * r); c.WAng = -60; }
                pose = Lerp(pose, c, 1);
            }
            if (s.Hurt > 0)
            {
                var c = pose; c.Lean = -.38f; c.Tilt = -.32f; c.Hip.x -= 1.8f * S; c.Hurt = true;
                c.HandF = V(.5f * r, .5f * r); c.HandB = V(-.35f * r, .6f * r); if (w == RWeapon.Shotgun) c.WAng = 50;
                pose = Lerp(pose, c, Ease(Mathf.Min(1, s.Hurt * 1.6f)));
            }

            // ================= 骨骼 =================
            Vector2 hip = pose.Hip; float lean = pose.Lean;
            Vector2 up = V(Mathf.Sin(lean), Mathf.Cos(lean));
            Vector2 neck = hip + up * P.TorsoH; Vector2 headC = neck + V(Mathf.Sin(lean + pose.Tilt), Mathf.Cos(lean + pose.Tilt)) * (P.HeadR * .82f);
            Vector2 shF = neck - up * (2.2f * S) + V(Mathf.Cos(lean), -Mathf.Sin(lean)) * (1.2f * S), shB = neck - up * (2.2f * S) - V(Mathf.Cos(lean), -Mathf.Sin(lean)) * (1.4f * S);
            Vector2 hipF = hip + V(1.0f * S, 0), hipB = hip - V(1.0f * S, 0);
            Vector2 hF = shF + pose.HandF, hB = shB + pose.HandB, fF = pose.FootF, fB = pose.FootB;
            float armBend = L.Arms == RArms.Tentacle ? 1 : -1;
            Vector2 eF = IK(shF, ref hF, P.ArmU, P.ArmF, armBend), eB = IK(shB, ref hB, P.ArmU, P.ArmF, armBend);
            Vector2 kF = IK(hipF, ref fF, P.LegU, P.LegF, 1), kB = IK(hipB, ref fB, P.LegU, P.LegF, 1);
            // 着地时让最低的脚刚好踩在地上
            float shift = 0;
            if (s.Ground && !s.Float) { float lo = Mathf.Min(fF.y, fB.y) - ankleY; shift = -lo; }
            // 武器
            float wang = pose.WAng; Vector2 wc = default; float wl = P.WeaponLen;
            if (P.Fill[RP.Weapon] != null)
            {
                if (w == RWeapon.Shotgun)
                {   // 双手持枪：前手托护木，后手握枪把
                    var d = Dir(wang); wc = hF + d * (wl * (.5f - P.WeaponGrip2));
                    Vector2 grip = wc + d * (wl * (P.WeaponGrip - .5f)); hB = grip; eB = IK(shB, ref hB, P.ArmU, P.ArmF, -1);
                }
                else if (w == RWeapon.Disc || w == RWeapon.Spore) wc = hF + Dir(wang) * (wl * .25f);
                else { var d = Dir(wang); wc = hF + d * (wl * (.5f - P.WeaponGrip)); }
            }
            // 旋转（星环回旋整个人转一圈）
            float spin = pose.Spin; Vector2 piv = hip + up * (P.TorsoH * .4f);
            Func<Vector2, Vector2> X = q => { q.y += shift; if (spin != 0) { var o = q - (piv + V(0, shift)); float ca = Mathf.Cos(spin * Mathf.Deg2Rad), sa = Mathf.Sin(spin * Mathf.Deg2Rad); q = piv + V(0, shift) + V(o.x * ca - o.y * sa, o.x * sa + o.y * ca); } return q; };
            float leanDeg = -lean * Mathf.Rad2Deg;

            // ================= 输出（从后到前）=================
            Action<int, Vector2, Vector2, bool> Seg = (part, a0, a1, back) =>
            {
                var bm = P.Fill[part]; if (bm == null) return; a0 = X(a0); a1 = X(a1);
                Vector2 m = (a0 + a1) / 2, dd = a1 - a0; float len = dd.magnitude;
                float bone = bm.WW - bm.WH; // 胶囊贴图：长度 = 骨长 + 粗细（两端圆头）
                outList.Add(new RigDraw { Part = part, X = m.x, Y = m.y, W = len + (bm.WW - bone), H = bm.WH, Rot = Mathf.Atan2(dd.y, dd.x) * Mathf.Rad2Deg + spin, Back = back });
            };
            Action<int, Vector2, float, bool> Put = (part, c, rot, back) =>
            {
                var bm = P.Fill[part]; if (bm == null) return; c = X(c);
                outList.Add(new RigDraw { Part = part, X = c.x, Y = c.y, W = bm.WW, H = bm.WH, Rot = rot + spin, Back = back });
            };
            int handB = P.Fill[RP.Hand2] != null ? RP.Hand2 : RP.Hand;
            bool twoHand = w == RWeapon.Shotgun;
            // 背后：披风 / 围巾 / 马尾 / 兜帽后摆
            if (P.Fill[RP.Back] != null)
            {
                float vx = s.Vx, vy = s.Vy;
                switch (L.Back)
                {
                    case RBack.Cape:
                        {
                            float a = 14 + Mathf.Clamp(Mathf.Abs(vx) * .08f, 0, 58) + Mathf.Clamp(-vy * .02f, -10, 25) + Mathf.Sin(s.T * 4.2f) * (4 + Mathf.Abs(vx) * .01f) + lean * 30;
                            Vector2 at = neck - up * (.6f * S) - V(Mathf.Cos(lean), -Mathf.Sin(lean)) * (3.2f * S);
                            Vector2 d = V(-Mathf.Sin(a * Mathf.Deg2Rad), -Mathf.Cos(a * Mathf.Deg2Rad));
                            Put(RP.Back, at + d * (P.BackLen * .48f), -a, true); break;
                        }
                    case RBack.Scarf:
                        {
                            float a = 180 - 12 - Mathf.Clamp(Mathf.Abs(vx) * .02f, 0, 10) + Mathf.Clamp(-vy * .03f, -20, 30) + Mathf.Sin(s.T * 6f) * (6 + Mathf.Abs(vx) * .012f);
                            if (Mathf.Abs(vx) < 30 && s.Ground) a = 240 + Mathf.Sin(s.T * 2.2f) * 6; // 站着不动时垂下来
                            Vector2 at = neck - up * (.8f * S); Vector2 d = Dir(a);
                            Put(RP.Back, at + d * (P.BackLen * .46f), a + 180, true); break;
                        }
                    case RBack.Ponytail:
                        {
                            float a = 22 + Mathf.Clamp(Mathf.Abs(vx) * .06f, 0, 55) + Mathf.Clamp(-vy * .02f, -10, 30) + Mathf.Sin(s.T * 3.3f) * 5 + lean * 40;
                            Vector2 at = headC + V(-P.HeadR * .62f, P.HeadR * .25f);
                            Vector2 d = V(-Mathf.Sin(a * Mathf.Deg2Rad), -Mathf.Cos(a * Mathf.Deg2Rad));
                            Put(RP.Back, at + d * (P.BackLen * .45f), -a, true); break;
                        }
                    case RBack.HoodTail:
                        {
                            float a = 12 + Mathf.Clamp(Mathf.Abs(vx) * .04f, 0, 35) + Mathf.Sin(s.T * 3f) * 3;
                            Vector2 at = headC + V(-P.HeadR * .55f, -P.HeadR * .1f);
                            Vector2 d = V(-Mathf.Sin(a * Mathf.Deg2Rad), -Mathf.Cos(a * Mathf.Deg2Rad));
                            Put(RP.Back, at + d * (P.BackLen * .4f), -a, true); break;
                        }
                }
            }
            // 后手（拿着双手武器时在武器后面）
            Seg(RP.UArm, shB, eB, true); Seg(RP.FArm, eB, hB, true);
            if (!twoHand) Put(handB, hB, Mathf.Atan2(hB.y - eB.y, hB.x - eB.x) * Mathf.Rad2Deg, true);
            // 后腿
            Seg(RP.Thigh, hipB, kB, true); Seg(RP.Shin, kB, fB, true);
            Put(RP.Foot, fB + V(P.FootL * .22f, -ankleY * .25f), pose.FootAngB, true);
            // 刀鞘（腰后）
            if (P.Fill[RP.Extra] != null) { var sc = hip + up * (P.TorsoH * .12f) - V(.6f * S, 0); Put(RP.Extra, sc + Dir(200 + leanDeg) * (P.Fill[RP.Extra].WW * .32f), 200 + leanDeg, true); }
            // 身体
            Put(RP.Torso, hip + up * (P.TorsoH / 2), leanDeg, false);
            // 前腿
            Seg(RP.Thigh, hipF, kF, false); Seg(RP.Shin, kF, fF, false);
            Put(RP.Foot, fF + V(P.FootL * .22f, -ankleY * .25f), pose.FootAngF, false);
            // 长袍下摆（盖住大腿）
            if (P.Fill[RP.Skirt] != null) { float k = P.Fill[RP.Skirt].WH; float sway = Mathf.Clamp(-s.Vx * .02f, -14, 14) + (kF.x - kB.x) * .6f; Put(RP.Skirt, hip + V(Mathf.Sin((lean * Mathf.Rad2Deg + sway) * Mathf.Deg2Rad), -Mathf.Cos((lean * Mathf.Rad2Deg + sway) * Mathf.Deg2Rad)) * (k * .42f) + up * (1.2f * S), leanDeg - sway * .5f, false); }
            // 头
            Put(pose.Hurt ? RP.HeadHurt : RP.Head, headC, -(lean + pose.Tilt) * Mathf.Rad2Deg, false);
            // 双手武器：后手握把在枪后面画
            if (twoHand)
            {
                if (!s.HideWeapon && P.Fill[RP.Weapon] != null) Put(RP.Weapon, wc, wang, false);
                Put(handB, hB, Mathf.Atan2(hB.y - eB.y, hB.x - eB.x) * Mathf.Rad2Deg, false);
            }
            // 前手 + 武器（武器先画，手握在上面）
            if (!twoHand && !s.HideWeapon && P.Fill[RP.Weapon] != null) Put(RP.Weapon, wc, w == RWeapon.Disc || w == RWeapon.Spore ? s.T * -240 : wang, false);
            Seg(RP.UArm, shF, eF, false); Seg(RP.FArm, eF, hF, false);
            Put(RP.Hand, hF, Mathf.Atan2(hF.y - eF.y, hF.x - eF.x) * Mathf.Rad2Deg, false);
        }

        /// <summary>整套姿势的外接框（世界单位，原点在脚底）。用来烘焙静态图。</summary>
        public static void Bounds(RigParts P, List<RigDraw> list, out float x0, out float y0, out float x1, out float y1)
        {
            x0 = y0 = 1e9f; x1 = y1 = -1e9f;
            foreach (var d in list)
            {
                float ca = Mathf.Abs(Mathf.Cos(d.Rot * Mathf.Deg2Rad)), sa = Mathf.Abs(Mathf.Sin(d.Rot * Mathf.Deg2Rad));
                float ex = (d.W * ca + d.H * sa) / 2, ey = (d.W * sa + d.H * ca) / 2;
                x0 = Mathf.Min(x0, d.X - ex); x1 = Mathf.Max(x1, d.X + ex); y0 = Mathf.Min(y0, d.Y - ey); y1 = Mathf.Max(y1, d.Y + ey);
            }
        }

        /// <summary>把一套姿势合成成一张图（头像 / 静态贴图 / 预览）。图的中心对应姿势坐标 (cx, cy)。</summary>
        public static RBitmap Bake(RigParts P, List<RigDraw> list, float cx, float cy, float w, float h, float ppu, bool silhouette = false, float hue = -1)
        {
            var b = new RBitmap(w, h, ppu);
            foreach (var d in list)
            {
                var src = silhouette ? P.Sil[d.Part] : P.Fill[d.Part]; if (src == null) continue;
                var tint = d.Back && !silhouette ? new Color(.8f, .8f, .86f, 1) : Color.white;
                if (hue >= 0 && d.Part == RP.Weapon && P.L.Weapon == RWeapon.Disc && P.L.Id == 0) tint *= HueTint(hue);
                b.Composite(src, d.X - cx, d.Y - cy, d.W, d.H, d.Rot, tint, false);
            }
            return b;
        }
        public static Color HueTint(float hueDeg)
        {
            float h = Mathf.Repeat(hueDeg / 360f, 1) * 6; int i = Mathf.FloorToInt(h); float f = h - i;
            float q = 1 - f; Color c;
            switch (i % 6) { case 0: c = new Color(1, f, 0); break; case 1: c = new Color(q, 1, 0); break; case 2: c = new Color(0, 1, f); break; case 3: c = new Color(0, q, 1); break; case 4: c = new Color(f, 0, 1); break; default: c = new Color(1, 0, q); break; }
            return Color.Lerp(Color.white, c, .55f);
        }
    }
}
