// IMGUI 界面皮肤（第十版翻新）：深色玻璃面板 + 金色细边与角饰、带高光的渐变按钮、悬停金光、标题用站酷庆科黄油体。
// 全部用代码生成贴图，按 1280×720 设计分辨率等比缩放。对外接口与旧版完全相同（GameMain 不用改也能直接换皮）。
using UnityEngine;

namespace Brawl.View
{
    public static class Ui
    {
        public static Font Font;
        public static GUIStyle Label, LabelC, Title, Panel, Button, ButtonSel, Field, Small, SmallC, Big;
        public static Texture2D White;
        public static float Scale = 1; public static Vector2 Offset;
        static bool ready;
        public static readonly Color GoldC = new Color(1, .82f, .36f), Ink = new Color(.02f, .02f, .06f, .95f);

        /// <summary>圆角渐变贴图：上亮下暗；edge 为外描边，inner 为内侧一圈细光；corner=true 时四角加金色角饰；gloss 为上半部高光。</summary>
        static Texture2D Skin(Color top, Color bottom, Color edge, Color inner, int r = 12, int size = 48, bool corner = false, float gloss = .1f)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float qx = Mathf.Max(0, Mathf.Abs(x + .5f - size / 2f) - (size / 2f - r)), qy = Mathf.Max(0, Mathf.Abs(y + .5f - size / 2f) - (size / 2f - r));
                    float d = Mathf.Sqrt(qx * qx + qy * qy) - r; // <0 内部
                    float a = Mathf.Clamp01(.5f - d);
                    float v = y / (float)(size - 1); // 0 = 底，1 = 顶（Texture2D 原点在左下）
                    var c = Color.Lerp(bottom, top, v);
                    if (v > .55f) { float g = (v - .55f) / .45f; c = Color.Lerp(c, Color.white, gloss * g * g); }
                    float e = Mathf.Clamp01(d + 2.2f); c = Color.Lerp(c, edge, e);                         // 外描边
                    float i2 = Mathf.Clamp01(1 - Mathf.Abs(d + 4f) / 1.1f); c = Color.Lerp(c, inner, i2 * inner.a); // 内侧细光
                    if (corner)
                    {   // 四角的 L 形金色角饰
                        int cx = Mathf.Min(x, size - 1 - x), cy = Mathf.Min(y, size - 1 - y);
                        bool L = (cx >= 3 && cx <= 4 && cy >= 3 && cy <= 11) || (cy >= 3 && cy <= 4 && cx >= 3 && cx <= 11);
                        if (L) c = Color.Lerp(c, new Color(1, .84f, .42f, 1), .9f);
                    }
                    c.a *= a; px[y * size + x] = c;
                }
            t.SetPixels(px); t.Apply(); return t;
        }

        public static void Init()
        {
            if (ready) return; ready = true;
            Font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Hiragino Sans GB", "Noto Sans CJK SC", "Source Han Sans SC", "SimHei", "Arial Unicode MS", "Arial" }, 32);
            var tf = Res10.TitleFont;
            White = Texture2D.whiteTexture;
            Label = new GUIStyle { font = Font, fontSize = 20, wordWrap = true, richText = true }; Label.normal.textColor = new Color(.93f, .94f, 1);
            LabelC = new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter };
            Small = new GUIStyle(Label) { fontSize = 15 }; Small.normal.textColor = new Color(.72f, .76f, .92f);
            SmallC = new GUIStyle(Small) { alignment = TextAnchor.MiddleCenter };
            Title = new GUIStyle(LabelC) { fontSize = 84, fontStyle = tf != null ? FontStyle.Normal : FontStyle.Bold, wordWrap = false, font = tf ?? Font };
            Big = new GUIStyle(LabelC) { fontSize = 44, fontStyle = tf != null ? FontStyle.Normal : FontStyle.Bold, wordWrap = false, font = tf ?? Font };
            Panel = new GUIStyle { border = new RectOffset(16, 16, 16, 16), padding = new RectOffset(16, 16, 14, 14) };
            Panel.normal.background = Skin(new Color(.1f, .11f, .24f, .93f), new Color(.035f, .035f, .1f, .95f), new Color(.55f, .45f, .28f, 1), new Color(1, .9f, .6f, .22f), 12, 48, true, .05f);
            Button = new GUIStyle { font = Font, fontSize = 22, alignment = TextAnchor.MiddleCenter, border = new RectOffset(14, 14, 14, 14), fontStyle = FontStyle.Bold };
            Button.normal.background = Skin(new Color(.22f, .24f, .5f, 1), new Color(.1f, .1f, .26f, 1), new Color(.42f, .46f, .9f, 1), new Color(.75f, .8f, 1, .35f), 12, 48, false, .16f); Button.normal.textColor = Color.white;
            Button.hover.background = Skin(new Color(.34f, .34f, .7f, 1), new Color(.16f, .15f, .38f, 1), new Color(1, .85f, .45f, 1), new Color(1, .95f, .75f, .5f), 12, 48, false, .2f); Button.hover.textColor = new Color(1, .95f, .72f);
            Button.active.background = Skin(new Color(.08f, .08f, .2f, 1), new Color(.16f, .16f, .36f, 1), new Color(1, .85f, .4f, 1), new Color(1, .9f, .6f, .3f), 12, 48, false, 0); Button.active.textColor = Color.white;
            ButtonSel = new GUIStyle(Button);
            ButtonSel.normal.background = Skin(new Color(1, .82f, .38f, 1), new Color(.72f, .4f, .08f, 1), new Color(1, .95f, .7f, 1), new Color(1, 1, .9f, .55f), 12, 48, false, .25f);
            ButtonSel.hover.background = Skin(new Color(1, .88f, .5f, 1), new Color(.8f, .48f, .12f, 1), new Color(1, 1, .85f, 1), new Color(1, 1, .95f, .6f), 12, 48, false, .3f);
            ButtonSel.normal.textColor = new Color(.22f, .08f, 0); ButtonSel.hover.textColor = new Color(.15f, .05f, 0);
            Field = new GUIStyle(Button) { fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(14, 14, 4, 4) };
            Field.normal.background = Skin(new Color(.04f, .05f, .12f, 1), new Color(.02f, .02f, .06f, 1), new Color(.42f, .46f, .9f, 1), new Color(0, 0, 0, 0), 10); Field.focused.background = Skin(new Color(.05f, .06f, .14f, 1), new Color(.02f, .02f, .06f, 1), new Color(1, .85f, .4f, 1), new Color(1, .9f, .6f, .25f), 10);
            Field.focused.textColor = Color.white; Field.hover.background = Field.normal.background; Field.normal.textColor = Color.white;
        }

        /// <summary>设置 GUI 矩阵：设计分辨率 1280×720，保持比例居中。</summary>
        public static void BeginScaled()
        {
            float s = Mathf.Min(Screen.width / 1280f, Screen.height / 720f); Scale = s;
            Offset = new Vector2((Screen.width - 1280 * s) / 2, (Screen.height - 720 * s) / 2);
            GUI.matrix = Matrix4x4.TRS(new Vector3(Offset.x, Offset.y, 0), Quaternion.identity, new Vector3(s, s, 1));
        }
        public static Vector2 ScreenToDesign(Vector2 guiScreen) => (guiScreen - Offset) / Scale;

        public static void Rect(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, White); GUI.color = o; }
        /// <summary>描边文字。标题字体里缺字（例如玩家名字）时自动退回系统字体；描边 ≥3 时再加一层下投影。</summary>
        public static void Text(Rect r, string s, GUIStyle st, Color c, float outline = 2, Color? oc = null)
        {
            if (string.IsNullOrEmpty(s)) return;
            var keepFont = st.font; if (st.font != Font && st.font != null && !Res10.Covers(st.font, StripTags(s))) st.font = Font;
            var keep = st.normal.textColor; var occ = oc ?? Ink;
            if (outline > 0)
            {
                st.normal.textColor = occ;
                if (outline >= 3) { var sh = occ; sh.a *= .6f; st.normal.textColor = sh; GUI.Label(new Rect(r.x + outline * .5f, r.y + outline * 1.6f, r.width, r.height), s, st); st.normal.textColor = occ; }
                for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4; GUI.Label(new Rect(r.x + Mathf.Cos(a) * outline, r.y + Mathf.Sin(a) * outline, r.width, r.height), s, st); }
            }
            st.normal.textColor = c; GUI.Label(r, s, st); st.normal.textColor = keep; st.font = keepFont;
        }
        static string StripTags(string s)
        {
            if (s.IndexOf('<') < 0) return s;
            var sb = new System.Text.StringBuilder(); bool tag = false;
            foreach (var ch in s) { if (ch == '<') tag = true; else if (ch == '>') tag = false; else if (!tag) sb.Append(ch); }
            return sb.ToString();
        }
        public static void Sprite(Rect r, Sprite s, Color c, bool flip = false)
        {
            if (s == null) return; var tr = s.textureRect; var tex = s.texture;
            var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            if (flip) { uv.x += uv.width; uv.width = -uv.width; }
            var o = GUI.color; GUI.color = c; GUI.DrawTextureWithTexCoords(r, tex, uv); GUI.color = o;
        }
        /// <summary>进度条：深色槽 + 渐变填充（上亮下暗）+ 高光 + 末端亮点；够宽时加 10% 刻度。</summary>
        public static void Bar(Rect r, float k, Color fill, Color back, float ghost = -1, Color? ghostC = null)
        {
            k = Mathf.Clamp01(k);
            Rect(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(.01f, .01f, .04f, .92f));
            Rect(r, back);
            Rect(new Rect(r.x, r.y, r.width, Mathf.Max(1, r.height * .35f)), new Color(0, 0, 0, .25f));
            if (ghost > k) Rect(new Rect(r.x, r.y, r.width * Mathf.Clamp01(ghost), r.height), ghostC ?? new Color(1, .95f, .85f, .6f));
            if (k > 0)
            {
                var dark = Color.Lerp(fill, Color.black, .35f); dark.a = fill.a;
                Rect(new Rect(r.x, r.y, r.width * k, r.height), dark);
                Rect(new Rect(r.x, r.y, r.width * k, r.height * .62f), fill);
                Rect(new Rect(r.x, r.y, r.width * k, Mathf.Max(1, r.height * .25f)), new Color(1, 1, 1, .32f));
                if (r.height >= 5 && k < 1) Rect(new Rect(r.x + r.width * k - 2, r.y, 2, r.height), new Color(1, 1, 1, .6f));
            }
            if (r.width >= 150 && r.height >= 8) for (int i = 1; i < 10; i++) Rect(new Rect(r.x + r.width * i / 10f, r.y + r.height * .5f, 1, r.height * .5f), new Color(0, 0, 0, .3f));
        }
        static int hoverId = -1;
        /// <summary>按钮：悬停时背后一团金光 + 轻响；按下时“咔”一声。</summary>
        public static bool Btn(Rect r, string s, bool sel = false)
        {
            var ev = Event.current;
            if (ev != null && ev.type == EventType.Repaint && GUI.enabled && r.Contains(ev.mousePosition))
            {
                Sprite(new Rect(r.x - 18, r.y - 14, r.width + 36, r.height + 28), Art.Soft, new Color(1, .8f, .35f, .28f));
                int id = Mathf.RoundToInt(r.x) * 7919 + Mathf.RoundToInt(r.y);
                if (id != hoverId) { hoverId = id; Res10.Play("ui_hover", .25f); }
            }
            bool hit = GUI.Button(r, s, sel ? ButtonSel : Button);
            if (hit) Res10.Play("ui_click", .45f);
            return hit;
        }
    }
}
