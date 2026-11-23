// 原创程序化美术：所有贴图在启动时用代码画出来（扁平描边 + 高饱和角色色），不依赖任何外部素材。
using System;
using UnityEngine;
using Brawl.Core;

namespace Brawl.View
{
    /// <summary>一块 RGBA 浮点画布，支持用有符号距离场画抗锯齿形状。</summary>
    public sealed class Canvas
    {
        public readonly int W, H; readonly Color[] px;
        public Canvas(int w, int h) { W = w; H = h; px = new Color[w * h]; }
        public delegate float Sdf(float x, float y); // 坐标为 -1..1（以画布中心为原点，y 向上），返回距离（<0 在内部）
        public delegate Color Shade(float x, float y, float d);

        public void Draw(Sdf sdf, Shade shade, float feather = 0.02f)
        {
            for (int j = 0; j < H; j++)
                for (int i = 0; i < W; i++)
                {
                    float x = (i + .5f) / W * 2 - 1, y = (j + .5f) / H * 2 - 1;
                    float d = sdf(x, y); float a = Mathf.Clamp01(.5f - d / feather);
                    if (a <= 0) continue;
                    var c = shade(x, y, d); c.a *= a; Blend(i, j, c);
                }
        }
        public void Draw(Sdf sdf, Color c, float feather = 0.02f) => Draw(sdf, (x, y, d) => c, feather);
        void Blend(int i, int j, Color c)
        {
            ref Color o = ref px[j * W + i];
            float a = c.a + o.a * (1 - c.a);
            if (a <= 0) { o = default; return; }
            o = new Color((c.r * c.a + o.r * o.a * (1 - c.a)) / a, (c.g * c.a + o.g * o.a * (1 - c.a)) / a, (c.b * c.a + o.b * o.a * (1 - c.a)) / a, a);
        }
        public Texture2D ToTexture(bool repeat = false)
        {
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp };
            t.SetPixels(px); t.Apply(false, false); return t;
        }
        public Canvas Silhouette()
        {
            var c = new Canvas(W, H); for (int k = 0; k < px.Length; k++) c.px[k] = new Color(1, 1, 1, px[k].a); return c;
        }
    }

    public static class Sd
    {
        public static float Circle(float x, float y, float cx, float cy, float r) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;
        public static float Ellipse(float x, float y, float cx, float cy, float rx, float ry)
        { float dx = (x - cx) / rx, dy = (y - cy) / ry; float k = Mathf.Sqrt(dx * dx + dy * dy); return (k - 1) * Mathf.Min(rx, ry); }
        public static float Box(float x, float y, float cx, float cy, float hw, float hh, float r)
        { float qx = Mathf.Abs(x - cx) - hw + r, qy = Mathf.Abs(y - cy) - hh + r; return Mathf.Sqrt(Mathf.Max(qx, 0) * Mathf.Max(qx, 0) + Mathf.Max(qy, 0) * Mathf.Max(qy, 0)) + Mathf.Min(Mathf.Max(qx, qy), 0) - r; }
        public static float Star(float x, float y, float r, float inner, int n, float rot = Mathf.PI / 2)
        {
            float a = Mathf.Atan2(y, x) - rot; float seg = Mathf.PI * 2 / n; a = Mathf.Repeat(a + seg / 2, seg) - seg / 2;
            float len = Mathf.Sqrt(x * x + y * y);
            float px = Mathf.Cos(a) * len, py = Mathf.Abs(Mathf.Sin(a) * len);
            // 星形边：从 (r,0) 到 inner 角点的线段
            float ix = Mathf.Cos(seg / 2) * inner, iy = Mathf.Sin(seg / 2) * inner;
            float ex = ix - r, ey = iy; float t = Mathf.Clamp01(((px - r) * ex + py * ey) / (ex * ex + ey * ey));
            float dx = px - (r + ex * t), dy = py - ey * t; float dist = Mathf.Sqrt(dx * dx + dy * dy);
            float side = (px - r) * ey - py * ex; return side > 0 ? -dist : dist;
        }
        public static float Union(float a, float b) => Mathf.Min(a, b);
        public static float Sub(float a, float b) => Mathf.Max(a, -b);
    }

    public static class Art
    {
        public static Sprite Soft, Dot, Square, Ring, RingThin, Star4, Spark, Disc, HoleSpr, Swirl, Sword, SwordGlow, Wave, Wing, Halo, Bomb, Crown, Arrow, Item, Pixel;
        public static Sprite[] Body = new Sprite[Defs.Chars.Length], BodyWhite = new Sprite[Defs.Chars.Length];
        public static Sprite GodBody, GodWhite, DvdGroove, XpBelly, CcldSwordHand, Cloud, Kunai, Claw, Scarf, Fist;
        public static Material Alpha, Add;
        public static Texture2D White;
        static bool ready;

        public static Color C(uint rgb, float a = 1) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
        static Sprite S(Texture2D t, float ppu = 1) => Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(.5f, .5f), ppu, 0, SpriteMeshType.FullRect);
        /// <summary>精灵按 1 单位 = 1 像素 尺寸制作；size 是想要的世界尺寸（像素）。</summary>
        static Sprite Make(Canvas c, float worldW) => S(c.ToTexture(), c.W / worldW);

        public static void Init()
        {
            if (ready) return; ready = true;
            var sh = Shader.Find("Sprites/Default");
            Alpha = new Material(sh);
            var addSh = Shader.Find("Brawl/Additive");
            Add = addSh != null ? new Material(addSh) : new Material(sh);
            White = new Texture2D(4, 4); var wp = new Color[16]; for (int i = 0; i < 16; i++) wp[i] = Color.white; White.SetPixels(wp); White.Apply();
            Pixel = S(White, 4);

            // ---- 通用形状（白色，运行时着色） ----
            var cv = new Canvas(64, 64); cv.Draw((x, y) => Sd.Circle(x, y, 0, 0, 1), (x, y, d) => new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(-d), 1.6f)), .001f); Soft = Make(cv, 1);
            cv = new Canvas(32, 32); cv.Draw((x, y) => Sd.Circle(x, y, 0, 0, .9f), Color.white, .08f); Dot = Make(cv, 1);
            cv = new Canvas(16, 16); cv.Draw((x, y) => Sd.Box(x, y, 0, 0, .9f, .9f, .25f), Color.white, .1f); Square = Make(cv, 1);
            cv = new Canvas(128, 128); cv.Draw((x, y) => Mathf.Abs(Sd.Circle(x, y, 0, 0, .9f)) - .07f, Color.white, .025f); Ring = Make(cv, 1);
            cv = new Canvas(128, 128); cv.Draw((x, y) => Mathf.Abs(Sd.Circle(x, y, 0, 0, .95f)) - .025f, Color.white, .02f); RingThin = Make(cv, 1);
            cv = new Canvas(64, 64); cv.Draw((x, y) => Sd.Star(x, y, 1, .18f, 4), (x, y, d) => new Color(1, 1, 1, Mathf.Clamp01(1.2f - Mathf.Sqrt(x * x + y * y))), .03f); Star4 = Make(cv, 1);
            cv = new Canvas(64, 16); cv.Draw((x, y) => Sd.Ellipse(x, y, 0, 0, 1, .55f), (x, y, d) => new Color(1, 1, 1, Mathf.Clamp01(1 - Mathf.Abs(x))), .05f); Spark = Make(cv, 1);

            // ---- 武器与弹道 ----
            cv = new Canvas(64, 64);
            cv.Draw((x, y) => Sd.Circle(x, y, 0, 0, .95f), C(0x05060e), .04f);
            cv.Draw((x, y) => Sd.Circle(x, y, 0, 0, .82f), (x, y, d) => Color.Lerp(new Color(1, 1, 1), new Color(.75f, .78f, .95f), (x + y + 2) / 4), .04f);
            cv.Draw((x, y) => Mathf.Abs(Sd.Circle(x, y, 0, 0, .55f)) - .03f, new Color(.6f, .6f, .75f, .8f), .03f);
            cv.Draw((x, y) => Mathf.Abs(Sd.Circle(x, y, 0, 0, .38f)) - .02f, new Color(.6f, .6f, .75f, .6f), .03f);
            cv.Draw((x, y) => Sd.Circle(x, y, 0, 0, .16f), C(0x05060e), .04f);
            cv.Draw((x, y) => Sd.Ellipse(x, y, -.35f, .4f, .25f, .1f), new Color(1, 1, 1, .9f), .05f);
            Disc = Make(cv, 1);
            cv = new Canvas(64, 64);
            cv.Draw((x, y) => Sd.Circle(x, y, 0, 0, 1), (x, y, d) => { float r = Mathf.Sqrt(x * x + y * y); return r < .55f ? Color.black : Color.Lerp(new Color(.08f, .02f, .12f), new Color(.48f, .17f, 1f, 0), (r - .55f) / .45f); }, .02f);
            HoleSpr = Make(cv, 1);
            cv = new Canvas(128, 128);
            cv.Draw((x, y) => Mathf.Abs(Sd.Circle(x, y, 0, 0, .8f)) - .12f, (x, y, d) => { float a = Mathf.Atan2(y, x); float s = .5f + .5f * Mathf.Sin(a * 3); return new Color(.78f, .49f, 1, .25f + .6f * s); }, .05f);
            Swirl = Make(cv, 1);
            cv = new Canvas(32, 128); // 剑：竖直，剑尖朝上
            cv.Draw((x, y) => Sd.Union(Sd.Box(x, y, 0, .15f, .22f, .78f, .2f), Sd.Star(x * 1.4f, (y - .9f) * 1.0f, .22f, .0f, 2)), C(0x05060e), .05f);
            cv.Draw((x, y) => Sd.Box(x, y, 0, .18f, .12f, .72f, .1f), (x, y, d) => Color.Lerp(new Color(.85f, .92f, 1), Color.white, x + .5f), .05f);
            cv.Draw((x, y) => Sd.Box(x, y, 0, -.62f, .7f, .06f, .05f), C(0xffc531), .05f);
            cv.Draw((x, y) => Sd.Box(x, y, 0, -.82f, .14f, .16f, .08f), C(0x8a5a2a), .05f);
            Sword = Make(cv, 1);
            cv = new Canvas(32, 128); cv.Draw((x, y) => Sd.Box(x, y, 0, 0, .35f, .95f, .35f), (x, y, d) => new Color(1, 1, 1, Mathf.Clamp01(-d * 3)), .1f); SwordGlow = Make(cv, 1);
            cv = new Canvas(64, 128); // 剑气：新月形
            cv.Draw((x, y) => Sd.Sub(Sd.Ellipse(x, y, 0, 0, .9f, .98f), Sd.Ellipse(x, y, -.55f, 0, .9f, 1.1f)), (x, y, d) => new Color(1, 1, 1, Mathf.Clamp01(-d * 6)), .04f);
            Wave = Make(cv, 1);
            cv = new Canvas(128, 64); // 翅膀羽毛
            cv.Draw((x, y) => Sd.Ellipse(x, y, .1f, 0, .9f, .45f), (x, y, d) => new Color(1, 1, 1, Mathf.Clamp01(.4f - d * 4) * (1 - (x + 1) * .25f)), .05f);
            Wing = Make(cv, 1);
            cv = new Canvas(128, 48); cv.Draw((x, y) => Mathf.Abs(Sd.Ellipse(x, y, 0, 0, .9f, .7f)) - .08f, Color.white, .05f); Halo = Make(cv, 1);
            cv = new Canvas(48, 48);
            cv.Draw((x, y) => Sd.Circle(x, y, 0, -.1f, .78f), C(0x05060e), .04f);
            cv.Draw((x, y) => Sd.Circle(x, y, 0, -.1f, .66f), (x, y, d) => Color.Lerp(C(0x3a3d55), C(0x15161f), (y + 1) * -.3f + .5f), .04f);
            cv.Draw((x, y) => Sd.Box(x, y, .25f, .7f, .1f, .2f, .05f), C(0xffb02e), .05f);
            cv.Draw((x, y) => Sd.Circle(x, y, -.25f, .15f, .14f), new Color(1, 1, 1, .7f), .05f);
            Bomb = Make(cv, 1);
            cv = new Canvas(64, 48);
            cv.Draw((x, y) => Sd.Union(Sd.Box(x, y, 0, -.35f, .8f, .35f, .08f), Sd.Union(Sd.Star(x * 1.2f + .78f, y * 1.2f - .25f, .9f, .3f, 1, Mathf.PI / 2), Sd.Union(Sd.Star(x * 1.2f, y * 1.2f - .25f, .95f, .3f, 1, Mathf.PI / 2), Sd.Star(x * 1.2f - .78f, y * 1.2f - .25f, .9f, .3f, 1, Mathf.PI / 2)))), C(0xffd84a), .05f);
            Crown = Make(cv, 1);
            cv = new Canvas(32, 32); cv.Draw((x, y) => Sd.Star(x, y + .2f, .9f, .25f, 1, -Mathf.PI / 2), Color.white, .08f); Arrow = Make(cv, 1);
            cv = new Canvas(52, 48);
            cv.Draw((x, y) => Sd.Box(x, y, 0, 0, .92f, .9f, .25f), C(0x05060e), .05f);
            cv.Draw((x, y) => Sd.Box(x, y, 0, 0, .78f, .76f, .18f), (x, y, d) => new Color(1, 1, 1, 1) * (0.75f + .25f * y), .05f);
            cv.Draw((x, y) => Sd.Box(x, y, 0, .55f, .78f, .12f, .05f), new Color(1, 1, 1, .5f), .05f);
            Item = Make(cv, 1);
            cv = new Canvas(128, 64);
            cv.Draw((x, y) => Sd.Union(Sd.Circle(x, y, -.4f, -.1f, .45f), Sd.Union(Sd.Circle(x, y, .1f, .1f, .6f), Sd.Circle(x, y, .55f, -.15f, .4f))), (x, y, d) => new Color(1, 1, 1, .9f), .03f);
            Cloud = Make(cv, 1);

            BuildCharacters();
            // 新角色：先用原版贴图兜底（px 暂用 gary 的），NewCharArt 会覆盖成专属贴图
            for (int i = 0; i < Body.Length; i++) if (Body[i] == null) { var b = Defs.BaseOf((CharId)i); if (b == (CharId)i) b = CharId.GARY; Body[i] = Body[(int)b]; BodyWhite[i] = BodyWhite[(int)b]; }
            NewCharArt.Build(); // 第六版：px 贴图、「-改」换色、新图标
            CharRig.Init();     // 第十四版：分段角色造型（同时把 Body / BodyWhite 换成新造型的静态图）
        }

        static readonly Color Ink = new Color(.02f, .024f, .055f, 1);

        static void Eye(Canvas cv, float x0, float y0, float r)
        {
            cv.Draw((x, y) => Sd.Ellipse(x, y, x0, y0, r, r * 1.25f), Color.white, .04f);
            cv.Draw((x, y) => Sd.Circle(x, y, x0 + r * .3f, y0, r * .55f), Ink, .04f);
            cv.Draw((x, y) => Sd.Circle(x, y, x0 + r * .5f, y0 + r * .35f, r * .2f), Color.white, .04f);
        }

        static void BuildCharacters()
        {
            // 每个角色贴图比碰撞盒大一点，方便描边和装饰；所有角色朝右绘制，左转时水平翻转。
            // DVD：光盘精灵（灰白，运行时按彩虹色调着色）
            var cv = new Canvas(128, 104);
            cv.Draw((x, y) => Sd.Ellipse(x, y, 0, 0, .9f, .86f), Ink, .03f);
            cv.Draw((x, y) => Sd.Ellipse(x, y, 0, 0, .78f, .74f), (x, y, d) => Color.Lerp(Color.white, new Color(.72f, .72f, .78f), Mathf.Clamp01(-y * .6f + .3f)), .03f);
            cv.Draw((x, y) => Mathf.Abs(Sd.Ellipse(x, y, 0, 0, .55f, .5f)) - .025f, new Color(.55f, .55f, .62f, .7f), .03f);
            cv.Draw((x, y) => Mathf.Abs(Sd.Ellipse(x, y, 0, 0, .36f, .33f)) - .02f, new Color(.55f, .55f, .62f, .6f), .03f);
            cv.Draw((x, y) => Sd.Ellipse(x, y, 0, 0, .14f, .13f), Ink, .03f);
            cv.Draw((x, y) => Sd.Ellipse(x, y, -.32f, .38f, .3f, .1f), new Color(1, 1, 1, .85f), .05f);
            Eye(cv, .5f, .25f, .13f);
            SetBody(CharId.DVD, cv, 36 * 1.25f);
            // xp：胖胖的绿色圆球 + 浅色肚皮 + 腮红
            cv = new Canvas(128, 128);
            cv.Draw((x, y) => Sd.Ellipse(x, y, 0, -.04f, .92f, .86f), Ink, .03f);
            cv.Draw((x, y) => Sd.Ellipse(x, y, 0, -.04f, .8f, .74f), (x, y, d) => Color.Lerp(C(0x7dffab), C(0x22b85a), Mathf.Clamp01(-y * .7f + .4f)), .03f);
            cv.Draw((x, y) => Sd.Ellipse(x, y, .12f, -.22f, .5f, .42f), (x, y, d) => Color.Lerp(C(0xd8ffe4), C(0xa8ffc4), -y), .03f);
            cv.Draw((x, y) => Mathf.Abs(Sd.Circle(x, y, .12f, -.22f, .06f)) - .015f, C(0x2a8a4c), .03f);
            cv.Draw((x, y) => Sd.Ellipse(x, y, -.25f, .5f, .3f, .1f), new Color(1, 1, 1, .5f), .06f);
            cv.Draw((x, y) => Sd.Ellipse(x, y, .7f, .05f, .1f, .06f), new Color(1, .45f, .55f, .7f), .05f);
            Eye(cv, .38f, .3f, .11f); Eye(cv, .66f, .3f, .09f);
            SetBody(CharId.XP, cv, 46 * 1.25f);
            // bw：黑洞球 + 紫色光边 + 发光的眼睛
            cv = new Canvas(128, 128);
            cv.Draw((x, y) => Sd.Circle(x, y, 0, 0, .95f), (x, y, d) => new Color(.48f, .17f, 1, Mathf.Clamp01(1 + d * 6) * .9f), .05f);
            cv.Draw((x, y) => Sd.Circle(x, y, 0, 0, .78f), (x, y, d) => Color.Lerp(Color.black, C(0x2a0a40), Mathf.Clamp01(1 + d * 2.2f)), .03f);
            cv.Draw((x, y) => Mathf.Abs(Sd.Circle(x, y, 0, 0, .78f)) - .03f, C(0xc77dff), .03f);
            cv.Draw((x, y) => Sd.Ellipse(x, y, .3f, .18f, .12f, .07f), C(0xe9c8ff), .04f);
            cv.Draw((x, y) => Sd.Ellipse(x, y, .6f, .18f, .1f, .06f), C(0xe9c8ff), .04f);
            SetBody(CharId.BW, cv, 34 * 1.3f);
            // ccld 剑客形态：胶囊身体 + 头巾 + 披风
            cv = new Canvas(104, 128);
            cv.Draw((x, y) => Sd.Box(x, y, -.15f, -.1f, .55f, .62f, .4f), C(0x8a2a2a), .03f); // 披风
            cv.Draw((x, y) => Sd.Box(x, y, .05f, 0, .66f, .84f, .55f), Ink, .03f);
            cv.Draw((x, y) => Sd.Box(x, y, .05f, 0, .54f, .72f, .45f), (x, y, d) => Color.Lerp(C(0xffe08a), C(0xd99a1c), Mathf.Clamp01(-y * .6f + .4f)), .03f);
            cv.Draw((x, y) => Sd.Box(x, y, .05f, .38f, .56f, .09f, .03f), C(0x3a1d12), .03f); // 头巾
            cv.Draw((x, y) => Sd.Box(x, y, -.6f, .3f, .14f, .05f, .03f), C(0x3a1d12), .03f);
            cv.Draw((x, y) => Sd.Box(x, y, .05f, -.3f, .55f, .07f, .03f), C(0x3a1d12), .03f); // 腰带
            Eye(cv, .3f, .12f, .09f); Eye(cv, .55f, .12f, .08f);
            SetBody(CharId.CCLD, cv, 30 * 1.6f);
            // ccld 神化：十角金星
            cv = new Canvas(128, 128);
            cv.Draw((x, y) => Mathf.Max(Sd.Star(x, y, .95f, .5f, 5), Sd.Circle(x, y, 0, 0, .96f)), Ink, .03f);
            cv.Draw((x, y) => Mathf.Max(Sd.Star(x, y, .8f, .42f, 5), Sd.Circle(x, y, 0, 0, .81f)), (x, y, d) => Color.Lerp(C(0xfff2a8), C(0xffa514), Mathf.Clamp01(-y * .5f + .5f)), .03f);
            cv.Draw((x, y) => Mathf.Max(Sd.Star(x, y, .4f, .22f, 5), Sd.Circle(x, y, 0, 0, .41f)), new Color(1, 1, 1, .55f), .03f);
            Eye(cv, .14f, .1f, .07f); Eye(cv, .34f, .1f, .06f);
            GodBody = Make(cv, 64); GodWhite = Make(cv.Silhouette(), 64);
            // gary：蒙面刺客，深色头套 + 眼缝 + 红围巾
            cv = new Canvas(104, 112);
            cv.Draw((x, y) => Sd.Box(x, y, 0, -.02f, .74f, .84f, .5f), Ink, .03f);
            cv.Draw((x, y) => Sd.Box(x, y, 0, -.02f, .62f, .72f, .42f), (x, y, d) => Color.Lerp(C(0xff9d55), C(0xc4561c), Mathf.Clamp01(-y * .6f + .4f)), .03f);
            cv.Draw((x, y) => Sd.Box(x, y, .02f, .38f, .62f, .3f, .28f), C(0x2a2236), .03f); // 头套
            cv.Draw((x, y) => Sd.Box(x, y, .28f, .34f, .3f, .07f, .05f), C(0xfff2e0), .03f); // 眼缝
            cv.Draw((x, y) => Sd.Circle(x, y, .42f, .34f, .045f), Ink, .03f);
            cv.Draw((x, y) => Sd.Box(x, y, 0, .02f, .66f, .1f, .05f), C(0xe0283c), .03f); // 围巾
            cv.Draw((x, y) => Sd.Box(x, y, -.05f, -.45f, .62f, .05f, .03f), C(0x2a2236), .03f); // 腰带
            SetBody(CharId.GARY, cv, 28 * 1.55f);
            cv = new Canvas(64, 24); // 苦无，朝右
            cv.Draw((x, y) => Sd.Union(Sd.Star(x - .55f, y, .9f, .0f, 2, 0), Sd.Box(x, y, -.35f, 0, .45f, .14f, .05f)), Ink, .06f);
            cv.Draw((x, y) => Sd.Star(x - .55f, y, .8f, .0f, 2, 0), new Color(.9f, .93f, 1), .06f);
            cv.Draw((x, y) => Sd.Box(x, y, -.6f, 0, .3f, .1f, .03f), C(0xff8a3c), .06f);
            cv.Draw((x, y) => Mathf.Abs(Sd.Circle(x, y, -.95f, 0, .05f)) - .02f, C(0xff8a3c), .04f);
            Kunai = Make(cv, 1);
            cv = new Canvas(32, 32); // 钩爪
            cv.Draw((x, y) => Mathf.Max(Mathf.Abs(Sd.Circle(x, y, 0, 0, .6f)) - .14f, -x - .1f), C(0xffd27a), .08f);
            cv.Draw((x, y) => Sd.Circle(x, y, -.2f, 0, .22f), C(0x6a5a3a), .08f);
            Claw = Make(cv, 1);
            cv = new Canvas(64, 16); cv.Draw((x, y) => Sd.Box(x, y, 0, 0, .95f, .7f, .5f), C(0xe0283c), .1f); Scarf = Make(cv, 1);
            // kai：雷霆拳师，青色短发 + 白色头带 + 胸口闪电纹
            cv = new Canvas(104, 120);
            cv.Draw((x, y) => Sd.Box(x, y, 0, -.04f, .72f, .86f, .48f), Ink, .03f);
            cv.Draw((x, y) => Sd.Box(x, y, 0, -.04f, .6f, .74f, .4f), (x, y, d) => Color.Lerp(C(0x9ff8ff), C(0x1a9fd0), Mathf.Clamp01(-y * .6f + .4f)), .03f);
            cv.Draw((x, y) => Sd.Union(Sd.Circle(x, y, -.25f, .7f, .2f), Sd.Union(Sd.Circle(x, y, .05f, .78f, .22f), Sd.Circle(x, y, .32f, .7f, .18f))), C(0x1d3a8a), .03f); // 刺猬头
            cv.Draw((x, y) => Sd.Box(x, y, .02f, .44f, .62f, .07f, .03f), C(0xf4fbff), .03f); // 头带
            cv.Draw((x, y) => Sd.Box(x, y, -.66f, .4f, .1f, .04f, .02f), C(0xf4fbff), .03f);
            cv.Draw((x, y) => Mathf.Max(Mathf.Abs(x + y * .5f - .05f) - .07f, Mathf.Abs(y + .2f) - .28f), C(0xffe14a), .03f); // 闪电纹
            Eye(cv, .3f, .2f, .085f); Eye(cv, .54f, .2f, .075f);
            SetBody(CharId.KAI, cv, 30 * 1.55f);
            cv = new Canvas(32, 28); cv.Draw((x, y) => Sd.Box(x, y, 0, 0, .8f, .72f, .35f), Ink, .06f); cv.Draw((x, y) => Sd.Box(x, y, 0, 0, .62f, .54f, .3f), C(0xf4fbff), .06f);
            Fist = Make(cv, 1);
            // dzy：粉红色软体，大眼睛 + 嘴边小触须 + 身上吸盘
            cv = new Canvas(112, 112);
            cv.Draw((x, y) => Sd.Union(Sd.Ellipse(x, y, 0, .05f, .82f, .78f), Sd.Union(Sd.Box(x, y, -.5f, -.62f, .14f, .3f, .12f), Sd.Union(Sd.Box(x, y, 0, -.66f, .14f, .3f, .12f), Sd.Box(x, y, .5f, -.62f, .14f, .3f, .12f)))), Ink, .03f);
            cv.Draw((x, y) => Sd.Union(Sd.Ellipse(x, y, 0, .05f, .7f, .66f), Sd.Union(Sd.Box(x, y, -.5f, -.6f, .08f, .25f, .08f), Sd.Union(Sd.Box(x, y, 0, -.64f, .08f, .25f, .08f), Sd.Box(x, y, .5f, -.6f, .08f, .25f, .08f)))), (x, y, d) => Color.Lerp(C(0xff8ab0), C(0xb02a5a), Mathf.Clamp01(-y * .6f + .4f)), .03f);
            cv.Draw((x, y) => Sd.Union(Sd.Circle(x, y, -.32f, -.2f, .07f), Sd.Union(Sd.Circle(x, y, -.1f, -.32f, .06f), Sd.Circle(x, y, -.45f, .05f, .05f))), C(0xffd0de), .03f);
            cv.Draw((x, y) => Sd.Ellipse(x, y, -.2f, .48f, .28f, .08f), new Color(1, 1, 1, .45f), .05f);
            Eye(cv, .3f, .22f, .15f); Eye(cv, .62f, .2f, .1f);
            cv.Draw((x, y) => Sd.Ellipse(x, y, .5f, -.1f, .14f, .05f), C(0x5a0a28), .03f);
            SetBody(CharId.DZY, cv, 32 * 1.4f);
        }

        static void SetBody(CharId id, Canvas cv, float worldW)
        {
            Body[(int)id] = Make(cv, worldW); BodyWhite[(int)id] = Make(cv.Silhouette(), worldW);
        }

        // ================= 地形贴图 =================
        public static void PaintTerrain(Texture2D tex, Color32[] buf, byte[] tiles, MapDef m)
        {
            const int T = Defs.T; int W = m.W, H = m.H, COLS = m.Cols, ROWS = m.Rows; int r0 = 0, r1 = ROWS - 1, c0 = 0, c1 = COLS - 1;
            Color32 dirt = C(m.Dirt), dark = C(m.DirtDark), grass = C(m.Grass), gl = C(m.GrassLight), st = C(m.Stone), sl = C(m.StoneLight);
            Color32 clear = new Color32(0, 0, 0, 0), ink = new Color32(5, 6, 14, 255);
            Func<int, int, bool> Sol = (c, r) => c >= 0 && c < COLS && r >= 0 && r < ROWS && tiles[r * COLS + c] > 0;
            r0 = Math.Max(0, r0); c0 = Math.Max(0, c0); r1 = Math.Min(ROWS - 1, r1); c1 = Math.Min(COLS - 1, c1);
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                {
                    byte v = tiles[r * COLS + c];
                    bool up = Sol(c, r - 1), dn = Sol(c, r + 1), lf = Sol(c - 1, r), rt = Sol(c + 1, r);
                    for (int py = 0; py < T; py++)
                        for (int pxx = 0; pxx < T; pxx++)
                        {
                            int gx = c * T + pxx, gy = r * T + py; // gy 向下
                            Color32 col = clear;
                            if (v > 0)
                            {
                                uint h = Hash((uint)gx, (uint)gy);
                                if (v == 2)
                                {
                                    bool mortar = py % 10 == 0 || ((py / 10) % 2 == 0 ? pxx == 0 : pxx == 10);
                                    col = mortar ? dark : (py % 10 < 3 ? sl : st);
                                    if (!mortar && (h & 31) == 0) col = Lerp(col, ink, .2f);
                                }
                                else if (v == 3)
                                {
                                    Color32 ice = C(m.Ice), iceL = new Color32(235, 250, 255, 255);
                                    col = Lerp(ice, iceL, ((pxx + py) % 13 == 0) ? .6f : (py < 3 ? .5f : .0f));
                                    if ((h & 63) == 0) col = iceL;
                                }
                                else if (v == 4)
                                {   // 弹跳蘑菇：红伞白点
                                    col = py < 6 ? new Color32(255, 96, 110, 255) : new Color32(214, 52, 70, 255);
                                    int sx = (gx % 14) - 7, sy = (gy % 14) - 7; if (sx * sx + sy * sy < 9) col = new Color32(255, 240, 230, 255);
                                }
                                else if (v == 5 || v == 6)
                                {   // 传送带：深色皮带 + 箭头纹
                                    col = py < 4 ? new Color32(90, 96, 110, 255) : new Color32(40, 42, 52, 255);
                                    int k = v == 5 ? gx % 20 : 19 - gx % 20; if (py >= 6 && py <= 14 && Math.Abs(k - 8 - Math.Abs(py - 10)) < 2) col = new Color32(255, 170, 60, 255);
                                }
                                else
                                {
                                    col = dirt; int n = (int)(h % 97);
                                    if (n < 6) col = dark; else if (n < 9) col = Lerp(dirt, gl, .25f);
                                    if (((gx / 5 + gy / 7) & 7) == 0 && (h & 3) == 0) col = Lerp(col, dark, .5f);
                                    if (up && (Hash((uint)(gx / 4), (uint)(gy / 4)) % 41) == 0) col = Lerp(st, sl, ((gx + gy) & 3) / 3f); // 土里的小石子
                                    if (up && dn && (Hash((uint)(gx / 6), (uint)(gy / 3)) % 157) == 0) col = Lerp(dirt, gl, .55f); // 化石 / 矿物亮点
                                    if (!up) { int gh = 5 + (int)((Hash((uint)gx, 7) >> 3) % 3); if (py < gh) col = py < 2 ? gl : grass; else if (py < gh + 2) col = Lerp(dirt, ink, .35f); }
                                }
                                // 边缘描边与倒角
                                int e = 2;
                                if ((!lf && pxx < e) || (!rt && pxx >= T - e) || (!dn && py >= T - e) || (!up && py < 1)) col = ink;
                                else if ((!dn && py >= T - 5)) col = Lerp(col, ink, .35f);
                            }
                            else if (Sol(c, r + 1) && tiles[(r + 1) * COLS + c] == 1 && py >= T - 7)
                            {
                                // 地表上的小草、小花（只是装饰，不挡路）
                                uint g = Hash((uint)gx, 911);
                                int bladeH = (int)(g % 7);
                                if ((g & 3) != 0 && T - 1 - py < bladeH) col = ((g >> 4) & 1) == 0 ? grass : gl;
                                if ((Hash((uint)(gx / 3), (uint)r) % 23) == 0 && py >= T - 4 && py <= T - 2 && (gx % 3) != 1) col = ((Hash((uint)(gx / 3), 5u) & 1) == 0) ? new Color32(255, 220, 120, 255) : new Color32(255, 140, 200, 255);
                            }
                            else if (Sol(c, r - 1) && py < 4)
                            {
                                // 平台底部投影
                                col = new Color32(0, 0, 0, (byte)(70 - py * 16));
                            }
                            buf[(H - 1 - gy) * W + gx] = col;
                        }
                }
            tex.SetPixels32(buf); tex.Apply(false, false);
        }
        static Color32 Lerp(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, t);
        static Color32 C(uint rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        public static uint Hash(uint x, uint y) { uint h = x * 374761393u + y * 668265263u; h = (h ^ (h >> 13)) * 1274126177u; return h ^ (h >> 16); }

        // ================= 背景 =================
        public static Texture2D SkyTexture(MapDef m)
        {
            var t = new Texture2D(2, 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            Color top = C(m.SkyTop), bot = C(m.SkyBottom);
            for (int j = 0; j < 256; j++) { var c = Color.Lerp(bot, top, j / 255f); t.SetPixel(0, j, c); t.SetPixel(1, j, c); }
            t.Apply(); return t;
        }
        public static Texture2D Mountains(Color colorIn, int seed, float baseH, float amp, float alpha)
        {
            int w = 1024, h = 256; var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h]; Color32 col = colorIn; col.a = (byte)(alpha * 255);
            for (int i = 0; i < w; i++)
            {
                float x = i / (float)w * Mathf.PI * 2;
                float y = baseH + amp * (Mathf.Sin(x * 3 + seed) * .5f + Mathf.Sin(x * 7 + seed * 2.1f) * .25f + Mathf.Sin(x * 17 + seed * .7f) * .12f + Mathf.Sin(x * 41 + seed) * .05f);
                int top = Mathf.Clamp((int)(y * h), 0, h);
                for (int j = 0; j < top; j++) { var c = col; if (j > top - 3) c = Color32.Lerp(col, new Color32(255, 255, 255, col.a), .12f); px[j * w + i] = c; }
            }
            t.SetPixels32(px); t.Apply(); return t;
        }
        public static Sprite MakeSprite(Texture2D t, float worldW) => S(t, t.width / worldW);
    }
}
