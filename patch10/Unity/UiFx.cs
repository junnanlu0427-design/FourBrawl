// 第十版界面动效：菜单背景（暗角 + 缓慢扫过的光 + 金色浮尘）、发光标题 Logo、小标题、角色卡光晕、
// 对局 HUD 角色卡底板、局间横幅（滑入 + 毛笔底纹）、夺冠结算（放射光 + 聚光 + 慢飘彩带）、切换界面淡入。
// 约束：发光 ≤0.5，所有变化都是慢速平滑的（最快约 1Hz），没有频闪。
using System.Collections.Generic;
using UnityEngine;

namespace Brawl.View
{
    public static class UiFx
    {
        public static bool Enabled = true;
        static Color A(Color c, float a) { c.a = a; return c; }
        static float T => Time.unscaledTime;
        static readonly Color Gold = new Color(1, .82f, .36f);

        // ---------------- 界面切换淡入 ----------------
        static int lastScr = -1; static float fadeT;
        /// <summary>OnGUI 开头调用（传当前界面编号），切换界面时触发 0.3 秒淡入。</summary>
        public static void Screen(int scr) { if (scr != lastScr) { lastScr = scr; fadeT = .3f; } }
        /// <summary>OnGUI 末尾调用：画淡入遮罩。</summary>
        public static void EndFrame()
        {
            if (!Enabled || Event.current.type != EventType.Repaint) return;
            if (fadeT > 0) { fadeT -= Time.unscaledDeltaTime; Ui.Rect(new Rect(-800, -400, 2880, 1520), new Color(0, 0, 0, Mathf.Clamp01(fadeT / .3f) * .7f)); }
        }

        // ---------------- 菜单背景 ----------------
        /// <summary>代替 OnGUI 里那块整屏暗色：上下渐暗 + 暗角 + 两道缓慢扫过的斜光 + 金色浮尘。title=true 时更通透（让标题后面的演示对局露出来）。</summary>
        public static void Backdrop(bool title)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!Enabled) { Ui.Rect(new Rect(-400, -200, 2080, 1120), new Color(.02f, .02f, .07f, title ? .45f : .7f)); return; }
            Ui.Rect(new Rect(-800, -400, 2880, 1520), new Color(.02f, .02f, .08f, title ? .28f : .6f));
            var fd = Res10.Tex("Ui/fade_down");
            if (fd != null)
            {
                var o = GUI.color; GUI.color = new Color(1, 1, 1, title ? .75f : .6f);
                GUI.DrawTexture(new Rect(-800, -60, 2880, 260), fd);                                      // 顶部压暗
                var km = GUI.matrix; GUIUtility.RotateAroundPivot(180, new Vector2(640, 620) * Ui.Scale + Ui.Offset);
                GUI.DrawTexture(new Rect(-800, 520, 2880, 200), fd); GUI.matrix = km; // 底部压暗
                GUI.color = o;
            }
            var vig = Res10.Tex("Ui/vignette"); if (vig != null) GUI.DrawTexture(new Rect(-120, -80, 1520, 880), vig);
            // 斜光：两条很宽很淡的光带，十几秒扫一次
            for (int i = 0; i < 2; i++)
            {
                float x = Mathf.Repeat(T * 42 + i * 760, 2200) - 460;
                var keep = GUI.matrix; GUIUtility.RotateAroundPivot(-22, new Vector2(x, 360) * Ui.Scale + Ui.Offset);
                Ui.Sprite(new Rect(x - 90, -300, 180, 1320), Art.Soft, new Color(1, .9f, .65f, title ? .07f : .05f));
                GUI.matrix = keep;
            }
            // 金色浮尘
            for (int i = 0; i < 34; i++)
            {
                float hx = Mathf.Repeat(i * .618f + T * (.004f + i % 5 * .0015f), 1), hy = Mathf.Repeat(i * .371f - T * (.012f + i % 3 * .006f), 1);
                float tw = .5f + .5f * Mathf.Sin(T * .9f + i * 1.7f), s = 3 + (i % 4) * 2;
                Ui.Sprite(new Rect(hx * 1400 - 60 - s * 2, hy * 800 - 40 - s * 2, s * 4, s * 4), Art.Soft, new Color(1, .85f, .45f, .25f * tw));
            }
        }

        // ---------------- 标题 Logo ----------------
        /// <summary>中心在 c 的发光 Logo（宽 w），上下轻轻浮动，每 5 秒一道高光从左扫到右。</summary>
        public static void Logo(Vector2 c, float w)
        {
            var t = Res10.Tex("Ui/logo"); float bob = Mathf.Sin(T * 1.6f) * 4;
            if (t == null) { Ui.Text(new Rect(0, c.y - 55 + bob, 1280, 110), "四人乱斗场", Ui.Title, new Color(1, .86f, .35f), 5, new Color(.25f, .05f, .3f, 1)); return; }
            float h = w * t.height / t.width; var r = new Rect(c.x - w / 2, c.y - h / 2 + bob, w, h);
            if (Event.current.type == EventType.Repaint)
            {
                Ui.Sprite(new Rect(r.x - 80, r.y - 60, r.width + 160, r.height + 120), Art.Soft, new Color(.75f, .45f, 1, .22f));
                Ui.Sprite(new Rect(r.x + 40, r.y - 10, r.width - 80, r.height + 20), Art.Soft, new Color(1, .75f, .3f, .2f + .06f * Mathf.Sin(T * 1.2f)));
                GUI.DrawTexture(r, t);
                // 高光扫过（裁剪在 Logo 范围内）
                float u = Mathf.Repeat(T / 5f, 1); if (u < .35f)
                {
                    float sx = Mathf.Lerp(-.3f, 1.3f, u / .35f) * r.width;
                    GUI.BeginGroup(r);
                    var keep = GUI.matrix; GUIUtility.RotateAroundPivot(18, new Vector2(r.x + sx, r.y + r.height / 2) * Ui.Scale + Ui.Offset);
                    Ui.Sprite(new Rect(sx - 40, -r.height, 80, r.height * 3), Art.Soft, new Color(1, 1, 1, .45f * Mathf.Sin(u / .35f * Mathf.PI)));
                    GUI.matrix = keep; GUI.EndGroup();
                }
                // 两侧闪烁的小星（慢）
                for (int i = 0; i < 4; i++)
                {
                    float tw = Mathf.Max(0, Mathf.Sin(T * .8f + i * 1.9f)); float s = 26 * tw;
                    float x = r.x + (i % 2 == 0 ? .08f : .92f) * r.width + (i < 2 ? -10 : 20), y = r.y + (i < 2 ? .15f : .8f) * r.height;
                    Ui.Sprite(new Rect(x - s / 2, y - s / 2, s, s), Art.Star4, new Color(1, .97f, .85f, .9f * tw));
                }
            }
        }

        // ---------------- 小标题 ----------------
        /// <summary>界面大标题：标题字体 + 金色描边 + 两侧渐隐金线 + 菱形点缀。</summary>
        public static void Heading(string s, float y, float h = 60)
        {
            var st = Ui.Big; var c = new Color(1, .93f, .75f);
            Ui.Text(new Rect(0, y, 1280, h), s, st, c, 3, new Color(.2f, .08f, .02f, .95f));
            if (Event.current.type != EventType.Repaint) return;
            float tw = st.CalcSize(new GUIContent(s)).x, cy = y + h / 2 + 2;
            for (int side = -1; side <= 1; side += 2)
            {
                float x0 = 640 + side * (tw / 2 + 24);
                for (int i = 0; i < 24; i++) { float k = 1 - i / 24f; Ui.Rect(new Rect(side > 0 ? x0 + i * 10 : x0 - (i + 1) * 10, cy - 1, 10, 2), A(Gold, .8f * k)); }
                var keep = GUI.matrix; var p = new Vector2(x0, cy); GUIUtility.RotateAroundPivot(45, p * Ui.Scale + Ui.Offset);
                Ui.Rect(new Rect(p.x - 5, p.y - 5, 10, 10), Gold); GUI.matrix = keep;
            }
        }

        // ---------------- 卡片强调条 / 光晕 ----------------
        /// <summary>卡片顶部的彩色强调条（中间亮、两端渐隐，带一点外发光）。</summary>
        public static void Accent(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            Ui.Sprite(new Rect(r.x, r.y - 10, r.width, r.height + 20), Art.Soft, A(c, .35f));
            int n = 16; for (int i = 0; i < n; i++) { float u = (i + .5f) / n, k = Mathf.Sin(u * Mathf.PI); Ui.Rect(new Rect(r.x + r.width * i / n, r.y, r.width / n + .5f, r.height), A(Color.Lerp(c, Color.white, .25f * k), .35f + .65f * k)); }
        }
        /// <summary>角色卡背后的颜色光晕（悬停时更亮）。在 GUI.Box 之前调用。</summary>
        public static void CardGlow(Rect r, Color c, bool hot = false)
        {
            if (Event.current.type != EventType.Repaint) return;
            bool hover = r.Contains(Event.current.mousePosition);
            Ui.Sprite(new Rect(r.x - 24, r.y - 20, r.width + 48, r.height + 40), Art.Soft, A(c, hover || hot ? .32f : .12f));
        }
        /// <summary>详情页：背后放大的颜色光、旋转放射光；ccld-改 额外有七芒星神纹。</summary>
        public static void DetailBackdrop(Brawl.Core.CharId id, Rect icon)
        {
            if (!Enabled || Event.current.type != EventType.Repaint) return;
            var c = Art.C(Brawl.Core.Defs.Char(id).Color); var ctr = icon.center;
            Ui.Sprite(new Rect(ctr.x - 260, ctr.y - 200, 520, 400), Art.Soft, A(c, .3f));
            var keep = GUI.matrix; GUIUtility.RotateAroundPivot(T * 6, ctr * Ui.Scale + Ui.Offset);
            Ui.Sprite(new Rect(ctr.x - 150, ctr.y - 150, 300, 300), Res10.Rays, A(Color.Lerp(c, Color.white, .4f), .35f));
            if (id == Brawl.Core.CharId.CCLD2) Ui.Sprite(new Rect(ctr.x - 110, ctr.y - 110, 220, 220), Res10.Sigil, new Color(1, .85f, .4f, .5f));
            GUI.matrix = keep;
        }

        // ---------------- 对局 HUD ----------------
        /// <summary>顶部角色卡底板：深色玻璃 + 左侧颜色光 + 顶边细光；倒下的人卡片变灰。</summary>
        public static void HudCard(Rect r, Color accent, bool alive, bool me)
        {
            if (Event.current.type != EventType.Repaint) return;
            Ui.Rect(new Rect(r.x + 2, r.y + 3, r.width, r.height), new Color(0, 0, 0, .35f));                  // 投影
            Ui.Rect(r, new Color(.03f, .035f, .09f, .78f));
            Ui.Rect(new Rect(r.x, r.y, r.width, r.height * .45f), new Color(1, 1, 1, .035f));                 // 玻璃高光
            Ui.Sprite(new Rect(r.x - r.height * .4f, r.y - 6, r.height * 1.6f, r.height + 12), Art.Soft, A(accent, alive ? .3f : .08f));
            Ui.Rect(new Rect(r.x, r.y, 4, r.height), alive ? accent : A(accent, .35f));
            Ui.Rect(new Rect(r.x, r.y, r.width, 1), A(Color.Lerp(accent, Color.white, .4f), .5f));
            if (me) { Ui.Rect(new Rect(r.x, r.yMax - 2, r.width, 2), new Color(1, .95f, .8f, .7f)); }
            if (!alive) Ui.Rect(r, new Color(.05f, .05f, .08f, .45f));
        }

        // ---------------- 局间横幅 ----------------
        static string bannerText; static float bannerAt;
        /// <summary>代替 DrawHud 里的大字横幅：底纹从中间往两边展开、大字从放大落定、副标题淡入。</summary>
        public static void Banner(string big, string small, Color c)
        {
            if (big != bannerText) { bannerText = big; bannerAt = T; }
            float age = T - bannerAt, k = Mathf.Clamp01(age / .22f), e = k * k * (3 - 2 * k);
            float h = small != null ? 150 : 112, y = 300;
            if (Event.current.type == EventType.Repaint)
            {
                float w = 1400 * e;
                Ui.Rect(new Rect(640 - w / 2, y, w, h), new Color(.02f, .02f, .06f, .62f));
                Ui.Sprite(new Rect(640 - w / 2, y - 30, w, h + 60), Art.Soft, A(c, .18f));
                Ui.Rect(new Rect(640 - w / 2, y, w, 2), A(c, .8f)); Ui.Rect(new Rect(640 - w / 2, y + h - 2, w, 2), A(c, .8f));
                var sl = Res10.Tex("Fx/slash"); // 一道毛笔刷痕做底纹
                if (sl != null) { var o = GUI.color; GUI.color = A(c, .22f * e); GUI.DrawTexture(new Rect(640 - 520 * e, y + 8, 1040 * e, h - 16), sl); GUI.color = o; }
            }
            var st = Ui.Title; int fs = st.fontSize; st.fontSize = (int)(84 * Mathf.Lerp(1.4f, 1, e));
            Ui.Text(new Rect(0, y + 10, 1280, 80), big, st, A(c, e), 4, new Color(.04f, .02f, .06f, e));
            st.fontSize = fs;
            if (small != null) { float a = Mathf.Clamp01((age - .15f) / .3f); Ui.Text(new Rect(0, y + 100, 1280, 36), small, Ui.LabelC, new Color(.85f, .88f, 1, a), 2); }
        }

        // ---------------- 夺冠结算 ----------------
        struct Conf { public float X, Y, Vx, Vy, Rot, Vr, Ph; public Color C; }
        static readonly List<Conf> conf = new List<Conf>(); static float confAt = -99; static readonly System.Random rng = new System.Random(21);
        /// <summary>结算界面背景：winner 头像后面的旋转放射光 + 聚光 + 缓慢飘落的金色彩带（第一次调用时生成）。</summary>
        public static void Victory(Rect icon, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (T - confAt > 2) // 新的一场结算
            {
                conf.Clear();
                for (int i = 0; i < 70; i++)
                    conf.Add(new Conf { X = (float)rng.NextDouble() * 1280, Y = -20 - (float)rng.NextDouble() * 700, Vx = ((float)rng.NextDouble() - .5f) * 40, Vy = 60 + (float)rng.NextDouble() * 70, Rot = (float)rng.NextDouble() * 360, Vr = ((float)rng.NextDouble() - .5f) * 240, Ph = (float)rng.NextDouble() * 6, C = i % 3 == 0 ? c : i % 3 == 1 ? Gold : new Color(1, .97f, .9f) });
            }
            confAt = T; float dt = Mathf.Min(.05f, Time.unscaledDeltaTime);
            var ctr = icon.center;
            Ui.Sprite(new Rect(ctr.x - 420, -40, 840, 760), Art.Soft, A(Color.Lerp(c, Color.white, .5f), .16f)); // 聚光
            var keep = GUI.matrix; GUIUtility.RotateAroundPivot(T * 8, ctr * Ui.Scale + Ui.Offset);
            Ui.Sprite(new Rect(ctr.x - 230, ctr.y - 230, 460, 460), Res10.Rays, A(Color.Lerp(c, Gold, .5f), .45f));
            GUI.matrix = keep;
            Ui.Sprite(new Rect(ctr.x - 120, ctr.y - 120, 240, 240), Art.Soft, A(c, .4f));
            for (int i = 0; i < conf.Count; i++)
            {
                var q = conf[i]; q.Y += q.Vy * dt; q.X += (q.Vx + Mathf.Sin(T * 1.3f + q.Ph) * 30) * dt; q.Rot += q.Vr * dt; if (q.Y > 760) { q.Y = -20; q.X = (float)rng.NextDouble() * 1280; }
                conf[i] = q;
                float flip = Mathf.Abs(Mathf.Cos(q.Rot * Mathf.Deg2Rad));
                var k2 = GUI.matrix; GUIUtility.RotateAroundPivot(q.Rot * .4f, new Vector2(q.X, q.Y) * Ui.Scale + Ui.Offset);
                Ui.Rect(new Rect(q.X - 5 * flip, q.Y - 3, 10 * flip + 1, 6), A(q.C, .85f)); GUI.matrix = k2;
            }
        }
        /// <summary>结算排行的一行底：名次越高越亮，第一名金边。</summary>
        public static void RankRow(Rect r, Color c, int rank)
        {
            if (Event.current.type != EventType.Repaint) return;
            Ui.Rect(r, new Color(.04f, .045f, .11f, rank == 0 ? .9f : .7f));
            Ui.Rect(new Rect(r.x, r.y, 4, r.height), c);
            Ui.Sprite(new Rect(r.x - 40, r.y - 8, 200, r.height + 16), Art.Soft, A(c, .18f));
            if (rank == 0) { Ui.Rect(new Rect(r.x, r.y, r.width, 1), Gold); Ui.Rect(new Rect(r.x, r.yMax - 1, r.width, 1), Gold); }
        }
    }
}
