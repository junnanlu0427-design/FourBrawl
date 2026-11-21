// 把 Sim 的状态画出来：背景视差、地形贴图、角色、子弹、黑洞、岩浆、粒子、镜头震动/拉近。
// 用“即时模式”精灵池：每帧从池里取 SpriteRenderer 摆好，没用到的隐藏。
using System;
using System.Collections.Generic;
using UnityEngine;
using Brawl.Core;
using static Brawl.Core.Defs;

namespace Brawl.View
{
    public sealed class SpritePool
    {
        readonly List<SpriteRenderer> pool = new List<SpriteRenderer>(); int used; readonly Transform root;
        public SpritePool(Transform root) { this.root = root; }
        public void Begin() { used = 0; }
        public void End() { for (int i = used; i < pool.Count; i++) if (pool[i].enabled) pool[i].enabled = false; }
        /// <summary>x,y 为游戏坐标（y 向下）。w,h 为世界尺寸（像素）。</summary>
        public SpriteRenderer Put(Sprite s, float x, float y, float w, float h, Color c, int order, float rotDeg = 0, bool add = false)
        {
            SpriteRenderer r;
            if (used < pool.Count) r = pool[used];
            else { var go = new GameObject("s"); go.transform.SetParent(root, false); r = go.AddComponent<SpriteRenderer>(); pool.Add(r); }
            used++;
            if (!r.enabled) r.enabled = true;
            if (r.sprite != s) r.sprite = s;
            var mat = add ? Art.Add : Art.Alpha; if (r.sharedMaterial != mat) r.sharedMaterial = mat;
            r.color = c; r.sortingOrder = order;
            var b = s.bounds.size; var t = r.transform;
            t.localPosition = new Vector3(x, -y, 0);
            t.localRotation = Quaternion.Euler(0, 0, rotDeg);
            t.localScale = new Vector3(b.x > 0 ? w / b.x : 1, b.y > 0 ? h / b.y : 1, 1);
            return r;
        }
    }

    struct Particle { public float X, Y, Vx, Vy, Life, Max, Size, Grav, Drag; public Color C; public bool Line, Add, Glow; }
    struct ZapFx { public Vector2[] Pts; public float Life, Max, W; public Color C; public bool Flesh; }
    struct RingFx { public float X, Y, R0, R1, Life, Max, W; public Color C; }
    public struct Popup { public float X, Y, Life, Max; public string Text; public Color C; public bool Big; }

    public sealed class WorldView : MonoBehaviour
    {
        Camera clearCam;
        public Camera Cam; public Sim Sim; public SfxPlayer Audio;
        public int LocalSlot = -1;           // 联机时“我”是哪个槽位（显示“你”标记、分身真身提示）
        public bool IsClient;                // 朋友端：时停槽位等来自快照
        public Func<int> TimeStopSlot;       // 朋友端提供
        public readonly List<Popup> Popups = new List<Popup>();
        public float WhiteFlash;

        SpritePool sp; Texture2D terrTex; Color32[] terrBuf; Sprite terrSprite; int drawnVersion = -1, drawnMap = -1;
        Sprite sky, far, near; MapDef map;
        readonly Particle[] parts = new Particle[1400]; int partN;
        readonly List<RingFx> rings = new List<RingFx>();
        readonly List<ZapFx> zaps = new List<ZapFx>();
        readonly List<Vector4> trail = new List<Vector4>(); int curTs = -1; float trailT; // 超频残影
        float shake, zoomT, zoomX, zoomY; Vector2 camPos = new Vector2(Defs.W / 2f, Defs.H / 2f); float camSize = Defs.H / 2f;
        int W => Sim != null ? Sim.W : Defs.W; int H => Sim != null ? Sim.H : Defs.H; // 当前地图尺寸（大地图更大）
        int texW, texH;
        public Player LocalFocus; // 大地图装不下所有人时镜头跟谁（联机：自己；本地：P1）
        public Rect ViewRect => new Rect(camPos.x - camSize * 16f / 9, camPos.y - camSize, camSize * 32f / 9, camSize * 2);
        readonly Dictionary<int, float> prevVy = new Dictionary<int, float>(), squash = new Dictionary<int, float>(), shownHp = new Dictionary<int, float>();
        readonly Vector2[] stars = new Vector2[90];
        public readonly Dictionary<int, float> HpGhost = new Dictionary<int, float>();
        float t;
        // 角色“透视层”：特效画完后再把每个人的身体淡淡地叠一遍，人永远不会被特效盖住
        struct Overlay { public Sprite S; public float X, Y, W, H, Rot; public bool Flip; public Color C; }
        readonly List<Overlay> overlays = new List<Overlay>();
        readonly Dictionary<int, float> flipT = new Dictionary<int, float>(); readonly Dictionary<int, int> prevJumps = new Dictionary<int, int>();
        Color PlayerColor(Player p) => Sim.Mode == GameMode.Team ? Art.C(TeamColors[p.Slot % 2]) : Art.C(SlotColors[p.Slot]);

        public void Init(Sim sim)
        {
            Sim = sim; sp = new SpritePool(transform);
            EnsureTerrTex();
            var r = new System.Random(3); for (int i = 0; i < stars.Length; i++) stars[i] = new Vector2((float)r.NextDouble(), (float)r.NextDouble()); // 按比例存，换大地图也铺满
            Attach(sim);
        }
        void EnsureTerrTex()
        {
            if (terrTex != null && texW == W && texH == H) return;
            if (terrTex != null) { Destroy(terrSprite); Destroy(terrTex); }
            texW = W; texH = H;
            terrTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            terrBuf = new Color32[W * H];
            terrSprite = Sprite.Create(terrTex, new Rect(0, 0, W, H), new Vector2(.5f, .5f), 1, 0, SpriteMeshType.FullRect);
        }
        public void Attach(Sim sim)
        {
            Sim = sim; sim.OnFx -= OnFx; sim.OnFx += OnFx; drawnVersion = -1; drawnMap = -1; ViewPolish.Attach(sim);
            partN = 0; rings.Clear(); zaps.Clear(); Popups.Clear();
        }

        // ================= 特效事件 =================
        void OnFx(Fx f)
        {
            switch (f.Type)
            {
                case FxType.Burst:
                    for (int i = 0; i < Mathf.Min(6, (int)f.A / 5); i++)
                    {
                        float a2 = UnityEngine.Random.value * 6.283f, s2 = UnityEngine.Random.Range(.4f, 1f) * f.B * 1.2f;
                        AddPart(new Particle { X = f.X, Y = f.Y, Vx = Mathf.Cos(a2) * s2, Vy = Mathf.Sin(a2) * s2, Life = UnityEngine.Random.Range(.25f, .5f), Max = .5f, C = Art.C(f.Color), Size = f.C * 1.6f, Glow = true, Add = true, Drag = .9f });
                    }
                    for (int i = 0; i < (int)(f.A * 1.4f); i++)
                    {
                        float a = UnityEngine.Random.value * 6.283f, s = UnityEngine.Random.Range(.3f, 1f) * f.B;
                        AddPart(new Particle { X = f.X, Y = f.Y, Vx = Mathf.Cos(a) * s, Vy = Mathf.Sin(a) * s - 60, Life = UnityEngine.Random.Range(.3f, .7f), Max = .7f, C = Art.C(f.Color), Size = UnityEngine.Random.Range(f.C * .5f, f.C), Grav = 600, Drag = .93f });
                    }
                    break;
                case FxType.Text: Popups.Add(new Popup { X = f.X, Y = f.Y, Text = f.Text, C = Art.C(f.Color), Big = f.Big, Life = f.Big ? 1.1f : .8f, Max = f.Big ? 1.1f : .8f }); break;
                case FxType.Zap:
                    {
                        float dx = f.A - f.X, dy = f.B - f.Y, len = Mathf.Sqrt(dx * dx + dy * dy); int n = Mathf.Clamp((int)(len / 22), 3, 24);
                        float nx = len > 0 ? -dy / len : 0, ny = len > 0 ? dx / len : 1; var pts = new Vector2[n + 1];
                        float side = UnityEngine.Random.value < .5f ? -1 : 1, amp = Mathf.Min(28, len * .18f);
                        for (int i = 0; i <= n; i++)
                        {
                            float k = i / (float)n, j;
                            if (f.Big) j = side * amp * (Mathf.Sin(k * Mathf.PI) + .35f * Mathf.Sin(k * Mathf.PI * 3)); // 触手：平滑的 S 形
                            else j = (i == 0 || i == n) ? 0 : UnityEngine.Random.Range(-1f, 1f) * Mathf.Min(16, len * .08f) * (1 + f.C * .3f);
                            pts[i] = new Vector2(f.X + dx * k + nx * j, f.Y + dy * k + ny * j);
                        }
                        zaps.Add(new ZapFx { Pts = pts, Life = f.Big ? .26f : .22f, Max = f.Big ? .26f : .22f, W = f.Big ? 3 + f.C * 1.6f : 2 + f.C * 1.5f, C = Art.C(f.Color), Flesh = f.Big });
                        AddPart(new Particle { X = f.A, Y = f.B, Life = .16f, Max = .16f, Size = 30 + f.C * 14, C = Art.C(f.Color), Glow = true, Add = true });
                        break;
                    }
                case FxType.Ring: rings.Add(new RingFx { X = f.X, Y = f.Y, R0 = f.A, R1 = f.B, Life = f.C, Max = f.C, C = Art.C(f.Color), W = f.Big ? 10 : 4 }); break;
                case FxType.Shake: shake = Mathf.Max(shake, f.A); break;
                case FxType.Flash:
                    WhiteFlash = Mathf.Max(WhiteFlash, f.A);
                    // 用冲击波代替强闪光：从画面中心向外扩散的彩色光环
                    rings.Add(new RingFx { X = camPos.x, Y = camPos.y, R0 = 40, R1 = 900, Life = .6f, Max = .6f, C = new Color(1, .85f, .5f, .35f), W = 10 });
                    break;
                case FxType.Sound: Audio?.Play((Sfx)(int)f.A, f.B); break;
                case FxType.Zoom: zoomT = .9f; zoomX = f.X; zoomY = f.Y; break;
                case FxType.Hit:
                    {
                        int n = Mathf.Min(12, 4 + (int)(f.B / 4)); float dir = f.A == 0 ? 1 : f.A;
                        for (int i = 0; i < n; i++)
                        {
                            float a = Mathf.Atan2(-.25f, dir) + UnityEngine.Random.Range(-.8f, .8f), s = UnityEngine.Random.Range(380, 820) * (1 + f.B / 40);
                            AddPart(new Particle { X = f.X, Y = f.Y, Vx = Mathf.Cos(a) * s, Vy = Mathf.Sin(a) * s, Life = UnityEngine.Random.Range(.12f, .26f), Max = .26f, C = i % 3 != 0 ? Color.white : new Color(1, .95f, .66f), Size = UnityEngine.Random.Range(2, 4.5f), Line = true, Add = true, Drag = .9f });
                        }
                        rings.Add(new RingFx { X = f.X, Y = f.Y, R0 = 4, R1 = 18 + f.B * 1.4f, Life = .12f, Max = .12f, C = Color.white, W = 3 });
                        if (f.B >= 14) rings.Add(new RingFx { X = f.X, Y = f.Y, R0 = 10, R1 = 60 + f.B * 3, Life = .3f, Max = .3f, C = new Color(1, .8f, .45f, .7f), W = 10 });
                        if (f.B >= 14) AddPart(new Particle { X = f.X, Y = f.Y, Life = .14f, Max = .14f, Size = 44 + f.B * 2, C = Color.white, Glow = true, Add = true });
                        AddPart(new Particle { X = f.X, Y = f.Y, Life = .1f, Max = .1f, Size = 22 + f.B * 1.2f, C = new Color(1, .96f, .8f, .9f), Glow = true, Add = true }); // 命中星芒
                        for (int i = 0; i < 3 + (int)(f.B / 8); i++) // 小碎屑
                        { float a = UnityEngine.Random.value * 6.283f, s = UnityEngine.Random.Range(120, 320); AddPart(new Particle { X = f.X, Y = f.Y, Vx = Mathf.Cos(a) * s + dir * 120, Vy = Mathf.Sin(a) * s - 160, Life = UnityEngine.Random.Range(.3f, .55f), Max = .55f, C = new Color(1, .85f, .5f, .9f), Size = UnityEngine.Random.Range(2, 4), Grav = 1400, Drag = .97f }); }
                        break;
                    }
                case FxType.Raise:
                    for (int i = 0; i < 16; i++)
                        AddPart(new Particle { X = (f.X + UnityEngine.Random.value * f.A) * T, Y = (f.Y + UnityEngine.Random.value * f.B) * T, Vx = UnityEngine.Random.Range(-60, 60), Vy = UnityEngine.Random.Range(-160, -40), Life = UnityEngine.Random.Range(.5f, 1f), Max = 1, Size = UnityEngine.Random.Range(4, 8), C = new Color(1, .93f, .6f, .9f), Glow = true, Add = true, Drag = .95f });
                    break;
                case FxType.Dig:
                    for (int i = 0; i < Mathf.Min(14, (int)(f.A / 3)); i++)
                        AddPart(new Particle { X = f.X + UnityEngine.Random.Range(-f.A, f.A) * .6f, Y = f.Y + UnityEngine.Random.Range(-f.A, f.A) * .6f, Vx = UnityEngine.Random.Range(-220, 220), Vy = UnityEngine.Random.Range(-420, -80), Life = UnityEngine.Random.Range(.5f, 1f), Max = 1, Size = UnityEngine.Random.Range(4, 8), C = map != null ? Art.C(map.Dirt) : Color.gray, Grav = 1500, Drag = .99f });
                    break;
            }
        }
        void AddPart(Particle p) { if (partN < parts.Length) parts[partN++] = p; else parts[UnityEngine.Random.Range(0, parts.Length)] = p; }

        // ================= 每帧绘制 =================
        void LateUpdate()
        {
            if (Sim == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, .05f); dt = ViewPolish.Tick(dt, Sim); t += dt;
            StepVisuals(dt);
            UpdateCamera(dt);
            sp.Begin();
            DrawBackground();
            DrawTerrain();
            DrawWorld();
            sp.End();
        }

        void StepVisuals(float dt)
        {
            for (int i = 0; i < partN; i++)
            {
                ref var q = ref parts[i];
                q.X += q.Vx * dt; q.Y += q.Vy * dt; float d = Mathf.Pow(q.Drag == 0 ? .93f : q.Drag, dt * 60); q.Vx *= d; q.Vy = q.Vy * (q.Grav > 0 ? 1 : d) + q.Grav * dt; q.Life -= dt;
                if (q.Life <= 0) { parts[i] = parts[--partN]; i--; }
            }
            for (int i = zaps.Count - 1; i >= 0; i--) { var z = zaps[i]; z.Life -= dt; if (z.Life <= 0) zaps.RemoveAt(i); else zaps[i] = z; }
            for (int i = rings.Count - 1; i >= 0; i--) { var r = rings[i]; r.Life -= dt; if (r.Life <= 0) rings.RemoveAt(i); else rings[i] = r; }
            for (int i = Popups.Count - 1; i >= 0; i--) { var p = Popups[i]; p.Life -= dt; p.Y -= 40 * dt; if (p.Life <= 0) Popups.RemoveAt(i); else Popups[i] = p; }
            shake *= Mathf.Exp(-10 * dt); WhiteFlash = Mathf.Max(0, WhiteFlash - dt * 1.6f); if (zoomT > 0) zoomT -= dt;
            // 落地挤压、拖尾、神化粒子
            foreach (var p in Sim.Players)
            {
                if (!p.Alive) continue;
                prevVy.TryGetValue(p.Slot, out float pv);
                if (pv > 380 && p.OnGround)
                {
                    squash[p.Slot] = Mathf.Min(.32f, pv / 2600);
                    for (int i = 0; i < 6; i++) AddPart(new Particle { X = p.Cx + UnityEngine.Random.Range(-p.W / 2, p.W / 2), Y = p.Y + p.H, Vx = (i % 2 == 1 ? 1 : -1) * UnityEngine.Random.Range(60, 180), Vy = UnityEngine.Random.Range(-60, -10), Life = .35f, Max = .35f, C = new Color(.56f, .59f, .76f, .8f), Size = UnityEngine.Random.Range(3, 6), Drag = .9f });
                }
                prevVy[p.Slot] = p.OnGround ? 0 : p.Vy;
                prevJumps.TryGetValue(p.Slot, out int pj); if (p.Jumps >= 2 && pj < 2 && !p.OnGround && !p.God) flipT[p.Slot] = .38f; prevJumps[p.Slot] = p.Jumps; // 二段跳空翻
                if (flipT.TryGetValue(p.Slot, out float ft) && ft > 0) flipT[p.Slot] = p.OnGround ? 0 : ft - dt;
                if (p.OnGround && Mathf.Abs(p.Vx) > 180 && UnityEngine.Random.value < dt * 14) // 跑动扬尘
                    AddPart(new Particle { X = p.Cx - Mathf.Sign(p.Vx) * p.W * .4f, Y = p.Y + p.H - 2, Vx = -Mathf.Sign(p.Vx) * UnityEngine.Random.Range(30, 90), Vy = UnityEngine.Random.Range(-50, -15), Life = .4f, Max = .4f, C = new Color(.75f, .78f, .9f, .45f), Size = UnityEngine.Random.Range(3, 6), Drag = .9f });
                squash.TryGetValue(p.Slot, out float sq); squash[p.Slot] = sq * Mathf.Exp(-14 * dt);
                if ((Mathf.Abs(p.Vx) > 900 || Mathf.Abs(p.Vy) > 1100) && !p.God && UnityEngine.Random.value < .7f)
                    AddPart(new Particle { X = p.Cx + UnityEngine.Random.Range(-6, 6), Y = p.Cy + UnityEngine.Random.Range(-6, 6), Vy = -20, Life = .4f, Max = .4f, C = new Color(.86f, .88f, 1, .6f), Size = UnityEngine.Random.Range(5, 9) });
                if (p.God)
                    for (int i = 0; i < 2; i++)
                        AddPart(new Particle { X = p.Cx + UnityEngine.Random.Range(-14, 14), Y = p.Cy + UnityEngine.Random.Range(-10, 14), Vx = UnityEngine.Random.Range(-40, 40) - p.Vx * .2f, Vy = UnityEngine.Random.Range(20, 90), Life = UnityEngine.Random.Range(.4f, .9f), Max = .9f, C = Art.C(new uint[] { 0xffe27a, 0xffc531, 0xffffff, 0xff9a3c }[i % 4]), Size = UnityEngine.Random.Range(3, 7), Add = true });
                if (p.BleedT > 0 && UnityEngine.Random.value < .15f) AddPart(new Particle { X = p.Cx + UnityEngine.Random.Range(-8, 8), Y = p.Cy, Vy = 60, Life = .5f, Max = .5f, C = Art.C(0xd01c3c), Size = 3, Grav = 600 });
                // 血条残影
                if (!HpGhost.TryGetValue(p.Slot, out float g) || g < p.Hp) g = p.Hp; else g = Mathf.MoveTowards(g, p.Hp, dt * p.MaxHp * .5f);
                HpGhost[p.Slot] = g;
            }
            foreach (var h in Sim.Holes)
                if (h.Active && UnityEngine.Random.value < .6f)
                {
                    float a = UnityEngine.Random.value * 6.283f;
                    AddPart(new Particle { X = h.X + Mathf.Cos(a) * h.R, Y = h.Y + Mathf.Sin(a) * h.R, Vx = -Mathf.Cos(a) * h.R * 1.6f, Vy = -Mathf.Sin(a) * h.R * 1.6f, Life = .6f, Max = .6f, C = Art.C(0xc77dff), Size = 3, Add = true, Drag = 1 });
                }
        }

        void UpdateCamera(float dt)
        {
            // 镜头：轻微跟随所有存活角色，击杀时拉近；比例固定 16:9 并加黑边
            float minX = 1e9f, maxX = -1e9f, minY = 1e9f, maxY = -1e9f; int n = 0;
            LocalFocus = null; Player onlyHuman = null; int humans = 0;
            foreach (var p in Sim.Players) { if (p.Slot == LocalSlot) LocalFocus = p; if (p.Kind == SlotMode.Human) { humans++; onlyHuman = p; } }
            if (LocalFocus == null && humans == 1) LocalFocus = onlyHuman;
            NewCharView.LocalSlot = LocalFocus != null ? LocalFocus.Slot : -1; // gary-改 的隐形地钉只让主人看见
            foreach (var p in Sim.Players) if (p.Alive && p.Y < H + 40) { minX = Mathf.Min(minX, p.Cx); maxX = Mathf.Max(maxX, p.Cx); minY = Mathf.Min(minY, p.Cy); maxY = Mathf.Max(maxY, p.Cy); n++; }
            // 大地图：镜头最多拉远到 1.4 倍标准视野，再远的人用屏幕边缘箭头 + 小地图指示
            float maxSize = Mathf.Min(H / 2f, W / 2f * 9 / 16, Defs.H / 2f * 1.4f), minSize = Defs.H / 2f * .5f;
            Vector2 target = new Vector2(W / 2f, H / 2f); float size = maxSize;
            if (n > 0 && Sim.State != GameState.Menu)
            {
                float spanX = maxX - minX + 560, spanY = maxY - minY + 380;
                size = Mathf.Clamp(Mathf.Max(spanY / 2, spanX / 2 / (16f / 9)), minSize, maxSize);
                if (maxSize < Mathf.Max(spanY / 2, spanX / 2 / (16f / 9)) && LocalFocus != null && LocalFocus.Alive)
                {   // 装不下所有人：镜头偏向本机玩家（联机时就是自己），别人靠箭头找
                    target = Vector2.Lerp(new Vector2((minX + maxX) / 2, (minY + maxY) / 2), new Vector2(LocalFocus.Cx, LocalFocus.Cy), .65f);
                }
                else // 地图变大：人靠得近时镜头拉近，人物显得更大更清楚
                target = new Vector2((minX + maxX) / 2, (minY + maxY) / 2);
            }
            if (zoomT > 0) { float k = Mathf.Sin(Mathf.Clamp01(zoomT / .9f) * Mathf.PI) * .5f; target = Vector2.Lerp(target, new Vector2(zoomX, zoomY), k); size = Mathf.Lerp(size, Defs.H / 2f * .55f, k); }
            ViewPolish.Camera(ref target, ref size);
            float halfW = size * 16f / 9;
            target.x = Mathf.Clamp(target.x, halfW, W - halfW); target.y = Mathf.Clamp(target.y, size, H - size);
            float follow = 1 - Mathf.Exp(-4 * dt);
            camPos = Vector2.Lerp(camPos, target, follow); camSize = Mathf.Lerp(camSize, size, follow);
            Vector2 sh = shake > .3f ? new Vector2(UnityEngine.Random.Range(-shake, shake), UnityEngine.Random.Range(-shake, shake)) : Vector2.zero; sh += ViewPolish.CamKick;
            Cam.orthographicSize = camSize;
            Cam.transform.position = new Vector3(camPos.x + sh.x, -(camPos.y + sh.y), -10);
            if (clearCam == null)
            {   // 非 16:9 屏幕：先用一台全屏黑相机清掉左右/上下的黑边，避免残影
                var go = new GameObject("LetterboxClear"); clearCam = go.AddComponent<Camera>();
                clearCam.depth = Cam.depth - 100; clearCam.clearFlags = CameraClearFlags.SolidColor; clearCam.backgroundColor = Color.black;
                clearCam.cullingMask = 0; clearCam.rect = new Rect(0, 0, 1, 1); clearCam.orthographic = true;
            }
            float targetAspect = 16f / 9, a = Screen.width / (float)Mathf.Max(1, Screen.height);
            Cam.rect = a > targetAspect ? new Rect((1 - targetAspect / a) / 2, 0, targetAspect / a, 1) : new Rect(0, (1 - a / targetAspect) / 2, 1, a / targetAspect);
        }

        void DrawBackground()
        {
            if (Sim.CurMap != drawnMap)
            {
                map = Maps.All[Mathf.Clamp(Sim.CurMap, 0, Maps.All.Length - 1)];
                sky = Art.MakeSprite(Art.SkyTexture(map), 1);
                far = Art.MakeSprite(Art.Mountains(Lighten(map.Hill, .25f), Sim.CurMap * 3 + 1, .55f, .35f, 1), 1);
                near = Art.MakeSprite(Art.Mountains(Art.C(map.Hill), Sim.CurMap * 5 + 2, .38f, .3f, 1), 1);
            }
            Vector2 cp = camPos - new Vector2(W / 2f, H / 2f);
            sp.Put(sky, W / 2f, H / 2f, W + 900, H + 600, Color.white, -100);
            // 星星闪烁
            for (int i = 0; i < stars.Length; i++)
            {
                var s = stars[i]; float tw = .4f + .6f * Mathf.Abs(Mathf.Sin(t * (.6f + i % 5 * .3f) + i));
                float sz = 1.5f + i % 3;
                sp.Put(Art.Dot, s.x * (W + 400) - 200 + cp.x * .9f, s.y * H * .64f - 60 + cp.y * .9f, sz, sz, new Color(.75f, .8f, 1, tw * .8f), -99, 0, true);
            }
            // 月亮/星球
            sp.Put(Art.Soft, W * .77f + cp.x * .85f, 130 + cp.y * .85f, 360, 360, new Color(1, .9f, .7f, .18f), -98, 0, true);
            sp.Put(Art.Dot, W * .77f + cp.x * .85f, 130 + cp.y * .85f, 90, 90, Lighten(map.SkyBottom, .6f, .95f), -97);
            sp.Put(Art.Dot, W * .755f + cp.x * .85f, 120 + cp.y * .85f, 70, 70, Lighten(map.SkyBottom, .75f, .5f), -97);
            // 云
            for (int i = 0; i < 7; i++)
            {
                float x = Mathf.Repeat(i * 330 + t * (8 + i * 3), W + 600) - 300;
                sp.Put(Art.Cloud, x + cp.x * .7f, 120 + i * 50 + cp.y * .7f, 260 - i * 20, 120 - i * 10, Lighten(map.SkyBottom, .3f, .25f), -96);
            }
            float scroll = cp.x;
            sp.Put(far, W / 2f + scroll * .75f, H * .61f + cp.y * .75f, W + 600, H * .67f, Color.white, -95);
            sp.Put(near, W / 2f + scroll * .55f, H * .72f + cp.y * .55f, W + 600, H * .61f, Color.white, -94);
            // 深渊光
            sp.Put(Art.Soft, W / 2f, H + 40, W * 1.6f, 200, new Color(1, .33f, .4f, .18f), -93, 0, true);
            // 飘浮尘埃
            for (int i = 0; i < 36; i++)
            {
                float x = Mathf.Repeat(i * 97 + t * 12 * (1 + i % 3), W), y = Mathf.Repeat(i * 53 - t * 18 * (1 + i % 2), H);
                sp.Put(Art.Soft, x, y, 10, 10, new Color(1, 1, 1, .12f), -92, 0, true);
            }
            ViewPolish.Background(sp, Sim, camPos, camSize, t);
        }
        static Color Lighten(uint c, float k, float a = 1) { var col = Color.Lerp(Art.C(c), Color.white, k); col.a = a; return col; }

        void DrawTerrain()
        {
            if (Sim.TileVersion != drawnVersion || Sim.CurMap != drawnMap)
            {
                EnsureTerrTex(); Art.PaintTerrain(terrTex, terrBuf, Sim.Tiles, map); drawnVersion = Sim.TileVersion; drawnMap = Sim.CurMap;
            }
            sp.Put(terrSprite, W / 2f, H / 2f + 4, W, H, new Color(0, 0, 0, .35f), -11); // 柔和投影
            sp.Put(terrSprite, W / 2f, H / 2f, W, H, Color.white, -10);
        }

        void DrawWorld()
        {
            var S = Sim;
            // 黑洞（尸体黑洞）
            foreach (var h in S.Holes)
            {
                if (!h.Active) { float pr = 14 + Mathf.Sin(t * 12) * 4; sp.Put(Art.HoleSpr, h.X, h.Y, pr * 2.4f, pr * 2.4f, Color.white, -5); continue; }
                sp.Put(Art.Soft, h.X, h.Y, h.R * 2.4f, h.R * 2.4f, new Color(.48f, .17f, 1, .35f), -6, 0, true);
                sp.Put(Art.Swirl, h.X, h.Y, h.R * 1.6f, h.R * 1.6f, new Color(1, 1, 1, .7f), -5, -t * 120, true);
                sp.Put(Art.Swirl, h.X, h.Y, h.R * 1.1f, h.R * 1.1f, new Color(1, 1, 1, .5f), -5, t * 200, true);
                sp.Put(Art.HoleSpr, h.X, h.Y, Mathf.Max(60, h.R * .6f), Mathf.Max(60, h.R * .6f), Color.white, -4);
            }
            // 道具箱
            foreach (var it in S.ItemsList)
            {
                if (it.Type == Items.Ult) // 终极球：彩虹光球
                {
                    float ux = it.X + 13, uy = it.Y + 13; var rc = Color.HSVToRGB((t * .4f) % 1, .6f, 1);
                    sp.Put(Art.Soft, ux, uy, 150, 150, new Color(rc.r, rc.g, rc.b, .45f + .15f * Mathf.Sin(t * 7)), 1, 0, true);
                    sp.Put(Art.Ring, ux, uy, 60, 60, new Color(1, .95f, .66f, .9f), 2, t * 120, true);
                    sp.Put(Art.Soft, ux, uy, 40, 40, Color.white, 3, 0, true);
                    sp.Put(Art.Star4, ux, uy, 54, 54, new Color(1, 1, 1, .9f), 4, t * 200, true);
                    continue;
                }
                float bob = it.Landed ? Mathf.Sin(t * 5 + it.X) * 2 : 0; var c = Art.C(Items.Colors[it.Type]);
                if (!it.Landed) sp.Put(Art.Soft, it.X + 13, it.Y - 30, 30, 60, new Color(c.r, c.g, c.b, .25f), 1, 0, true);
                sp.Put(Art.Soft, it.X + 13, it.Y + 12 + bob, 70, 70, new Color(c.r, c.g, c.b, .35f + .15f * Mathf.Sin(t * 6)), 1, 0, true);
                sp.Put(Art.Item, it.X + 13, it.Y + 12 + bob, 30, 28, c, 2);
                sp.Put(Art.Star4, it.X + 13, it.Y + 12 + bob, 14, 14, Color.white, 3, t * 90, true);
            }
            int tsSlot = IsClient && TimeStopSlot != null ? TimeStopSlot() : (S.TimeStopT > 0 && S.TimeStopper != null ? S.TimeStopper.Slot : -1);
            if (tsSlot != curTs) trail.Clear(); curTs = tsSlot;
            DrawMapExtras();
            foreach (var tc in S.Tentacles) DrawTentacle(tc);
            // 幽灵（被淘汰的人）
            foreach (var p in S.Players) if (!p.Alive && p.Ghost) DrawGhost(p);
            // 角色
            foreach (var p in S.Players) if (!p.Hidden) DrawPlayer(p, p.X, p.Y, p.Face, false, tsSlot >= 0 && tsSlot != p.Slot);
            DrawCrown();
            // 分身：与真身画得完全一样
            foreach (var c in S.Clones) if (c.Owner != null && c.Owner.Alive && c.Kind != 3) DrawPlayer(c.Owner, c.X, c.Y, c.Face, true, false);
            NewCharView.Draw(sp, S, t); // 第六版新角色：机关、新子弹、新状态
            // 黑洞牵引线
            foreach (var p in S.Players)
            {
                if (!p.Alive) continue; float lx, ly; bool has = false; lx = ly = 0;
                if (!IsClient && p.Latch != null) { lx = p.Latch.X; ly = p.Latch.Y; has = true; }
                else if (IsClient && ClientLatch != null && ClientLatch(p.Slot, out lx, out ly)) has = true;
                if (!has) continue;
                float dx = p.Cx - lx, dy = p.Cy - ly, len = Mathf.Sqrt(dx * dx + dy * dy);
                for (float k = 0; k < len; k += 10) sp.Put(Art.Dot, lx + dx * k / len, ly + dy * k / len, 4, 4, new Color(.78f, .49f, 1, .8f), 8, 0, true);
                sp.Put(Art.HoleSpr, lx, ly, 30, 30, Color.white, 9, t * 200);
            }
            // 子弹
            foreach (var b in S.Projs) DrawProj(b);
            // 粒子
            for (int i = 0; i < partN; i++)
            {
                var q = parts[i]; float k = Mathf.Clamp01(q.Life / q.Max); var c = q.C; c.a *= k;
                if (q.Glow) sp.Put(Art.Star4, q.X, q.Y, q.Size, q.Size, c, 30, 45, true);
                else if (q.Line) { float ang = Mathf.Atan2(-q.Vy, q.Vx) * Mathf.Rad2Deg; float len = Mathf.Min(40, Mathf.Sqrt(q.Vx * q.Vx + q.Vy * q.Vy) * .04f); sp.Put(Art.Spark, q.X, q.Y, len, q.Size, c, 29, ang, true); }
                else sp.Put(q.Add ? Art.Soft : Art.Square, q.X, q.Y, q.Size * (q.Add ? 2.2f : 1), q.Size * (q.Add ? 2.2f : 1), c, 20, 0, q.Add);
            }
            foreach (var z in zaps)
            {
                float k = z.Life / z.Max; var glow = z.C; glow.a = .55f * k; var core = Color.Lerp(z.C, Color.white, .7f); core.a = k;
                float flickW = .85f + .15f * Mathf.Sin(t * 9);
                for (int i = 0; i + 1 < z.Pts.Length; i++)
                {
                    var a = z.Pts[i]; var b = z.Pts[i + 1]; float dx = b.x - a.x, dy = b.y - a.y, len = Mathf.Sqrt(dx * dx + dy * dy) + 1, ang = -Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    if (z.Flesh)
                    {
                        float wk = z.W * (1.6f - .9f * i / z.Pts.Length); var dk = z.C * .45f; dk.a = k;  var fc = z.C; fc.a = k;
                        sp.Put(Art.Dot, a.x, a.y, wk * 2.4f, wk * 2.4f, dk, 26, 0, false); sp.Put(Art.Dot, a.x, a.y, wk * 1.8f, wk * 1.8f, fc, 27, 0, false);
                        continue;
                    }
                    sp.Put(Art.Pixel, (a.x + b.x) / 2, (a.y + b.y) / 2, len, z.W * 4, glow, 26, ang, true);
                    sp.Put(Art.Pixel, (a.x + b.x) / 2, (a.y + b.y) / 2, len, z.W * flickW, core, 27, ang, true);
                }
            }
            foreach (var r in rings)
            {
                float k = 1 - r.Life / r.Max, rad = r.R0 + (r.R1 - r.R0) * k; var c = r.C; c.a *= 1 - k;
                sp.Put(r.W > 6 ? Art.Ring : Art.RingThin, r.X, r.Y, rad * 2 + r.W, rad * 2 + r.W, c, 25, 0, true);
            }
            ViewPolish.WorldFx(sp, camPos, camSize, t);
            // 透视层 + 头顶箭头（画在所有特效之上）
            foreach (var ov in overlays) { var r = sp.Put(ov.S, ov.X, ov.Y, ov.W, ov.H, ov.C, 38, ov.Rot); r.flipX = ov.Flip; }
            overlays.Clear();
            foreach (var p in S.Players)
            {
                if (!p.Alive) continue; var pc = PlayerColor(p); bool me = LocalSlot == p.Slot;
                float top = p.Y - (p.God ? 50 : 16) - (me ? Mathf.Abs(Mathf.Sin(t * 5)) * 4 : 0);
                sp.Put(Art.Arrow, p.Cx, top - 4, me ? 22 : 16, me ? 16 : 12, new Color(0, 0, 0, .7f), 38);
                sp.Put(Art.Arrow, p.Cx, top - 5, me ? 18 : 13, me ? 13 : 10, me ? Color.Lerp(pc, Color.white, .45f) : pc, 39);
            }
            // 岩浆
            if (S.LavaY < H)
            {
                float top = S.LavaY; float hgt = H - top + 60;
                sp.Put(Art.Pixel, W / 2f, top + hgt / 2, W + 600, hgt, Art.C(0xc8301c), 40);
                sp.Put(Art.Pixel, W / 2f, top + 26 + hgt / 2, W + 600, hgt, Art.C(0x7a0f1a, .8f), 41);
                for (int i = 0; i < 60; i++)
                {
                    float x = i * 36 - 60, y = top + Mathf.Sin(t * 3 + i * .9f) * 5;
                    sp.Put(Art.Dot, x, y + 6, 46, 26, Art.C(0xff5a1f), 42);
                    sp.Put(Art.Dot, x + 10, y + 2, 26, 10, Art.C(0xffb02e), 43);
                }
                sp.Put(Art.Soft, W / 2f, top, W * 1.6f, 140, new Color(1, .5f, .15f, .45f), 44, 0, true);
                for (int i = 0; i < 10; i++)
                {
                    float bx = Mathf.Repeat(i * 173 + t * 40, W), by = top + 14 + Mathf.Repeat(i * 37 + t * 20, Mathf.Max(10, H - top - 10));
                    sp.Put(Art.Dot, bx, by, 6 + i % 3 * 2, 6 + i % 3 * 2, Art.C(0xffe27a), 45);
                }
            }
            // 时停：全屏去色罩，只有剑圣保持彩色
            if (tsSlot >= 0)
            {
                sp.Put(Art.Pixel, camPos.x, camPos.y, W * 2, H * 2, new Color(.55f, .6f, .85f, .28f), 60);
                foreach (var p in S.Players) if (p.Slot == tsSlot && p.Alive)
                    {
                        DrawPlayer(p, p.X, p.Y, p.Face, false, false, 70);
                        sp.Put(Art.RingThin, p.Cx, p.Cy, 240 + Mathf.Sin(t * 6) * 16, 240 + Mathf.Sin(t * 6) * 16, new Color(.87f, .9f, 1, .8f), 69, t * 40, true);
                        sp.Put(Art.Ring, p.Cx, p.Cy, 150, 150, new Color(.87f, .9f, 1, .4f), 69, -t * 80, true);
                    }
            }
            bool anyGod = false; foreach (var p in S.Players) if (p.Alive && p.God) anyGod = true;
            if (anyGod) sp.Put(Art.Soft, camPos.x, camPos.y, W * 2.2f, H * 2.2f, new Color(1, .66f, .16f, .05f + .02f * Mathf.Sin(t * 5)), 59, 0, true);
            if (WhiteFlash > 0) sp.Put(Art.Pixel, camPos.x, camPos.y, W * 2, H * 2, new Color(1, .93f, .82f, Mathf.Min(.22f, WhiteFlash * .3f)), 90); // 柔和闪光，避免刺眼
        }

        void DrawGhost(Player p)
        {
            float bob = Mathf.Sin(t * 3 + p.Slot) * 5; var body = Art.BodyWhite[(int)p.Ch];
            var c = Art.C(SlotColors[p.Slot]); c.a = .45f;
            sp.Put(Art.Soft, p.Cx, p.Cy + bob, 80, 80, new Color(c.r, c.g, c.b, .25f), 6, 0, true);
            var r = sp.Put(body, p.Cx, p.Cy + bob, body.bounds.size.x, body.bounds.size.y, c, 7); r.flipX = p.Face < 0;
            for (int i = 0; i < 3; i++) sp.Put(Art.Dot, p.Cx - 8 + i * 8, p.Y + p.H + 4 + bob + Mathf.Sin(t * 8 + i) * 3, 6, 6, c, 7);
            if (p.GhostCd <= 0 || IsClient) sp.Put(Art.RingThin, p.Cx, p.Cy + bob, 44, 44, new Color(1, 1, 1, .25f), 7, t * 90, true);
        }

        void DrawCrown()
        {
            var S = Sim; if (!S.CrownOn) return;
            Player h = null; foreach (var p in S.Players) if (p.Slot == S.CrownHolder && p.Alive) h = p;
            float x = h != null ? h.Cx : S.CrownX, y = h != null ? h.Y - (h.God ? 64 : 22) : S.CrownY;
            if (h != null) sp.Put(Art.Soft, x, y - 200, 60, 460, new Color(1, .85f, .3f, .16f + .05f * Mathf.Sin(t * 4)), 5, 0, true);
            else sp.Put(Art.Soft, x, y - 40, 40, 140, new Color(1, .85f, .3f, .3f), 5, 0, true);
            sp.Put(Art.Soft, x, y, 70, 70, new Color(1, .85f, .3f, .45f), 49, 0, true);
            sp.Put(Art.Crown, x, y + (h == null ? Mathf.Sin(t * 4) * 3 : 0), 30, 22, Color.white, 50, h != null ? Mathf.Sin(t * 3) * 6 : 0);
            sp.Put(Art.Star4, x + 8, y - 6, 10, 10, Color.white, 51, t * 120, true);
        }

        void DrawMapExtras()
        {
            var S = Sim; var m = Maps.All[Mathf.Clamp(S.CurMap, 0, Maps.All.Length - 1)];
            // 传送门
            if (m.Portals != null)
                foreach (var pt in m.Portals)
                    for (int k = 0; k < 2; k++)
                    {
                        float x = pt[k * 2], y = pt[k * 2 + 1]; var c = Art.C((uint)pt[4]);
                        sp.Put(Art.Soft, x, y, 110, 130, new Color(c.r, c.g, c.b, .4f), -3, 0, true);
                        sp.Put(Art.Ring, x, y, 34 + Mathf.Sin(t * 3) * 3, 70, c, -2);
                        sp.Put(Art.RingThin, x, y, 20, 52, new Color(1, 1, 1, .7f), -2, t * 200, true);
                        for (int i = 0; i < 2; i++) { float a = t * 3 + i * Mathf.PI + k; sp.Put(Art.Dot, x + Mathf.Cos(a) * 14, y + Mathf.Sin(a) * 30, 5, 5, Color.white, -1, 0, true); }
                    }
            // ---- 第五版大地图机关 ----
            if (m.Hazard == MapHazard.Train)
            {   // 高速感：横向速度线；隧道预警红灯；隧道口黑影扫过车顶
                for (int i = 0; i < 30; i++) { float y = 60 + (i * 53) % (H - 200), x = Mathf.Repeat(-i * 173 - t * 1600, W + 400) - 200; sp.Put(Art.Spark, x, y, 140, 2, new Color(1, 1, 1, .12f), -9, 0, true); }
                float roof = Sim.TRAIN_ROOF * T;
                if (S.HazPhase == 1) { float a = Mathf.PingPong(t * 6, 1); for (int i = 0; i < 6; i++) sp.Put(Art.Soft, 150 + i * 580, roof - 70, 70, 70, new Color(1, .15f, .15f, .25f + .5f * a), 30, 0, true); sp.Put(Art.Square, W / 2f, roof - 60, W, 120, new Color(1, .1f, .1f, .08f + .1f * a), 29); }
                if (S.HazPhase == 2) { float k = 1 - Mathf.Clamp01(S.HazT / 1.1f); sp.Put(Art.Square, Mathf.Lerp(W + 400, -400, k), roof - 260, 900, 520, new Color(.02f, .02f, .05f, .92f), 34); }
            }
            else if (m.Hazard == MapHazard.Space)
            {
                if (m.LowGravX0 >= 0) { sp.Put(Art.Square, (m.LowGravX0 + m.LowGravX1) / 2, H / 2f, m.LowGravX1 - m.LowGravX0, H, new Color(.5f, .8f, 1, .05f), -12); for (int i = 0; i < 18; i++) sp.Put(Art.Dot, m.LowGravX0 + (i * 157) % (m.LowGravX1 - m.LowGravX0), Mathf.Repeat(H - i * 89 - t * 30, H), 4, 4, new Color(.7f, .9f, 1, .3f), -8, 0, true); }
                if (S.HazPhase >= 1)
                {
                    float doorX = S.HazSide < 0 ? 11 * T : 133 * T, a = S.HazPhase == 1 ? Mathf.PingPong(t * 5, 1) : 1;
                    sp.Put(Art.Soft, doorX, 59 * T, 180, 320, new Color(.5f, .95f, 1, .25f * a), 30, 0, true);
                    if (S.HazPhase == 2) for (int i = 0; i < 14; i++) { float k = Mathf.Repeat(t * 2 + i * .13f, 1); sp.Put(Art.Spark, doorX - S.HazSide * (800 * (1 - k)), (53 + (i * 7) % 13) * T, 90, 2, new Color(.8f, 1, 1, .5f * k), 35, 0, true); }
                }
            }
            else if (m.Hazard == MapHazard.Forge)
            {
                foreach (var hm in Sim.Hammers)
                {
                    float x0 = hm[0] * T, x1 = (hm[1] + 1) * T, cx = (x0 + x1) / 2, top = Sim.HAMMER_TOP * T, bot = Sim.HAMMER_BOTTOM * T;
                    float k = S.HazPhase == 2 ? 1 : S.HazPhase == 1 ? -.08f * Mathf.Clamp01(1 - S.HazT / 1.2f) : 0; // 预警时先往上抬
                    float headY = Mathf.Lerp(top + 40, bot - 50, Mathf.Clamp01(k));
                    sp.Put(Art.Square, cx, (top + headY) / 2, 26, headY - top, new Color(.35f, .33f, .38f), -4);
                    sp.Put(Art.Square, cx, headY, x1 - x0, 90, new Color(.45f, .42f, .48f), -3);
                    sp.Put(Art.Square, cx, headY + 40, x1 - x0, 10, new Color(.25f, .22f, .26f), -2);
                    if (S.HazPhase == 1) sp.Put(Art.Square, cx, (top + bot) / 2, x1 - x0, bot - top, new Color(1, .2f, .1f, .1f + .15f * Mathf.PingPong(t * 8, 1)), -5);
                }
            }
            // 风
            if (S.WindState != 0)
            {
                bool on = S.WindState == 1;
                for (int i = 0; i < 26; i++)
                {
                    float y = (i * 67) % H, x = Mathf.Repeat(i * 211 + t * 260 * S.WindDir * (on ? 3 : 1), W + 200) - 100;
                    sp.Put(Art.Spark, x, y, on ? 110 : 50, 3, new Color(.8f, .92f, 1, on ? .45f : .15f), 35, 0, true);
                }
            }
            // 冰锥 / 流星
            foreach (var s in S.Falls)
            {
                bool warn = s.Warn > 0;
                if (warn)
                {
                    for (float yy = s.Y; yy < H; yy += 22) sp.Put(Art.Pixel, s.X, yy, 2, 10, s.Meteor ? new Color(1, .5f, .2f, .35f) : new Color(.62f, .9f, 1, .3f), 4);
                    if (s.Meteor) sp.Put(Art.Soft, s.X, 30, 60, 60, new Color(1, .4f, .1f, .5f + .3f * Mathf.Sin(t * 20)), 4, 0, true);
                }
                if (s.Meteor)
                {
                    if (warn) continue;
                    sp.Put(Art.Spark, s.X, s.Y - 40, 90, 22, new Color(1, .6f, .2f, .7f), 33, 90, true);
                    sp.Put(Art.Soft, s.X, s.Y, 60, 60, new Color(1, .55f, .2f, .8f), 34, 0, true);
                    sp.Put(Art.Dot, s.X, s.Y, 20, 20, Art.C(0xffe2a0), 35);
                }
                else
                {
                    float sx = s.X + (warn ? Mathf.Sin(t * 33) * 2 : 0);
                    sp.Put(Art.Sword, sx, s.Y, 16, 36, warn ? Art.C(0xeafaff) : Art.C(0x9fe4ff), 34, 180);
                }
            }
            // 低重力：飘浮的光点
            if (S.EventId == 0 && S.EventT > 0) for (int i = 0; i < 20; i++) sp.Put(Art.Soft, Mathf.Repeat(i * 97, W), Mathf.Repeat(i * 61 - t * 30, H), 14, 14, new Color(.75f, .9f, 1, .25f), -9, 0, true);
        }

        public delegate bool LatchQuery(int slot, out float x, out float y);
        public LatchQuery ClientLatch;

        void DrawPlayer(Player p, float px, float py, int face, bool isClone, bool frozen, int baseOrder = 10)
        {
            if (!p.Alive) return;
            if (!isClone) ViewPolish.HoldPos(p, ref px, ref py);
            int f = face == 0 ? 1 : face; float w = p.W, h = p.H, cx = px + w / 2, cy = py + h / 2;
            float vy = Mathf.Clamp(p.Vy / 2400, -.14f, .16f); squash.TryGetValue(p.Slot, out float sq);
            if (p.OnGround) vy = 0;
            float sx = 1 - vy * .5f + sq * .9f, sy = 1 + vy - sq;
            bool running = p.OnGround && Mathf.Abs(p.Vx) > 30;
            float step = Mathf.Sin(t * 15 + p.Slot);
            float bob = running ? Mathf.Abs(step) * 4 : 0;
            if (running) { sy *= 1 + Mathf.Abs(step) * .05f; sx *= 1 - Mathf.Abs(step) * .03f; }
            else if (p.OnGround) { float br = Mathf.Sin(t * 3 + p.Slot * 1.3f); sy *= 1 + br * .025f; sx *= 1 - br * .018f; } // 站着时轻轻呼吸
            if (p.Swing > 0) sx *= 1.1f; // 出手时往前一冲
            ViewPolish.Scale(p, isClone, ref sx, ref sy);
            float footY = py + h; // 以脚底为挤压中心
            bool fl = p.Flash > 0; bool blink = p.Inv > 0 && p.Ch == CharId.XP && !p.Slam ? false : (p.Inv > 0 && Mathf.Repeat(t * 12, 1) < .5f);
            Color tint = frozen ? new Color(.78f, .84f, .95f) : Color.white;
            int o = baseOrder + p.Slot * 3;
            // 脚下影子
            sp.Put(Art.Soft, cx, footY, w * 1.6f, 10, new Color(0, 0, 0, .35f), o - 1);
            // 光环（神化）
            if (p.God)
            {
                float pulse = 1 + .12f * Mathf.Sin(t * 8);
                sp.Put(Art.Soft, cx, cy, 240 * pulse, 240 * pulse, new Color(1, .77f, .2f, .45f), o - 1, 0, true);
                for (int s = -1; s <= 1; s += 2)
                    for (int L = 0; L < 3; L++)
                    {
                        float flap = Mathf.Sin(t * 6) * 14 + L * 18; var wc = L == 0 ? new Color(1, .98f, .86f, .95f) : L == 1 ? new Color(1, .84f, .35f, .8f) : new Color(1, .6f, .2f, .6f);
                        sp.Put(Art.Wing, cx + s * (34 + L * 10), cy - 6 - L * 4, 70 - L * 8, 24, wc, o, s > 0 ? flap : 180 - flap, true);
                    }
                sp.Put(Art.Halo, cx, cy - 40, 44, 14, new Color(1, .95f, .6f, .9f), o + 2, 0, true);
                for (int i = 0; i < 4; i++) { float a = t * 3 + i * Mathf.PI / 2; sp.Put(Art.Sword, cx + Mathf.Cos(a) * 62, cy + Mathf.Sin(a) * 40, 8, 30, Color.white, o + 1, -a * Mathf.Rad2Deg); }
                // 创世神：身后缓缓转动的星盘 + 3 颗星辰（星环会灼烧靠近的人）
                sp.Put(Art.RingThin, cx, cy, 180, 180, new Color(1, .9f, .55f, .35f), o - 2, t * 20, true);
                sp.Put(Art.RingThin, cx, cy, 130, 130, new Color(.6f, .9f, 1, .3f), o - 2, -t * 35, true);
                for (int i = 0; i < 8; i++) { float a = t * .35f + i * Mathf.PI / 4; sp.Put(Art.Star4, cx + Mathf.Cos(a) * 90, cy + Mathf.Sin(a) * 90, 7, 7, new Color(1, .95f, .7f, .6f), o - 2, 0, true); }
                for (int i = 0; i < 3; i++)
                {
                    float a = -t * 2.2f + i * Mathf.PI * 2 / 3, sx2 = cx + Mathf.Cos(a) * 80, sy2 = cy + Mathf.Sin(a) * 60;
                    sp.Put(Art.Soft, sx2, sy2, 40, 40, new Color(1, .85f, .4f, .5f), o + 2, 0, true); sp.Put(Art.Star4, sx2, sy2, 16, 16, new Color(1, 1, .9f, 1), o + 3, t * 120, true);
                }
            }
            // 吸收护盾（bw 技能）/ 疾跑 / 狂暴
            if (p.Shield > 0)
            {   // 吞噬盾：深紫黑色的漩涡球，很显眼
                float R = h * 2.4f + 30;
                sp.Put(Art.Soft, cx, cy, R * 1.6f, R * 1.6f, new Color(.3f, .05f, .55f, .55f), o - 1, 0, true);
                sp.Put(Art.HoleSpr, cx, cy, R, R, new Color(1, 1, 1, .55f), o - 1, -t * 160);
                sp.Put(Art.Swirl, cx, cy, R * 1.15f, R * 1.15f, new Color(.6f, .3f, 1, .9f), o + 2, t * 240, true);
                sp.Put(Art.Swirl, cx, cy, R * .8f, R * .8f, new Color(.2f, .05f, .35f, .9f), o + 2, -t * 300, false);
                sp.Put(Art.Ring, cx, cy, R, R, new Color(.78f, .49f, 1, .7f + .2f * Mathf.Sin(t * 20)), o + 2, t * 90, true);
                if (UnityEngine.Random.value < .8f) { float a = UnityEngine.Random.value * 6.283f; AddPart(new Particle { X = cx + Mathf.Cos(a) * R * .9f, Y = cy + Mathf.Sin(a) * R * .9f, Vx = -Mathf.Cos(a) * R * 2.2f, Vy = -Mathf.Sin(a) * R * 2.2f, Life = .4f, Max = .4f, C = new Color(.65f, .35f, 1, .9f), Size = 4, Add = true, Drag = 1 }); }
            }
            if (p.BurnT > 0 && p.Alive && UnityEngine.Random.value < .5f) // 侵蚀燃烧：紫黑色火苗
                AddPart(new Particle { X = cx + UnityEngine.Random.Range(-w * .45f, w * .45f), Y = cy + UnityEngine.Random.Range(-h * .3f, h * .4f), Vx = UnityEngine.Random.Range(-20, 20), Vy = UnityEngine.Random.Range(-140, -70), Life = .45f, Max = .45f, C = UnityEngine.Random.value < .5f ? new Color(.62f, .25f, 1, .9f) : new Color(.18f, .04f, .3f, .9f), Size = UnityEngine.Random.Range(4, 8), Add = UnityEngine.Random.value < .5f, Drag = .95f });
            if (p.BwShield > 0) sp.Put(Art.RingThin, cx, cy, h + 18, h + 18, new Color(.78f, .49f, 1, Mathf.Clamp01(p.BwShield / 40)), o + 2, 0, true);
            if (p.PowT > 0) sp.Put(Art.Soft, cx, cy, w * 3, h * 3, new Color(1, .6f, .24f, .35f), o - 1, 0, true);
            if (p.Ult && p.Alive) { var rc = Color.HSVToRGB((t * .5f) % 1, .55f, 1); sp.Put(Art.Soft, cx, cy, w * 4, h * 3.2f, new Color(rc.r, rc.g, rc.b, .4f + .15f * Mathf.Sin(t * 9)), o - 1, 0, true); sp.Put(Art.Star4, cx, cy - h * .9f, 26, 26, Color.white, o + 3, t * 180, true); }
            if (p.SpdT > 0 && Mathf.Abs(p.Vx) > 50) sp.Put(Art.Spark, cx - Sign(p.Vx) * w, cy, w * 1.6f, h * .5f, new Color(.35f, .7f, 1, .5f), o - 1, 0, true);
            // 挥砍弧光
            if (p.Swing > 0 && p.Ch != CharId.BW)
            {
                var sc = p.Ch == CharId.CCLD ? new Color(.9f, .95f, 1, .8f) : p.Ch == CharId.KAI ? new Color(.3f, .95f, 1, .7f) : new Color(.3f, 1, .54f, .6f);
                float R = p.Ch == CharId.CCLD ? 64 : 46;
                sp.Put(Art.Wave, cx + f * (w / 2 + R / 3), cy, R * .7f, R * 1.4f, sc, o + 2, f > 0 ? 0 : 180, true);
            }
            // 身体
            Sprite body = p.God ? Art.GodBody : Art.Body[(int)p.Ch], white = p.God ? Art.GodWhite : Art.BodyWhite[(int)p.Ch];
            float grow = p.God ? 1 : w / p.C.W; Vector2 bs = body.bounds.size * grow; float bw = bs.x * sx, bh = bs.y * sy;
            bool deity = p.Ch == CharId.CCLD2 && p.Divine; // ccld-改 神化：身体放大 1.35 倍、通体金光
            if (deity) { bs *= 1.35f; bw = bs.x * sx; bh = bs.y * sy; }
            float bodyCy = footY - h / 2 * sy - bob - (deity ? (bh - h * sy) * .5f : 0);
            Color bc = tint;
            if (p.Ch == CharId.DVD && !p.God) bc *= Color.HSVToRGB(p.Hue / 360f, .7f, 1f);
            if (blink) bc.a = .4f;
            float rot = p.Ch == CharId.DVD ? Mathf.Sin(t * 4 + p.Slot) * 6 : (p.Slam ? -f * 12 : 0);
            if (p.OnGround) rot += -Mathf.Clamp(p.Vx / 55, -7, 7); // 跑起来身体前倾
            else rot += -Mathf.Clamp(p.Vx / 90, -5, 5) * Mathf.Sign(-p.Vy + 1);
            if (p.Flash > 0 || p.Stun > 0) rot += f * 9 * Mathf.Clamp01(Mathf.Max(p.Flash / .12f, p.Stun / .25f)); // 挨打后仰
            if (!isClone && flipT.TryGetValue(p.Slot, out float ftt) && ftt > 0) rot += -f * 360 * (1 - ftt / .38f);
            ViewPolish.Lean(p, isClone, ref rot);
            float lunge = p.Swing > 0 ? f * 4 : 0; cx += lunge;
            if (p.Ch == CharId.BW && !p.God) sp.Put(Art.Swirl, cx, cy, bs.x * 1.5f, bs.y * 1.5f, new Color(1, 1, 1, .7f), o, t * 160, true);
            if (!isClone && p.Slot == curTs)
            {
                // 超频：彩色残影拖尾 + 轻微色差，世界其他部分略微偏冷
                trailT -= Time.unscaledDeltaTime;
                if (trailT <= 0) { trailT = .035f; trail.Add(new Vector4(cx, bodyCy, f, 0)); if (trail.Count > 12) trail.RemoveAt(0); }
                for (int i = 0; i < trail.Count; i++)
                {
                    var q = trail[i]; float k = (i + 1) / (float)trail.Count; var tc = i % 2 == 0 ? new Color(.2f, .95f, 1f, .32f * k) : new Color(1f, .3f, .85f, .28f * k);
                    var tr = sp.Put(white, q.x, q.y, bw, bh, tc, o - 1, rot, true); tr.flipX = q.z < 0;
                }
                var ca = sp.Put(white, cx - 3, bodyCy, bw, bh, new Color(1, .2f, .6f, .35f), o, rot, true); ca.flipX = f < 0;
                var cb = sp.Put(white, cx + 3, bodyCy, bw, bh, new Color(.2f, .9f, 1, .35f), o, rot, true); cb.flipX = f < 0;
            }
            if (p.Ch == CharId.DZY && p.Stk > 0)
            {   // 触手怪：身后 4~6 条摆动的触手，越进化越长
                int nt = Mathf.Min(6, 3 + p.Stk / 3); float L = 22 + p.Stk * 2.5f; var fc = Art.C(0xe0457b); var dk = Art.C(0x5a0a28);
                for (int k = 0; k < nt; k++)
                {
                    float th = -.35f + 1.6f * k / Mathf.Max(1, nt - 1), sw = Mathf.Sin(t * 4 + k * 1.7f);
                    for (int q = 0; q < 7; q++)
                    {
                        float u = q / 6f, a = th + sw * .45f * u, rr = w * .3f + L * u;
                        float qx = cx - f * Mathf.Cos(a) * rr, qy = cy + Mathf.Sin(a) * rr * .9f;
                        float sz = Mathf.Lerp(10, 4, u) * (1 + p.Stk * .03f);
                        sp.Put(Art.Dot, qx, qy, sz + 3, sz + 3, dk, o - 1); sp.Put(Art.Dot, qx, qy, sz, sz, fc, o);
                    }
                }
            }
            // 彩色描边：每个人身体外面一圈自己的颜色（团队模式为队伍颜色）
            var pcol = PlayerColor(p); var oc = pcol; oc.a = blink ? .35f : .95f;
            if (deity)
            {
                float gk = .5f + .5f * Mathf.Sin(t * 3);
                var g1 = sp.Put(white, cx, bodyCy, bw + 22 + 6 * gk, bh + 22 + 6 * gk, new Color(1, .82f, .3f, .45f), o - 1, rot, true); g1.flipX = f < 0;
                var g2 = sp.Put(white, cx, bodyCy, bw + 13, bh + 13, new Color(1, .95f, .7f, .9f), o, rot); g2.flipX = f < 0;
                bc = Color.Lerp(bc, new Color(1, .93f, .62f, bc.a), .3f);
            }
            var oln = sp.Put(white, cx, bodyCy, bw + 7, bh + 7, oc, o, rot); oln.flipX = f < 0;
            sp.Put(Art.RingThin, cx - lunge, footY, w * 1.9f + 10, 12, new Color(pcol.r, pcol.g, pcol.b, .8f), o - 1, 0, true); // 脚下光圈
            var r = sp.Put(body, cx, bodyCy, bw, bh, bc, o + 1, rot);
            r.flipX = f < 0;
            if (baseOrder < 60) overlays.Add(new Overlay { S = body, X = cx, Y = bodyCy, W = bw, H = bh, Rot = rot, Flip = f < 0, C = new Color(bc.r, bc.g, bc.b, .42f) });
            if (fl) { var wr = sp.Put(white, cx, bodyCy, bw, bh, new Color(1, 1, 1, .9f), o + 2, rot); wr.flipX = f < 0; }
            ViewPolish.PlayerFx(sp, p, isClone, white, cx, bodyCy, bw, bh, rot, f, o);
            // 剑客手里的剑
            if (p.Ch == CharId.CCLD && !p.God)
            {
                float swingA = p.Swing > 0 ? Mathf.Lerp(-100, 40, 1 - p.Swing / .16f) : -20;
                sp.Put(Art.Sword, cx + f * (w / 2 + 4), cy + 4, 9, 36, tint, o + (f > 0 ? 2 : 0), f > 0 ? swingA : -swingA);
            }
            if (p.Ch == CharId.GARY)
            {
                float flow = Mathf.Sin(t * 14 + p.Slot) * 8 - Mathf.Clamp(p.Vx * .02f, -14, 14);
                sp.Put(Art.Scarf, cx - f * (w / 2 + 6), bodyCy - h * .04f, 20, 6, tint, o, -f * 0 + flow * f, false);
                sp.Put(Art.Kunai, cx + f * (w / 2 + 2), cy + 6, 16, 6, tint, o + 2, f > 0 ? -30 : 210);
                if (p.Grap)
                {
                    float dx = p.GrapX - p.Cx, dy = p.GrapY - p.Cy, len = Mathf.Sqrt(dx * dx + dy * dy);
                    sp.Put(Art.Pixel, p.Cx + dx / 2, p.Cy + dy / 2, len, 2.5f, Art.C(0xffd27a), o - 1, -Mathf.Atan2(dy, dx) * Mathf.Rad2Deg);
                    sp.Put(Art.Claw, p.GrapX, p.GrapY, 14, 14, Color.white, o - 1, -Mathf.Atan2(dy, dx) * Mathf.Rad2Deg);
                }
            }
            if (p.Ch == CharId.KAI)
            {
                sp.Put(Art.Fist, cx + f * (w / 2 + 3), cy + 5 - (p.Swing > 0 ? 3 : 0), 13, 12, tint, o + 2, 0).flipX = f < 0;
                for (int i = 0; i < p.Stk; i++)
                {
                    float a = t * 4 + i * Mathf.PI * 2 / Mathf.Max(1, p.Stk);
                    sp.Put(Art.Star4, cx + Mathf.Cos(a) * (w * .8f), cy + Mathf.Sin(a) * (h * .65f), 9, 9, new Color(.55f, 1, 1, .9f), o + 3, t * 200, true);
                }
                if (p.Stk >= 5) sp.Put(Art.Soft, cx, cy, w * 2.6f, h * 2.6f, new Color(.25f, .9f, 1, .25f + .15f * Mathf.Sin(t * 16)), o - 1, 0, true);
            }
            if (p.Ch == CharId.XP && (p.Rage)) sp.Put(Art.Soft, cx, cy, w * 2.2f, h * 2.2f, new Color(.3f, 1, .54f, .25f + .15f * Mathf.Sin(t * 10)), o, 0, true);
            if (p.Bomb) sp.Put(Art.Bomb, cx + f * (w / 2 + 6), cy - 4, 18, 18, Color.white, o + 2);
            if (p.IceBomb) sp.Put(Art.Bomb, cx + f * (w / 2 + 6), cy - 4, 18, 18, Art.C(0x9fe8ff), o + 2);
            if (p.Bubble > 0) { sp.Put(Art.Soft, cx, cy, h * 2.6f, h * 2.6f, new Color(.66f, 1, .94f, .18f), o + 2, 0, true); sp.Put(Art.RingThin, cx, cy, h * 1.9f, h * 1.9f, new Color(.66f, 1, .94f, .8f), o + 2, 0, true); sp.Put(Art.Dot, cx - h * .4f, cy - h * .45f, 6, 4, new Color(1, 1, 1, .8f), o + 2); }
            if (p.SpringT > 0) for (int k = -1; k <= 1; k += 2) sp.Put(Art.Square, cx + k * w / 4, footY + 3, 6, 6 + Mathf.Abs(Mathf.Sin(t * 10)) * 4, Art.C(0xffe27a), o + 2);
            if (p.FrozenT > 0) { sp.Put(Art.Square, cx, cy, w + 14, h + 14, new Color(.63f, .9f, 1, .55f), o + 3); sp.Put(Art.Square, cx - w * .25f, cy - h * .3f, w * .3f, 4, new Color(1, 1, 1, .8f), o + 3); }
            if (p.GiantT > 0) sp.Put(Art.Soft, cx, cy, w * 2.4f, h * 2.4f, new Color(.75f, .3f, 1, .18f), o - 1, 0, true);
            if (Sim.Leader() == p.Slot && Sim.Mode == GameMode.Classic) sp.Put(Art.Crown, cx, py - (p.God ? 70 : 34), 18, 13, new Color(1, .85f, .3f, .9f), o + 3);
            if (p.Inv > 0 && Sim.Mode == GameMode.Crown && !p.Slam) sp.Put(Art.RingThin, cx, cy, h + 24, h + 24, new Color(1, 1, 1, .5f), o + 3, t * 200, true);
            // 状态：眩晕星星、坍缩层、流血
            if (p.Daze > 0) for (int i = 0; i < 3; i++) { float a = t * 6 + i * 2.09f; sp.Put(Art.Star4, cx + Mathf.Cos(a) * 14, py - 8 + Mathf.Sin(a) * 4, 9, 9, Art.C(0xffe27a), o + 2); }
            for (int i = 0; i < p.Dots; i++) sp.Put(Art.Dot, cx - 15 + i * 6, py - 10, 5, 5, Art.C(0xc77dff), o + 2);
            // 头顶箭头在 DrawWorld 的透视层里统一画（在特效之上）
            bool me = LocalSlot == p.Slot;
            if (isClone) sp.Put(Art.Arrow, cx, py - 20, 13, 10, pcol, 39);
            if (me && !isClone && p.CloneT > 0) sp.Put(Art.RingThin, cx, cy, w + 26, w + 26, new Color(1, 1, 1, .6f), o + 2, 0, true);
        }

        void DrawTentacle(Tentacle tc)
        {
            var fc = Art.C(0xe0457b); var dk = Art.C(0x5a0a28); var lt = Art.C(0xff9ab8);
            float life = Mathf.Clamp01(tc.T / 1.2f), grow = Mathf.Clamp01((16 - tc.T) * 4); float hgt = 64 * grow * Mathf.Min(1, life + .3f);
            sp.Put(Art.Soft, tc.X, tc.Y, 40, 10, new Color(0, 0, 0, .4f), 9);
            int n = 9; float tipX = tc.X, tipY = tc.Y - hgt; bool lash = tc.Lash > 0;
            for (int i = 0; i <= n; i++)
            {
                float u = i / (float)n, sw = Mathf.Sin(t * 3.2f + tc.X * .05f + u * 3) * 10 * u;
                float x = tc.X + sw, y = tc.Y - hgt * u;
                if (lash) { float k = u * u; x = Mathf.Lerp(x, tc.LashX, k * .75f); y = Mathf.Lerp(y, tc.LashY, k * .75f); }
                float sz = Mathf.Lerp(16, 6, u);
                sp.Put(Art.Dot, x, y, sz + 4, sz + 4, dk, 11); sp.Put(Art.Dot, x, y, sz, sz, fc, 12);
                if (i % 2 == 1) sp.Put(Art.Dot, x + sz * .25f, y, sz * .35f, sz * .35f, lt, 13);
                tipX = x; tipY = y;
            }
            sp.Put(Art.Soft, tipX, tipY, 26, 26, new Color(1, .35f, .55f, .35f), 13, 0, true);
        }
        void DrawProj(Proj b)
        {
            var c = Art.C(b.Color);
            switch (b.Kind)
            {
                case ProjKind.Hole:
                    sp.Put(Art.Soft, b.X, b.Y, b.R * 5, b.R * 5, new Color(.48f, .17f, 1, .5f), 14, 0, true);
                    sp.Put(Art.HoleSpr, b.X, b.Y, b.R * 2.6f, b.R * 2.6f, Color.white, 15, t * 300); break;
                case ProjKind.Wave:
                    {
                        float a = b.Ang * Mathf.Rad2Deg; // 游戏 y 向下 → 旋转取反
                        sp.Put(Art.Soft, b.X, b.Y, 120, 130, new Color(1, .95f, .66f, .5f), 14, -a, true);
                        sp.Put(Art.Wave, b.X, b.Y, 40, b.Hh > 0 ? b.Hh * 2.2f : 100, new Color(1, .97f, .8f, 1), 15, -a, true);
                        sp.Put(Art.Wave, b.X - Mathf.Cos(b.Ang) * 18, b.Y - Mathf.Sin(b.Ang) * 18, 30, 80, new Color(1, .8f, .3f, .6f), 15, -a, true);
                        break;
                    }
                case ProjKind.Sword:
                    sp.Put(Art.SwordGlow, b.X, b.Y - 20, 26, 90, new Color(.88f, .96f, 1, .6f), 14, 180, true);
                    sp.Put(Art.Sword, b.X, b.Y, 12, 52, Color.white, 15, 180); break;
                case ProjKind.Disc:
                    sp.Put(Art.Soft, b.X, b.Y, b.R * 4, b.R * 4, new Color(c.r, c.g, c.b, .4f), 14, 0, true);
                    sp.Put(Art.Disc, b.X, b.Y, b.R * 2.4f, b.R * 2.4f, Color.HSVToRGB(Mathf.Repeat(t * .5f + b.X * .002f, 1), .45f, 1), 15, -t * 720); break;
                case ProjKind.Kunai:
                    {
                        float a = Mathf.Atan2(-b.Vy, b.Vx) * Mathf.Rad2Deg;
                        sp.Put(Art.Spark, b.X - b.Vx * .012f, b.Y - b.Vy * .012f, 34, 5, new Color(1, .55f, .25f, .45f), 14, a, true);
                        sp.Put(Art.Kunai, b.X, b.Y, 22, 8, Color.white, 15, a);
                        break;
                    }
                case ProjKind.Hook:
                    {
                        float a = Mathf.Atan2(-b.Vy, b.Vx) * Mathf.Rad2Deg;
                        if (b.Owner != null && b.Owner.Alive)
                        {
                            float dx = b.X - b.Owner.Cx, dy = b.Y - b.Owner.Cy, len = Mathf.Sqrt(dx * dx + dy * dy);
                            sp.Put(Art.Pixel, b.Owner.Cx + dx / 2, b.Owner.Cy + dy / 2, len, 2.5f, Art.C(0xffd27a), 14, -Mathf.Atan2(dy, dx) * Mathf.Rad2Deg);
                        }
                        sp.Put(Art.Claw, b.X, b.Y, 16, 16, Color.white, 15, a);
                        break;
                    }
                case ProjKind.IceBomb:
                    sp.Put(Art.Soft, b.X, b.Y, 50, 50, new Color(.5f, .85f, 1, .5f), 14, 0, true);
                    sp.Put(Art.Bomb, b.X, b.Y, 22, 22, Art.C(0x9fe8ff), 15, -t * 360); break;
                case ProjKind.Bomb:
                    sp.Put(Art.Bomb, b.X, b.Y, 22, 22, Color.white, 15, -t * 360);
                    sp.Put(Art.Star4, b.X + 6, b.Y - 10, 10 + Mathf.Sin(t * 30) * 3, 10 + Mathf.Sin(t * 30) * 3, Art.C(0xffe27a), 16, t * 300, true); break;
                case ProjKind.Pellet: case ProjKind.Spore: case ProjKind.Push: break; // 由 NewCharView 画
                default:
                    {
                        float a = Mathf.Atan2(-b.Vy, b.Vx) * Mathf.Rad2Deg;
                        float spd = Mathf.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy), tl = Mathf.Clamp(spd * .05f, b.R * 2, 70);
                        sp.Put(Art.Spark, b.X - b.Vx / Mathf.Max(1, spd) * tl * .5f, b.Y - b.Vy / Mathf.Max(1, spd) * tl * .5f, tl, b.R * 1.2f, new Color(c.r, c.g, c.b, .35f), 13, a, true); // 拖尾
                        sp.Put(Art.Soft, b.X, b.Y, b.R * 4, b.R * 4, new Color(c.r, c.g, c.b, .5f), 14, 0, true);
                        sp.Put(Art.Spark, b.X, b.Y, b.R * 4, b.R * 1.6f, Color.Lerp(c, Color.white, .5f), 15, a, true);
                        sp.Put(Art.Dot, b.X, b.Y, b.R * 1.1f, b.R * 1.1f, Color.Lerp(c, Color.white, .75f), 16, 0, true);
                        break;
                    }
            }
        }

        /// <summary>游戏坐标 → GUI 坐标（用于伤害数字、名字）。</summary>
        public Vector2 ToGui(float x, float y)
        {
            var s = Cam.WorldToScreenPoint(new Vector3(x, -y, 0));
            return new Vector2(s.x, Screen.height - s.y);
        }
        public float GuiScale => Cam.pixelHeight / (camSize * 2);
    }
}
