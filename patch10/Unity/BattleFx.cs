// 第十版通用战斗特效加强（所有角色）：重击星芒 + 方向刀风、击倒时的灵魂光柱与放射光、大爆炸的冲击环和烟、电弧两端的光点。
// 纯画面层，只读 Sim。由 GodView 统一调度（Attach / Tick / Draw），不需要额外挂钩。
using System.Collections.Generic;
using UnityEngine;
using Brawl.Core;

namespace Brawl.View
{
    public static class BattleFx
    {
        public static bool Enabled = true;
        const float GLOW = .5f; static float G(float a) => Mathf.Clamp(a, 0, GLOW);
        static Color A(Color c, float a) { c.a = a; return c; }
        static Sim sim; static readonly System.Random rng = new System.Random(4);
        static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        struct E { public int K; public float X, Y, Dir, Life, Max, Size, Rot; public Color C; }
        // K: 0 重击星芒 1 方向刀风 2 击倒光柱 3 击倒放射光 4 冲击环 5 烟团 6 电弧端点
        static readonly List<E> es = new List<E>();
        static readonly bool[] alive = new bool[16]; static readonly bool[] seen = new bool[16];
        static float slowT;
        static float camTop; static float camHalf = 540;

        public static void Attach(Sim s)
        {
            if (sim != null) sim.OnFx -= OnFx;
            sim = s; if (s != null) { s.OnFx -= OnFx; s.OnFx += OnFx; }
            es.Clear(); for (int i = 0; i < 16; i++) seen[i] = false; slowT = 0;
        }
        static void Add(E e) { if (es.Count < 160) es.Add(e); }

        static void OnFx(Fx f)
        {
            if (!Enabled || sim == null) return;
            switch (f.Type)
            {
                case FxType.Hit:
                    {
                        float dmg = f.B, dir = f.A == 0 ? 1 : Mathf.Sign(f.A);
                        if (dmg >= 9) Add(new E { K = 0, X = f.X, Y = f.Y, Life = .22f, Max = .22f, Size = 70 + dmg * 4, C = new Color(1, .95f, .8f) });
                        if (dmg >= 12) Add(new E { K = 1, X = f.X, Y = f.Y, Dir = dir, Life = .2f, Max = .2f, Size = 90 + dmg * 5, Rot = R(-14, 14), C = new Color(1, .9f, .7f) });
                        if (dmg >= 20) Add(new E { K = 4, X = f.X, Y = f.Y, Life = .35f, Max = .35f, Size = 160 + dmg * 6, C = new Color(1, .85f, .55f) });
                        break;
                    }
                case FxType.Burst:
                    if (f.A >= 40 && f.B >= 500)
                    {
                        var c = Art.C(f.Color);
                        Add(new E { K = 4, X = f.X, Y = f.Y, Life = .45f, Max = .45f, Size = f.B * .7f, C = Color.Lerp(c, Color.white, .3f) });
                        Add(new E { K = 0, X = f.X, Y = f.Y, Life = .3f, Max = .3f, Size = f.B * .35f, C = Color.Lerp(c, Color.white, .5f) });
                        for (int i = 0; i < 6; i++) { float a = i * 1.047f + R(-.3f, .3f); Add(new E { K = 5, X = f.X + Mathf.Cos(a) * 20, Y = f.Y + Mathf.Sin(a) * 20, Dir = a, Life = R(.6f, .9f), Max = .9f, Size = R(40, 70), C = new Color(.2f, .18f, .25f, .45f) }); }
                    }
                    break;
                case FxType.Zap:
                    if (f.C >= 2) Add(new E { K = 6, X = f.A, Y = f.B, Life = .25f, Max = .25f, Size = 50 + f.C * 12, C = Art.C(f.Color) });
                    break;
            }
        }

        /// <summary>每帧（真实 dt）。返回画面时间倍率（击倒瞬间短暂慢一下）。</summary>
        public static float Tick(float dt, Sim S)
        {
            if (!Enabled || S == null) return 1;
            if (S != sim) Attach(S);
            foreach (var p in S.Players)
            {
                int i = p.Slot & 15;
                if (!seen[i]) { seen[i] = true; alive[i] = p.Alive; continue; }
                if (alive[i] && !p.Alive && S.State == GameState.Play) KO(p);
                alive[i] = p.Alive;
            }
            float scale = 1; if (slowT > 0) { slowT -= dt; scale = .35f; }
            float vdt = dt * scale;
            for (int i = es.Count - 1; i >= 0; i--) { var e = es[i]; e.Life -= vdt; if (e.Life <= 0) es.RemoveAt(i); else { if (e.K == 5) { e.X += Mathf.Cos(e.Dir) * 60 * vdt; e.Y += Mathf.Sin(e.Dir) * 60 * vdt - 20 * vdt; } es[i] = e; } }
            return scale;
        }
        static void KO(Player p)
        {
            var c = Art.C(p.C.Color); float x = p.Cx, y = Mathf.Min(p.Cy, sim.H - 20);
            Add(new E { K = 2, X = x, Y = y, Life = 1.1f, Max = 1.1f, Size = 120, C = Color.Lerp(c, Color.white, .35f) });
            Add(new E { K = 3, X = x, Y = y, Life = .8f, Max = .8f, Size = 700, C = Color.Lerp(c, Color.white, .4f) });
            Add(new E { K = 4, X = x, Y = y, Life = .5f, Max = .5f, Size = 420, C = Color.white });
            Add(new E { K = 0, X = x, Y = y, Life = .4f, Max = .4f, Size = 300, C = Color.white });
            slowT = .16f;
        }

        public static void Draw(SpritePool sp, Vector2 camPos, float camSize, float t)
        {
            if (!Enabled) return;
            camTop = camPos.y - camSize - 100;
            foreach (var e in es)
            {
                float u = 1 - e.Life / e.Max, k = 1 - u;
                switch (e.K)
                {
                    case 0: sp.Put(Res10.Flare, e.X, e.Y, e.Size * (1 + .5f * u), e.Size * .7f * (1 + .5f * u), A(e.C, G(.5f * k)), 31, 0, true); break;
                    case 1:
                        {
                            float ang = (e.Dir > 0 ? 0 : 180) + e.Rot, len = e.Size * (.6f + .6f * u);
                            sp.Put(Res10.Slash, e.X + e.Dir * len * .35f, e.Y, len, 18 * k + 4, A(e.C, G(.5f * k)), 31, ang, true);
                            sp.Put(Res10.Slash, e.X + e.Dir * len * .35f, e.Y, len * .9f, 6 * k + 2, A(Color.white, .9f * k), 32, ang, true);
                            break;
                        }
                    case 2:
                        {
                            float len = Mathf.Max(120, e.Y - camTop), w = e.Size * (1 - .7f * u);
                            sp.Put(Res10.Pillar, e.X, e.Y - len / 2, w, len, A(e.C, G(.5f * k)), 3, 0, true);
                            sp.Put(Res10.Pillar, e.X, e.Y - len / 2, w * .3f, len, A(Color.white, G(.5f * k)), 3, 0, true);
                            break;
                        }
                    case 3: sp.Put(Res10.Rays, e.X, e.Y, e.Size * (.6f + .5f * u), e.Size * (.6f + .5f * u), A(e.C, G(.42f * k)), 2, t * 20, true); break;
                    case 4:
                        {
                            float s = e.Size * (1 - k * k * k);
                            sp.Put(Art.Ring, e.X, e.Y, s, s, A(e.C, G(.5f * k)), 25, 0, true);
                            sp.Put(Art.Soft, e.X, e.Y, s * 1.1f, s * 1.1f, A(e.C, G(.14f * k)), 24, 0, true);
                            break;
                        }
                    case 5: sp.Put(Art.Soft, e.X, e.Y, e.Size * (1 + u), e.Size * (1 + u), A(e.C, e.C.a * k), 21, 0, false); break;
                    case 6: sp.Put(Res10.Flare, e.X, e.Y, e.Size, e.Size, A(Color.Lerp(e.C, Color.white, .5f), G(.5f * k)), 30, 0, true); break;
                }
            }
        }
    }
}
