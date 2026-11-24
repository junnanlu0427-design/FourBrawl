// 第十四版：角色造型（Unity 侧）。把 CharRigCore 画好的部件做成 Sprite，在 WorldView.DrawPlayer 里按骨骼姿势画出来。
// 接入（见 handoff/接入说明.md）：
//   Art.Init 末尾        → CharRig.Init()（生成部件，并把 Art.Body / BodyWhite / GodBody / GodWhite 换成新造型的静态图，旧的描边 / 残影 / 幽灵 / 泥人都会跟着变）
//   WorldView.DrawPlayer → 画身体那几行换成 CharRig.Draw(...)
//   WorldView 透视层      → CharRig.Overlays(sp)
//   ViewPolish.CharIcon  → CharRig.Portrait(c)（选人卡、图鉴、详情页、HUD 头像都走这里）
// 纯画面层：只读 Player，不写任何 Sim 字段。
using System.Collections.Generic;
using UnityEngine;
using Brawl.Core;

namespace Brawl.View
{
    public static class CharRig
    {
        /// <summary>总开关：关掉就回到第十三版的圆形身体。</summary>
        public static bool Enabled = true;
        static bool ready;
        static readonly RigParts[] parts = new RigParts[RigLooks.COUNT];
        static readonly Sprite[][] fill = new Sprite[RigLooks.COUNT][], sil = new Sprite[RigLooks.COUNT][];
        static readonly Sprite[] portraits = new Sprite[RigLooks.COUNT];
        static readonly List<RigDraw> list = new List<RigDraw>();

        // ================= 初始化 =================
        public static void Init()
        {
            if (ready) return; ready = true;
            for (int i = 0; i < RigLooks.COUNT; i++) Ensure(i);
            // 静态图：脚底对齐碰撞盒底边、图中心 = 碰撞盒中心（和旧贴图一样的摆法，旧代码里所有用 Body/BodyWhite 的地方不用改）
            for (int i = 0; i < Defs.Chars.Length && i < 15; i++) { Sprite b, w; BakeBody(i, (CharId)i, out b, out w); if (b != null) { Art.Body[i] = b; Art.BodyWhite[i] = w; } }
            { Sprite b, w; BakeBody(RigLooks.GOD, CharId.CCLD, out b, out w); if (b != null) { Art.GodBody = b; Art.GodWhite = w; } }
        }
        static int LookOf(CharId c, bool god) => god && c == CharId.CCLD ? RigLooks.GOD : Mathf.Clamp((int)c, 0, 14);
        static RigParts Ensure(int look)
        {
            if (parts[look] != null) return parts[look];
            var def = Defs.Char(look == RigLooks.GOD ? CharId.CCLD : (CharId)look);
            var P = RigArt.Build(RigLooks.Get(look), def.W, def.H); parts[look] = P;
            fill[look] = new Sprite[RP.Count]; sil[look] = new Sprite[RP.Count];
            for (int k = 0; k < RP.Count; k++) { fill[look][k] = ToSprite(P.Fill[k]); sil[look][k] = ToSprite(P.Sil[k]); }
            return P;
        }
        static Sprite ToSprite(RBitmap b)
        {
            if (b == null) return null;
            var t = new Texture2D(b.W, b.H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[b.W * b.H];
            for (int k = 0; k < px.Length; k++) px[k] = new Color32((byte)(255 * Mathf.Clamp01(b.P[k * 4])), (byte)(255 * Mathf.Clamp01(b.P[k * 4 + 1])), (byte)(255 * Mathf.Clamp01(b.P[k * 4 + 2])), (byte)(255 * Mathf.Clamp01(b.P[k * 4 + 3])));
            t.SetPixels32(px); t.Apply(false, true);
            return Sprite.Create(t, new Rect(0, 0, b.W, b.H), new Vector2(.5f, .5f), b.Ppu, 0, SpriteMeshType.FullRect);
        }
        static RigIn Idle() => new RigIn { T = .3f, Swing = -1, Cast = -1, Ground = true };
        static void BakeBody(int look, CharId def, out Sprite body, out Sprite white)
        {
            var P = Ensure(look); var d = Defs.Char(def);
            RigPose.Build(P, Idle(), list); float x0, y0, x1, y1; RigPose.Bounds(P, list, out x0, out y0, out x1, out y1);
            float hw = Mathf.Max(-x0, x1) + 1, top = y1 + 1, cy = d.H / 2f, hh = Mathf.Max(top - cy, cy + 1);
            var b = RigPose.Bake(P, list, 0, cy, hw * 2, hh * 2, 4); var w = RigPose.Bake(P, list, 0, cy, hw * 2, hh * 2, 4, true);
            body = ToSprite(b); white = ToSprite(w);
        }

        /// <summary>选人卡 / 图鉴 / 详情页 / HUD 用的半身像（头 + 上半身 + 武器）。</summary>
        public static Sprite Portrait(CharId c)
        {
            Init(); int look = LookOf(c, false); if (portraits[look] != null) return portraits[look];
            var P = Ensure(look); var s = Idle(); s.T = 0; RigPose.Build(P, s, list);
            float x0, y0, x1, y1; RigPose.Bounds(P, list, out x0, out y0, out x1, out y1);
            float hipY = P.FootH * .55f + (P.LegU + P.LegF) * .9f, bottom = hipY + P.TorsoH * .05f, size = (y1 - bottom) * 1.04f;
            float cx = Mathf.Clamp(0, x0 + size / 2, x1 - size / 2) + P.HeadR * .1f;
            portraits[look] = ToSprite(RigPose.Bake(P, list, cx, bottom + size / 2, size, size, Mathf.Max(6, 160f / size)));
            return portraits[look];
        }

        // ================= 每个人的动画状态 =================
        sealed class St { public float LastT = -1, Run, LastSwing, SwingMax = .16f, CastT, LastSkCd, HurtT; public bool LastFlash; public int Step, LastCombo = -1; }
        static readonly Dictionary<int, St> states = new Dictionary<int, St>();
        static St Get(Player p) { St s; if (!states.TryGetValue(p.Slot, out s)) states[p.Slot] = s = new St { LastSkCd = p.SkCd }; return s; }

        static RigIn Input(Player p, int f, float t, bool isClone)
        {
            var st = Get(p);
            if (!isClone)
            {
                float dt = st.LastT < 0 ? 0 : Mathf.Clamp(t - st.LastT, 0, .1f); st.LastT = t;
                st.Run += dt * Mathf.Abs(p.Vx) * .045f;
                if (p.Swing > st.LastSwing + .005f) { st.SwingMax = Mathf.Max(.05f, p.Swing); st.Step++; }
                st.LastSwing = p.Swing;
                if (p.SkCd > st.LastSkCd + .25f) st.CastT = .45f;
                st.LastSkCd = p.SkCd; if (st.CastT > 0) st.CastT -= dt;
                bool fl = p.Flash > 0; if (fl && !st.LastFlash) st.HurtT = .32f; st.LastFlash = fl; if (st.HurtT > 0) st.HurtT -= dt;
            }
            var s = new RigIn { T = t + p.Slot * 1.7f, Vx = p.Vx * f, Vy = -p.Vy, Ground = p.OnGround, Run = st.Run, Swing = -1, Cast = -1, Hue = p.Hue };
            if (p.Swing > 0) { s.Swing = Mathf.Clamp01(1 - p.Swing / st.SwingMax); s.Step = p.Ch == CharId.KAI || p.Ch == CharId.KAI2 ? p.ComboN : st.Step; }
            if (p.Ch == CharId.CCLD2 && p.Divine && p.Swing > 0) s.Combo = Mathf.Clamp(p.Combo3, 1, 5);
            if (st.CastT > 0) s.Cast = 1 - st.CastT / .45f;
            if (st.HurtT > 0) s.Hurt = st.HurtT / .32f;
            s.Stun = p.Stun > 0 || p.Daze > 0;
            if (p.WallDir != 0 && !p.OnGround && p.Vy > 0) s.Wall = p.WallDir * f;
            s.Float = p.Ch == CharId.CCLD2 && p.Divine && !p.OnGround;
            if ((p.Ch == CharId.DVD || p.Ch == CharId.DVD2 || p.Ch == CharId.DZY2) && s.Swing > .45f) s.HideWeapon = true; // 扔出去了
            if (p.Ch == CharId.DVD2 && p.Ammo <= 0) s.HideWeapon = true;
            return s;
        }

        // ================= 画 =================
        struct Drawn { public Sprite S, W; public float X, Y, Wd, Ht, Rot; public bool Flip, Back; public Color C; }
        sealed class Cache { public readonly List<Drawn> L = new List<Drawn>(); public int Frame = -1; public Vector2 Head; public float HeadR; }
        static readonly Dictionary<int, Cache> caches = new Dictionary<int, Cache>();

        /// <summary>
        /// 画一个人（代替旧的「描边 + 身体贴图」）。cx/footY：脚底中心（游戏坐标），f：朝向，sx/sy：挤压拉伸，rot：整体旋转（Unity 角度，和旧代码同一个值），
        /// o：这个人的排序基准（描边画在 o，身体画在 o+1，挨打白闪在 o+2），tint：整体乘色（冰冻 / 闪烁透明），rim：外圈描边色。
        /// </summary>
        public static void Draw(SpritePool sp, Player p, bool isClone, float cx, float footY, int f, float sx, float sy, float rot, float t, int o, Color tint, Color rim, float rimPx, bool flash, bool boss)
        {
            Init();
            bool god = p.God && p.Ch == CharId.CCLD; int look = LookOf(p.Ch, god); var P = Ensure(look);
            var s = Input(p, f, t, isClone);
            RigPose.Build(P, s, list);
            var def = Defs.Char(god ? CharId.CCLD : p.Ch);
            float k = Mathf.Sqrt(Mathf.Max(1, p.W * p.H) / Mathf.Max(1, def.W * def.H)); // 巨大化、Boss 都按碰撞盒整体放大
            if (p.Ch == CharId.CCLD2 && p.Divine) k *= 1.35f;
            float rr = rot * f, ca = Mathf.Cos(rr * Mathf.Deg2Rad), sa = Mathf.Sin(rr * Mathf.Deg2Rad), pivY = def.H * .5f;
            bool deity = p.Ch == CharId.CCLD2 && p.Divine;
            Color warm = deity ? new Color(1, .97f, .88f, 1) : Color.white;
            Cache cache = null;
            if (!isClone) { if (!caches.TryGetValue(p.Slot, out cache)) caches[p.Slot] = cache = new Cache(); cache.L.Clear(); cache.Frame = Time.frameCount; }
            var F = fill[look]; var W = sil[look];
            float z = 0; Vector2 head = new Vector2(cx, footY - P.HeadR * 3); float headR = P.HeadR * k;
            foreach (var d in list)
            {
                var spr = F[d.Part]; if (spr == null) continue;
                float rx = d.X, ry = d.Y - pivY; float qx = rx * ca - ry * sa, qy = rx * sa + ry * ca + pivY; // 整体旋转（绕身体中心）
                float gx = cx + f * qx * k * sx, gy = footY - qy * k * sy, w = d.W * k * sx, h = d.H * k * sy;
                float ur = f > 0 ? d.Rot + rot : -d.Rot + rot; bool flip = f < 0;
                Color c = (d.Back ? new Color(.8f, .8f, .86f, 1) : Color.white) * tint * warm;
                if (look == 0 && d.Part == RP.Weapon) c *= RigPose.HueTint(p.Hue);
                // 外圈描边（玩家颜色）
                if (rim.a > 0 && W[d.Part] != null) { var e = sp.Put(W[d.Part], gx, gy, w + rimPx * 2, h + rimPx * 2, rim, o, ur); e.flipX = flip; }
                var r = sp.Put(spr, gx, gy, w, h, c, o + 1, ur); r.flipX = flip;
                z -= .01f; var lp = r.transform.localPosition; lp.z = z; r.transform.localPosition = lp; // 同一排序层里按 z 分前后
                if (flash && W[d.Part] != null) { var wr = sp.Put(W[d.Part], gx, gy, w, h, new Color(1, 1, 1, boss ? .28f : .7f), o + 2, ur); wr.flipX = flip; }
                if (cache != null) cache.L.Add(new Drawn { S = spr, W = W[d.Part], X = gx, Y = gy, Wd = w, Ht = h, Rot = ur, Flip = flip, Back = d.Back, C = c });
                if (d.Part == RP.Head || d.Part == RP.HeadHurt) head = new Vector2(gx, gy);
            }
            if (cache != null) { cache.Head = head; cache.HeadR = headR; }
            if (boss)
            {   // Boss：头顶一顶暗红金边小皇冠（慢慢起伏）
                float bob = Mathf.Sin(t * 1.8f) * 1.5f * k;
                sp.Put(Art.Soft, head.x, head.y - headR * 1.25f + bob, headR * 2.2f, headR * 1.4f, new Color(.9f, .15f, .1f, .3f), o + 3, 0, true);
                sp.Put(Art.Crown, head.x + f * headR * .1f, head.y - headR * 1.2f + bob, headR * 1.3f, headR * .95f, new Color(1, .55f, .45f, 1), o + 3, -f * 8);
            }
        }

        /// <summary>透视层（排序 38）：把这一帧画过的人用半透明再盖一层，被特效挡住时也看得见人。</summary>
        public static void Overlays(SpritePool sp, float alpha = .38f)
        {
            if (!Enabled) return;
            foreach (var kv in caches)
            {
                var c = kv.Value; if (c.Frame != Time.frameCount) continue; float z = 0;
                foreach (var d in c.L) { if (d.Back) continue; var col = d.C; col.a *= alpha; var r = sp.Put(d.S, d.X, d.Y, d.Wd, d.Ht, col, 38, d.Rot); r.flipX = d.Flip; z -= .01f; var lp = r.transform.localPosition; lp.z = z; r.transform.localPosition = lp; }
            }
        }

        /// <summary>把某个人这一帧的造型在更高的排序上原样再画一遍（GodView 神化时用：本体压在光效上面，外面一圈深色边）。</summary>
        public static bool Redraw(SpritePool sp, int slot, int edgeOrder, Color edge, float edgePx, int bodyOrder, Color mul)
        {
            Cache c; if (!Enabled || !caches.TryGetValue(slot, out c) || c.Frame != Time.frameCount || c.L.Count == 0) return false;
            if (edge.a > 0) foreach (var d in c.L) if (d.W != null) { var e = sp.Put(d.W, d.X, d.Y, d.Wd + edgePx * 2, d.Ht + edgePx * 2, edge, edgeOrder, d.Rot); e.flipX = d.Flip; }
            float z = 0;
            foreach (var d in c.L) { var r = sp.Put(d.S, d.X, d.Y, d.Wd, d.Ht, d.C * mul, bodyOrder, d.Rot); r.flipX = d.Flip; z -= .01f; var lp = r.transform.localPosition; lp.z = z; r.transform.localPosition = lp; }
            return true;
        }
    }
}
