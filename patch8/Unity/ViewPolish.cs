// 第八版「画面展现升级」：纯表现层，只读 Sim，不写 Sim 的任何字段，不用 Sim 的随机数 → 联机确定性不受影响。
// 内容：
//   1）打击感：画面顿帧（只放慢画面/特效时间，并让挨打者和出手者的“画出来的位置”停一下再追上，模拟照常跑）、
//      大招镜头轻推近、受击白闪（加色 ≤0.5）+ 方向性冲击波纹、命中镜头往击退方向轻顶一下。
//   2）角色动作：起跳拉伸、落地扬尘、跑动按加速度前倾 + 踩步扬尘、转身压缩 + 急停扬尘、普攻挥动残影。
//   3）选人界面/图鉴：角色呼吸、每隔几秒一个小动作（按角色不同）、悬停/选中有光晕、换角色时弹一下。
//   4）地图：每张图一套主题剪影视差层（远/近两层）+ 雾带 + 主题环境粒子（星光、余烬、沙尘、雪、雨、孢子、落叶、高速气流）。
// 约束：所有发光（加色）层透明度 ≤ 0.5；不做频闪（闪烁频率都在 2Hz 以下，白闪只出现一次、0.1 秒内淡出）。
// 接入方式见 handoff/接入说明.md。所有入口都是静态方法，Enabled=false 时全部直接返回，原画面不变。
using System;
using System.Collections.Generic;
using UnityEngine;
using Brawl.Core;
using static Brawl.Core.Defs;

namespace Brawl.View
{
    public static class ViewPolish
    {
        /// <summary>总开关（可以接到设置里）。关掉后所有钩子都不起作用。</summary>
        public static bool Enabled = true;
        /// <summary>单独关掉顿帧（有人不喜欢可以关）。</summary>
        public static bool HitStopOn = true;
        const float GLOW = .5f; // 发光层透明度上限
        static float G(float a) => Mathf.Clamp(a, 0, GLOW);

        static Sim sim; static int curMap = -999;
        static readonly System.Random rng = new System.Random(8);
        static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        // ================= 状态 =================
        sealed class PS
        {
            public bool Ground = true, Swing, God, Divine, Alive; public int Face = 1, Jumps, GhostFrame = -1;
            public float AirVy, JumpT, TurnT, Lean, LastVx, FlashT, Step, GhostT, Accel;
        }
        static readonly PS[] ps = new PS[16];
        static PS St(Player p) { int i = p.Slot & 15; return ps[i] ?? (ps[i] = new PS()); }

        struct Hold { public bool On; public float X, Y, Ox, Oy; public bool Victim; }
        static readonly Hold[] holds = new Hold[16];
        static float stopT, stopMax, stopCd;

        static float ultT; static int ultSlot = -1; static float ultX, ultY; const float ULT = .8f;
        /// <summary>命中时镜头往击退方向顶一下的偏移（加到 WorldView 的镜头位置上）。</summary>
        public static Vector2 CamKick;

        struct Ghost { public Sprite S; public float X, Y, W, H, Rot, Life, Max; public bool Flip; public Color C; public int Order; }
        static readonly List<Ghost> ghosts = new List<Ghost>();
        struct Puff { public float X, Y, Vx, Vy, Size, Grow, Life, Max; public Color C; }
        static readonly List<Puff> puffs = new List<Puff>();
        struct Ripple { public float X, Y, Dir, Life, Max, Size; public int Kind; public Color C; }
        static readonly List<Ripple> ripples = new List<Ripple>();

        // ================= 生命周期 =================
        /// <summary>WorldView.Attach 里调用（本地局、朋友端镜像、标题界面演示局都会走到）。</summary>
        public static void Attach(Sim s)
        {
            if (sim != null) sim.OnFx -= OnFx;
            sim = s; if (s != null) { s.OnFx -= OnFx; s.OnFx += OnFx; }
            for (int i = 0; i < ps.Length; i++) { ps[i] = null; holds[i] = default(Hold); }
            ghosts.Clear(); puffs.Clear(); ripples.Clear(); stopT = 0; stopCd = 0; ultT = 0; CamKick = Vector2.zero; curMap = -999;
        }

        /// <summary>
        /// WorldView.LateUpdate 每帧调用，传入真实 dt，返回“画面 dt”（顿帧期间几乎为 0）。
        /// 只影响画面：粒子、动画时间、镜头跟随。模拟由 GameMain 按真实时间推进，不受影响。
        /// </summary>
        public static float Tick(float dt, Sim S)
        {
            if (!Enabled || S == null) return dt;
            if (S != sim) Attach(S);
            stopCd -= dt;
            float vdt = dt;
            if (stopT > 0)
            {
                stopT -= dt; vdt = dt * .06f;
                if (stopT <= 0) // 结束顿帧：把“停住的位置”和真实位置的差记成偏移，再平滑追上
                    foreach (var p in S.Players) { ref var h = ref holds[p.Slot & 15]; if (!h.On) continue; h.On = false; h.Ox = Mathf.Clamp(h.X - p.X, -60, 60); h.Oy = Mathf.Clamp(h.Y - p.Y, -60, 60); }
            }
            for (int i = 0; i < holds.Length; i++) { holds[i].Ox *= Mathf.Exp(-26 * dt); holds[i].Oy *= Mathf.Exp(-26 * dt); }
            CamKick *= Mathf.Exp(-16 * dt);
            if (ultT > 0) ultT -= dt;

            foreach (var p in S.Players)
            {
                var s = St(p);
                if (!p.Alive) { s.Alive = false; holds[p.Slot & 15] = default(Hold); continue; }
                if (!s.Alive) { s.Alive = true; s.Ground = p.OnGround; s.Face = p.Face; s.LastVx = p.Vx; s.God = p.God; s.Divine = p.Divine; s.Jumps = p.Jumps; }
                // 起跳：离地且向上速度大 → 拉伸 + 脚下两团小尘
                bool tookOff = s.Ground && !p.OnGround && p.Vy < -250;
                bool airJump = !p.OnGround && p.Jumps > s.Jumps && p.Vy < -250;
                if (tookOff || airJump)
                {
                    s.JumpT = .17f;
                    if (tookOff) { for (int k = -1; k <= 1; k += 2) AddPuff(p.Cx + k * p.W * .3f, p.Y + p.H, k * R(70, 130), R(-30, -8), 7, 34, .32f, Dust(.4f)); }
                    else ripples.Add(new Ripple { X = p.Cx, Y = p.Y + p.H, Life = .22f, Max = .22f, Size = p.W * 1.6f, Kind = 3, C = new Color(1, 1, 1, .35f) });
                }
                // 落地：按下落速度扬尘
                if (!s.Ground && p.OnGround && s.AirVy > 260)
                {
                    float k = Mathf.Clamp01((s.AirVy - 260) / 1200); int n = 4 + (int)(k * 6);
                    for (int i = 0; i < n; i++) { float side = i % 2 == 0 ? 1 : -1; AddPuff(p.Cx + side * R(0, p.W * .4f), p.Y + p.H - 2, side * R(60, 160 + 200 * k), R(-40, -6), R(6, 10), 30 + 40 * k, R(.3f, .5f), Dust(.45f)); }
                    ripples.Add(new Ripple { X = p.Cx, Y = p.Y + p.H, Life = .25f + .1f * k, Max = .25f + .1f * k, Size = p.W * (1.6f + 2 * k), Kind = 3, C = Dust(.35f) });
                }
                s.AirVy = p.OnGround ? 0 : Mathf.Max(0, p.Vy);
                // 转身：朝向改变 → 横向压缩；地上高速转身 → 急停扬尘
                if (p.Face != s.Face && p.Face != 0)
                {
                    s.TurnT = .12f;
                    if (p.OnGround && Mathf.Abs(s.LastVx) > 150) for (int i = 0; i < 3; i++) AddPuff(p.Cx - p.Face * p.W * .2f, p.Y + p.H - 2, -p.Face * R(40, 120), R(-30, -5), 6, 22, .3f, Dust(.38f));
                    s.Face = p.Face;
                }
                // 跑动：按加速度前倾 + 踩步扬尘（和 WorldView 的步伐同频：约每 0.21 秒一步）
                float acc = dt > 0 ? (p.Vx - s.LastVx) / dt : 0; s.Accel = Mathf.Lerp(s.Accel, acc, 1 - Mathf.Exp(-10 * dt));
                float leanTarget = p.OnGround ? -Mathf.Clamp(s.Accel * .0032f, -7, 7) : 0;
                s.Lean = Mathf.Lerp(s.Lean, leanTarget, 1 - Mathf.Exp(-12 * dt));
                if (p.OnGround && Mathf.Abs(p.Vx) > 140)
                {
                    float before = s.Step; s.Step += dt * 4.8f * Mathf.Clamp(Mathf.Abs(p.Vx) / 320, .6f, 1.4f);
                    if ((int)before != (int)s.Step) AddPuff(p.Cx - Mathf.Sign(p.Vx) * p.W * .35f, p.Y + p.H - 2, -Mathf.Sign(p.Vx) * R(30, 80), R(-35, -10), 5, 16, .35f, Dust(.32f));
                }
                s.LastVx = p.Vx; s.Ground = p.OnGround; s.Jumps = p.Jumps;
                // 大招：成神 / 天降（ccld-改）→ 镜头轻推近
                if ((p.God && !s.God) || (p.Divine && !s.Divine)) Ult(p);
                s.God = p.God; s.Divine = p.Divine;
                s.JumpT = Mathf.Max(0, s.JumpT - vdt); s.TurnT = Mathf.Max(0, s.TurnT - vdt); s.FlashT = Mathf.Max(0, s.FlashT - dt);
            }

            for (int i = puffs.Count - 1; i >= 0; i--)
            {
                var q = puffs[i]; q.Life -= vdt; if (q.Life <= 0) { puffs.RemoveAt(i); continue; }
                float d = Mathf.Pow(.9f, vdt * 60); q.X += q.Vx * vdt; q.Y += q.Vy * vdt; q.Vx *= d; q.Vy *= d; q.Size += q.Grow * vdt; puffs[i] = q;
            }
            for (int i = ghosts.Count - 1; i >= 0; i--) { var g = ghosts[i]; g.Life -= vdt; if (g.Life <= 0) ghosts.RemoveAt(i); else ghosts[i] = g; }
            for (int i = ripples.Count - 1; i >= 0; i--) { var r = ripples[i]; r.Life -= vdt; if (r.Life <= 0) ripples.RemoveAt(i); else ripples[i] = r; }
            StepAmbient(vdt, S);
            return vdt;
        }

        static void OnFx(Fx f)
        {
            if (!Enabled || sim == null) return;
            switch (f.Type)
            {
                case FxType.Hit:
                    {
                        Player v = Nearest(f.X, f.Y, 70, null); float dir = f.A == 0 ? 1 : Mathf.Sign(f.A), dmg = f.B;
                        var col = v != null ? Art.C(v.C.Color) : Color.white;
                        ripples.Add(new Ripple { X = f.X, Y = f.Y, Dir = dir, Life = .2f, Max = .2f, Size = 30 + dmg * 2.2f, Kind = 0, C = Color.Lerp(col, Color.white, .6f) });
                        if (dmg >= 14) ripples.Add(new Ripple { X = f.X, Y = f.Y, Dir = dir, Life = .32f, Max = .32f, Size = 60 + dmg * 3.2f, Kind = 1, C = new Color(1, .9f, .7f) });
                        if (v != null) St(v).FlashT = .1f;
                        CamKick += new Vector2(dir * Mathf.Min(7, 1.5f + dmg * .18f), -Mathf.Min(3, dmg * .06f));
                        if (HitStopOn && stopCd <= 0 && dmg >= 8)
                        {
                            stopMax = stopT = Mathf.Clamp(.04f + dmg * .0016f, .04f, .08f); stopCd = stopT + .18f; // 连续命中时最多约 30% 的时间在顿帧，不会显得卡
                            if (v != null) HoldNow(v, true);
                            var atk = Nearest(f.X - dir * 60, f.Y, 220, v); if (atk != null) HoldNow(atk, false);
                        }
                        break;
                    }
                case FxType.Text:
                    if (f.Big && ultT <= 0 && f.Text != null && !f.Text.StartsWith("-"))
                    { var p = Nearest(f.X, f.Y + 40, 150, null); if (p != null) Ult(p); }
                    break;
            }
        }
        static void HoldNow(Player p, bool victim) { ref var h = ref holds[p.Slot & 15]; if (h.On) return; h.On = true; h.X = p.X + h.Ox; h.Y = p.Y + h.Oy; h.Ox = h.Oy = 0; h.Victim = victim; }
        static void Ult(Player p) { ultT = ULT; ultSlot = p.Slot; ultX = p.Cx; ultY = p.Cy; }
        static Player Nearest(float x, float y, float maxD, Player not)
        {
            Player best = null; float bd = maxD * maxD;
            foreach (var p in sim.Players) { if (!p.Alive || p == not) continue; float dx = p.Cx - x, dy = p.Cy - y, d = dx * dx + dy * dy; if (d < bd) { bd = d; best = p; } }
            return best;
        }
        static Color Dust(float a)
        {
            var m = CurMapDef(); var c = m != null ? Color.Lerp(Art.C(m.Dirt), Color.white, .55f) : new Color(.8f, .8f, .9f); c.a = a; return c;
        }
        static void AddPuff(float x, float y, float vx, float vy, float size, float grow, float life, Color c)
        { if (puffs.Count < 220) puffs.Add(new Puff { X = x, Y = y, Vx = vx, Vy = vy, Size = size, Grow = grow, Life = life, Max = life, C = c }); }
        static MapDef CurMapDef() => sim == null ? null : Maps.All[Mathf.Clamp(sim.CurMap, 0, Maps.All.Length - 1)];

        // ================= WorldView 钩子 =================
        /// <summary>DrawPlayer 开头：顿帧时把挨打者/出手者画在“停住”的位置，结束后平滑追上；挨打者顿帧时轻微抖动。</summary>
        public static void HoldPos(Player p, ref float px, ref float py)
        {
            if (!Enabled) return; var h = holds[p.Slot & 15];
            if (h.On) { px = h.X; py = h.Y; if (h.Victim && stopMax > 0) px += Mathf.Sin(Time.unscaledTime * 95) * 2.5f * Mathf.Clamp01(stopT / stopMax); }
            else { px += h.Ox; py += h.Oy; }
        }
        /// <summary>DrawPlayer 里算完 sx/sy（“出手往前一冲”那行之后）调用：起跳拉伸、转身压缩。</summary>
        public static void Scale(Player p, bool isClone, ref float sx, ref float sy)
        {
            if (!Enabled || isClone) return; var s = St(p);
            if (s.JumpT > 0) { float k = s.JumpT / .17f; k *= k; sy *= 1 + .22f * k; sx *= 1 - .14f * k; }
            if (s.TurnT > 0) { float k = Mathf.Sin(s.TurnT / .12f * Mathf.PI); sx *= 1 - .2f * k; sy *= 1 + .05f * k; }
        }
        /// <summary>DrawPlayer 里算完 rot（二段跳空翻那行之后）调用：按加速度前倾。</summary>
        public static void Lean(Player p, bool isClone, ref float rot)
        {
            if (!Enabled) return; rot += St(p).Lean;
        }
        /// <summary>DrawPlayer 画完身体（白闪那行之后）调用：记录挥动残影、画受击白闪描边。</summary>
        public static void PlayerFx(SpritePool sp, Player p, bool isClone, Sprite white, float cx, float bodyCy, float bw, float bh, float rot, int face, int order)
        {
            if (!Enabled || isClone || white == null) return; var s = St(p);
            if (p.Swing > 0 && s.GhostFrame != Time.frameCount)
            {
                s.GhostFrame = Time.frameCount; s.GhostT -= Time.unscaledDeltaTime;
                if (!s.Swing) // 刚出手：一道挥动弧影
                {
                    var cc = Art.C(p.C.Color);
                    ripples.Add(new Ripple { X = cx, Y = bodyCy, Dir = face, Life = .16f, Max = .16f, Size = Mathf.Max(bw, bh) * 1.25f, Kind = 2, C = Color.Lerp(cc, Color.white, .55f) });
                    s.GhostT = 0;
                }
                if (s.GhostT <= 0 && ghosts.Count < 80)
                {
                    s.GhostT = .03f; var c = Color.Lerp(Art.C(p.C.Color), Color.white, .35f); c.a = .32f;
                    ghosts.Add(new Ghost { S = white, X = cx, Y = bodyCy, W = bw, H = bh, Rot = rot, Flip = face < 0, Life = .15f, Max = .15f, C = c, Order = order - 1 });
                }
            }
            s.Swing = p.Swing > 0;
            if (s.FlashT > 0)
            {
                float k = s.FlashT / .1f; var r = sp.Put(white, cx, bodyCy, bw + 6, bh + 6, new Color(1, 1, 1, G(.5f * k)), order + 2, rot, true); r.flipX = face < 0;
            }
        }
        /// <summary>UpdateCamera 里，算出 target/size 之后、夹到地图边界之前调用：大招时镜头轻推近。</summary>
        public static void Camera(ref Vector2 target, ref float size)
        {
            if (!Enabled || ultT <= 0 || sim == null) return;
            float x = ultX, y = ultY; foreach (var p in sim.Players) if (p.Slot == ultSlot && p.Alive) { x = p.Cx; y = p.Cy; }
            float u = 1 - ultT / ULT, k = Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI); k = k * k * (3 - 2 * k);
            target = Vector2.Lerp(target, new Vector2(x, y), .35f * k); size *= 1 - .14f * k;
        }

        /// <summary>DrawWorld 里画完光环（rings）之后调用：扬尘、残影、冲击波纹、前景环境粒子。</summary>
        public static void WorldFx(SpritePool sp, Vector2 camPos, float camSize, float t)
        {
            if (!Enabled || sim == null) return;
            foreach (var q in puffs)
            {
                float k = q.Life / q.Max; var c = q.C; c.a *= k * k;
                sp.Put(Art.Soft, q.X, q.Y - q.Size * .25f, q.Size * 1.5f, q.Size, c, 9);
            }
            foreach (var g in ghosts)
            {
                float k = g.Life / g.Max; var c = g.C; c.a = G(c.a * k);
                var r = sp.Put(g.S, g.X, g.Y, g.W, g.H, c, g.Order, g.Rot, true); r.flipX = g.Flip;
            }
            foreach (var r in ripples)
            {
                float u = 1 - r.Life / r.Max, e = 1 - (1 - u) * (1 - u), fade = 1 - u;
                switch (r.Kind)
                {
                    case 0: // 方向性冲击波纹：三道朝击退方向推开的弧
                    case 1:
                        for (int j = 0; j < (r.Kind == 0 ? 3 : 2); j++)
                        {
                            float rad = r.Size * (.35f + .3f * j) * (.3f + .7f * e), a = G((r.Kind == 0 ? .5f : .4f) * fade * (1 - j * .25f));
                            var c = r.C; c.a = a;
                            sp.Put(Arc, r.X + r.Dir * rad * .45f, r.Y, rad * 1.1f, rad * 2f, c, 28, r.Dir > 0 ? 0 : 180, true);
                        }
                        if (r.Kind == 0) for (int j = -1; j <= 1; j++)
                            {
                                float len = r.Size * (.9f + .4f * e), ang = j * 18f; var c = r.C; c.a = G(.4f * fade);
                                float rx = Mathf.Cos(ang * Mathf.Deg2Rad) * r.Dir, ry = Mathf.Sin(ang * Mathf.Deg2Rad);
                                sp.Put(Art.Spark, r.X + rx * len * .6f, r.Y - ry * len * .6f, len * .7f, 3, c, 28, (r.Dir > 0 ? 0 : 180) + ang * r.Dir, true);
                            }
                        break;
                    case 2: // 普攻挥动弧影：沿朝向扫过的三层弧
                        for (int j = 0; j < 3; j++)
                        {
                            float sweep = Mathf.Lerp(-55, 35, Mathf.Clamp01(u * 1.3f - j * .12f)), a = G(.42f * fade * (1 - j * .3f));
                            var c = r.C; c.a = a; float ang = r.Dir > 0 ? -sweep : 180 + sweep;
                            float ox = Mathf.Cos(ang * Mathf.Deg2Rad) * r.Size * .45f, oy = -Mathf.Sin(ang * Mathf.Deg2Rad) * r.Size * .45f;
                            sp.Put(Arc, r.X + ox * .4f, r.Y + oy * .4f, r.Size * .7f, r.Size * 1.3f, c, 27, ang, true);
                        }
                        break;
                    case 3: // 起跳/落地时地面一圈扁平的尘环
                        {
                            float w = r.Size * (.5f + e); var c = r.C; c.a *= fade;
                            sp.Put(Art.RingThin, r.X, r.Y, w, w * .16f, c, 9);
                            break;
                        }
                }
            }
            DrawAmbient(sp, camPos, camSize, t, true);
        }

        // ================= 程序生成的小贴图 =================
        static Sprite arc;
        /// <summary>朝 +x 方向的弧形（冲击波/挥砍用），两端淡出。</summary>
        static Sprite Arc
        {
            get
            {
                if (arc != null) return arc;
                int n = 64; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                    {
                        float u = (x + .5f) / n * 2 - 1, v = (y + .5f) / n * 2 - 1, d = Mathf.Sqrt(u * u + v * v), ang = Mathf.Atan2(v, u);
                        float band = Mathf.Clamp01(1 - Mathf.Abs(d - .78f) / .16f), spread = Mathf.Clamp01(Mathf.Cos(ang) * 1.3f);
                        float a = band * band * spread; px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                    }
                tex.SetPixels32(px); tex.Apply();
                arc = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 1);
                return arc;
            }
        }

        // ================= 地图主题：视差剪影 + 环境粒子 =================
        enum Th { Stars, Dusk, Sand, Snow, Storm, Factory, Train, Forest, Space, Forge, City }
        static Th ThemeOf(MapDef m)
        {
            switch (m.Name)
            {
                case "浮空岛": return Th.Stars; case "双塔对峙": return Th.Dusk; case "金字塔": return Th.Sand; case "冰川裂谷": return Th.Snow;
                case "风暴之巅": return Th.Storm; case "传送门工厂": return Th.Factory; case "天空列车": return Th.Train; case "蘑菇森林": return Th.Forest;
                case "星港空间站": return Th.Space; case "熔岩锻炉": return Th.Forge; case "双城巨桥": return Th.City;
            }
            switch (m.Hazard)
            {
                case MapHazard.Ice: return Th.Snow; case MapHazard.Wind: return Th.Storm; case MapHazard.Portal: return Th.Factory; case MapHazard.Train: return Th.Train;
                case MapHazard.Mushroom: return Th.Forest; case MapHazard.Space: return Th.Space; case MapHazard.Forge: return Th.Forge; case MapHazard.Bridge: return Th.City;
            }
            return Th.Stars;
        }
        static Th theme; static Sprite silFar, silNear; static Color fogC;

        struct Amb { public float X, Y, Vx, Vy, K, Size, Ph, Rot; public int Kind; public Color C; }
        const int AMB_N = 110; static readonly Amb[] amb = new Amb[AMB_N]; static int ambN;
        const float REFW = 2200, REFH = 1240; // 粒子平面尺寸（设计像素），按镜头视差折算到世界坐标
        static float shootT = 3, shootX, shootY, shootLife;

        static void BuildTheme(MapDef m, int seed)
        {
            theme = ThemeOf(m);
            var hill = Art.C(m.Hill); var skyB = Art.C(m.SkyBottom);
            var farC = Color.Lerp(hill, skyB, .45f); farC.a = .85f;
            var nearC = Color.Lerp(hill, Color.black, .25f); nearC.a = .92f;
            fogC = Color.Lerp(skyB, Color.white, .25f); fogC.a = .13f;
            if (silFar != null) { UnityEngine.Object.Destroy(silFar.texture); UnityEngine.Object.Destroy(silFar); }
            if (silNear != null) { UnityEngine.Object.Destroy(silNear.texture); UnityEngine.Object.Destroy(silNear); }
            silFar = Sil(theme, farC, seed, false); silNear = Sil(theme, nearC, seed * 7 + 3, true);
            // 环境粒子
            ambN = 0; int n; switch (theme) { case Th.Storm: n = 110; break; case Th.Train: n = 70; break; case Th.Space: case Th.Stars: n = 80; break; default: n = 90; break; }
            for (int i = 0; i < n; i++)
            {
                var a = new Amb { X = R(0, 1), Y = R(0, 1), Ph = R(0, 6.283f), Rot = R(0, 360) };
                bool front = i % 4 == 0; a.K = front ? R(1.15f, 1.4f) : R(.3f, .85f); // 1/4 在前景（比角色更靠近镜头）
                float depth = front ? 1.4f : Mathf.Lerp(.55f, 1, (a.K - .3f) / .55f);
                switch (theme)
                {
                    case Th.Stars: a.Kind = i % 3 == 0 ? 5 : 0; a.Size = R(5, 11) * depth; a.Vx = R(-6, 6); a.Vy = R(-10, -3); a.C = i % 3 == 0 ? new Color(.75f, .7f, 1) : new Color(.85f, .9f, 1); break;
                    case Th.Space: a.Kind = 0; a.Size = R(4, 10) * depth; a.Vx = R(-14, -4); a.Vy = 0; a.C = i % 4 == 0 ? new Color(.6f, .9f, 1) : new Color(1, .95f, .85f); break;
                    case Th.Dusk: a.Kind = i % 2 == 0 ? 6 : 1; a.Size = R(4, 8) * depth; a.Vx = R(-30, 30); a.Vy = a.Kind == 1 ? R(-50, -20) : R(30, 60); a.C = a.Kind == 1 ? new Color(1, .6f, .25f) : Color.Lerp(new Color(.85f, .4f, .2f), new Color(.95f, .75f, .3f), R(0, 1)); break;
                    case Th.Forge: a.Kind = 1; a.Size = R(3, 8) * depth; a.Vx = R(-20, 20); a.Vy = R(-110, -40); a.C = i % 3 == 0 ? new Color(1, .85f, .4f) : new Color(1, .45f, .15f); break;
                    case Th.Factory: a.Kind = i % 3 == 0 ? 1 : 2; a.Size = R(3, 6) * depth; a.Vx = R(-30, 30); a.Vy = a.Kind == 1 ? R(-80, -30) : R(-12, 12); a.C = a.Kind == 1 ? new Color(1, .7f, .3f) : new Color(.8f, .75f, .85f); break;
                    case Th.Sand: a.Kind = 2; a.Size = R(2.5f, 5) * depth; a.Vx = R(120, 260); a.Vy = R(-10, 20); a.C = new Color(.95f, .82f, .55f); break;
                    case Th.Snow: a.Kind = 3; a.Size = R(3, 7) * depth; a.Vx = R(-15, 25); a.Vy = R(45, 95) * depth; a.C = Color.white; break;
                    case Th.Storm: a.Kind = i % 5 == 0 ? 6 : 4; a.Size = a.Kind == 4 ? R(28, 46) * depth : R(5, 8) * depth; a.Vx = a.Kind == 4 ? 180 : R(60, 140); a.Vy = a.Kind == 4 ? R(850, 1050) : R(30, 70); a.C = a.Kind == 4 ? new Color(.75f, .85f, 1) : new Color(.45f, .7f, .35f); break;
                    case Th.Train: a.Kind = i % 3 == 0 ? 6 : 7; a.Size = a.Kind == 7 ? R(90, 200) * depth : R(4, 8) * depth; a.Vx = a.Kind == 7 ? -R(1200, 1900) * depth : -R(500, 900); a.Vy = R(-10, 30); a.C = a.Kind == 7 ? Color.white : new Color(.9f, .7f, .35f); break;
                    case Th.Forest: a.Kind = 5; a.Size = R(4, 9) * depth; a.Vx = R(-12, 12); a.Vy = R(-22, -6); a.C = i % 3 == 0 ? new Color(.9f, 1, .5f) : new Color(.45f, 1, .85f); break;
                    case Th.City: a.Kind = 6; a.Size = R(4, 8) * depth; a.Vx = R(20, 60); a.Vy = R(35, 70); a.C = Color.Lerp(new Color(.9f, .55f, .25f), new Color(.6f, .8f, .35f), R(0, 1)); break;
                }
                amb[ambN++] = a;
            }
        }

        static void StepAmbient(float dt, Sim S)
        {
            if (ambN == 0) return;
            float wind = S.WindState == 1 ? S.WindDir * 520 : S.WindState != 0 ? S.WindDir * 120 : 0;
            for (int i = 0; i < ambN; i++)
            {
                ref var a = ref amb[i];
                float vx = a.Vx, vy = a.Vy;
                switch (a.Kind)
                {
                    case 3: vx += Mathf.Sin(Time.unscaledTime * .9f + a.Ph) * 22; break;                 // 雪：左右飘
                    case 6: vx += Mathf.Sin(Time.unscaledTime * 1.3f + a.Ph) * 35; a.Rot += dt * (80 + a.Ph * 20); break; // 落叶：摆动 + 翻转
                    case 1: vx += Mathf.Sin(Time.unscaledTime * 1.7f + a.Ph) * 18; break;                 // 余烬：上升时晃动
                    case 5: vx += Mathf.Sin(Time.unscaledTime * .7f + a.Ph) * 16; vy += Mathf.Cos(Time.unscaledTime * .6f + a.Ph) * 10; break; // 孢子/光点：游走
                }
                if (a.Kind != 7) vx += wind * (a.Kind == 4 ? .6f : 1);
                a.X = Mathf.Repeat(a.X + vx * dt / REFW, 1); a.Y = Mathf.Repeat(a.Y + vy * dt / REFH, 1);
            }
            if (theme == Th.Space || theme == Th.Stars)
            {   // 偶尔一颗流星（慢，0.7 秒划过，不闪）
                shootT -= dt; if (shootLife > 0) shootLife -= dt;
                if (shootT <= 0) { shootT = R(3.5f, 7); shootLife = .7f; shootX = R(.15f, .85f); shootY = R(.05f, .35f); }
            }
        }

        static void DrawAmbient(SpritePool sp, Vector2 camPos, float camSize, float t, bool front)
        {
            float vw = camSize * 2 * 16f / 9 * 1.1f, vh = camSize * 2 * 1.1f, zs = camSize / (Defs.H / 2f);
            for (int i = 0; i < ambN; i++)
            {
                var a = amb[i]; if ((a.K > 1) != front) continue;
                float nx = Mathf.Repeat(a.X - camPos.x * a.K / REFW, 1), ny = Mathf.Repeat(a.Y - camPos.y * a.K / REFH, 1);
                float x = camPos.x + (nx - .5f) * vw, y = camPos.y + (ny - .5f) * vh, s = a.Size * zs;
                int order = front ? 36 : -91; float fa = front ? .6f : 1; // 前景淡一些，不挡人
                var c = a.C;
                switch (a.Kind)
                {
                    case 0: // 星光：缓慢明暗（≤0.6Hz）
                        {
                            float tw = .5f + .5f * Mathf.Sin(t * 1.4f + a.Ph * 3);
                            c.a = G((.12f + .33f * tw) * fa); sp.Put(Art.Star4, x, y, s * (1 + .3f * tw), s * (1 + .3f * tw), c, order, a.Rot + t * 6, true);
                            c.a = G(.4f * fa); sp.Put(Art.Dot, x, y, s * .25f, s * .25f, c, order, 0, true);
                            break;
                        }
                    case 1: // 余烬
                        { float fl = .6f + .4f * Mathf.Sin(t * 2.2f + a.Ph * 5); c.a = G(.42f * fl * fa); sp.Put(Art.Soft, x, y, s * 2.2f, s * 2.2f, c, order, 0, true); c.a = G(.5f * fl * fa); sp.Put(Art.Dot, x, y, s * .5f, s * .5f, c, order, 0, true); break; }
                    case 2: // 沙尘 / 漂浮尘埃（普通混合）
                        c.a = .4f * fa; sp.Put(Art.Dot, x, y, s * 1.4f, s, c, order, 0, false); break;
                    case 3: // 雪
                        c.a = (front ? .45f : .5f); sp.Put(Art.Dot, x, y, s, s, c, order, 0, false);
                        if (front) { c.a = .18f; sp.Put(Art.Soft, x, y, s * 2.6f, s * 2.6f, c, order, 0, false); }
                        break;
                    case 4: // 雨丝（随风斜）
                        { c.a = .22f * fa; float wx = a.Vx + (sim != null && sim.WindState == 1 ? sim.WindDir * 520 * .6f : 0); float ang = Mathf.Atan2(-a.Vy, wx) * Mathf.Rad2Deg; sp.Put(Art.Spark, x, y, s, 2.2f * zs, c, order, ang, false); break; }
                    case 5: // 孢子 / 萤火：慢慢呼吸的光点
                        { float pu = .5f + .5f * Mathf.Sin(t * 1.1f + a.Ph * 2); c.a = G((.1f + .3f * pu) * fa); sp.Put(Art.Soft, x, y, s * 2.4f, s * 2.4f, c, order, 0, true); c.a = G((.25f + .25f * pu) * fa); sp.Put(Art.Dot, x, y, s * .45f, s * .45f, c, order, 0, true); break; }
                    case 6: // 落叶：翻转（宽度随角度变化）
                        { float flip = Mathf.Abs(Mathf.Cos(a.Rot * Mathf.Deg2Rad)); c.a = .75f * fa; sp.Put(Art.Dot, x, y, s * (.4f + .9f * flip), s * .55f, c, order, a.Rot * .5f, false); break; }
                    case 7: // 高速气流（天空列车）
                        c.a = G(.1f * fa); sp.Put(Art.Soft, x, y, s, s * .09f, c, order, 0, true); break;
                }
            }
            if (!front && shootLife > 0)
            {
                float k = 1 - shootLife / .7f, x = camPos.x + (shootX - .5f + k * .25f) * vw, y = camPos.y + (shootY - .5f + k * .12f) * vh;
                float a = G(.45f * Mathf.Sin(k * Mathf.PI));
                sp.Put(Art.Spark, x, y, 160 * zs, 3 * zs, new Color(1, .95f, .85f, a), -98, -25, true);
                sp.Put(Art.Soft, x + 70 * zs * .9f, y + 33 * zs * .9f, 18 * zs, 18 * zs, new Color(1, 1, 1, a), -98, 0, true);
            }
        }

        /// <summary>DrawBackground 末尾（飘浮尘埃之后）调用：主题剪影两层视差 + 雾带 + 光束 + 背景环境粒子。</summary>
        public static void Background(SpritePool sp, Sim S, Vector2 camPos, float camSize, float t)
        {
            if (!Enabled || S == null) return;
            if (S.CurMap != curMap) { curMap = S.CurMap; BuildTheme(Maps.All[Mathf.Clamp(S.CurMap, 0, Maps.All.Length - 1)], S.CurMap * 13 + 5); }
            float W = S.W, H = S.H; Vector2 cp = camPos - new Vector2(W / 2f, H / 2f);
            // 远层剪影：在云和远山之间（同 sortingOrder 时靠 z 决定前后）
            Tile(sp, silFar, cp, W, H, .8f, H * .6f, H * .42f, 1500, -96, -.5f);
            // 近层剪影：在近山前、地形后
            Tile(sp, silNear, cp, W, H, .42f, H * .8f, H * .34f, 1300, -94, -.5f);
            // 雾带（普通混合，压一压近层的对比，让角色更突出）
            var fr = sp.Put(Art.Soft, W / 2f + cp.x * .4f, H * .86f + cp.y * .4f, W * 2.4f, H * .5f, fogC, -94); var fp = fr.transform.localPosition; fp.z = -.8f; fr.transform.localPosition = fp;
            // 主题光束（森林/黄昏/城市：斜照的光；空间站：大行星）
            if (theme == Th.Forest || theme == Th.Dusk || theme == Th.City)
            {
                var bc = theme == Th.Forest ? new Color(.7f, 1, .8f) : new Color(1, .8f, .5f);
                for (int i = 0; i < 4; i++)
                {
                    float x = W * (.15f + .25f * i) + cp.x * .6f + Mathf.Sin(t * .25f + i) * 30; bc.a = G(.05f + .025f * Mathf.Sin(t * .4f + i * 1.7f));
                    sp.Put(Art.Soft, x, H * .45f + cp.y * .6f, 140, H * 1.3f, bc, -93, -18, true);
                }
            }
            if (theme == Th.Space)
            {
                float px = W * .25f + cp.x * .92f, py = H * .22f + cp.y * .92f;
                sp.Put(Art.Soft, px, py, 520, 520, new Color(.4f, .7f, 1, .16f), -98, 0, true);
                sp.Put(Art.Dot, px, py, 260, 260, new Color(.22f, .3f, .55f, 1), -97);
                sp.Put(Art.Dot, px - 30, py - 30, 200, 200, new Color(.32f, .45f, .75f, .8f), -97);
                sp.Put(Art.RingThin, px, py, 460, 90, new Color(.75f, .85f, 1, .4f), -97, -12, true);
            }
            DrawAmbient(sp, camPos, camSize, t, false);
        }
        static void Tile(SpritePool sp, Sprite s, Vector2 cp, float W, float H, float k, float cy, float h, float tileW, int order, float z)
        {
            if (s == null) return;
            float off = cp.x * k, x0 = -300 + Mathf.Repeat(off, tileW) - tileW; int n = Mathf.CeilToInt((W + 600) / tileW) + 2;
            for (int i = 0; i < n; i++)
            {
                var r = sp.Put(s, x0 + tileW * (i + .5f), cy + cp.y * k, tileW + 2, h, Color.white, order);
                var lp = r.transform.localPosition; lp.z = z; r.transform.localPosition = lp;
            }
        }

        // 剪影贴图：横向无缝，y=0 在底部。windows=true 时建筑上有零星亮窗。
        static Sprite Sil(Th th, Color col, int seed, bool windows)
        {
            const int w = 768, h = 192; var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[w * h]; var r = new System.Random(seed);
            Func<float> rn = () => (float)r.NextDouble();
            int n = 0; var S = new float[40 * 4]; // 每个形状：中心 x、宽、高、类型参数
            int count = th == Th.Snow ? 22 : th == Th.Sand ? 7 : th == Th.Stars ? 7 : th == Th.Forest ? 11 : 14;
            for (int i = 0; i < count && n < 40; i++, n++) { S[n * 4] = rn(); S[n * 4 + 1] = .02f + rn() * .05f; S[n * 4 + 2] = .35f + rn() * .55f; S[n * 4 + 3] = rn(); }
            var lit = new Color(1, .86f, .55f, Mathf.Min(col.a, .5f)); Color32 c32 = col, l32 = lit;
            for (int y = 0; y < h; y++)
            {
                float v = y / (float)h;
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w; bool inside = false; bool win = false;
                    // 底座：起伏的地面，各主题都有
                    float ground = .12f + .05f * Mathf.Sin(u * 6.283f * 3 + seed) + .03f * Mathf.Sin(u * 6.283f * 11 + seed * 2);
                    if (th != Th.Stars && th != Th.Train && v < ground) inside = true;
                    for (int i = 0; i < n && !inside; i++)
                    {
                        float cx = S[i * 4], hw = S[i * 4 + 1], ht = S[i * 4 + 2], p = S[i * 4 + 3];
                        float dx = u - cx; dx -= Mathf.Round(dx); float ax = Mathf.Abs(dx);
                        switch (th)
                        {
                            case Th.Stars: // 浮空小岛：平顶，下面收成尖
                                {
                                    float cyI = .35f + p * .45f, rw = hw * 1.6f;
                                    if (ax < rw && v <= cyI + .03f && v >= cyI - (1 - ax / rw) * .25f) inside = true;
                                    break;
                                }
                            case Th.Dusk: // 远处城堡塔楼：方塔 + 尖顶
                                { float tw = hw * .7f, th2 = ht * .75f; if (ax < tw && v < th2) inside = true; else if (v >= th2 && v < th2 + .18f && ax < tw * (1 - (v - th2) / .18f) * 1.2f) inside = true; win = windows && v < th2 - .05f && Win(x, y, 3); break; }
                            case Th.Sand: // 金字塔
                                { float base0 = hw * 4.5f; if (v < ht * .8f * (1 - ax / base0)) inside = true; break; }
                            case Th.Snow: // 冰锥/冰峰：又高又尖
                                { if (v < ht * (1 - ax / (hw * 1.2f))) inside = true; break; }
                            case Th.Storm: // 松树：三层三角
                                {
                                    float tw = hw * 1.3f, th2 = ht * .8f;
                                    if (ax < tw * .12f && v < th2 * .3f) inside = true;
                                    for (int L = 0; L < 3 && !inside; L++) { float b = th2 * (.22f + L * .24f), top = b + th2 * .38f; if (v >= b && v < top && ax < tw * (1 - L * .22f) * (1 - (v - b) / (top - b))) inside = true; }
                                    break;
                                }
                            case Th.Factory:
                            case Th.Forge: // 厂房 + 烟囱
                                {
                                    if (p < .5f) { if (ax < hw * 1.4f && v < ht * .45f) { inside = true; win = windows && v < ht * .4f && Win(x, y, 4); } }
                                    else if (ax < hw * .25f && v < ht * .95f) inside = true;
                                    break;
                                }
                            case Th.Train: // 云海：一排鼓起的云团
                                { float rr = hw * 2.2f, cyI = .15f + p * .12f; float dy = v - cyI; if (ax * ax + dy * dy * .6f < rr * rr || v < .12f + .04f * Mathf.Sin(u * 6.283f * 5)) inside = true; break; }
                            case Th.Forest: // 巨型蘑菇：柄 + 半椭圆伞盖
                                {
                                    float stemW = hw * .3f, capY = ht * .7f, capW = hw * 1.8f;
                                    if (ax < stemW && v < capY) inside = true;
                                    float dy = (v - capY) / (ht * .28f); if (dy >= 0 && (ax / capW) * (ax / capW) + dy * dy < 1) { inside = true; win = windows && dy < .7f && Spot(x, y); }
                                    break;
                                }
                            case Th.Space: // 天线塔 + 碟形
                                {
                                    if (ax < hw * .12f && v < ht) inside = true;
                                    else if (p > .5f && v <= ht + .06f && (ax / (hw * .7f)) * (ax / (hw * .7f)) + ((v - ht - .06f) / .06f) * ((v - ht - .06f) / .06f) < 1) inside = true; // 碟形天线（碗口朝上）
                                    else if (ax < hw * 1.2f && v < ht * .3f) { inside = true; win = windows && Win(x, y, 4); }
                                    break;
                                }
                            case Th.City: // 高楼天际线
                                { if (ax < hw * .9f && v < ht * .85f) { inside = true; win = windows && Win(x, y, 3); } break; }
                        }
                    }
                    px[y * w + x] = inside ? (win ? l32 : c32) : new Color32(0, 0, 0, 0);
                }
            }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 1);
        }

        // 亮窗：6×8 像素一格，隔行隔列，再按哈希挑一部分亮
        static bool Win(int x, int y, uint k) { int cx = x / 6, cy = y / 8; return cx % 2 == 0 && cy % 2 == 1 && Art.Hash((uint)cx, (uint)cy) % k == 0; }
        static bool Spot(int x, int y) { int cx = x / 7, cy = y / 7; return cx % 3 == 0 && cy % 3 == 0 && Art.Hash((uint)cx, (uint)cy + 99) % 2 == 0; }

        // ================= 选人界面 / 图鉴：会动的角色 =================
        sealed class IconState { public CharId C; public float ChangedAt = -9; }
        static readonly Dictionary<int, IconState> icons = new Dictionary<int, IconState>();
        /// <summary>
        /// 代替 GameMain.DrawCharIcon 的身体：呼吸 + 每隔几秒一个小动作；悬停或 selected 时有光晕、旋转光环和小星星；换角色时弹一下。
        /// animate=false 时和原来一样画静态图（局内 HUD 用）。
        /// </summary>
        public static void CharIcon(Rect r, CharId c, bool selected, bool animate = true)
        {
            var s = Art.Body[(int)c]; if (s == null) return;
            float T = Time.unscaledTime;
            var col = c == CharId.DVD ? Color.HSVToRGB(Mathf.Repeat(T * .2f, 1), .7f, 1) : Color.white;
            float a = s.rect.width / s.rect.height;
            var rr = a > 1 ? new Rect(r.x, r.y + r.height * (1 - 1 / a) / 2, r.width, r.height / a) : new Rect(r.x + r.width * (1 - a) / 2, r.y, r.width * a, r.height);
            if (!animate || !Enabled) { Ui.Sprite(rr, s, col); return; }
            if (Event.current != null && Event.current.type != EventType.Repaint) return; // 只在绘制事件里画，省事也不抢按钮
            int key = Mathf.RoundToInt(r.x) * 73856093 ^ Mathf.RoundToInt(r.y) * 19349663;
            IconState st; if (!icons.TryGetValue(key, out st)) { st = new IconState { C = c }; icons[key] = st; }
            if (st.C != c) { st.C = c; st.ChangedAt = T; }
            bool hover = Event.current != null && r.Contains(Event.current.mousePosition), hi = selected || hover;
            var cc = Art.C(Char(c).Color);
            float ph = (int)c * 1.37f + (key & 255) * .02f;
            // 呼吸（约 0.35Hz）
            float br = Mathf.Sin(T * 2.2f + ph), sx = 1 - br * .025f, sy = 1 + br * .035f, rot = 0, ox = 0, hop = 0;
            // 每 3.2 秒一个小动作，按角色不同
            float cyc = Mathf.Repeat(T * (hi ? 1.25f : 1) + ph, 3.2f), k = cyc < .5f ? Mathf.Sin(cyc / .5f * Mathf.PI) : 0;
            switch (c)
            {
                case CharId.XP: case CharId.XP2: sx *= 1 + .08f * k; sy *= 1 - .06f * k; break;                // 胖子：肚子一鼓
                case CharId.BW: case CharId.BW2: rot = Mathf.Sin(T * 1.2f + ph) * 6; break;                     // 黑洞/白洞：慢慢摇摆
                case CharId.CCLD: case CharId.CCLD2: ox = k * .05f; rot = -k * 6; break;                        // 剑客：前倾拔剑
                case CharId.GARY: case CharId.GARY2: hop = k * .5f; rot = -k * 10; break;                       // 刺客：小跳一下
                case CharId.KAI: case CharId.KAI2: ox = Mathf.Sin(cyc / .5f * Mathf.PI * 2) * k * .04f; break;  // 拳手：出两下拳
                case CharId.DZY: case CharId.DZY2: sx *= 1 + .05f * Mathf.Sin(T * 5 + ph) * k; sy *= 1 - .05f * Mathf.Sin(T * 5 + ph) * k; break; // 触手：蠕动
                case CharId.PX: ox = -k * .03f; rot = k * 5; break;                                             // 霰弹：后坐
                default: hop = k * .35f; break;                                                                 // DVD 等：弹一下
            }
            if (hi) { sx *= 1.06f; sy *= 1.06f; }
            float pop = Mathf.Clamp01(1 - (T - st.ChangedAt) / .35f); if (pop > 0) { float pk = Mathf.Sin(pop * Mathf.PI) * .22f; sx *= 1 + pk; sy *= 1 + pk; }
            float footX = rr.center.x + ox * r.width, footY = rr.yMax - hop * r.height * .12f;
            // 光晕 + 光环 + 星星（全部 ≤0.5 透明度，缓慢变化）
            if (hi || pop > 0)
            {
                float g = (hi ? 1 : pop) * (.8f + .2f * Mathf.Sin(T * 2.4f));
                var gc = cc; gc.a = G(.38f * g); Ui.Sprite(Grow(r, 1.55f), Art.Soft, gc);
                var ring = Grow(r, 1.15f + .04f * Mathf.Sin(T * 2)); var rc = Color.Lerp(cc, Color.white, .4f); rc.a = G(.4f * g);
                var keep = GUI.matrix; Rotate(T * 40, ring.center); Ui.Sprite(ring, Art.RingThin, rc); GUI.matrix = keep;
                for (int i = 0; i < 3; i++)
                {
                    float ang = T * 1.3f + i * 2.094f, rad = r.width * .58f; var sc = new Color(1, .97f, .85f, G(.45f * g * (.6f + .4f * Mathf.Sin(T * 1.8f + i))));
                    float ss = r.width * .12f; var sr = new Rect(r.center.x + Mathf.Cos(ang) * rad - ss / 2, r.center.y + Mathf.Sin(ang) * rad * .8f - ss / 2, ss, ss);
                    Ui.Sprite(sr, Art.Star4, sc);
                }
            }
            // 脚下影子（跳起来时变小变淡）
            float shW = rr.width * (.8f - hop * .3f); Ui.Sprite(new Rect(footX - shW / 2, rr.yMax - rr.height * .06f, shW, rr.height * .12f), Art.Soft, new Color(0, 0, 0, .35f * (1 - hop * .5f)));
            // 身体：以脚底为中心缩放
            float bw = rr.width * sx, bh = rr.height * sy; var br2 = new Rect(footX - bw / 2, footY - bh, bw, bh);
            var m0 = GUI.matrix; if (rot != 0) Rotate(rot, new Vector2(footX, footY - bh * .3f));
            Ui.Sprite(br2, s, col);
            GUI.matrix = m0;
        }
        // GUIUtility.RotateAroundPivot 在 GUI.matrix 带缩放时支点会偏，这里自己乘矩阵（支点用设计坐标）
        static void Rotate(float deg, Vector2 pivot) { GUI.matrix = GUI.matrix * Matrix4x4.TRS(pivot, Quaternion.Euler(0, 0, deg), Vector3.one) * Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one); }
        static Rect Grow(Rect r, float k) { float w = r.width * k, h = r.height * k; return new Rect(r.center.x - w / 2, r.center.y - h / 2, w, h); }
    }
}
