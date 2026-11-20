// 第六版新角色的程序化美术：px 专属身体贴图、「-改」版按 CharDef.Color 重新上色、以及 NewCharView 用到的小图标。
// 在 Art.Init 末尾调用 NewCharArt.Build()（可重复调用，只会做一次）。
using UnityEngine;
using Brawl.Core;

namespace Brawl.View
{
    public static class NewCharArt
    {
        public static Sprite Plus, Minus, Reticle, Mine, Spike, Anchor, Shotgun, Hook, Spore, Leaf, Feather, Petal, Blindfold, Fang;
        public static bool Ready;

        static readonly Color Ink = new Color(.02f, .024f, .055f, 1);
        static Color C(uint rgb, float a = 1) => Art.C(rgb, a);
        static Sprite Make(Canvas c, float worldW)
        {
            var t = c.ToTexture();
            return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(.5f, .5f), c.W / worldW, 0, SpriteMeshType.FullRect);
        }

        public static void Build()
        {
            if (Ready) return; Ready = true;
            BuildIcons();
            BuildPx();
            for (int i = (int)CharId.CCLD2; i < Art.Body.Length && i < Defs.Chars.Length; i++) BuildRemix((CharId)i);
        }

        // ================= 小图标 =================
        static void BuildIcons()
        {
            // ＋ / －：粗体、带深色描边，远处也看得清
            var cv = new Canvas(64, 64);
            cv.Draw((x, y) => Sd.Union(Sd.Box(x, y, 0, 0, .86f, .3f, .12f), Sd.Box(x, y, 0, 0, .3f, .86f, .12f)), Ink, .04f);
            cv.Draw((x, y) => Sd.Union(Sd.Box(x, y, 0, 0, .7f, .16f, .08f), Sd.Box(x, y, 0, 0, .16f, .7f, .08f)), Color.white, .04f);
            Plus = Make(cv, 1);
            cv = new Canvas(64, 64);
            cv.Draw((x, y) => Sd.Box(x, y, 0, 0, .86f, .3f, .12f), Ink, .04f);
            cv.Draw((x, y) => Sd.Box(x, y, 0, 0, .7f, .16f, .08f), Color.white, .04f);
            Minus = Make(cv, 1);
            // 印记：准星（白色，运行时着色）
            cv = new Canvas(96, 96);
            cv.Draw((x, y) => Mathf.Abs(Sd.Circle(x, y, 0, 0, .62f)) - .07f, Color.white, .03f);
            cv.Draw((x, y) => Mathf.Min(Mathf.Min(Sd.Box(x, y, 0, .82f, .06f, .16f, .03f), Sd.Box(x, y, 0, -.82f, .06f, .16f, .03f)), Mathf.Min(Sd.Box(x, y, .82f, 0, .16f, .06f, .03f), Sd.Box(x, y, -.82f, 0, .16f, .06f, .03f))), Color.white, .03f);
            cv.Draw((x, y) => Sd.Circle(x, y, 0, 0, .1f), Color.white, .04f);
            Reticle = Make(cv, 1);
            // 地雷：扁圆铁壳 + 中间指示灯座（灯由运行时叠加）
            cv = new Canvas(64, 32);
            cv.Draw((x, y) => Sd.Ellipse(x, y, 0, -.2f, .95f, .7f), Ink, .05f);
            cv.Draw((x, y) => Sd.Ellipse(x, y, 0, -.2f, .82f, .55f), (x, y, d) => Color.Lerp(C(0x5a6a48), C(0x2c3524), Mathf.Clamp01(-y * .8f + .4f)), .05f);
            cv.Draw((x, y) => Sd.Box(x, y, 0, .35f, .22f, .3f, .08f), Ink, .05f);
            cv.Draw((x, y) => Sd.Box(x, y, -.5f, -.2f, .06f, .25f, .03f), C(0x8fd14f, .8f), .05f);
            cv.Draw((x, y) => Sd.Box(x, y, .5f, -.2f, .06f, .25f, .03f), C(0x8fd14f, .8f), .05f);
            Mine = Make(cv, 1);
            // 地钉：三根小尖刺
            cv = new Canvas(48, 32);
            cv.Draw((x, y) => Sd.Union(Sd.Star((x + .5f) * 1.3f, (y + .1f) * .8f, .9f, 0, 2), Sd.Union(Sd.Star(x * 1.3f, (y + .05f) * .7f, 1f, 0, 2), Sd.Star((x - .5f) * 1.3f, (y + .1f) * .8f, .9f, 0, 2))), Ink, .06f);
            cv.Draw((x, y) => Sd.Union(Sd.Star((x + .5f) * 1.5f, (y + .1f) * .9f, .8f, 0, 2), Sd.Union(Sd.Star(x * 1.5f, (y + .05f) * .8f, .9f, 0, 2), Sd.Star((x - .5f) * 1.5f, (y + .1f) * .9f, .8f, 0, 2))), Color.white, .06f);
            Spike = Make(cv, 1);
            // 锚点：发光小钉（钉头 + 钉身）
            cv = new Canvas(32, 32);
            cv.Draw((x, y) => Sd.Union(Sd.Circle(x, y, 0, 0, .55f), Sd.Box(x, y, -.55f, 0, .45f, .14f, .05f)), Ink, .06f);
            cv.Draw((x, y) => Sd.Circle(x, y, 0, 0, .4f), Color.white, .06f);
            Anchor = Make(cv, 1);
            // px 的霰弹枪：朝右，木托 + 双管
            cv = new Canvas(96, 32);
            cv.Draw((x, y) => Sd.Union(Sd.Box(x, y, .2f, .15f, .78f, .3f, .1f), Sd.Box(x, y, -.7f, -.25f, .28f, .45f, .15f)), Ink, .04f);
            cv.Draw((x, y) => Sd.Box(x, y, .25f, .25f, .7f, .1f, .05f), C(0x8a93a6), .04f);
            cv.Draw((x, y) => Sd.Box(x, y, .25f, .02f, .7f, .1f, .05f), C(0x5c6476), .04f);
            cv.Draw((x, y) => Sd.Box(x, y, -.55f, -.12f, .32f, .38f, .12f), (x, y, d) => Color.Lerp(C(0xb4773a), C(0x6e4220), Mathf.Clamp01(-y + .3f)), .04f);
            cv.Draw((x, y) => Sd.Box(x, y, -.1f, -.1f, .12f, .12f, .04f), C(0x8fd14f), .04f);
            Shotgun = Make(cv, 1);
            // 钩子：弯钩，朝右
            cv = new Canvas(32, 32);
            cv.Draw((x, y) => Mathf.Max(Mathf.Abs(Sd.Circle(x, y, .1f, -.1f, .55f)) - .16f, -(y + .1f) - x * .2f), Ink, .07f);
            cv.Draw((x, y) => Mathf.Max(Mathf.Abs(Sd.Circle(x, y, .1f, -.1f, .55f)) - .08f, -(y + .1f) - x * .2f), C(0xd8e6c0), .07f);
            cv.Draw((x, y) => Sd.Box(x, y, -.45f, .35f, .4f, .1f, .04f), C(0x8fd14f), .07f);
            Hook = Make(cv, 1);
            // 孢子：凹凸的绿色团 + 亮点
            cv = new Canvas(48, 48);
            cv.Draw((x, y) => { float a = Mathf.Atan2(y, x), r = Mathf.Sqrt(x * x + y * y); return r - (.8f + .1f * Mathf.Sin(a * 7)); }, Ink, .05f);
            cv.Draw((x, y) => { float a = Mathf.Atan2(y, x), r = Mathf.Sqrt(x * x + y * y); return r - (.66f + .08f * Mathf.Sin(a * 7)); }, (x, y, d) => Color.Lerp(C(0xc8ff8a), C(0x3a9a28), Mathf.Clamp01(-y * .6f + .45f - x * .2f)), .05f);
            cv.Draw((x, y) => Sd.Union(Sd.Circle(x, y, -.25f, .25f, .12f), Sd.Union(Sd.Circle(x, y, .25f, -.1f, .09f), Sd.Circle(x, y, -.05f, -.32f, .07f))), C(0xeaffc8), .05f);
            Spore = Make(cv, 1);
            // 藤蔓叶片
            cv = new Canvas(48, 24);
            cv.Draw((x, y) => Sd.Ellipse(x, y, 0, 0, .95f, .55f), Ink, .06f);
            cv.Draw((x, y) => Sd.Ellipse(x, y, 0, 0, .82f, .42f), (x, y, d) => Color.Lerp(C(0x9dff7a), C(0x2a8a3a), Mathf.Clamp01(-y + .4f)), .06f);
            cv.Draw((x, y) => Sd.Box(x, y, 0, 0, .7f, .03f, .02f), C(0x1f6a2a), .05f);
            Leaf = Make(cv, 1);
            // 羽毛（神化光环 / 滑翔）
            cv = new Canvas(64, 24);
            cv.Draw((x, y) => Sd.Ellipse(x, y, .05f, 0, .92f, .5f), (x, y, d) => new Color(1, 1, 1, Mathf.Clamp01(.5f - d * 5) * (1 - (x + 1) * .3f)), .05f);
            Feather = Make(cv, 1);
            // 花瓣形光片（日 / 光的点缀）
            cv = new Canvas(32, 32);
            cv.Draw((x, y) => Sd.Ellipse(x, y, 0, 0, .45f, .95f), (x, y, d) => new Color(1, 1, 1, Mathf.Clamp01(-d * 4)), .05f);
            Petal = Make(cv, 1);
            // 致盲：眼罩横条
            cv = new Canvas(64, 20);
            cv.Draw((x, y) => Sd.Box(x, y, 0, 0, .95f, .7f, .5f), Ink, .08f);
            cv.Draw((x, y) => Sd.Box(x, y, 0, 0, .85f, .45f, .4f), C(0x2a2440), .08f);
            Blindfold = Make(cv, 1);
            // 小菌怪 / 钻出咬人的獠牙
            cv = new Canvas(32, 32);
            cv.Draw((x, y) => Sd.Star(x, y + .3f, .8f, .2f, 1, -Mathf.PI / 2), Color.white, .06f);
            Fang = Make(cv, 1);
        }

        // ================= px：霰弹猎手 =================
        // 草绿色身体、宽檐猎帽、斜挎弹带（一排橙色霰弹）、单眼护目镜。朝右绘制。
        static void BuildPx()
        {
            int id = (int)CharId.PX; if (id >= Art.Body.Length) return;
            var cv = PxCanvas();
            var sprite = Make(cv, 30 * 1.55f);
            Art.Body[id] = sprite; Art.BodyWhite[id] = Make(cv.Silhouette(), 30 * 1.55f);
        }
        public static Canvas PxCanvas()
        {
            var cv = new Canvas(104, 120);
            cv.Draw((x, y) => Sd.Box(x, y, -.55f, -.05f, .2f, .45f, .15f), C(0x4a5a2a), .03f); // 背后的小披肩
            cv.Draw((x, y) => Sd.Box(x, y, 0, -.08f, .7f, .8f, .46f), Ink, .03f);
            cv.Draw((x, y) => Sd.Box(x, y, 0, -.08f, .58f, .68f, .38f), (x, y, d) => Color.Lerp(C(0xc4f27f), C(0x5a9a2a), Mathf.Clamp01(-y * .6f + .35f)), .03f);
            // 弹带：从右肩斜到左腰
            cv.Draw((x, y) => Mathf.Max(Mathf.Abs((x - y * .9f) - .05f) - .1f, Sd.Box(x, y, 0, -.1f, .58f, .6f, .3f)), C(0x5a3a1e), .03f);
            for (int i = 0; i < 4; i++) { float u = -.32f + i * .2f, sx = u * .9f + .05f, sy = u; cv.Draw((x, y) => Sd.Box(x, y, sx, sy, .045f, .07f, .02f), C(0xff9a3c), .03f); }
            // 宽檐猎帽
            cv.Draw((x, y) => Sd.Union(Sd.Box(x, y, .05f, .66f, .52f, .2f, .16f), Sd.Box(x, y, .12f, .5f, .82f, .07f, .05f)), Ink, .03f);
            cv.Draw((x, y) => Sd.Union(Sd.Box(x, y, .05f, .66f, .44f, .14f, .12f), Sd.Box(x, y, .12f, .5f, .74f, .035f, .03f)), (x, y, d) => Color.Lerp(C(0x8a6a3a), C(0x5a4222), Mathf.Clamp01(-y * 2 + 1.3f)), .03f);
            cv.Draw((x, y) => Sd.Box(x, y, .05f, .57f, .44f, .035f, .02f), C(0xff9a3c), .03f); // 帽带
            // 护目镜（单眼）+ 另一只普通眼
            cv.Draw((x, y) => Sd.Circle(x, y, .4f, .24f, .15f), Ink, .03f);
            cv.Draw((x, y) => Sd.Circle(x, y, .4f, .24f, .11f), (x, y, d) => Color.Lerp(C(0xfff2a8), C(0xffa514), Mathf.Clamp01((x - .3f) * 3)), .03f);
            cv.Draw((x, y) => Sd.Circle(x, y, .36f, .28f, .035f), new Color(1, 1, 1, .9f), .03f);
            cv.Draw((x, y) => Sd.Box(x, y, -.25f, .3f, .3f, .022f, .01f), Ink, .03f); // 护目镜带（绕到脑后）
            cv.Draw((x, y) => Sd.Ellipse(x, y, .13f, .22f, .085f, .105f), Color.white, .03f);
            cv.Draw((x, y) => Sd.Circle(x, y, .16f, .22f, .05f), Ink, .03f);
            cv.Draw((x, y) => Sd.Circle(x, y, .175f, .25f, .018f), Color.white, .03f);
            cv.Draw((x, y) => Sd.Box(x, y, .3f, .0f, .1f, .025f, .015f), C(0x2a3a1a), .03f); // 嘴
            cv.Draw((x, y) => Sd.Ellipse(x, y, -.2f, .3f, .2f, .07f), new Color(1, 1, 1, .35f), .05f);
            cv.Draw((x, y) => Sd.Box(x, y, 0, -.5f, .58f, .05f, .03f), C(0x3a2a16), .03f); // 腰带
            return cv;
        }

        /// <summary>按色相差 dh、饱和度倍率 satK、明度倍率 valK 给有颜色的像素换色；invert：明暗反转（白洞）；grayBase：原图是灰白的（DVD）。</summary>
        public static void RecolorPixels(Color[] px, bool invert, bool grayBase, Color tc, float dh, float satK, float valK)
        {
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i]; if (c.a <= .01f) continue;
                Color.RGBToHSV(c, out float h, out float s, out float v);
                if (invert)
                {   // 白洞：黑洞的明暗整个反过来——亮白的球、冷蓝的边、深色的眼睛
                    float nv = Mathf.Clamp01(1 - v * .92f + .04f); var n = Color.HSVToRGB(.57f, Mathf.Clamp01(s * .35f + .06f), nv); n.a = c.a; px[i] = n; continue;
                }
                if (grayBase)
                {   // 原版 DVD 是灰白贴图、运行时彩虹着色；改版直接把亮部染成紫色
                    if (v > .45f && s < .25f) { var n = Color.Lerp(c, new Color(c.r * tc.r, c.g * tc.g, c.b * tc.b), .85f); n.a = c.a; px[i] = n; }
                    continue;
                }
                if (s < .18f || v < .12f) continue; // 描边、眼睛、高光
                var o = Color.HSVToRGB(Mathf.Repeat(h + dh, 1), Mathf.Clamp01(s * satK), Mathf.Clamp01(v * valK)); o.a = c.a; px[i] = o;
            }
        }

        // ================= 「-改」：原版贴图按 CharDef.Color 重新上色 =================
        // 只改有颜色的部分（身体、头巾、围巾……），描边、眼白、瞳孔这些无彩色像素保持不变，所以一眼能认出是谁的改版。
        static void BuildRemix(CharId id)
        {
            var b = Defs.BaseOf(id); if (b == id) return;
            var src = Art.Body[(int)b]; if (src == null) return;
            var tex = src.texture; Color[] px;
            try { px = tex.GetPixels(); } catch { return; } // 贴图不可读时保留兜底（原版贴图）
            Color.RGBToHSV(C(Defs.Char(id).Color), out float th, out float ts, out float tv);
            Color.RGBToHSV(C(Defs.Char(b).Color), out float bh, out float bs, out float bv);
            float dh = th - bh, satK = Mathf.Clamp(ts / Mathf.Max(.05f, bs), .2f, 1.6f), valK = Mathf.Clamp(tv / Mathf.Max(.05f, bv), .75f, 1.25f);
            RecolorPixels(px, id == CharId.BW2, b == CharId.DVD, C(Defs.Char(id).Color), dh, satK, valK);
            var nt = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            nt.SetPixels(px); nt.Apply(false, false);
            Art.Body[(int)id] = Sprite.Create(nt, new Rect(0, 0, nt.width, nt.height), new Vector2(.5f, .5f), src.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            if (Art.BodyWhite[(int)b] != null) Art.BodyWhite[(int)id] = Art.BodyWhite[(int)b]; // 轮廓与原版相同
        }
    }
}
