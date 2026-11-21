// 第十版「神」：ccld-改 的七日创世 / 神降 / 居合 / 出场特写，以及原版 ccld 的剑神。
// 纯画面层：只读 Sim，不写 Sim 的任何字段，不用 Sim 的随机数，慢动作只放慢“画面时间”（粒子、动画、镜头），模拟照常跑 → 联机确定性不受影响。
// 发光约束：加色层单层透明度 ≤ 0.5；全屏光芒只做一次平滑的亮起 → 淡出（不闪烁）；所有脉动都是 ≤1Hz 的正弦。
// 接入（共 6 处挂钩，见 handoff/接入说明.md）：
//   WorldView.Attach      → GodView.Attach(sim)
//   WorldView.LateUpdate  → dt *= GodView.Tick(真实dt, Sim, Audio != null)
//   WorldView.UpdateCamera→ GodView.Camera(ref target, ref size)（ViewPolish.Camera 之后）
//   WorldView.DrawBackground 末尾 → GodView.Background(sp, Sim, camPos, camSize, t)
//   WorldView.DrawPlayer  → GodView.Body(...)（画身体之前）与 GodView.Swing(...)（挥砍弧光处）
//   WorldView.DrawWorld   → GodView.World(sp, Sim, t)（NewCharView.Draw 之后）
//   GameMain.OnGUI（对局中，DrawHud 之后）→ GodView.Gui()
using System.Collections.Generic;
using UnityEngine;
using Brawl.Core;
using static Brawl.Core.Defs;

namespace Brawl.View
{
    public static class GodView
    {
        public static bool Enabled = true;
        /// <summary>出场 / 神降特写是否推镜头 + 慢动作（有人嫌晃可以关）。</summary>
        public static bool Cinematic = true;
        const float GLOW = .5f; static float G(float a) => Mathf.Clamp(a, 0, GLOW);
        static Color A(Color c, float a) { c.a = a; return c; }
        static readonly Color Gold = new Color(1, .82f, .3f), Cream = new Color(1, .96f, .8f), Ember = new Color(1, .55f, .18f), Blood = new Color(1, .3f, .32f);
        static readonly Color[] DayCol =
        {
            new Color(1, .97f, .82f),   // 光
            new Color(.45f, .78f, 1),   // 水
            new Color(.55f, 1, .55f),   // 地
            new Color(1, .62f, .25f),   // 日月
            new Color(1, .86f, .35f),   // 飞鸟
            new Color(.88f, .66f, .42f),// 造人
            new Color(.85f, .72f, 1),   // 安息
        };
        static readonly Color[] SkyCol =
        {
            new Color(1, .93f, .72f, .16f),
            new Color(.05f, .2f, .45f, .42f),
            new Color(.12f, .28f, .08f, .36f),
            new Color(.45f, .12f, .02f, .4f),
            new Color(.25f, .55f, .8f, .3f),
            new Color(.35f, .2f, .08f, .38f),
            new Color(.12f, .05f, .3f, .5f),
        };

        static Sim sim; static bool audible;
        static System.Random rng = new System.Random(10);
        static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        static float now; // 真实时间（不受慢动作影响）
        static float vnow; // 画面时间

        // ================= 状态 =================
        sealed class GS
        {
            public bool Divine, God, Alive, Seen; public int Day = -1; public float AfterT, LastX, LastY, Rim;
            public readonly List<Vector4> Trail = new List<Vector4>(); // x, y, face, 出生画面时间
            public float TrailBw, TrailBh;
        }
        static readonly GS[] gs = new GS[16];
        static GS St(Player p) { int i = p.Slot & 15; return gs[i] ?? (gs[i] = new GS()); }

        enum CK { None, Enter, Descend, Day, Iai, SwordGod, Finish }
        static CK ck; static float cT, cMax; static int cSlot = -1, cDay; static float cX, cY; static bool cFull;
        static float skyT, skyMax; static Color skyC; static int skyDay = -1;
        static float radiance, lb, lbTarget, dim; // 全屏光芒、电影黑边、压暗
        static float iaiT; static Vector2 iaiA, iaiB; static bool iaiArmed; static float iaiArmT;
        static GameState lastState = GameState.Menu; static int lastRound = -1;
        static Vector2 camP; static float camS = Defs.H / 2f;

        // 世界里的一次性特效
        struct Ev { public int K; public float X, Y, X2, Y2, Life, Max, Size, Rot; public Color C; public int Slot; }
        // K: 0 光柱 1 地面神纹 2 竖立神纹 3 放射光 4 星芒 5 冲击环 6 居合刀痕 7 横贯刀线 8 残影 9 大光翼展开 10 光矛落点
        static readonly List<Ev> evs = new List<Ev>();
        struct Mote { public float X, Y, Vx, Vy, Life, Max, Size, Rot, Vr, Ph; public int K; public Color C; }
        // K: 0 光点 1 羽毛 2 星芒 3 火花线 4 水花 5 金色符文（上升的小星）
        static readonly List<Mote> motes = new List<Mote>();
        static void AddMote(Mote m) { if (motes.Count < 420) motes.Add(m); }
        static void AddEv(Ev e) { if (evs.Count < 120) evs.Add(e); }

        // 光矛（第一日 / 第四日 / 第七日）位置跟踪：联机朋友端每帧重建子弹对象，所以按位置匹配
        static readonly List<Vector3> spearsPrev = new List<Vector3>(), spearsNow = new List<Vector3>();

        // ================= 生命周期 =================
        public static void Attach(Sim s)
        {
            if (sim != null) sim.OnFx -= OnFx;
            sim = s; if (s != null) { s.OnFx -= OnFx; s.OnFx += OnFx; }
            for (int i = 0; i < gs.Length; i++) gs[i] = null;
            evs.Clear(); motes.Clear(); spearsPrev.Clear(); ck = CK.None; cT = 0; skyT = 0; radiance = 0; lb = lbTarget = 0; dim = 0; iaiT = 0; iaiArmed = false;
            lastState = s != null ? s.State : GameState.Menu; lastRound = s != null ? s.RoundNo : -1;
            BattleFx.Attach(s);
        }

        static void Start(CK k, Player p, float dur, int day = 0)
        {
            if (ck == CK.Descend && cT > 0 && k != CK.Descend) return;               // 神降最优先
            if (ck == CK.Enter && cT > 0 && (k == CK.Day || k == CK.Iai)) return;
            ck = k; cT = cMax = dur; cSlot = p != null ? p.Slot : -1; cDay = day; cX = p != null ? p.Cx : camP.x; cY = p != null ? p.Cy : camP.y;
        }

        /// <summary>WorldView.LateUpdate：传真实 dt，返回画面时间倍率（慢动作时 &lt; 1）。</summary>
        public static float Tick(float dt, Sim S, bool sound)
        {
            if (!Enabled || S == null) return 1;
            if (S != sim) Attach(S);
            audible = sound; now += dt;
            // 出场：每局倒计时开始时，场上有 ccld-改 就来一段（第一局完整版，之后短版）
            if (S.State == GameState.Countdown && (lastState != GameState.Countdown || S.RoundNo != lastRound))
            {
                Player hero = null; foreach (var p in S.Players) if (p.Alive && p.Ch == CharId.CCLD2) { hero = p; break; }
                if (hero != null)
                {
                    cFull = S.RoundNo <= 1; Start(CK.Enter, hero, cFull ? 2.4f : 1.3f);
                    AddEv(new Ev { K = 0, X = hero.Cx, Y = hero.Y + hero.H, Life = 1.6f, Max = 1.6f, Size = 150, C = Gold, Slot = hero.Slot });
                    AddEv(new Ev { K = 1, X = hero.Cx, Y = hero.Y + hero.H, Life = 2.2f, Max = 2.2f, Size = 260, C = Gold, Slot = hero.Slot });
                    Feathers(hero.Cx, hero.Cy - 300, 14, 260);
                    if (cFull) { Sky(-1, new Color(.08f, .05f, .18f, .45f), 2.4f); Play("god_enter", .9f); }
                }
            }
            lastState = S.State; lastRound = S.RoundNo;

            foreach (var p in S.Players)
            {
                var s = St(p);
                if (!s.Seen) { s.Seen = true; s.Divine = p.Divine; s.God = p.God; s.Alive = p.Alive; s.Day = p.Day; }
                if (p.Ch == CharId.CCLD2 && p.Divine && !s.Divine && p.Alive) Descend(p);
                if (p.Ch == CharId.CCLD && p.God && !s.God && p.Alive) SwordGod(p);
                s.Divine = p.Divine; s.God = p.God; s.Alive = p.Alive;
                if (!p.Alive) { s.Trail.Clear(); continue; }
                // 神化后的金色气场粒子（画面时间里生成）
                if (p.Ch == CharId.CCLD2 && p.Divine)
                {
                    if (R(0, 1) < dt * 26) AddMote(new Mote { K = 0, X = p.Cx + R(-40, 40), Y = p.Y + p.H - R(0, 10), Vx = R(-10, 10), Vy = R(-120, -60), Life = R(.8f, 1.4f), Max = 1.4f, Size = R(4, 8), C = R(0, 1) < .5f ? Gold : Cream });
                    if (R(0, 1) < dt * 6) AddMote(new Mote { K = 5, X = p.Cx + R(-70, 70), Y = p.Cy + R(-10, 30), Vy = R(-70, -40), Life = 1.6f, Max = 1.6f, Size = R(8, 13), Rot = R(0, 360), Vr = R(-60, 60), C = Gold });
                }
                else if (p.Ch == CharId.CCLD2 && p.Energy >= 100 && R(0, 1) < dt * 18) // 神力已满：脚下冒金色火星，像在等待降临
                    AddMote(new Mote { K = 0, X = p.Cx + R(-16, 16), Y = p.Y + p.H, Vx = R(-20, 20), Vy = R(-160, -80), Life = .7f, Max = .7f, Size = R(3, 6), C = Gold });
                else if (p.Ch == CharId.CCLD && p.God && R(0, 1) < dt * 20)
                    AddMote(new Mote { K = 3, X = p.Cx + R(-30, 30), Y = p.Cy + R(-20, 30), Vx = R(-30, 30), Vy = R(-220, -120), Life = .5f, Max = .5f, Size = R(2, 3.5f), C = R(0, 1) < .5f ? Ember : Gold });
            }

            // 时间轴
            float scale = 1;
            if (cT > 0)
            {
                cT -= dt; float u = 1 - Mathf.Max(0, cT) / cMax;
                switch (ck)
                {
                    case CK.Descend:
                        if (Cinematic) scale = u < .08f ? Mathf.Lerp(1, .3f, u / .08f) : u < .45f ? .3f : Mathf.Lerp(.3f, 1, Mathf.Clamp01((u - .45f) / .25f));
                        lbTarget = u < .85f ? 1 : 0; dim = Mathf.Max(dim, u < .15f ? u / .15f * .5f : u < .75f ? .5f : (1 - u) / .25f * .5f);
                        break;
                    case CK.Enter:
                        if (Cinematic && cFull) scale = u < .5f ? .55f : Mathf.Lerp(.55f, 1, (u - .5f) / .5f);
                        lbTarget = cFull && u < .85f ? 1 : 0; if (cFull) dim = Mathf.Max(dim, .35f * Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI));
                        break;
                    case CK.Iai:
                        if (Cinematic) scale = u < .6f ? .22f : Mathf.Lerp(.22f, 1, (u - .6f) / .4f);
                        dim = Mathf.Max(dim, .45f * (1 - u));
                        break;
                    case CK.Day:
                        if (Cinematic && u < .12f) scale = .55f;
                        lbTarget = u < .7f ? .45f : 0;
                        break;
                    case CK.SwordGod:
                        if (Cinematic) scale = u < .3f ? .45f : Mathf.Lerp(.45f, 1, (u - .3f) / .7f);
                        lbTarget = u < .8f ? .7f : 0; dim = Mathf.Max(dim, .3f * Mathf.Sin(u * Mathf.PI));
                        break;
                }
                if (cT <= 0) { ck = CK.None; lbTarget = 0; }
            }
            else lbTarget = 0;
            lb = Mathf.MoveTowards(lb, lbTarget, dt * 3.2f);
            dim = Mathf.MoveTowards(dim, 0, dt * .9f);
            radiance = Mathf.MoveTowards(radiance, 0, dt * .55f);
            if (skyT > 0) skyT -= dt;
            if (iaiT > 0) iaiT -= dt;
            if (iaiArmed && now - iaiArmT > .25f) iaiArmed = false;
            float vdt = dt * scale; vnow += vdt;

            for (int i = motes.Count - 1; i >= 0; i--)
            {
                var m = motes[i]; m.Life -= vdt; if (m.Life <= 0) { motes.RemoveAt(i); continue; }
                m.X += m.Vx * vdt; m.Y += m.Vy * vdt; m.Rot += m.Vr * vdt;
                if (m.K == 1) { m.X += Mathf.Sin(vnow * 2.2f + m.Ph) * 40 * vdt; m.Rot = Mathf.Sin(vnow * 2.2f + m.Ph) * 35; }
                if (m.K == 4) m.Vy += 1400 * vdt;
                if (m.K == 3) { m.Vx *= Mathf.Pow(.9f, vdt * 60); m.Vy *= Mathf.Pow(.9f, vdt * 60); }
                motes[i] = m;
            }
            for (int i = evs.Count - 1; i >= 0; i--) { var e = evs[i]; e.Life -= vdt; if (e.Life <= 0) evs.RemoveAt(i); else evs[i] = e; }
            TrackSpears(S);
            return scale * BattleFx.Tick(dt, S);
        }

        // ================= 事件 =================
        static void OnFx(Fx f)
        {
            if (!Enabled || sim == null) return;
            if (f.Type == FxType.Text && f.Text != null)
            {
                if (f.Big)
                {
                    int day = System.Array.IndexOf(Sim.DayNames, f.Text);
                    if (day >= 0) { var p = NearestCh(f.X, f.Y + 60, 200, CharId.CCLD2); if (p != null) Day(p, day); return; }
                    if (f.Text == "见 切 !") { var p = NearestCh(f.X, f.Y + 60, 200, CharId.CCLD2); if (p != null) IaiBegin(p); return; }
                    if (f.Text == "终 结 !") { Start(CK.Finish, null, 1.6f); Play("finish", .6f); return; }
                }
            }
            else if (f.Type == FxType.Zap && iaiArmed && f.Color == 0xff4d5eu)
            {   // 见切后瞬移到对方身后：这条红线就是居合的路径
                iaiArmed = false; IaiPath(new Vector2(f.X, f.Y), new Vector2(f.A, f.B));
            }
        }
        static Player NearestCh(float x, float y, float maxD, CharId ch)
        {
            Player best = null; float bd = maxD * maxD;
            foreach (var p in sim.Players) { if (!p.Alive || p.Ch != ch) continue; float dx = p.Cx - x, dy = p.Cy - y, d = dx * dx + dy * dy; if (d < bd) { bd = d; best = p; } }
            if (best == null) foreach (var p in sim.Players) if (p.Alive && p.Ch == ch) { best = p; break; }
            return best;
        }
        static void Play(string n, float v) { if (audible) Res10.Play(n, v); }
        static void Sky(int day, Color c, float dur) { skyDay = day; skyC = c; skyT = skyMax = dur; }
        static void Feathers(float x, float y, int n, float spread)
        {
            for (int i = 0; i < n; i++) AddMote(new Mote { K = 1, X = x + R(-spread, spread), Y = y + R(-80, 60), Vx = R(-20, 20), Vy = R(40, 90), Life = R(1.6f, 2.6f), Max = 2.6f, Size = R(14, 24), Ph = R(0, 6.28f), C = R(0, 1) < .6f ? Cream : Gold });
        }
        static void Burst(float x, float y, int n, float sp, Color c, int k = 0)
        {
            for (int i = 0; i < n; i++) { float a = R(0, 6.283f), s = R(.3f, 1) * sp; AddMote(new Mote { K = k, X = x, Y = y, Vx = Mathf.Cos(a) * s, Vy = Mathf.Sin(a) * s, Life = R(.4f, .9f), Max = .9f, Size = k == 3 ? R(2, 4) : R(4, 9), C = c }); }
        }

        static void Descend(Player p)
        {
            Start(CK.Descend, p, 2.7f);
            float fx = p.Cx, fy = p.Y + p.H;
            AddEv(new Ev { K = 0, X = fx, Y = fy, Life = 2.4f, Max = 2.4f, Size = 340, C = Cream, Slot = p.Slot });
            AddEv(new Ev { K = 1, X = fx, Y = fy, Life = 2.8f, Max = 2.8f, Size = 520, C = Gold, Slot = p.Slot });
            AddEv(new Ev { K = 2, X = p.Cx, Y = p.Cy - 10, Life = 2.8f, Max = 2.8f, Size = 680, C = Gold, Slot = p.Slot });
            AddEv(new Ev { K = 3, X = p.Cx, Y = p.Cy - 10, Life = 2.8f, Max = 2.8f, Size = 1500, C = Cream, Slot = p.Slot });
            AddEv(new Ev { K = 9, X = p.Cx, Y = p.Cy - 10, Life = 2.4f, Max = 2.4f, Size = 2.2f, C = Cream, Slot = p.Slot });
            for (int i = 0; i < 3; i++) AddEv(new Ev { K = 5, X = p.Cx, Y = p.Cy, Life = .9f + i * .25f, Max = .9f + i * .25f, Size = 500 + i * 260, C = i == 1 ? Cream : Gold });
            AddEv(new Ev { K = 4, X = p.Cx, Y = p.Cy - 10, Life = 1.2f, Max = 1.2f, Size = 520, C = Cream });
            Feathers(p.Cx, p.Cy - 380, 34, 420); Burst(p.Cx, p.Cy, 60, 700, Gold, 3); Burst(p.Cx, p.Cy, 30, 360, Cream, 0);
            Sky(-1, new Color(.04f, .02f, .12f, .55f), 2.9f);
            radiance = .32f; Play("god_descend", 1);
        }
        static void SwordGod(Player p)
        {
            Start(CK.SwordGod, p, 1.7f);
            AddEv(new Ev { K = 0, X = p.Cx, Y = p.Y + p.H, Life = 1.5f, Max = 1.5f, Size = 220, C = Ember, Slot = p.Slot });
            AddEv(new Ev { K = 3, X = p.Cx, Y = p.Cy, Life = 1.7f, Max = 1.7f, Size = 900, C = Gold, Slot = p.Slot });
            AddEv(new Ev { K = 4, X = p.Cx, Y = p.Cy, Life = .9f, Max = .9f, Size = 380, C = Gold });
            for (int i = 0; i < 2; i++) AddEv(new Ev { K = 5, X = p.Cx, Y = p.Cy, Life = .8f + i * .3f, Max = .8f + i * .3f, Size = 420 + i * 240, C = i == 0 ? Gold : Ember });
            Burst(p.Cx, p.Cy, 50, 650, Ember, 3); Sky(-1, new Color(.3f, .1f, .02f, .35f), 1.8f); radiance = .22f; Play("god_day", .8f);
        }
        static void Day(Player p, int day)
        {
            St(p).Day = day; Start(CK.Day, p, 1.9f, day);
            var c = DayCol[day];
            AddEv(new Ev { K = 0, X = p.Cx, Y = p.Y + p.H, Life = 1.2f, Max = 1.2f, Size = 200, C = c, Slot = p.Slot });
            AddEv(new Ev { K = 1, X = p.Cx, Y = p.Y + p.H, Life = 1.6f, Max = 1.6f, Size = 330, C = c, Slot = p.Slot });
            AddEv(new Ev { K = 2, X = p.Cx, Y = p.Cy - 10, Life = 1.5f, Max = 1.5f, Size = 430, C = c, Slot = p.Slot });
            AddEv(new Ev { K = 3, X = p.Cx, Y = p.Cy - 10, Life = 1.4f, Max = 1.4f, Size = 1000, C = c, Slot = p.Slot });
            AddEv(new Ev { K = 9, X = p.Cx, Y = p.Cy - 10, Life = 1.1f, Max = 1.1f, Size = 1.7f, C = Color.Lerp(c, Color.white, .5f), Slot = p.Slot });
            AddEv(new Ev { K = 5, X = p.Cx, Y = p.Cy, Life = .8f, Max = .8f, Size = 520, C = c });
            Burst(p.Cx, p.Cy, 30, 520, c, 3);
            for (int i = 0; i < 10; i++) AddMote(new Mote { K = 5, X = p.Cx + R(-90, 90), Y = p.Cy + R(-20, 40), Vy = R(-160, -90), Life = 1.3f, Max = 1.3f, Size = R(10, 16), Rot = R(0, 360), Vr = R(-90, 90), C = c });
            switch (day)
            {
                case 0: radiance = .3f; break;
                case 4: Feathers(p.Cx, p.Cy - 200, 16, 300); break;
                case 6: Feathers(p.Cx, p.Cy - 300, 26, 380); radiance = .18f; break;
            }
            Sky(day, SkyCol[day], 2.6f); Play("god_day", .85f);
        }
        static void IaiBegin(Player p)
        {
            iaiArmed = true; iaiArmT = now; iaiA = new Vector2(p.Cx, p.Cy); iaiB = iaiA + new Vector2(p.Face * 160, 0);
            Start(CK.Iai, p, .75f); iaiT = .75f; Play("iai", 1);
            // 残影：原地留下一个白色剪影
            AddEv(new Ev { K = 8, X = p.Cx, Y = p.Cy, Life = .6f, Max = .6f, Size = 1, Rot = p.Face, Slot = p.Slot, C = new Color(1, .85f, .85f) });
            AddEv(new Ev { K = 6, X = iaiA.x - p.Face * 60, Y = iaiA.y, X2 = iaiB.x + p.Face * 60, Y2 = iaiB.y, Life = .55f, Max = .55f, Size = 70, C = Blood });
        }
        static void IaiPath(Vector2 a, Vector2 b)
        {
            iaiA = a; iaiB = b;
            for (int i = evs.Count - 1; i >= 0; i--) if (evs[i].K == 6) evs.RemoveAt(i); // 换成真实路径
            Vector2 d = (b - a); float len = Mathf.Max(1, d.magnitude); Vector2 n = d / len;
            AddEv(new Ev { K = 6, X = a.x - n.x * 80, Y = a.y - n.y * 80, X2 = b.x + n.x * 140, Y2 = b.y + n.y * 140, Life = .6f, Max = .6f, Size = 80, C = Blood });
            AddEv(new Ev { K = 7, X = b.x, Y = b.y, Life = .42f, Max = .42f, Size = 1, C = Color.white });
            AddEv(new Ev { K = 4, X = b.x, Y = b.y, Life = .4f, Max = .4f, Size = 260, C = new Color(1, .85f, .85f) });
            for (int i = 0; i < 26; i++) { float k = R(0, 1); AddMote(new Mote { K = 3, X = Mathf.Lerp(a.x, b.x, k), Y = Mathf.Lerp(a.y, b.y, k), Vx = -n.y * R(-260, 260) + n.x * R(100, 400), Vy = n.x * R(-260, 260) + n.y * R(100, 400), Life = R(.25f, .5f), Max = .5f, Size = R(2, 3.5f), C = R(0, 1) < .5f ? Color.white : Blood }); }
        }

        static void TrackSpears(Sim S)
        {
            spearsNow.Clear();
            foreach (var b in S.Projs) if (b.Kind == ProjKind.Sword && b.Color == 0xfff2a8u && b.Owner != null && b.Owner.Ch == CharId.CCLD2) spearsNow.Add(new Vector3(b.X, b.Y, b.Vy));
            foreach (var o in spearsPrev)
            {
                bool still = false;
                foreach (var n in spearsNow) if (Mathf.Abs(n.x - o.x) < 24 && n.y >= o.y - 30 && n.y < o.y + 260) { still = true; break; }
                if (still || o.y < -40) continue;
                // 落地：竖直光爆 + 星芒 + 冲击环 + 金色碎光
                AddEv(new Ev { K = 10, X = o.x, Y = o.y, Life = .5f, Max = .5f, Size = 1, C = Cream });
                AddEv(new Ev { K = 4, X = o.x, Y = o.y, Life = .35f, Max = .35f, Size = 220, C = Cream });
                AddEv(new Ev { K = 5, X = o.x, Y = o.y, Life = .45f, Max = .45f, Size = 180, C = Gold });
                Burst(o.x, o.y, 14, 420, Gold, 3);
            }
            spearsPrev.Clear(); spearsPrev.AddRange(spearsNow);
        }

        // ================= 镜头 =================
        public static void Camera(ref Vector2 target, ref float size)
        {
            if (!Enabled || !Cinematic || cT <= 0 || sim == null) return;
            float x = cX, y = cY; foreach (var p in sim.Players) if (p.Slot == cSlot && p.Alive) { x = p.Cx; y = p.Cy; }
            float u = 1 - cT / cMax, k = 0, z = 1;
            switch (ck)
            {
                case CK.Descend: k = Ease(u < .12f ? u / .12f : u < .7f ? 1 : 1 - (u - .7f) / .3f); z = .58f; y -= 40; break;
                case CK.Enter: if (!cFull) return; k = Ease(u < .2f ? u / .2f : u < .75f ? 1 : 1 - (u - .75f) / .25f); z = .62f; y -= 30; break;
                case CK.Iai: { var m = (iaiA + iaiB) / 2; x = m.x; y = m.y; k = Ease(u < .15f ? u / .15f : 1 - (u - .15f) / .85f); z = .8f; break; }
                case CK.Day: k = Ease(Mathf.Sin(u * Mathf.PI)); z = .9f; break;
                case CK.SwordGod: k = Ease(u < .2f ? u / .2f : u < .6f ? 1 : 1 - (u - .6f) / .4f); z = .7f; break;
                default: return;
            }
            target = Vector2.Lerp(target, new Vector2(x, y), k * .85f); size = Mathf.Lerp(size, Mathf.Max(Defs.H / 2f * .42f, size * z), k);
        }
        static float Ease(float k) { k = Mathf.Clamp01(k); return k * k * (3 - 2 * k); }

        // ================= 背景：天色 + 神光 =================
        public static void Background(SpritePool sp, Sim S, Vector2 camPos, float camSize, float t)
        {
            camP = camPos; camS = camSize;
            if (!Enabled || S == null) return;
            float vw = camSize * 2 * 16f / 9 + 200, vh = camSize * 2 + 200;
            if (skyT > 0)
            {
                float u = 1 - skyT / skyMax, a = Mathf.Min(1, u / .12f) * Mathf.Min(1, skyT / .9f);
                var c = skyC; c.a *= a; sp.Put(Art.Pixel, camPos.x, camPos.y, vw, vh, c, -90);
                var rc = skyDay >= 0 ? Color.Lerp(DayCol[skyDay], Color.white, .3f) : Cream;
                // 从天上照下来的巨大神光（缓慢转动）
                float rx = cX, ry = camPos.y - camSize - 40;
                foreach (var p in S.Players) if (p.Slot == cSlot && p.Alive) rx = p.Cx;
                sp.Put(Res10.Rays, rx, ry, camSize * 3.4f, camSize * 3.4f, A(rc, G(.42f * a)), -89, t * 4, true);
                sp.Put(Art.Soft, rx, ry + 60, camSize * 2.2f, camSize * 1.2f, A(rc, G(.3f * a)), -89, 0, true);
                if (skyDay == 6 || skyDay == -1) for (int i = 0; i < 40; i++)
                    {   // 安息 / 神降：夜空里慢慢亮起的金色星
                        float hx = Mathf.Repeat(i * .618f, 1), hy = Mathf.Repeat(i * .377f, 1) * .6f, tw = .5f + .5f * Mathf.Sin(t * 1.3f + i);
                        sp.Put(Art.Star4, camPos.x + (hx - .5f) * vw, camPos.y - vh / 2 + hy * vh, 10 + 6 * tw, 10 + 6 * tw, A(Gold, G(.45f * a * tw)), -89, t * 20, true);
                    }
            }
            // 神化期间：天顶一束常驻的淡金光照着他
            foreach (var p in S.Players)
                if (p.Alive && p.Ch == CharId.CCLD2 && p.Divine)
                    sp.Put(Res10.Rays, p.Cx, camPos.y - camSize - 80, camSize * 2.2f, camSize * 2.2f, A(Cream, .16f), -89, -t * 3, true);
        }

        // ================= 角色身上：金色残影 + 气场 =================
        /// <summary>DrawPlayer 里画身体之前调用（white 是身体的纯白剪影，bw/bh 是身体尺寸）。</summary>
        public static void Body(SpritePool sp, Player p, bool isClone, Sprite white, float cx, float bodyCy, float footY, float bw, float bh, float rot, int f, int o, float t)
        {
            if (!Enabled || isClone || white == null) return;
            bool deity = p.Ch == CharId.CCLD2 && p.Divine, sword = p.Ch == CharId.CCLD && p.God;
            if (!deity && !sword && !(p.Ch == CharId.CCLD2 && p.Energy >= 100)) { St(p).Trail.Clear(); return; }
            var s = St(p);
            if (deity || sword)
            {
                // 残影：移动时每 0.045 秒（画面时间）留一个金色剪影，0.32 秒淡出
                float spd = Mathf.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy);
                if (vnow - s.AfterT > .045f && spd > 80) { s.AfterT = vnow; s.Trail.Add(new Vector4(cx, bodyCy, f, vnow)); if (s.Trail.Count > 9) s.Trail.RemoveAt(0); }
                for (int i = s.Trail.Count - 1; i >= 0; i--)
                {
                    var q = s.Trail[i]; float age = (vnow - q.w) / .32f; if (age >= 1) { s.Trail.RemoveAt(i); continue; }
                    var tc = deity ? Color.Lerp(Cream, Gold, age) : Color.Lerp(Gold, Ember, age);
                    var r = sp.Put(white, q.x, q.y, bw * (1 + .06f * age), bh * (1 + .06f * age), A(tc, G(.42f * (1 - age))), o - 2, rot, true); r.flipX = q.z < 0;
                }
            }
            if (deity)
            {
                float k = .5f + .5f * Mathf.Sin(t * 1.6f);
                // 脚下旋转的七芒星神纹（压扁成地面上的椭圆）
                sp.Put(Res10.Sigil, cx, footY - 2, 190 + 10 * k, 52 + 3 * k, A(Gold, G(.38f + .08f * k)), o - 3, 0, true);
                // 身后竖立的符文环 + 一对大光翼
                sp.Put(Res10.RuneRing, cx, bodyCy - 4, 250, 250, A(Gold, .26f), 5, t * 9, true);
                float flap = Mathf.Sin(t * 2.1f) * 7, glide = !p.OnGround && p.Vy > 0 ? 10 : 0;
                for (int side = -1; side <= 1; side += 2)
                {
                    float ang = side > 0 ? 14 + flap + glide : 180 - 14 - flap - glide;
                    var r = sp.Put(Res10.WingBig, cx + side * 74, bodyCy - 26, 170, 106, A(Cream, .42f), 5, side > 0 ? 18 + flap + glide : -18 - flap - glide, true); r.flipX = side < 0;
                }
                // 身体金色描边（慢呼吸）
                var rim = sp.Put(white, cx, bodyCy, bw + 10 + 4 * k, bh + 10 + 4 * k, A(Gold, G(.3f + .12f * k)), o - 1, rot, true); rim.flipX = f < 0;
            }
            else if (sword)
            {   // 原版 ccld 剑神：橙金火焰气场（向上飘的火舌）
                float k = .5f + .5f * Mathf.Sin(t * 1.8f);
                sp.Put(Art.Soft, cx, bodyCy - 10, 150 + 20 * k, 190 + 20 * k, A(Ember, .28f), o - 2, 0, true);
                sp.Put(Res10.Sigil, cx, footY - 2, 150, 40, A(Ember, .32f), o - 3, -t * 30, true);
                var rim = sp.Put(white, cx, bodyCy, bw + 10, bh + 10, A(Gold, G(.3f + .1f * k)), o - 1, rot, true); rim.flipX = f < 0;
            }
            else
            {   // ccld-改 神力已满：身体外一圈慢慢呼吸的金光（等待降临）
                float k = .5f + .5f * Mathf.Sin(t * 2.4f);
                var rim = sp.Put(white, cx, bodyCy, bw + 8 + 6 * k, bh + 8 + 6 * k, A(Gold, G(.22f + .2f * k)), o - 1, rot, true); rim.flipX = f < 0;
                sp.Put(Art.Soft, cx, footY - 4, 90, 20, A(Gold, .3f * k), o - 2, 0, true);
            }
        }

        /// <summary>挥砍弧光（替代 WorldView 里 ccld 的单层刀光）：返回 true 表示已经画了，WorldView 不用再画。</summary>
        public static bool Swing(SpritePool sp, Player p, float cx, float cy, float w, int f, int o)
        {
            if (!Enabled || p.Swing <= 0) return false;
            if (p.Ch != CharId.CCLD && p.Ch != CharId.CCLD2) return false;
            float u = 1 - Mathf.Clamp01(p.Swing / .16f), sweep = Mathf.Lerp(-70, 50, Ease(u)), R = p.Ch == CharId.CCLD ? 78 : 70;
            bool divine = p.Ch == CharId.CCLD2 && p.Divine, god = p.Ch == CharId.CCLD && p.God;
            Color glow = divine ? Gold : p.Ch == CharId.CCLD2 ? Blood : god ? Ember : new Color(.55f, .8f, 1);
            if (divine || god) R *= 1.35f;
            float ox = cx + f * (w * .3f);
            for (int i = 2; i >= 0; i--)
            {   // 三层：外层彩色辉光、中层颜色、内层白芯；后面的层稍微落后形成拖影
                float ang = sweep - i * 16, rr = R * (1 + i * .1f);
                float rot = f > 0 ? -ang : 180 + ang;
                var c = i == 0 ? A(Color.white, .95f) : A(glow, i == 1 ? .5f : .3f);
                sp.Put(Res10.Crescent, ox, cy, rr * 1.15f, rr * 1.6f, c, o + 3 - i, rot, true);
            }
            // 刀尖星芒
            float ta = (f > 0 ? -sweep : 180 + sweep) * Mathf.Deg2Rad;
            sp.Put(Res10.Flare, ox + Mathf.Cos(ta) * R * .55f, cy - Mathf.Sin(ta) * R * .55f, 46, 46, A(Color.Lerp(glow, Color.white, .5f), .45f), o + 4, 0, true);
            return true;
        }

        // ================= 场上特效 =================
        public static void World(SpritePool sp, Sim S, float t)
        {
            if (!Enabled || S == null) return;
            BattleFx.Draw(sp, camP, camS, t);
            // —— 七日技能的场上物件加强 ——
            foreach (var g in S.Gadgets)
            {
                if (g.Dead || g.Owner == null || g.Owner.Ch != CharId.CCLD2) continue;
                switch (g.Kind)
                {
                    case GKind.WaterWall:
                        {   // 分开诸水：浪尖白沫喷溅 + 水雾 + 水墙上的光纹
                            float H = g.R * 2, k = .5f + .5f * Mathf.Sin(t * 1.5f);
                            sp.Put(Art.Soft, g.X, g.Y + H / 2, 320, 80, new Color(.75f, .9f, 1, .3f), 19, 0, true);
                            sp.Put(Res10.Pillar, g.X, g.Y, 150, H * 1.05f, new Color(.55f, .85f, 1, .32f + .06f * k), 18, 0, true);
                            sp.Put(Art.Wave, g.X + g.Dir * 50, g.Y - H * .38f, 70, 140, new Color(.9f, .97f, 1, .5f), 19, g.Dir > 0 ? -30 : 210, true);
                            if (R(0, 1) < .8f) AddMote(new Mote { K = 4, X = g.X + g.Dir * R(10, 50), Y = g.Y - H / 2 + R(-6, 10), Vx = g.Dir * R(150, 380), Vy = R(-420, -180), Life = R(.4f, .7f), Max = .7f, Size = R(3, 7), C = new Color(.85f, .95f, 1, .9f) });
                            if (R(0, 1) < .5f) AddMote(new Mote { K = 0, X = g.X + R(-40, 40), Y = g.Y + H / 2 - R(0, 30), Vx = g.Dir * R(40, 120), Vy = R(-60, -20), Life = .8f, Max = .8f, Size = R(10, 18), C = new Color(.7f, .88f, 1, .5f) });
                            break;
                        }
                    case GKind.Sun:
                        {   // 日月：日冕光芒 + 镜头光 + 热浪光点
                            float sz = Mathf.Max(30, g.R * .45f), a = Mathf.Clamp01(g.T / .5f);
                            sp.Put(Res10.Rays, g.X, g.Y, sz * 6, sz * 6, A(new Color(1, .7f, .3f), G(.4f * a)), 16, t * 8, true);
                            sp.Put(Res10.Rays, g.X, g.Y, sz * 4.2f, sz * 4.2f, A(new Color(1, .9f, .6f), G(.3f * a)), 16, -t * 13, true);
                            sp.Put(Res10.Flare, g.X, g.Y, sz * 4, sz * 4, A(Color.white, .4f * a), 19, 0, true);
                            if (R(0, 1) < .6f) { float an = R(0, 6.283f); AddMote(new Mote { K = 0, X = g.X + Mathf.Cos(an) * sz, Y = g.Y + Mathf.Sin(an) * sz, Vx = Mathf.Cos(an) * 80, Vy = Mathf.Sin(an) * 80 - 40, Life = .7f, Max = .7f, Size = R(5, 9), C = new Color(1, .75f, .3f) }); }
                            break;
                        }
                    case GKind.Vine:
                        {   // 大地与草木：藤蔓顶上开出发光的金花
                            float age = Mathf.Max(0, S.RoundT - g.Born), grow = Mathf.Clamp01((age - .3f) / .4f) * Mathf.Clamp01(g.T / .5f);
                            for (int i = 0; i < 5; i++)
                            {
                                float bx = g.X - g.R * .8f + i * g.R * .4f, hy = g.Y - (34 + (i * 37 % 20)) * grow;
                                sp.Put(Art.Soft, bx, hy, 26 * grow, 26 * grow, new Color(.8f, 1, .5f, .35f), 12, 0, true);
                                for (int pt = 0; pt < 5; pt++) sp.Put(NewCharArt.Petal ?? Art.Star4, bx, hy, 5 * grow, 11 * grow, new Color(1, .95f, .6f, .9f), 12, pt * 72 + t * 20, false);
                                sp.Put(Art.Dot, bx, hy, 4 * grow, 4 * grow, Gold, 13);
                            }
                            break;
                        }
                }
            }
            // 泥人：金色双眼 + 胸口神纹
            foreach (var c in S.Clones)
            {
                if (c.Kind != 2 || c.Owner == null || !c.Owner.Alive) continue;
                float cx = c.X + c.W / 2, top = c.Y + c.H * .3f; int f = c.Face == 0 ? 1 : c.Face; float k = .5f + .5f * Mathf.Sin(t * 1.7f + c.X * .01f);
                sp.Put(Res10.Sigil, cx, c.Y + c.H * .62f, 22, 22, A(Gold, .45f), 27, t * 40, true);
                for (int e = -1; e <= 1; e += 2) { sp.Put(Art.Soft, cx + f * 4 + e * 4, top, 12, 12, A(Gold, .4f + .1f * k), 27, 0, true); sp.Put(Art.Dot, cx + f * 4 + e * 4, top, 3.5f, 3.5f, Cream, 28); }
                sp.Put(Res10.Sigil, cx, c.Y + c.H, 64, 16, A(new Color(.9f, .65f, .4f), .3f), 9, -t * 20, true);
            }
            // 光矛：天上拖下来的光柱 + 地面落点预警神纹；光鸟：金色的鸟
            foreach (var b in S.Projs)
            {
                if (b.Owner == null || b.Owner.Ch != CharId.CCLD2) continue;
                if (b.Kind == ProjKind.Sword && b.Color == 0xfff2a8u)
                {
                    float topY = camP.y - camS - 60, len = Mathf.Max(60, b.Y - topY);
                    sp.Put(Res10.Pillar, b.X, b.Y - len / 2, 40, len, A(Cream, .3f), 12, 0, true);
                    sp.Put(Res10.Flare, b.X, b.Y + 20, 70, 70, A(Color.white, .4f), 16, 0, true);
                    float gy = GroundBelow(S, b.X, Mathf.Max(0, b.Y));
                    if (gy > b.Y)
                    {
                        float near = Mathf.Clamp01(1 - (gy - b.Y) / 900);
                        sp.Put(Res10.Sigil, b.X, gy - 2, 70 + 40 * near, 16 + 10 * near, A(Gold, G(.2f + .3f * near)), 9, t * 90, true);
                        sp.Put(Res10.Pillar, b.X, gy - 120, 26, 240, A(Gold, .12f + .18f * near), 8, 0, true);
                    }
                }
                else if (b.Kind == ProjKind.Bolt && b.Color == 0xffd84au)
                {
                    float ang = Mathf.Atan2(-b.Vy, b.Vx) * Mathf.Rad2Deg; bool flip = b.Vx < 0; int fr = Mathf.Repeat(t * 9 + b.X * .01f, 1) < .5f ? 0 : 1;
                    sp.Put(Art.Soft, b.X, b.Y, 70, 70, A(Gold, .35f), 15, 0, true);
                    var r = sp.Put(Res10.Bird(fr), b.X, b.Y, 54, 36, new Color(1, .9f, .55f, .95f), 17, flip ? ang - 180 : ang, true); r.flipX = flip;
                    if (R(0, 1) < .35f) AddMote(new Mote { K = 1, X = b.X, Y = b.Y, Vx = -b.Vx * .05f, Vy = -b.Vy * .05f + 20, Life = .9f, Max = .9f, Size = 10, Ph = R(0, 6), C = Gold });
                }
            }
            // 第七日 · 安息：无敌期间的金色圣域穹顶
            foreach (var p in S.Players)
            {
                if (!p.Alive || p.Ch != CharId.CCLD2 || !p.Divine || p.Inv <= 0 || St(p).Day != 6) continue;
                float k = .5f + .5f * Mathf.Sin(t * 1.4f);
                sp.Put(Art.Soft, p.Cx, p.Cy, 380, 380, A(Gold, .18f), 6, 0, true);
                sp.Put(Res10.RuneRing, p.Cx, p.Cy, 360, 360, A(Cream, .3f + .06f * k), 7, -t * 14, true);
                sp.Put(Art.Ring, p.Cx, p.Cy, 330, 330, A(Gold, .3f), 7, t * 20, true);
            }

            // —— 一次性特效 ——
            float vw = camS * 2 * 16f / 9 + 400;
            foreach (var e in evs)
            {
                float u = 1 - e.Life / e.Max, fade = Mathf.Min(1, e.Life / (e.Max * .35f)), fin = Mathf.Min(1, u / .12f);
                float ex = e.X, ey = e.Y;
                if (e.Slot >= 0 && e.K != 0 && e.K != 1 && e.K != 6 && e.K != 7 && e.K != 8) foreach (var p in S.Players) if (p.Slot == e.Slot && p.Alive && (e.K == 2 || e.K == 3 || e.K == 9)) { ex = p.Cx; ey = p.Cy - 10; }
                switch (e.K)
                {
                    case 0: // 天降光柱（从画面顶上到脚下），先粗后细
                        {
                            float topY = camP.y - camS - 100, len = Mathf.Max(100, ey - topY), wdt = e.Size * (1.2f - .6f * Ease(u)) * fin;
                            sp.Put(Res10.Pillar, ex, ey - len / 2, wdt, len, A(e.C, G(.5f * fade)), 3, 0, true);
                            sp.Put(Res10.Pillar, ex, ey - len / 2, wdt * .35f, len, A(Color.white, G(.5f * fade)), 3, 0, true);
                            sp.Put(Art.Soft, ex, ey, wdt * 2.4f, wdt * .5f, A(e.C, G(.45f * fade)), 3, 0, true);
                            break;
                        }
                    case 1: // 地面神纹（扁椭圆，展开后慢慢转）
                        {
                            float s = e.Size * (.4f + .6f * Ease(u * 3));
                            sp.Put(Res10.Sigil, ex, ey - 2, s, s * .26f, A(e.C, G(.5f * fade)), 2, 0, true);
                            sp.Put(Res10.RuneRing, ex, ey - 2, s * 1.25f, s * .32f, A(e.C, G(.3f * fade)), 2, 0, true);
                            break;
                        }
                    case 2: // 身后竖立的巨大神纹
                        {
                            float s = e.Size * (.55f + .45f * Ease(u * 2.5f));
                            sp.Put(Res10.Sigil, ex, ey, s, s, A(e.C, G(.45f * fade)), 4, vnow * 25, true);
                            sp.Put(Res10.RuneRing, ex, ey, s * 1.3f, s * 1.3f, A(Color.Lerp(e.C, Color.white, .4f), G(.3f * fade)), 4, -vnow * 15, true);
                            break;
                        }
                    case 3: // 放射神光
                        sp.Put(Res10.Rays, ex, ey, e.Size * (.7f + .3f * Ease(u * 2)), e.Size * (.7f + .3f * Ease(u * 2)), A(e.C, G(.38f * fade * fin)), 1, vnow * 6, true);
                        break;
                    case 4: // 星芒
                        { float s = e.Size * (.6f + .6f * u); sp.Put(Res10.Flare, ex, ey, s * 1.6f, s, A(e.C, G(.5f * (1 - u))), 34, 0, true); break; }
                    case 5: // 冲击环
                        { float s = e.Size * Ease(u); sp.Put(Art.Ring, ex, ey, s, s, A(e.C, G(.45f * (1 - u))), 25, 0, true); sp.Put(Art.RingThin, ex, ey, s * .9f, s * .9f, A(Color.white, G(.35f * (1 - u))), 25, 0, true); break; }
                    case 6: // 居合刀痕：毛笔一划，先“画出来”再淡出
                        {
                            float dx = e.X2 - e.X, dy = e.Y2 - e.Y, len = Mathf.Sqrt(dx * dx + dy * dy), reveal = Ease(Mathf.Min(1, u / .14f));
                            float ang = -Mathf.Atan2(dy, dx) * Mathf.Rad2Deg, L = len * reveal, mx = e.X + dx / Mathf.Max(1, len) * L / 2, my = e.Y + dy / Mathf.Max(1, len) * L / 2;
                            float thin = 1 - .5f * Ease(Mathf.Max(0, u - .3f) / .7f);
                            sp.Put(Res10.Slash, mx, my, L, e.Size * 1.3f * thin, A(e.C, G(.5f * fade)), 36, ang, true);
                            sp.Put(Res10.Slash, mx, my, L, e.Size * .55f * thin, A(Color.white, fade), 37, ang, true);
                            break;
                        }
                    case 7: // 横贯整个画面的一道细刀线
                        {
                            float k = 1 - u; sp.Put(Art.Pixel, ex, ey, vw * Ease(Mathf.Min(1, u / .1f)), 3 + 4 * k, A(Color.white, .9f * k), 37, -4, true);
                            sp.Put(Art.Pixel, ex, ey, vw, 26 * k, A(Blood, G(.3f * k)), 36, -4, true);
                            break;
                        }
                    case 8: // 居合残影（原地留下的白色剪影）
                        {
                            var w = Art.BodyWhite[(int)CharId.CCLD2]; if (w == null) break; var bs = w.bounds.size;
                            var r = sp.Put(w, ex, ey, bs.x * 1.05f, bs.y * 1.05f, A(e.C, G(.5f * (1 - u))), 33, 0, true); r.flipX = e.Rot < 0;
                            break;
                        }
                    case 9: // 光翼大展开
                        {
                            float sc = e.Size * (.6f + .4f * Ease(u * 3)), a = G(.5f * fade);
                            for (int side = -1; side <= 1; side += 2)
                            {
                                var r = sp.Put(Res10.WingBig, ex + side * 110 * sc, ey - 40 * sc, 260 * sc, 160 * sc, A(e.C, a), 4, side * (22 + 6 * Mathf.Sin(vnow * 2)), true); r.flipX = side < 0;
                            }
                            break;
                        }
                    case 10: // 光矛落地：竖直光爆
                        { float k = 1 - u; sp.Put(Res10.Pillar, ex, ey - 110, 90 * (1 + u), 260 * k + 40, A(e.C, G(.5f * k)), 33, 0, true); break; }
                }
            }
            // 粒子
            foreach (var m in motes)
            {
                float k = m.Life / m.Max;
                switch (m.K)
                {
                    case 0: sp.Put(Art.Soft, m.X, m.Y, m.Size * 2.2f, m.Size * 2.2f, A(m.C, G(.45f * k)), 32, 0, true); sp.Put(Art.Dot, m.X, m.Y, m.Size * .4f, m.Size * .4f, A(Color.white, .7f * k), 32, 0, true); break;
                    case 1: sp.Put(Res10.Feather, m.X, m.Y, m.Size * .45f, m.Size, A(m.C, .85f * Mathf.Min(1, k * 3)), 33, m.Rot, false); break;
                    case 2: sp.Put(Res10.Flare, m.X, m.Y, m.Size * 3, m.Size * 3, A(m.C, G(.5f * k)), 33, 0, true); break;
                    case 3: { float ang = Mathf.Atan2(-m.Vy, m.Vx) * Mathf.Rad2Deg, len = Mathf.Clamp(Mathf.Sqrt(m.Vx * m.Vx + m.Vy * m.Vy) * .05f, 6, 34); sp.Put(Art.Spark, m.X, m.Y, len, m.Size, A(m.C, .9f * k), 33, ang, true); break; }
                    case 4: sp.Put(Art.Dot, m.X, m.Y, m.Size, m.Size, A(m.C, m.C.a * k), 20, 0, false); break;
                    case 5: sp.Put(Art.Star4, m.X, m.Y, m.Size, m.Size, A(m.C, G(.5f * Mathf.Sin(k * Mathf.PI))), 33, m.Rot, true); break;
                }
            }
        }
        static float GroundBelow(Sim S, float x, float y)
        {
            int c = Mathf.FloorToInt(x / T); if (c < 0 || c >= S.COLS) return -1;
            for (int r = Mathf.Max(0, Mathf.FloorToInt(y / T)); r < S.ROWS; r++) if (S.Tiles[r * S.COLS + c] > 0) return r * T;
            return -1;
        }

        // ================= 屏幕层：电影黑边、压暗、全屏光芒、神谕大字 =================
        /// <summary>GameMain.OnGUI 里（对局中、DrawHud 之后）调用。坐标是 1280×720 设计坐标。</summary>
        public static void Gui()
        {
            if (!Enabled || sim == null || Event.current == null || Event.current.type != EventType.Repaint) return;
            // 压暗 + 暗角
            if (dim > .01f)
            {
                Ui.Rect(new Rect(-800, -400, 2880, 1520), new Color(0, 0, 0, dim * .55f));
                var vig = Res10.Tex("Ui/vignette"); if (vig != null) { var o = GUI.color; GUI.color = new Color(1, 1, 1, dim * 1.6f); GUI.DrawTexture(new Rect(-200, -120, 1680, 960), vig); GUI.color = o; }
            }
            // 全屏光芒（一次平滑亮起 → 淡出）
            if (radiance > .005f) Ui.Rect(new Rect(-800, -400, 2880, 1520), new Color(1, .95f, .82f, Mathf.Min(.3f, radiance)));
            // 电影黑边
            if (lb > .005f)
            {
                float h = 74 * Ease(lb);
                Ui.Rect(new Rect(-800, -400, 2880, 400 + h), Color.black); Ui.Rect(new Rect(-800, 720 - h, 2880, 400 + h), Color.black);
                Ui.Rect(new Rect(-800, h - 2, 2880, 2), A(Gold, .5f * lb)); Ui.Rect(new Rect(-800, 720 - h, 2880, 2), A(Gold, .5f * lb));
            }
            if (cT <= 0) return;
            float u = 1 - cT / cMax;
            switch (ck)
            {
                case CK.Descend:
                    Card("Text/descend", new Vector2(640, 250), 620, u, .1f, .78f, "神 降", Gold);
                    Sub("—— ccld-改 · 七日创世神 ——", 382, u, .25f, .78f);
                    break;
                case CK.Enter:
                    if (cFull) { Card("Text/entrance", new Vector2(640, 520), 560, u, .15f, .8f, "七日创世神", Gold); }
                    else Sub("七日创世神 · ccld-改", 560, u, .05f, .75f);
                    break;
                case CK.Day:
                    {
                        Card("Text/day" + cDay, new Vector2(640, 168), 560, u, .05f, .72f, Sim.DayNames[cDay], DayCol[cDay]);
                        DayRing(new Vector2(640, 262), u);
                        break;
                    }
                case CK.Iai:
                    Card("Text/iai", new Vector2(1010, 560), 300, u, .0f, .7f, "居 合", Blood, true);
                    break;
                case CK.SwordGod:
                    Card("Text/swordgod", new Vector2(640, 250), 520, u, .1f, .75f, "剑 神", Gold);
                    break;
                case CK.Finish:
                    Card("Text/finish", new Vector2(640, 200), 420, u, .0f, .55f, "终 结", Blood, true);
                    break;
            }
        }
        /// <summary>字卡：放大 → 落定 → 停留 → 淡出；横向两侧拉出金线。brush=true 时从左往右“写出来”。</summary>
        static void Card(string tex, Vector2 c, float width, float u, float inAt, float outAt, string fallback, Color col, bool brush = false)
        {
            if (u < inAt) return;
            float k = Mathf.Clamp01((u - inAt) / .14f), a = k * Mathf.Clamp01((1 - u) / Mathf.Max(.01f, 1 - outAt));
            float sc = Mathf.Lerp(1.35f, 1, Ease(k)) + .02f * (u - inAt);
            var t = Res10.Tex(tex);
            float w = width * sc, h = t != null ? w * t.height / (float)t.width : w * .3f;
            var r = new Rect(c.x - w / 2, c.y - h / 2, w, h);
            // 背后一团金光 + 两侧金线
            Ui.Sprite(new Rect(c.x - w * .75f, c.y - h * .9f, w * 1.5f, h * 1.8f), Art.Soft, A(col, .35f * a));
            float lw = 380 * Ease(k); Ui.Rect(new Rect(c.x - w / 2 - lw - 10, c.y, lw, 2), A(col, .7f * a)); Ui.Rect(new Rect(c.x + w / 2 + 10, c.y, lw, 2), A(col, .7f * a));
            var o = GUI.color; GUI.color = new Color(1, 1, 1, a);
            if (t != null)
            {
                if (brush && k < 1) { GUI.BeginGroup(new Rect(r.x, r.y - 20, r.width * Ease(k), r.height + 40)); GUI.DrawTexture(new Rect(0, 20, r.width, r.height), t); GUI.EndGroup(); }
                else GUI.DrawTexture(r, t);
            }
            else
            {
                var st = Ui.Title; var f0 = st.font; int fs = st.fontSize; if (Res10.Brush != null) st.font = Res10.Brush; st.fontSize = (int)(96 * sc);
                Ui.Text(new Rect(c.x - 600, c.y - 70, 1200, 140), fallback, st, A(col, a), 4, new Color(.15f, .04f, .02f, a)); st.font = f0; st.fontSize = fs;
            }
            GUI.color = o;
        }
        static void Sub(string s, float y, float u, float inAt, float outAt)
        {
            if (u < inAt) return;
            float k = Mathf.Clamp01((u - inAt) / .15f), a = k * Mathf.Clamp01((1 - u) / Mathf.Max(.01f, 1 - outAt));
            var st = Ui.LabelC; var f0 = st.font; int fs = st.fontSize; if (Res10.Covers(Res10.Brush, s)) st.font = Res10.Brush; st.fontSize = 30;
            Ui.Text(new Rect(0, y - 20 + 10 * (1 - Ease(k)), 1280, 44), s, st, A(Cream, a), 2, new Color(.12f, .04f, .02f, a)); st.font = f0; st.fontSize = fs;
        }
        /// <summary>七日轮：七颗星排成一道弧，已过的天暗金，今天亮白金并带光晕。</summary>
        static void DayRing(Vector2 c, float u)
        {
            float k = Mathf.Clamp01(u / .15f), a = k * Mathf.Clamp01((1 - u) / .28f);
            for (int i = 0; i < 7; i++)
            {
                float ang = Mathf.Lerp(-.55f, .55f, i / 6f), x = c.x + Mathf.Sin(ang) * 230, y = c.y - Mathf.Cos(ang) * 40 + 40;
                bool cur = i == cDay, past = i < cDay; float s = cur ? 30 : 16;
                if (cur) Ui.Sprite(new Rect(x - 34, y - 34, 68, 68), Art.Soft, A(DayCol[i], .5f * a));
                Ui.Sprite(new Rect(x - s / 2, y - s / 2, s, s), Art.Star4, A(cur ? Color.white : past ? Gold : new Color(1, 1, 1, .35f), a * (cur ? 1 : .8f)));
            }
        }
    }
}
