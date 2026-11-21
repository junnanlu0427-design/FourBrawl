// 只用于第十版画面预览截图（不随正式版发布）：FourBrawl.exe -preview10 <文件夹>
// 依次截：标题、图鉴、ccld-改 详情、本地对战设置、出场特写、神降、七日每一天、居合、结算。通过反射驱动 GameMain，不改任何正式代码。
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
        string dir; float t; int n; GameMain gm; FieldInfo fScr, fDetail, fSlots, fSim, fMode; MethodInfo mStart;
        int stage; float nextShot = 2.5f; int lastDay = -1; bool wasDivine; float divineAt = -1, castT, iaiT = -1; int shotsDay; bool s1 = true, s2 = true;
        void Shot(string name) { n++; ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, n.ToString("00") + "_" + name + ".png")); }
        void SetScr(int v) => fScr.SetValue(gm, Enum.ToObject(fScr.FieldType, v));
        void Update()
        {
            t += Time.unscaledDeltaTime;
            if (gm == null)
            {
                gm = FindFirstObjectByType<GameMain>(); if (gm == null) return; var T = typeof(GameMain); var bf = BindingFlags.NonPublic | BindingFlags.Instance;
                fScr = T.GetField("scr", bf); fDetail = T.GetField("detailChar", bf); fSlots = T.GetField("localSlots", bf); fSim = T.GetField("sim", bf); fMode = T.GetField("localMode", bf); mStart = T.GetMethod("StartLocal", bf);
            }
            switch (stage)
            {
                case 0: if (t > 3) { Shot("title"); stage++; } break;
                case 1: if (t > 3.6f) { SetScr(6); } if (t > 5) { Shot("roster"); stage++; } break;
                case 2: if (t > 5.4f) fDetail.SetValue(gm, (int)CharId.CCLD2); if (t > 6.6f) { Shot("detail_ccld2"); fDetail.SetValue(gm, -1); stage++; } break;
                case 3: if (t > 7) SetScr(1); if (t > 8.2f) { Shot("local"); stage++; } break;
                case 4:
                    if (t > 8.6f)
                    {
                        var slots = (SlotConfig[])fSlots.GetValue(gm); var chs = new[] { CharId.CCLD2, CharId.XP, CharId.KAI, CharId.CCLD };
                        for (int i = 0; i < slots.Length; i++) { slots[i].Mode = i < 4 ? SlotMode.Cpu : SlotMode.Off; slots[i].Ch = chs[i % 4]; }
                        mStart.Invoke(gm, null); stage++; t = 0;
                    }
                    break;
                case 5: // 出场
                    if (t > .9f && n < 6) Shot("enter_a"); if (t > 1.7f && n < 7) { Shot("enter_b"); stage++; t = 0; }
                    break;
                case 6:
                    {
                        var sim = (Sim)fSim.GetValue(gm); if (sim == null) break; Player c2 = null, c1 = null;
                        foreach (var p in sim.Players) { if (p.Ch == CharId.CCLD2) c2 = p; if (p.Ch == CharId.CCLD) c1 = p; }
                        if (c2 == null) break;
                        if (c2.Alive) { c2.Hp = c2.MaxHp; }
                        if (c1 != null && c1.Alive && t > 26 && !c1.God) c1.Energy = 100; // 最后看一下原版剑神
                        if (!c2.Divine && sim.State == GameState.Play && t > 2) c2.Energy = 100;
                        if (c2.Divine && !wasDivine) { divineAt = t; }
                        wasDivine = c2.Divine;
                        if (divineAt >= 0)
                        {
                            float d = t - divineAt;
                            if (d > .35f && shotsDay == 0) { Shot("descend_a"); shotsDay = 1; }
                            if (d > 1.1f && shotsDay == 1) { Shot("descend_b"); shotsDay = 2; }
                            if (d > 2.4f && shotsDay == 2) { Shot("divine_idle"); shotsDay = 3; }
                            if (d > 3 && c2.SkCd > .5f && t - castT > 2.3f) { c2.SkCd = 0; }
                            if (c2.Day != lastDay && d > 1) { lastDay = c2.Day; castT = t; s1 = s2 = false; }
                            int dd = ((c2.Day % 7) + 6) % 7;
                            if (castT > 0 && !s1 && t > castT + .3f) { s1 = true; Shot("day" + dd + "_a"); }
                            if (castT > 0 && !s2 && t > castT + .9f) { s2 = true; Shot("day" + dd + "_b"); }
                        }
                        if (t > 48 || n >= 34) { Application.Quit(); }
                        break;
                    }
            }
        }
    }
}
