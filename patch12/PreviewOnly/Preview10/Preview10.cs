// 只用于第十版画面预览截图（不随正式版发布）：FourBrawl.exe -preview10 <文件夹>
// 依次截：标题、图鉴、ccld-改 详情、本地对战设置、出场特写、原版 ccld「瞬步 · 一闪」、ccld-改 居合、终极球屏幕边箭头、神降、
// 神化 5 段连招（镜头锁在 ccld-改 身上，每段截两张）、七日每一天。
// 通过反射驱动 GameMain / Sim（只在预览里这样做；正式代码一行都不改）。在大地图上开局，保证终极球能出现在镜头外。
using System;
using System.Reflection;
using UnityEngine;
using Brawl.Core;

namespace Brawl.View
{
    public sealed class Preview10 : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var a = Environment.GetCommandLineArgs(); string dir = null;
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-preview10") dir = a[i + 1];
            if (dir == null) return;
            System.IO.Directory.CreateDirectory(dir);
            var go = new GameObject("Preview10"); DontDestroyOnLoad(go); go.AddComponent<Preview10>().dir = dir;
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
        }

        const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        string dir; float t; int n; GameMain gm; FieldInfo fScr, fDetail, fSlots, fSim, fMap; MethodInfo mStart;
        int stage; Sim hooked;
        // 定时截图：(时间, 名字)
        readonly System.Collections.Generic.List<(float at, string name)> pending = new System.Collections.Generic.List<(float, string)>();
        bool didFlash, didIai, didUlt, didDescend; float divineAt = -1; int lastDay = -1; float lastCast = -99; int comboN; float comboAt = -99;

        void Shot(string name) { n++; ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, n.ToString("00") + "_" + name + ".png")); }
        void Later(float dt, string name) => pending.Add((t + dt, name));
        void SetScr(int v) => fScr.SetValue(gm, Enum.ToObject(fScr.FieldType, v));
        static object Call(Sim s, string m, params object[] a) { var mi = typeof(Sim).GetMethod(m, BF); return mi != null ? mi.Invoke(s, a) : null; }

        void Update()
        {
            t += Time.unscaledDeltaTime;
            for (int i = pending.Count - 1; i >= 0; i--) if (t >= pending[i].at) { Shot(pending[i].name); pending.RemoveAt(i); }
            if (gm == null)
            {
                gm = FindFirstObjectByType<GameMain>(); if (gm == null) return; var T = typeof(GameMain);
                fScr = T.GetField("scr", BF); fDetail = T.GetField("detailChar", BF); fSlots = T.GetField("localSlots", BF); fSim = T.GetField("sim", BF); fMap = T.GetField("localMap", BF); mStart = T.GetMethod("StartLocal", BF);
            }
            switch (stage)
            {
                case 0: if (t > 3) { Shot("title"); stage++; } break;
                case 1: if (t > 3.6f) SetScr(6); if (t > 5) { Shot("roster"); stage++; } break;
                case 2: if (t > 5.4f) fDetail.SetValue(gm, (int)CharId.CCLD2); if (t > 6.6f) { Shot("detail_ccld2"); stage++; } break;
                case 3: if (t > 6.9f) { fDetail.SetValue(gm, -1); SetScr(1); } if (t > 8.2f) { Shot("local"); stage++; } break; // 详情页在截图之后的下一步才关
                case 4:
                    if (t > 8.6f)
                    {
                        var slots = (SlotConfig[])fSlots.GetValue(gm); var chs = new[] { CharId.CCLD2, CharId.XP, CharId.KAI, CharId.CCLD };
                        for (int i = 0; i < slots.Length; i++) { slots[i].Mode = i < 4 ? SlotMode.Cpu : SlotMode.Off; slots[i].Ch = chs[i % 4]; }
                        int big = -1; for (int i = 0; i < Maps.All.Length; i++) if (Maps.All[i].Big) { big = i; break; }
                        if (big >= 0 && fMap != null) fMap.SetValue(gm, big);
                        mStart.Invoke(gm, null); stage++; t = 0; pending.Clear();
                        Later(.8f, "enter_a"); Later(1.6f, "enter_b"); Later(2.0f, "countdown_banner");
                    }
                    break;
                case 5: Game(); break;
            }
        }

        void Game()
        {
            var sim = (Sim)fSim.GetValue(gm); if (sim == null) return;
            if (hooked != sim) { hooked = sim; sim.OnFx += OnFx; }
            Player c2 = null, c1 = null;
            foreach (var p in sim.Players) { if (p.Ch == CharId.CCLD2) c2 = p; if (p.Ch == CharId.CCLD) c1 = p; }
            if (c2 == null) return;
            if (c2.Alive) c2.Hp = c2.MaxHp;
            if (c1 != null && c1.Alive) c1.Hp = c1.MaxHp;
            bool play = sim.State == GameState.Play;
            // 1）原版 ccld 瞬步 · 一闪
            if (play && !didFlash && t > 3.5f && c1 != null && c1.Alive && !c1.God) { didFlash = true; Call(sim, "FlashStep", c1); }
            // 2）ccld-改 居合（见切成功）：拿最近的敌人当出手者
            if (play && !didIai && t > 5.5f && !c2.Divine)
            {
                Player e = null; float bd = 1e9f; foreach (var q in sim.Players) if (q != c2 && q.Alive) { float d = Mathf.Abs(q.Cx - c2.Cx) + Mathf.Abs(q.Cy - c2.Cy); if (d < bd) { bd = d; e = q; } }
                if (e != null) { didIai = true; Call(sim, "ParrySuccess", c2, e); }
            }
            // 3）终极球：放在大地图角落（镜头外），看屏幕边箭头
            if (play && !didUlt && t > 7.5f)
            {
                didUlt = true; sim.ItemsList.Add(new Item { X = 40, Y = 40, Type = Items.Ult, Life = 30, Landed = false });
                Later(.5f, "ult_arrow");
            }
            // 4）神降 + 七日
            if (play && t > 9.5f && !c2.Divine && t - lastCast > 1) { c2.Energy = 100; lastCast = t; Call(sim, "SkillNew", c2, Activator.CreateInstance(typeof(PInput))); }
            if (c2.Divine && !didDescend) { didDescend = true; divineAt = t; Later(.35f, "descend_a"); Later(1.1f, "descend_b"); Later(2.4f, "divine_idle"); }
            // 5）神化连招：镜头锁在 ccld-改 身上，每 0.55 秒打一段（连招窗口 1 秒），每段截出招瞬间和刀光最满两张
            if (divineAt >= 0 && t - divineAt > 3 && comboN < 5)
            {
                GodView.PreviewLockSlot = c2.Slot;
                if (t - divineAt > 3.6f && t - comboAt > .55f && c2.Alive)
                {
                    comboAt = t; comboN++;
                    Call(sim, "GodCombo", c2, Activator.CreateInstance(typeof(PInput)));
                    Later(.06f, "combo" + comboN + "_a"); Later(.18f, "combo" + comboN + "_b");
                }
                return;
            }
            if (comboN >= 5 && t - comboAt > .8f) GodView.PreviewLockSlot = -1;
            if (divineAt >= 0 && t - divineAt > 3 && comboN >= 5 && t - comboAt > 1)
            {
                if (c2.Day != lastDay) { lastDay = c2.Day; int dd = ((c2.Day % 7) + 6) % 7; if (t - divineAt > 3.2f) { Later(.3f, "day" + dd + "_a"); Later(.9f, "day" + dd + "_b"); } }
                if (t - lastCast > 2.4f && c2.Alive) { lastCast = t; Call(sim, "SkillNew", c2, Activator.CreateInstance(typeof(PInput))); }
            }
            if (t > 60 || n >= 52) Application.Quit();
        }

        void OnFx(Fx f)
        {
            if (f.Type != FxType.Text || f.Text == null) return;
            if (f.Text == "一闪!") { Later(.06f, "flashstep_a"); Later(.2f, "flashstep_b"); }
            if (f.Text == "见 切 !") { Later(.1f, "iai_a"); Later(.35f, "iai_b"); }
        }
    }
}
