// 游戏主控：启动自举、屏幕切换、固定步长、输入、联机会话、所有界面（IMGUI）。
// 不需要手动搭场景：任意场景按 Play，Boot 会自动创建一切。
using System;
using System.Collections.Generic;
using UnityEngine;
using Brawl.Core;
using Brawl.Net;
using static Brawl.Core.Defs;

namespace Brawl.View
{
    public static class Boot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            if (UnityEngine.Object.FindFirstObjectByType<GameMain>() != null) return;
            var go = new GameObject("四人乱斗场"); UnityEngine.Object.DontDestroyOnLoad(go); go.AddComponent<GameMain>();
        }
    }

    public sealed class GameMain : MonoBehaviour
    {
        enum Scr { Title, Local, Online, Lan, Lobby, Game, Roster, Settings, Rules }
        GameMode localMode = GameMode.Classic; int localMap = -1; int detailChar = -1; Updater upd; float updCountdown = -1;
        Scr scr = Scr.Title;

        Camera cam; WorldView view; SfxPlayer sfx;
        Sim attract; Sim sim; HostSession host; ClientSession client; INetTransport net;
        float acc, netAcc; bool paused; string status = "", roomCode = "", ipInput = "", codeInput = "";
        string playerName; CharId myChar = CharId.CCLD; bool busy; float joinWait; List<string> lanIps;
        readonly SlotConfig[] localSlots = new SlotConfig[MAX_P];
        readonly byte[] sticky = new byte[6];

        static readonly KeyCode[][][] KEYMAP =
        {
            new[]{ new[]{KeyCode.W}, new[]{KeyCode.S}, new[]{KeyCode.A}, new[]{KeyCode.D}, new[]{KeyCode.Q}, new[]{KeyCode.E} },
            new[]{ new[]{KeyCode.T}, new[]{KeyCode.G}, new[]{KeyCode.F}, new[]{KeyCode.H}, new[]{KeyCode.R}, new[]{KeyCode.Y} },
            new[]{ new[]{KeyCode.I}, new[]{KeyCode.K}, new[]{KeyCode.J}, new[]{KeyCode.L}, new[]{KeyCode.U}, new[]{KeyCode.O} },
            new[]{ new[]{KeyCode.UpArrow}, new[]{KeyCode.DownArrow}, new[]{KeyCode.LeftArrow}, new[]{KeyCode.RightArrow}, new[]{KeyCode.Period, KeyCode.Keypad1}, new[]{KeyCode.Slash, KeyCode.Keypad2} },
            new[]{ new[]{KeyCode.Keypad8}, new[]{KeyCode.Keypad5}, new[]{KeyCode.Keypad4}, new[]{KeyCode.Keypad6}, new[]{KeyCode.Keypad7, KeyCode.Keypad0}, new[]{KeyCode.Keypad9, KeyCode.KeypadPeriod} },
        };
        static readonly string[] KEYLABEL = { "WASD 移动/跳 · Q 攻击 · E 技能", "TFGH 移动/跳 · R 攻击 · Y 技能", "IJKL 移动/跳 · U 攻击 · O 技能", "方向键 · 句号(.) 攻击 · 斜杠(/) 技能", "小键盘 8456 移动/跳 · 7 攻击 · 9 技能" };
        static readonly KeyCode[][] NETKEYS =
        {
            new[]{KeyCode.W, KeyCode.UpArrow, KeyCode.Space}, new[]{KeyCode.S, KeyCode.DownArrow}, new[]{KeyCode.A, KeyCode.LeftArrow}, new[]{KeyCode.D, KeyCode.RightArrow},
            new[]{KeyCode.J, KeyCode.Q, KeyCode.Period, KeyCode.Mouse0}, new[]{KeyCode.K, KeyCode.E, KeyCode.Slash, KeyCode.Mouse1},
        };
        const string NETLABEL = "WASD/方向键 移动跳跃 · J 或鼠标左键 攻击 · K 或右键 技能";

        void Awake()
        {
            Application.runInBackground = true; // 房主切到后台也要继续跑
            Application.targetFrameRate = 120; QualitySettings.vSyncCount = 1;
            Art.Init(); Ui.Init();
            var camGo = new GameObject("Camera"); camGo.transform.SetParent(transform);
            cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.orthographicSize = H / 2f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            cam.nearClipPlane = .1f; cam.farClipPlane = 100; camGo.AddComponent<AudioListener>();
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (c != cam) c.enabled = false; // 关掉场景自带的相机
            sfx = gameObject.AddComponent<SfxPlayer>(); upd = gameObject.AddComponent<Updater>();
            var vgo = new GameObject("World"); vgo.transform.SetParent(transform); view = vgo.AddComponent<WorldView>(); view.Cam = cam; view.Audio = null;
            playerName = PlayerPrefs.GetString("name", "玩家" + UnityEngine.Random.Range(10, 99));
            myChar = (CharId)Mathf.Clamp(PlayerPrefs.GetInt("char", (int)CharId.CCLD), 0, Chars.Length - 1);
            sfx.Volume = PlayerPrefs.GetFloat("vol", .7f);
            ipInput = PlayerPrefs.GetString("ip", "");
            for (int i = 0; i < MAX_P; i++) localSlots[i] = new SlotConfig { Mode = i < 2 ? SlotMode.Human : (i < 4 ? SlotMode.Cpu : SlotMode.Off), Ch = Order[i % Order.Length] };
            NewAttract(); view.Init(attract);
            // 隐藏的自检模式：FourBrawl.exe -brawlshots <文件夹> [team] → 电脑自动打，定时截图后退出
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-brawlshots") shotDir = args[i + 1];
            if (shotDir != null)
            {
                Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
                for (int i = 0; i < MAX_P; i++) { localSlots[i].Mode = i < 4 ? SlotMode.Cpu : SlotMode.Off; localSlots[i].Ch = Order[(i * 2 + 1) % Order.Length]; }
                if (Array.IndexOf(args, "team") >= 0) localMode = GameMode.Team;
                StartLocal();
            }
        }
        string shotDir; float shotT; int shotN;
        void ShotTick(float dt)
        {
            if (shotDir == null || sim == null) return;
            shotT += dt;
            if (shotT > 2.5f) { shotT = 0; shotN++; ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(shotDir, "shot" + shotN.ToString("00") + ".png")); if (shotN >= 10) Application.Quit(); }
        }

        void NewAttract()
        {
            attract = new Sim(Environment.TickCount);
            int skip = UnityEngine.Random.Range(0, Chars.Length);
            for (int i = 0; i < MAX_P; i++) { attract.Slots[i].Mode = i < 4 ? SlotMode.Cpu : SlotMode.Off; attract.Slots[i].Ch = (CharId)((i + skip) % Chars.Length); }
            attract.StartMatch(); attract.Timer = 0;
            if (view != null) { view.Attach(attract); view.Audio = null; view.LocalSlot = -1; view.IsClient = false; }
        }

        // ================= 每帧 =================
        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
            host?.Pump(dt);
            if (client != null) { client.Pump(dt); client.Smooth(dt); ClientFrame(dt); }

            Sim stepper = null;
            if (scr == Scr.Game) { if (client == null && !(paused && net == null)) stepper = sim; }
            else stepper = attract;

            if (stepper != null)
            {
                if (stepper == sim) ReadLocalInputs();
                acc += dt; int n = 0;
                while (acc >= Sim.DT && n < 5)
                {
                    stepper.Step(); acc -= Sim.DT; n++;
                    if (stepper == sim && host != null) host.AfterStep();
                    stepper.FxOut.Clear();
                }
                if (n == 5) acc = 0;
                if (stepper == attract && (attract.State == GameState.MatchEnd || attract.RoundNo > 6)) NewAttract();
            }
            else acc = 0;
            if (host != null && stepper != sim) { netAcc += dt; while (netAcc >= Sim.DT) { host.AfterStep(); netAcc -= Sim.DT; } }

            // 快捷键
            if (scr == Scr.Game)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) paused = !paused;
                if (sim != null && client == null && sim.State == GameState.MatchEnd && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))) Rematch();
                if (paused && client == null && host == null && Input.GetKeyDown(KeyCode.M)) ToMenu();
            }
            else if (Input.GetKeyDown(KeyCode.Escape) && detailChar >= 0) detailChar = -1;
            else if (Input.GetKeyDown(KeyCode.Escape) && scr != Scr.Title && scr != Scr.Lobby) scr = Scr.Title;
            if (Input.GetKeyDown(KeyCode.F11) || (Input.GetKeyDown(KeyCode.Return) && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)))) Screen.fullScreen = !Screen.fullScreen;
            ShotTick(Mathf.Min(Time.unscaledDeltaTime, .1f));
        }

        static bool Down(KeyCode[] ks) { foreach (var k in ks) if (Input.GetKey(k)) return true; return false; }
        static bool Pressed(KeyCode[] ks) { foreach (var k in ks) if (Input.GetKeyDown(k)) return true; return false; }

        void ReadLocalInputs()
        {
            if (sim == null) return;
            bool editing = GUIUtility.keyboardControl != 0;
            for (int i = 0; i < MAX_P; i++)
            {
                if (sim.Slots[i].Mode != SlotMode.Human || editing) { if (sim.Slots[i].Mode == SlotMode.Human) sim.Inputs[i] = default; continue; }
                if (host != null) sim.Inputs[i] = new PInput { U = Down(NETKEYS[0]), D = Down(NETKEYS[1]), L = Down(NETKEYS[2]), R = Down(NETKEYS[3]), A = Down(NETKEYS[4]), S = Down(NETKEYS[5]) };
                else { var k = KEYMAP[i]; sim.Inputs[i] = new PInput { U = Down(k[0]), D = Down(k[1]), L = Down(k[2]), R = Down(k[3]), A = Down(k[4]), S = Down(k[5]) }; }
            }
        }

        void ClientFrame(float dt)
        {
            // 发送输入：点按会“粘住”几帧，保证房主一定收到
            byte bits = 0; bool editing = GUIUtility.keyboardControl != 0;
            if (!editing && scr == Scr.Game && !paused)
                for (int b = 0; b < 6; b++)
                {
                    if (Pressed(NETKEYS[b])) sticky[b] = 4;
                    bool on = Down(NETKEYS[b]) || sticky[b] > 0; if (sticky[b] > 0) sticky[b]--;
                    if (on) bits |= (byte)(b == 0 ? 4 : b == 1 ? 8 : b == 2 ? 1 : b == 3 ? 2 : b == 4 ? 16 : 32);
                }
            client.MyChar = myChar; client.MyName = playerName;
            client.SendInput(bits);
            if (client.Rejected != null) { status = "加入失败：" + client.Rejected; LeaveOnline(); scr = Scr.Title; return; }
            if (!client.GotSnap) { joinWait += dt; if (joinWait > 20) { status = "连不上房主（20 秒无响应）。检查房间码 / IP、防火墙后重试。"; LeaveOnline(); scr = Scr.Title; } return; }
            if (client.SinceSnap > 6) { status = "与房主断开了连接"; LeaveOnline(); scr = Scr.Title; return; }
            if (client.Started && scr == Scr.Lobby)
            {
                scr = Scr.Game; view.Attach(client.Mirror); view.Audio = sfx; view.IsClient = true; view.LocalSlot = client.YourSlot;
                view.TimeStopSlot = () => client.TimeStopSlot;
                view.ClientLatch = (int slot, out float x, out float y) => { if (client.Latches.TryGetValue(slot, out var l)) { x = l.x; y = l.y; return true; } x = y = 0; return false; };
            }
            else if (!client.Started && scr == Scr.Game) { scr = Scr.Lobby; NewAttract(); }
            if (scr == Scr.Game) view.LocalSlot = client.YourSlot;
        }

        // ================= 流程 =================
        void StartLocal()
        {
            sim = new Sim(Environment.TickCount);
            for (int i = 0; i < MAX_P; i++) { sim.Slots[i].Mode = localSlots[i].Mode; sim.Slots[i].Ch = localSlots[i].Ch; sim.Slots[i].Label = localSlots[i].Mode == SlotMode.Human ? "玩家" : "电脑"; }
            int n = 0; foreach (var s in localSlots) if (s.Mode != SlotMode.Off) n++;
            if (n < 2) { status = "至少要两个人（可以是电脑）"; return; }
            sim.Mode = localMode; sim.MapPick = localMap; sim.StartMatch(); EnterGame(sim, -1);
        }
        void EnterGame(Sim s, int localSlot)
        {
            scr = Scr.Game; paused = false; view.Attach(s); view.Audio = sfx; view.IsClient = false; view.LocalSlot = localSlot; view.TimeStopSlot = null; view.ClientLatch = null; acc = 0;
        }
        void Rematch()
        {
            if (host != null) host.StartMatch(); else sim.StartMatch();
            view.Attach(sim);
        }
        void ToMenu()
        {
            LeaveOnline(); sim = null; paused = false; scr = Scr.Title; NewAttract();
        }
        void LeaveOnline()
        {
            try { net?.Close(); } catch { }
            net = null; host = null; client = null; busy = false; joinWait = 0; roomCode = "";
            if (scr == Scr.Game || scr == Scr.Lobby) NewAttract();
        }
        void SavePrefs() { PlayerPrefs.SetString("name", playerName); PlayerPrefs.SetInt("char", (int)myChar); PlayerPrefs.SetString("ip", ipInput); PlayerPrefs.SetFloat("vol", sfx.Volume); PlayerPrefs.Save(); }

        void BeginHost(INetTransport t, string code)
        {
            net = t; roomCode = code; sim = new Sim(Environment.TickCount);
            host = new HostSession(sim, t, myChar, playerName);
            host.OnLog += s => status = s;
            scr = Scr.Lobby; status = "";
        }
        void BeginClient(INetTransport t, string code)
        {
            net = t; roomCode = code; client = new ClientSession(t, myChar, playerName); joinWait = 0; scr = Scr.Lobby; status = "正在加入…";
        }

        async void CreateRelay()
        {
            if (busy) return; busy = true; status = "正在向 Unity Relay 申请房间…"; SavePrefs();
            try { var t = await RelayTransport.CreateRoom(MAX_P - 1); BeginHost(t, t.JoinCode); }
            catch (Exception e) { Debug.LogException(e); status = RelayHelp(e); }
            busy = false;
        }
        async void JoinRelay()
        {
            if (busy) return; if (codeInput.Trim().Length < 4) { status = "请输入房主给你的房间码"; return; }
            busy = true; status = "正在加入房间 " + codeInput.Trim().ToUpperInvariant() + " …"; SavePrefs();
            try { var t = await RelayTransport.JoinRoom(codeInput); BeginClient(t, codeInput.Trim().ToUpperInvariant()); }
            catch (Exception e) { Debug.LogException(e); status = RelayHelp(e); }
            busy = false;
        }
        static string RelayHelp(Exception e)
        {
            string m = e.Message ?? "";
            for (var ie = e.InnerException; ie != null; ie = ie.InnerException) m += " | " + ie.Message;
            if (string.IsNullOrEmpty(Application.cloudProjectId) || m.Contains("couldn't be initialized"))
                return "房间码联机初始化失败，先用「联机 · 局域网 / IP」玩。原始错误：" + m;
            if (m.Contains("project") || m.Contains("Project") || m.Contains("cloud") || m.Contains("environment"))
                return "Unity 云服务没配好：请在编辑器 Edit > Project Settings > Services 里关联项目并在后台打开 Relay（见 说明.md）。原始错误：" + m;
            if (m.Contains("join code") || m.Contains("Not Found") || m.Contains("404")) return "房间码不对或房间已关闭：" + m;
            return "联机失败：" + m;
        }
        void HostLan()
        {
            SavePrefs();
            try { var t = UdpTransport.Host(); lanIps = UdpTransport.LocalAddresses(); BeginHost(t, string.Join("  /  ", lanIps)); }
            catch (Exception e) { status = "开房失败（端口 " + UdpTransport.DefaultPort + " 可能被占用）：" + e.Message; }
        }
        void JoinLan()
        {
            SavePrefs();
            if (ipInput.Trim() == "") { status = "请输入房主的 IP 地址"; return; }
            try { var t = UdpTransport.Join(ipInput); BeginClient(t, ipInput.Trim()); }
            catch (Exception e) { status = "地址无效：" + e.Message; }
        }

        void OnApplicationQuit() { try { net?.Close(); } catch { } }

        // ================= 界面 =================
        void OnGUI()
        {
            Ui.BeginScaled(); GUI.skin.settings.cursorColor = Color.white;
            UiFx.Screen((int)scr);
            if (scr != Scr.Game) UiFx.Backdrop(scr == Scr.Title); // 第十版：菜单背景（暗角 + 扫光 + 金色浮尘）
            switch (scr)
            {
                case Scr.Title: DrawTitle(); break;
                case Scr.Local: DrawLocal(); break;
                case Scr.Online: DrawOnline(); break;
                case Scr.Lan: DrawLan(); break;
                case Scr.Lobby: DrawLobby(); break;
                case Scr.Roster: DrawRoster(); break;
                case Scr.Rules: DrawRules(); break;
                case Scr.Settings: DrawSettings(); break;
                case Scr.Game: GodView.Gui((wx, wy) => Ui.ScreenToDesign(view.ToGui(wx, wy))); DrawHud(); break; // 第十版：神降/七日/居合的电影黑边与神谕大字
            }
            if (detailChar >= 0 && scr != Scr.Game) DrawCharDetail((CharId)detailChar);
            DrawUpdate();
            UiFx.EndFrame();
        }

        // ================= 自动更新提示 =================
        void DrawUpdate()
        {
            if (upd == null) return;
            if (upd.State == Updater.St.Ready)
            {
                bool idle = scr == Scr.Title || scr == Scr.Local || scr == Scr.Roster || scr == Scr.Rules || scr == Scr.Settings || scr == Scr.Online || scr == Scr.Lan;
                if (!idle) { updCountdown = -1; Ui.Text(new Rect(0, 694, 1280, 24), "新版本 v" + upd.Remote + " 已下载，回到主菜单就会自动更新", Ui.SmallC, new Color(.5f, 1, .6f), 1); return; }
                if (updCountdown < 0) updCountdown = 4;
                updCountdown -= Time.unscaledDeltaTime;
                Ui.Rect(new Rect(340, 250, 600, 200), new Color(.03f, .05f, .12f, .94f));
                Ui.Text(new Rect(340, 268, 600, 50), "发现新版本 v" + upd.Remote, Ui.Big, new Color(.5f, 1, .6f), 2);
                Ui.Text(new Rect(340, 324, 600, 30), Mathf.CeilToInt(Mathf.Max(0, updCountdown)) + " 秒后自动更新并重新打开游戏", Ui.LabelC, Color.white, 1);
                if (Ui.Btn(new Rect(400, 375, 220, 50), "现在更新", true) || updCountdown <= 0) { updCountdown = 999; upd.ApplyAndRestart(); }
                if (Ui.Btn(new Rect(660, 375, 220, 50), "稍后")) { upd.State = Updater.St.Idle; }
            }
            else if (scr == Scr.Title) Ui.Text(new Rect(900, 694, 370, 24), upd.Line(), Ui.Small, new Color(.6f, .64f, .8f), 1);
        }

        void Back(Action a = null)
        {
            if (Ui.Btn(new Rect(30, 660, 150, 44), "← 返回")) { if (a != null) a(); else scr = Scr.Title; status = ""; }
        }
        void StatusLine(float y = 610)
        {
            if (status != "") Ui.Text(new Rect(140, y, 1000, 50), status, Ui.SmallC, new Color(1, .85f, .5f), 1);
        }

        void DrawTitle()
        {
            // 第十版标题：发光 Logo + 玻璃菜单面板 + 右侧轮播的角色展示卡
            UiFx.Logo(new Vector2(640, 128), 640);
            Ui.Text(new Rect(0, 214, 1280, 34), "横版平台格斗 · 可破坏地形 · 最多 10 人联机 · 团队 2v2 ~ 5v5", Ui.LabelC, new Color(.85f, .88f, 1), 2);
            float x = 490, y = 270, w = 300, h = 54, g = 66;
            GUI.Box(new Rect(x - 26, y - 22, w + 52, g * 4 + h + 44), "", Ui.Panel);
            if (Ui.Btn(new Rect(x, y, w, h), "本地对战", true)) { scr = Scr.Local; status = ""; }
            if (Ui.Btn(new Rect(x, y + g, w, h), "联机 · 房间码")) { scr = Scr.Online; status = ""; }
            if (Ui.Btn(new Rect(x, y + g * 2, w, h), "联机 · 局域网 / IP")) { scr = Scr.Lan; status = ""; }
            if (Ui.Btn(new Rect(x, y + g * 3, w / 2 - 6, h), "角色图鉴")) scr = Scr.Roster;
            if (Ui.Btn(new Rect(x + w / 2 + 6, y + g * 3, w / 2 - 6, h), "玩法说明")) scr = Scr.Rules;
            if (Ui.Btn(new Rect(x, y + g * 4, w / 2 - 6, h), "设置")) scr = Scr.Settings;
            if (Ui.Btn(new Rect(x + w / 2 + 6, y + g * 4, w / 2 - 6, h), "退出")) Application.Quit();
            // 右侧：每 4 秒换一个角色的展示卡（点一下看详情）
            int idx = (int)(Time.unscaledTime / 4f) % Order.Length; var cid = Order[idx]; var cd = Char(cid);
            var card = new Rect(912, 300, 250, 250);
            UiFx.CardGlow(card, Art.C(cd.Color), true);
            GUI.Box(card, "", Ui.Panel);
            UiFx.Accent(new Rect(card.x + 12, card.y + 8, card.width - 24, 4), Art.C(cd.Color));
            Ui.Text(new Rect(card.x, card.y + 14, card.width, 24), "角色展示", Ui.SmallC, new Color(.75f, .8f, .95f), 1);
            DrawCharIcon(new Rect(card.center.x - 60, card.y + 42, 120, 120), cid, true);
            if (IsRemix(cid)) RemixBadge(new Rect(card.xMax - 36, card.y + 16, 24, 24));
            Ui.Text(new Rect(card.x, card.y + 166, card.width, 44), cd.Name, Ui.Big, Art.C(cd.Color), 2);
            Ui.Text(new Rect(card.x + 8, card.y + 210, card.width - 16, 30), cd.Role, Ui.SmallC, Color.white, 1);
            if (GUI.Button(card, "", GUIStyle.none)) detailChar = (int)cid;
            StatusLine(620);
            Ui.Text(new Rect(0, 680, 1280, 30), "F11 全屏 · Esc 返回/暂停", Ui.SmallC, new Color(.6f, .64f, .8f), 1);
        }

        /// <summary>选人：第一排原版 + px，第二排「-改」版排在原版正下方。</summary>
        void CharPicker(float x, float y, ref CharId ch, float size = 72)
        {
            int row0 = 0; foreach (var id in Order) if (!IsRemix(id)) row0++;
            for (int k = 0; k < Order.Length; k++)
            {
                var id = Order[k]; int col = k < row0 ? k : k - row0, row = k < row0 ? 0 : 1;
                var r = new Rect(x + col * (size + 10), y + row * (size + 30), size, size + 26); bool sel = ch == id;
                if (GUI.Button(r, "", sel ? Ui.ButtonSel : Ui.Button)) ch = id;
                DrawCharIcon(new Rect(r.x + 10, r.y + 5, size - 20, size - 20), id, sel);
                var c = Char(id);
                Ui.Text(new Rect(r.x - 4, r.y + size - 20, size + 8, 30), c.Name, Ui.SmallC, Art.C(c.Color), 2);
                if (IsRemix(id)) RemixBadge(new Rect(r.x + size - 24, r.y + 2, 22, 22));
            }
        }
        void RemixBadge(Rect r)
        {
            Ui.Rect(r, new Color(.9f, .25f, .3f, .95f));
            Ui.Text(new Rect(r.x - 4, r.y - 3, r.width + 8, r.height + 6), "改", Ui.SmallC, Color.white, 1);
        }
        void DrawCharIcon(Rect r, CharId c, bool sel = false, bool animate = true) => ViewPolish.CharIcon(r, c, sel, animate);

        void NameAndChar(float y)
        {
            Ui.Text(new Rect(240, y, 200, 44), "你的名字", Ui.Label, Color.white, 1);
            playerName = GUI.TextField(new Rect(360, y, 300, 44), playerName, 12, Ui.Field);
            Ui.Text(new Rect(240, y + 64, 200, 40), "选择角色", Ui.Label, Color.white, 1);
            CharPicker(360, y + 60, ref myChar);
        }

        const float CW = 232, CH = 166, CSTEP = 246, CX0 = 33, RSTEP = 176; // 10 张座位卡：两排各 5 张
        static readonly string[] KEYSHORT = { "WASD · Q攻 E技", "TFGH · R攻 Y技", "IJKL · U攻 O技", "方向键 · .攻 /技", "小键盘8456 · 7攻 9技" };
        Rect SeatRect(int i, float y0) => new Rect(CX0 + (i % 5) * CSTEP, y0 + (i / 5) * RSTEP, CW, CH);
        void DrawLocal()
        {
            UiFx.Heading("本地对战（同一台电脑，最多 10 人；P6 起只能是电脑）", 20, 60);
            for (int i = 0; i < MAX_P; i++)
            {
                var s = localSlots[i]; var r = SeatRect(i, 86); float x = r.x, y = r.y;
                GUI.Box(r, "", Ui.Panel);
                var sc = localMode == GameMode.Team ? Art.C(TeamColors[i % 2]) : Art.C(SlotColors[i]);
                UiFx.Accent(new Rect(x + 10, y + 6, CW - 20, 4), sc);
                Ui.Text(new Rect(x + 10, y + 12, 110, 30), "P" + (i + 1) + (localMode == GameMode.Team ? " · " + TeamNames[i % 2] : ""), Ui.Label, sc, 1.5f);
                bool canHuman = i < KEYMAP.Length;
                if (!canHuman && s.Mode == SlotMode.Human) s.Mode = SlotMode.Cpu;
                string mode = s.Mode == SlotMode.Human ? "玩家" : s.Mode == SlotMode.Cpu ? "电脑" : "关闭";
                if (Ui.Btn(new Rect(x + CW - 104, y + 12, 94, 30), mode))
                    s.Mode = s.Mode == SlotMode.Human ? SlotMode.Cpu : s.Mode == SlotMode.Cpu ? SlotMode.Off : (canHuman ? SlotMode.Human : SlotMode.Cpu);
                if (s.Mode == SlotMode.Off) { Ui.Text(new Rect(x, y + 76, CW, 30), "（关闭）", Ui.SmallC, new Color(.5f, .55f, .7f), 1); continue; }
                var ir = new Rect(x + (CW - 76) / 2, y + 46, 76, 76);
                DrawCharIcon(ir, s.Ch);
                if (GUI.Button(ir, "", GUIStyle.none)) detailChar = (int)s.Ch; // 点头像看详情
                if (Ui.Btn(new Rect(x + 8, y + 64, 38, 42), "◀")) s.Ch = (CharId)(((int)s.Ch + Chars.Length - 1) % Chars.Length);
                if (Ui.Btn(new Rect(x + CW - 46, y + 64, 38, 42), "▶")) s.Ch = (CharId)(((int)s.Ch + 1) % Chars.Length);
                var c = Char(s.Ch);
                Ui.Text(new Rect(x, y + 120, CW, 28), c.Name, Ui.LabelC, Art.C(c.Color), 2);
                if (s.Mode == SlotMode.Human) Ui.Text(new Rect(x + 4, y + 142, CW - 8, 22), KEYSHORT[i], Ui.SmallC, new Color(1, .9f, .6f), 1);
                else Ui.Text(new Rect(x + 4, y + 142, CW - 8, 22), c.Role, Ui.SmallC, new Color(.7f, .75f, .9f), 1);
            }
            Ui.Text(new Rect(0, 444, 1280, 26), "点头像看角色详情 · 操作：" + string.Join("　", KEYLABEL, 0, 2) + " …", Ui.SmallC, new Color(.7f, .75f, .9f), 1);
            ModeToggle(ref localMode, 110, 556);
            MapPicker(ref localMap, 610, 556);
            if (Ui.Btn(new Rect(1030, 550, 220, 60), "开 战 !", true)) StartLocal();
            if (localMode == GameMode.Team) Ui.Text(new Rect(0, 612, 1280, 26), TeamHint(), Ui.SmallC, new Color(1, .9f, .6f), 1);
            StatusLine(632); Back();
        }

        void DrawOnline()
        {
            UiFx.Heading("联机 · 房间码（Unity Relay，免费额度）", 24, 60);
            Ui.Text(new Rect(140, 92, 1000, 50), "朋友各自在自己电脑上打开游戏。一人创建房间，把房间码发给大家，其他人输入房间码加入。不用开端口。", Ui.SmallC, new Color(.75f, .8f, .95f), 1);
            NameAndChar(150);
            GUI.enabled = !busy;
            if (Ui.Btn(new Rect(360, 430, 240, 56), "创建房间", true)) CreateRelay();
            Ui.Text(new Rect(640, 412, 300, 24), "房间码", Ui.Small, Color.white, 1);
            codeInput = GUI.TextField(new Rect(640, 436, 200, 50), codeInput.ToUpperInvariant(), 8, Ui.Field);
            if (Ui.Btn(new Rect(850, 436, 140, 50), "加入")) JoinRelay();
            GUI.enabled = true;
            StatusLine(520); Back();
        }

        void DrawLan()
        {
            UiFx.Heading("联机 · 局域网 / 直连 IP（完全免费）", 24, 60);
            Ui.Text(new Rect(140, 92, 1000, 50), "同一个 Wi-Fi 直接用；不在一起的话，大家装同一个免费虚拟局域网（ZeroTier / Radmin VPN / Tailscale）后用它给的 IP。", Ui.SmallC, new Color(.75f, .8f, .95f), 1);
            NameAndChar(150);
            if (Ui.Btn(new Rect(360, 430, 240, 56), "开局域网房间", true)) HostLan();
            Ui.Text(new Rect(640, 412, 300, 24), "房主 IP（可带 :端口）", Ui.Small, Color.white, 1);
            ipInput = GUI.TextField(new Rect(640, 436, 260, 50), ipInput, 40, Ui.Field);
            if (Ui.Btn(new Rect(910, 436, 120, 50), "加入")) JoinLan();
            StatusLine(520); Back();
        }

        void DrawLobby()
        {
            bool isHost = host != null; Seat[] seats = isHost ? host.Seats : client?.Seats;
            if (seats == null) { scr = Scr.Title; return; }
            UiFx.Heading(isHost ? "你的房间" : "已加入房间", 20, 60);
            bool relay = net is RelayTransport;
            string codeLine = relay ? "房间码：" + roomCode : (isHost ? "让朋友输入你的 IP：" + roomCode + (UdpTransport.DefaultPort != 27777 ? "" : "") : "房主：" + roomCode);
            Ui.Text(new Rect(0, 82, 1280, 50), codeLine, relay ? Ui.Big : Ui.LabelC, new Color(1, .86f, .35f), 2);
            if (isHost && Ui.Btn(new Rect(990, 86, 120, 40), "复制")) { GUIUtility.systemCopyBuffer = relay ? roomCode : (lanIps != null && lanIps.Count > 0 ? lanIps[0] : roomCode); status = "已复制，发给朋友吧"; }
            int maxP = isHost ? host.MaxPlayers : client.MaxPlayers;
            if (isHost && !host.Started)
            {
                Ui.Text(new Rect(30, 132, 140, 34), "房间人数", Ui.Label, Color.white, 1);
                for (int n = 2; n <= MAX_P; n++) // 2~10 人
                {
                    bool ok = n >= host.MinAllowedPlayers;
                    GUI.enabled = ok;
                    if (Ui.Btn(new Rect(150 + (n - 2) * 50, 128, 44, 38), n.ToString(), n == host.MaxPlayers)) host.SetMaxPlayers(n);
                    GUI.enabled = true;
                }
                Ui.Text(new Rect(560, 132, 700, 34), "（含房主。空位开局由电脑补，点空位可改成关闭）", Ui.Small, new Color(.7f, .75f, .9f), 1);
            }
            else Ui.Text(new Rect(30, 132, 400, 34), "房间人数 " + maxP, Ui.Label, Color.white, 1);
            for (int i = 0; i < MAX_P; i++)
            {
                var s = seats[i]; var rr = SeatRect(i, 172); float x = rr.x, y = rr.y; GUI.Box(rr, "", Ui.Panel);
                if (i >= maxP) { Ui.Text(new Rect(x, y + 66, CW, 30), "未开放", Ui.LabelC, new Color(.5f, .55f, .7f), 1); continue; }
                GameMode lm = isHost ? host.Mode : client.LobbyMode; var sc = lm == GameMode.Team ? Art.C(TeamColors[i % 2]) : Art.C(SlotColors[i]);
                UiFx.Accent(new Rect(x + 10, y + 6, CW - 20, 4), sc);
                Ui.Text(new Rect(x + 10, y + 12, 90, 30), "P" + (i + 1) + (lm == GameMode.Team ? " · " + TeamNames[i % 2] : ""), Ui.Label, sc, 1.5f);
                bool mine = (isHost && i == 0) || (!isHost && client.YourSlot == i);
                string who = s.Kind == SeatKind.Host ? "房主 " + s.Label : s.Kind == SeatKind.Peer ? (s.Label == "" ? "朋友" : s.Label) : s.Kind == SeatKind.Open ? "空位" : s.Kind == SeatKind.Cpu ? "电脑" : "关闭";
                if (mine) who += "（你）";
                if (isHost && (s.Kind == SeatKind.Open || s.Kind == SeatKind.Cpu || s.Kind == SeatKind.Off))
                { if (Ui.Btn(new Rect(x + CW - 124, y + 12, 114, 30), who)) host.CycleSeat(i); }
                else Ui.Text(new Rect(x + CW - 134, y + 12, 128, 30), who, Ui.SmallC, Color.white, 1);
                if (s.Kind != SeatKind.Off)
                {
                    var ir = new Rect(x + (CW - 76) / 2, y + 46, 76, 76);
                    DrawCharIcon(ir, s.Ch, mine);
                    if (GUI.Button(ir, "", GUIStyle.none)) detailChar = (int)s.Ch;
                    var c = Char(s.Ch); Ui.Text(new Rect(x, y + 120, CW, 28), c.Name, Ui.LabelC, Art.C(c.Color), 2);
                    Ui.Text(new Rect(x + 4, y + 142, CW - 8, 22), c.Role, Ui.SmallC, new Color(.7f, .75f, .9f), 1);
                    bool canChange = isHost && (s.Kind != SeatKind.Peer);
                    if (canChange)
                    {
                        if (Ui.Btn(new Rect(x + 8, y + 64, 38, 42), "◀")) { host.CycleSeatChar(i, -1); if (i == 0) myChar = host.Seats[0].Ch; }
                        if (Ui.Btn(new Rect(x + CW - 46, y + 64, 38, 42), "▶")) { host.CycleSeatChar(i, 1); if (i == 0) myChar = host.Seats[0].Ch; }
                    }
                    else if (mine && !isHost)
                    {
                        if (Ui.Btn(new Rect(x + 8, y + 64, 38, 42), "◀")) myChar = (CharId)(((int)myChar + Chars.Length - 1) % Chars.Length);
                        if (Ui.Btn(new Rect(x + CW - 46, y + 64, 38, 42), "▶")) myChar = (CharId)(((int)myChar + 1) % Chars.Length);
                    }
                }
            }
            if (isHost && !host.Started) { ModeToggle(ref host.Mode, 110, 528); MapPicker(ref host.MapPick, 610, 528); }
            else
            {
                var lm2 = isHost ? host.Mode : client.LobbyMode; int lmap = isHost ? host.MapPick : client.LobbyMap;
                Ui.Text(new Rect(0, 532, 1280, 40), "模式：" + ModeName(lm2) + "　　地图：" + MapName(lmap), Ui.LabelC, new Color(1, .86f, .35f), 1);
            }
            if ((isHost ? host.Mode : client.LobbyMode) == GameMode.Team) Ui.Text(new Rect(0, 576, 1280, 24), TeamHint(), Ui.SmallC, new Color(1, .9f, .6f), 1);
            Ui.Text(new Rect(0, 684, 1280, 26), "操作：" + NETLABEL, Ui.SmallC, new Color(1, .9f, .6f), 1);
            if (isHost)
            {
                if (Ui.Btn(new Rect(540, 600, 200, 50), host.Started ? "回到对局" : "开 战 !", true)) { if (!host.Started) host.StartMatch(); EnterGame(sim, 0); }
                Ui.Text(new Rect(0, 652, 1280, 26), $"已加入朋友 {host.PeerCount} 人 · 开局后也能中途加入（接管电脑位）", Ui.SmallC, new Color(.7f, .75f, .9f), 1);
            }
            else
            {
                string st = client.GotSnap ? "等待房主开局…" : (net != null ? net.Status : "") + "（" + Mathf.CeilToInt(20 - joinWait) + "）";
                Ui.Text(new Rect(0, 604, 1280, 40), st, Ui.LabelC, new Color(.8f, .85f, 1), 1);
            }
            StatusLine(660);
            Back(() => { LeaveOnline(); scr = Scr.Title; });
        }

        /// <summary>技能充能进度（0~1）；ready = 现在按技能键就能放。</summary>
        static float SkillPct(Sim s, Player p, out bool ready)
        {
            float k = p.SkPctNet >= 0 ? p.SkPctNet : Mathf.Clamp01(1 - p.SkCd / p.C.SkCd);
            if (p.Ch == CharId.CCLD && !p.God) k = Mathf.Clamp01(p.Energy / 100f);       // 成神前看神力
            else if (p.Ch == CharId.DVD) k = Mathf.Min(k, Mathf.Clamp01(p.Stk / (float)DVD_ULT)); // 要攒够层数
            if (p.Ch == CharId.CCLD2 && !p.Divine && p.Energy >= 100) k = 1; // 神力满：按技能就降临
            ready = k >= .995f;
            if (p.Ch == CharId.DZY && ready) { bool any = false; foreach (var tc in s.Tentacles) if (tc.Owner == p) any = true; ready = any; if (!any) k = Mathf.Min(k, .9f); } // 场上要有触手
            if (p.Ch == CharId.DVD && p.CloneT > 0) { ready = false; }
            return k;
        }
        static string ModeName(GameMode m) => m == GameMode.Crown ? "王冠争夺" : m == GameMode.Team ? "团队对战" : "经典淘汰";
        static string MapName(int i) => i >= 0 && i < Maps.All.Length ? Maps.All[i].Name : "随机轮换";
        static string TeamHint() => "团队对战：单号 P1 / P3 / P5… 是红队，双号 P2 / P4 / P6… 是蓝队（4 人 = 2v2，10 人 = 5v5）。队友打不到队友，最后剩下的队伍赢。";
        void ModeToggle(ref GameMode m, float x, float y)
        {
            Ui.Text(new Rect(x - 70, y + 8, 70, 40), "模式", Ui.Label, Color.white, 1);
            if (Ui.Btn(new Rect(x, y, 124, 46), "经典淘汰", m == GameMode.Classic)) m = GameMode.Classic;
            if (Ui.Btn(new Rect(x + 130, y, 124, 46), "团队对战", m == GameMode.Team)) m = GameMode.Team;
            if (Ui.Btn(new Rect(x + 260, y, 124, 46), "王冠争夺", m == GameMode.Crown)) m = GameMode.Crown;
        }
        void MapPicker(ref int map, float x, float y)
        {
            Ui.Text(new Rect(x - 70, y + 8, 70, 40), "地图", Ui.Label, Color.white, 1);
            if (Ui.Btn(new Rect(x, y, 44, 46), "◀")) map = map <= -1 ? Maps.All.Length - 1 : map - 1;
            Ui.Rect(new Rect(x + 50, y, 240, 46), new Color(.05f, .06f, .14f, .8f));
            if (map >= 0) { var m = Maps.All[map]; Ui.Rect(new Rect(x + 50, y, 240, 4), Art.C(m.Grass)); }
            Ui.Text(new Rect(x + 50, y + 6, 240, 36), MapName(map), Ui.LabelC, map >= 0 ? Art.C(Maps.All[map].GrassLight) : Color.white, 1);
            if (Ui.Btn(new Rect(x + 296, y, 44, 46), "▶")) map = map >= Maps.All.Length - 1 ? -1 : map + 1;
        }

        void DrawCharDetail(CharId id)
        {
            var c = Char(id); var det = CharText.Of(id);
            Ui.Rect(new Rect(-400, -200, 2080, 1120), new Color(0, 0, 0, .65f));
            GUI.Box(new Rect(120, 24, 1040, 672), "", Ui.Panel);
            UiFx.Accent(new Rect(140, 32, 1000, 4), Art.C(c.Color));
            UiFx.DetailBackdrop(id, new Rect(150, 58, 110, 110));
            DrawCharIcon(new Rect(150, 58, 110, 110), id);
            if (IsRemix(id)) RemixBadge(new Rect(238, 56, 24, 24));
            Ui.Text(new Rect(280, 56, 420, 50), c.Name, Ui.Title, Art.C(c.Color), 3);
            Ui.Text(new Rect(280, 106, 420, 30), c.Role, Ui.Label, Color.white, 1);
            Ui.Text(new Rect(280, 138, 440, 24), $"生命 {Mathf.RoundToInt(c.Hp * HP_MULT)}　移速 {Mathf.RoundToInt(c.Speed)}　攻击间隔 {c.AtkCd:0.##} 秒　技能冷却 {c.SkCd:0.#} 秒", Ui.Small, new Color(1, .9f, .6f), 0);
            for (int k = 0; k < c.Stats.Length; k++)
            {
                float sy = 58 + k * 26;
                Ui.Text(new Rect(740, sy, 70, 24), c.StatNames[k], Ui.Small, new Color(.8f, .84f, 1), 0);
                Ui.Bar(new Rect(800, sy + 7, 280, 10), c.Stats[k] / 100f, Art.C(c.Color), new Color(.1f, .1f, .2f));
                Ui.Text(new Rect(1090, sy, 50, 24), c.Stats[k].ToString(), Ui.Small, Color.white, 0);
            }
            string[] heads = CharText.Heads, body = det ?? new[] { c.Atk, "同普攻。", c.Sk, "同技能。", "" };
            float y = 180, x0 = 150, tw = 980;
            var st = Ui.Small; bool wrap = st.wordWrap; st.wordWrap = true; st.richText = true;
            for (int k = 0; k < heads.Length; k++)
            {
                if (string.IsNullOrEmpty(body[k])) continue;
                string tx = body[k].Replace("【新】", "<color=#7dffb0>【新】</color>");
                float h = Mathf.Max(22, st.CalcHeight(new GUIContent(tx), tw - 90));
                Ui.Rect(new Rect(x0, y + 2, 76, 20), new Color(1, .85f, .3f, .18f));
                Ui.Text(new Rect(x0, y, 76, 24), heads[k], Ui.SmallC, new Color(1, .86f, .3f), 1);
                Ui.Text(new Rect(x0 + 88, y, tw - 90, h + 4), tx, st, Color.white, 0);
                y += h + 10;
            }
            st.wordWrap = wrap;
            Ui.Text(new Rect(140, 612, 1000, 22), "数字为基础值；实战中伤害、生命还会按全局倍率放大。", Ui.SmallC, new Color(.6f, .65f, .8f), 1);
            if (Ui.Btn(new Rect(540, 638, 200, 44), "关闭")) detailChar = -1;
        }

        void DrawRules()
        {
            UiFx.Heading("玩法说明", 16, 60);
            GUI.Box(new Rect(40, 80, 590, 560), "", Ui.Panel); GUI.Box(new Rect(650, 80, 590, 560), "", Ui.Panel);
            string left =
                "<b><color=#ffd84a>经典淘汰</color></b>\n每局活到最后的人赢，先赢 3 局夺冠。被淘汰的人变成<b>幽灵</b>：可以飘来飘去，攻击键吹气把活人往外推（不掉血）。\n\n" +
                "<b><color=#ffd84a>团队对战</color></b>\n单号 P1/P3/P5… 红队对双号 P2/P4/P6… 蓝队（4 人 2v2，最多 10 人 5v5）。队友互相打不到，子弹会穿过队友，最后剩下的队伍全员记一局。\n\n" +
                "<b><color=#ffd84a>王冠争夺</color></b>\n抢到王冠的人每秒 +1 分，先到 20 分赢本局。戴冠者移速 -10%、头顶有光柱；累计挨 25 伤害或被击杀就掉冠。死了 3 秒后复活，复活有 1.5 秒无敌。岩浆 60 秒才涨，而且涨不满。\n\n" +
                "<b><color=#ffd84a>悬赏</color></b>\n赢局最多的人头顶有小王冠。谁击倒他，立刻回 30 血并狂暴 5 秒。\n\n" +
                "<b><color=#ffd84a>随机事件（第 25 秒）</color></b>\n低重力 · 流星雨 · 道具雨 · 地震，持续 10 秒。\n\n" +
                "<b><color=#ffd84a>岩浆</color></b>\n第 45 秒起从底部上涨，逼大家打起来。第 120 秒进入<b>突然死亡</b>：岩浆一直涨到顶，一定分出胜负。\n\n<b><color=#fff2a8>终极球</color></b>\n每局第 30 秒，空中会飘出一颗彩虹终极球。谁先摸到，下一次按技能就放全屏终极爆发（大范围重伤 + 击飞 + 眩晕）。";
            string right = "<b><color=#ffd84a>道具</color></b>\n";
            for (int i = 0; i < Items.Names.Length; i++) right += $"<color=#{Items.Colors[i]:x6}>■</color> <b>{Items.Names[i]}</b>：{Items.Desc[i]}\n";
            right += "\n<b><color=#ffd84a>地图</color></b>\n浮空岛 · 双塔对峙 · 金字塔：经典地形\n冰川裂谷：冰面很滑，天上会掉冰锥（先抖再落）\n风暴之巅：每 12 秒刮 3 秒强风，躲在柱子背风面\n传送门工厂：人、子弹、道具都能穿门\n开局前可以选固定地图，选「随机轮换」则每局换一张。";
            Ui.Text(new Rect(64, 100, 545, 530), left, Ui.Label, Color.white, 0);
            Ui.Text(new Rect(674, 100, 545, 530), right, Ui.Small, Color.white, 0);
            Back();
        }

        void DrawRoster()
        {
            UiFx.Heading("角色图鉴", 16, 56);
            Ui.Text(new Rect(0, 62, 1280, 24), "第二排是「-改」版，在原版正下方 · 点卡片看完整技能说明", Ui.SmallC, new Color(.75f, .8f, .95f), 1);
            int row0 = 0; foreach (var id in Order) if (!IsRemix(id)) row0++;
            float w = (1200 - (row0 - 1) * 10f) / row0, h = 270;
            for (int k = 0; k < Order.Length; k++)
            {
                var c = Char(Order[k]); int col = k < row0 ? k : k - row0, row = k < row0 ? 0 : 1;
                float x = 40 + col * (w + 10), y = 92 + row * (h + 12);
                UiFx.CardGlow(new Rect(x, y, w, h), Art.C(c.Color));
                GUI.Box(new Rect(x, y, w, h), "", Ui.Panel);
                UiFx.Accent(new Rect(x + 10, y + 8, w - 20, 4), Art.C(c.Color));
                DrawCharIcon(new Rect(x + w / 2 - 38, y + 18, 76, 76), c.Id);
                if (IsRemix(c.Id)) RemixBadge(new Rect(x + w - 32, y + 16, 24, 24));
                Ui.Text(new Rect(x, y + 96, w, 36), c.Name, Ui.Big, Art.C(c.Color), 2);
                Ui.Text(new Rect(x + 6, y + 134, w - 12, 40), c.Role, Ui.SmallC, Color.white, 1);
                for (int i = 0; i < c.Stats.Length; i++)
                {
                    Ui.Text(new Rect(x + 8, y + 176 + i * 22, 50, 22), c.StatNames[i], Ui.Small, new Color(.8f, .84f, 1), 0);
                    Ui.Bar(new Rect(x + 50, y + 183 + i * 22, w - 88, 9), c.Stats[i] / 100f, Art.C(c.Color), new Color(.1f, .1f, .2f));
                    Ui.Text(new Rect(x + w - 34, y + 176 + i * 22, 32, 22), c.Stats[i].ToString(), Ui.Small, Color.white, 0);
                }
                if (GUI.Button(new Rect(x, y, w, h), "", GUIStyle.none)) detailChar = (int)c.Id;
            }
            Back();
        }

        void DrawSettings()
        {
            UiFx.Heading("设置", 60, 60);
            GUI.Box(new Rect(390, 160, 500, 300), "", Ui.Panel);
            Ui.Text(new Rect(430, 190, 200, 40), "音量", Ui.Label, Color.white, 1);
            sfx.Volume = GUI.HorizontalSlider(new Rect(540, 202, 300, 20), sfx.Volume, 0, 1);
            if (Ui.Btn(new Rect(430, 260, 420, 50), Screen.fullScreen ? "切换到窗口" : "切换到全屏")) Screen.fullScreen = !Screen.fullScreen;
            Ui.Text(new Rect(430, 330, 420, 100), "联机时每台电脑只有一个人玩，操作统一为：" + NETLABEL, Ui.Small, new Color(.8f, .84f, 1), 0);
            Back(() => { SavePrefs(); scr = Scr.Title; });
        }

        // ================= 对局 HUD =================
        void DrawHud()
        {
            Sim s = client != null ? client.Mirror : sim; if (s == null) return;
            // 飘字（伤害数字等）
            foreach (var p in view.Popups)
            {
                if (GodView.HidesPopup(p.Text)) continue; // 第十一版：这些技能名改由 GodView 的头顶小字卡显示
                var g = Ui.ScreenToDesign(view.ToGui(p.X, p.Y)); float a = Mathf.Clamp01(p.Life / .4f);
                var st = p.Big ? Ui.Big : Ui.LabelC; int keep = st.fontSize; st.fontSize = p.Big ? 30 : 18;
                float pop = p.Big ? 1 + Mathf.Clamp01((p.Life - p.Max + .15f) / .15f) * .4f : 1;
                st.fontSize = (int)(st.fontSize * pop);
                var c = p.C; c.a = a; Ui.Text(new Rect(g.x - 200, g.y - 20, 400, 40), p.Text, st, c, 2, new Color(.02f, .02f, .06f, a));
                st.fontSize = keep;
            }
            // 名字牌（分身也挂同样的牌子）
            // 名字牌：彩色底条 + 编号 + 名字 + 小血条，团队模式用队伍颜色，一眼分清谁是谁
            Action<Player, float, float, float> Tag = (p, x, y, lift) =>
            {
                var g = Ui.ScreenToDesign(view.ToGui(x + p.W / 2, y - (p.God ? 62 : 34))); g.y -= lift;
                float lbp = GodView.LetterboxPx; // 第十版：电影黑边期间，压到黑边里的名字牌 / 脚下血条不画
                if (lbp > 0) { var fy = Ui.ScreenToDesign(view.ToGui(x + p.W / 2, y + p.H + 6)).y; if (g.y - 16 < lbp || fy + 14 > 720 - lbp) return; }
                bool me = view.LocalSlot == p.Slot && !(s.Clones.Count > 0 && client == null && host == null);
                string label = (me ? "你 · " : "P" + (p.Slot + 1) + " ") + p.C.Name;
                var tc = s.Mode == GameMode.Team ? Art.C(TeamColors[p.Slot % 2]) : Art.C(SlotColors[p.Slot]);
                float wTag = 34 + label.Length * 13;
                Ui.Rect(new Rect(g.x - wTag / 2, g.y - 14, wTag, 22), new Color(tc.r * .25f, tc.g * .25f, tc.b * .25f, .78f));
                Ui.Rect(new Rect(g.x - wTag / 2, g.y - 14, 4, 22), tc);
                if (me) { Ui.Rect(new Rect(g.x - wTag / 2 - 2, g.y - 16, wTag + 4, 2), Color.white); Ui.Rect(new Rect(g.x - wTag / 2 - 2, g.y + 8, wTag + 4, 2), Color.white); }
                Ui.Text(new Rect(g.x - 90, g.y - 15, 180, 24), label, Ui.SmallC, Color.Lerp(tc, Color.white, .35f), 1.5f);
                // 脚下：血条 + 技能条（技能好了会发金光闪烁，并显示 ★）
                var f = Ui.ScreenToDesign(view.ToGui(x + p.W / 2, y + p.H + 6));
                float hk = p.MaxHp > 0 ? Mathf.Clamp01(p.Hp / p.MaxHp) : 0;
                const float BW = 58;
                Ui.Rect(new Rect(f.x - BW / 2 - 1, f.y - 1, BW + 2, 13), new Color(0, 0, 0, .75f));
                Ui.Rect(new Rect(f.x - BW / 2, f.y, BW * hk, 6), hk > .5f ? new Color(.35f, 1, .45f) : hk > .25f ? new Color(1, .85f, .25f) : new Color(1, .3f, .3f));
                bool ready; float sk = SkillPct(s, p, out ready);
                var skc = ready ? Color.Lerp(new Color(1, .82f, .2f), Color.white, (.5f + .5f * Mathf.Sin(Time.unscaledTime * 3.2f))) : new Color(.45f, .7f, 1, .9f);
                Ui.Rect(new Rect(f.x - BW / 2, f.y + 7, BW * sk, 4), skc);
                if (ready)
                {
                    Ui.Rect(new Rect(f.x - BW / 2 - 2, f.y - 3, BW + 4, 17), new Color(1, .85f, .3f, .18f + .14f * (.5f + .5f * Mathf.Sin(Time.unscaledTime * 3.2f))));
                    Ui.Text(new Rect(f.x + BW / 2 + 1, f.y - 7, 24, 24), "★", Ui.Small, skc, 1.5f);
                }
            };
            // 人挤在一起时名字牌往上错开，不叠在一起
            tagList.Clear();
            foreach (var p in s.Players) if (p.Alive && !p.Hidden) tagList.Add((p, p.X, p.Y));
            foreach (var c in s.Clones) if (c.Owner != null && c.Owner.Alive && c.Kind != 3) tagList.Add((c.Owner, c.X, c.Y));
            tagPos.Clear();
            foreach (var e in tagList)
            {
                var g = Ui.ScreenToDesign(view.ToGui(e.x + e.p.W / 2, e.y - (e.p.God ? 62 : 34))); float w = 34 + ((view.LocalSlot == e.p.Slot ? 4 : 3) + e.p.C.Name.Length) * 13;
                tagPos.Add(new Vector3(g.x, g.y, w));
            }
            var lifts = new float[tagList.Count]; var order = new List<int>(); for (int i = 0; i < tagList.Count; i++) order.Add(i);
            order.Sort((i, j) => tagPos[j].y.CompareTo(tagPos[i].y)); // 从下往上排
            for (int oi = 0; oi < order.Count; oi++)
            {
                int i = order[oi]; float y = tagPos[i].y;
                for (int guard = 0; guard < 10; guard++)
                {
                    bool hit = false;
                    for (int oj = 0; oj < oi; oj++) { int j = order[oj]; if (Mathf.Abs(tagPos[i].x - tagPos[j].x) < (tagPos[i].z + tagPos[j].z) / 2 && Mathf.Abs(y - (tagPos[j].y - lifts[j])) < 23) { hit = true; break; } }
                    if (!hit) break; y -= 24;
                }
                lifts[i] = tagPos[i].y - y;
            }
            for (int i = 0; i < tagList.Count; i++) Tag(tagList[i].p, tagList[i].x, tagList[i].y, lifts[i]);
            DrawOffscreen(s); DrawMinimap(s);

            // 顶部角色卡
            var list = new List<Player>(s.Players); list.Sort((a, b) => a.Slot - b.Slot);
            int n = list.Count;
            if (n > 6)
            {   // 7~10 人：紧凑卡片（头像 + 名字 + 血条 + 技能条 + 胜场）
                float w = (1240 - (n - 1) * 6) / n;
                for (int k = 0; k < n; k++)
                {
                    var p = list[k]; float x = 20 + k * (w + 6), y = 8; var col = Art.C(p.C.Color);
                    var sc = s.Mode == GameMode.Team ? Art.C(TeamColors[p.Slot % 2]) : Art.C(SlotColors[p.Slot]);
                    UiFx.HudCard(new Rect(x, y, w, 50), sc, p.Alive, view.LocalSlot == p.Slot); Ui.Rect(new Rect(x, y, w, 3), sc);
                    var icon = new Rect(x + 3, y + 6, 26, 26); DrawCharIcon(icon, p.Ch, false, false); if (!p.Alive) Ui.Rect(icon, new Color(0, 0, 0, .6f));
                    Ui.Text(new Rect(x + 31, y + 4, w - 32, 20), (view.LocalSlot == p.Slot ? "你 " : "P" + (p.Slot + 1) + " ") + p.C.Name, Ui.Small, col, 1);
                    for (int wv = 0; wv < s.Wins[p.Slot] && wv < WIN_ROUNDS; wv++) Ui.Sprite(new Rect(x + w - 10 - wv * 9, y + 24, 8, 8), Art.Star4, new Color(1, .85f, .3f));
                    float hpk = p.MaxHp > 0 ? p.Hp / p.MaxHp : 0;
                    Ui.Bar(new Rect(x + 4, y + 36, w - 8, 7), hpk, hpk > .3f ? col : new Color(1, .3f, .3f), new Color(.08f, .08f, .16f));
                    bool rdy; float skp = SkillPct(s, p, out rdy);
                    Ui.Bar(new Rect(x + 4, y + 44, w - 8, 3), skp, rdy ? new Color(1, .85f, .3f) : new Color(.6f, .75f, 1), new Color(.08f, .08f, .16f));
                }
            }
            else if (n > 0)
            {
                float w = (1240 - (n - 1) * 12) / n;
                for (int k = 0; k < n; k++)
                {
                    var p = list[k]; float x = 20 + k * (w + 12), y = 10; var col = Art.C(p.C.Color);
                    UiFx.HudCard(new Rect(x, y, w, 70), s.Mode == GameMode.Team ? Art.C(TeamColors[p.Slot % 2]) : Art.C(SlotColors[p.Slot]), p.Alive, view.LocalSlot == p.Slot); Ui.Rect(new Rect(x, y, 4, 70), s.Mode == GameMode.Team ? Art.C(TeamColors[p.Slot % 2]) : Art.C(SlotColors[p.Slot]));
                    if (s.Mode == GameMode.Team) Ui.Rect(new Rect(x, y + 68, w, 2), Art.C(TeamColors[p.Slot % 2]));
                    var icon = new Rect(x + 10, y + 8, 40, 40); DrawCharIcon(icon, p.Ch, false, false);
                    if (!p.Alive) Ui.Rect(icon, new Color(0, 0, 0, .6f));
                    string lbl = (view.LocalSlot == p.Slot ? "你" : p.Label);
                    Ui.Text(new Rect(x + 58, y + 4, w - 60, 26), $"P{p.Slot + 1} <b>{p.C.Name}</b>  <size=13>{lbl}</size>", Ui.Small, col, 1);
                    for (int wv = 0; wv < WIN_ROUNDS; wv++) Ui.Sprite(new Rect(x + w - 22 - wv * 18, y + 8, 14, 14), Art.Star4, wv < s.Wins[p.Slot] ? new Color(1, .85f, .3f) : new Color(1, 1, 1, .15f));
                    float hpk = p.MaxHp > 0 ? p.Hp / p.MaxHp : 0; view.HpGhost.TryGetValue(p.Slot, out float gh);
                    Ui.Bar(new Rect(x + 58, y + 32, w - 72, 12), hpk, hpk > .3f ? col : new Color(1, .3f, .3f), new Color(.08f, .08f, .16f), p.MaxHp > 0 ? gh / p.MaxHp : 0);
                    float sk = p.SkPctNet >= 0 ? p.SkPctNet : Mathf.Clamp01(1 - p.SkCd / p.C.SkCd);
                    string extra = "";
                    if (p.Ch == CharId.CCLD && !p.God) { sk = p.Energy / 100f; extra = p.Energy >= 100 ? "神力已满！按技能成神" : "神力"; }
                    else if (p.Ch == CharId.CCLD) extra = "剑意 ×" + p.Mult.ToString("0.00");
                    else if (p.Ch == CharId.DVD) extra = "层数 " + p.Stk + "/6";
                    else if (p.Ch == CharId.KAI) extra = p.Stk >= 5 ? "电荷满！雷遁带天雷" : "电荷 " + p.Stk + "/5";
                    else if (p.Ch == CharId.BW && p.BwShield > 0) extra = "护盾 " + Mathf.RoundToInt(p.BwShield);
                    else if (p.Ch == CharId.XP && p.Rage) extra = "暴怒就绪";
                    else if (p.Ch == CharId.PX) extra = p.HookStk >= 3 ? "钩子满层！下一钩勾全部" : "钩子层数 " + p.HookStk + "/3";
                    else if (p.Ch == CharId.CCLD2 && !p.Divine) { sk = p.Energy / 100f; extra = p.Energy >= 100 ? "神力已满！按技能降临" : "神力"; }
                    else if (p.Ch == CharId.CCLD2) extra = "下一日：" + Sim.DayNames[p.Day % 7];
                    else if (p.Ch == CharId.DVD2) extra = "光盘 " + p.Ammo + "/" + p.AmmoMax;
                    else if (p.Ch == CharId.BW2) extra = "能量 " + Mathf.RoundToInt(p.Energy);
                    else if (p.Ch == CharId.KAI2) extra = "自身极性 " + (p.SelfPol > 0 ? "＋（红）" : "－（蓝）");
                    if (p.Ult) extra = "★终极就绪！按技能★";
                    var skc = ((p.Ch == CharId.CCLD && !p.God) || (p.Ch == CharId.CCLD2 && !p.Divine)) && p.Energy >= 100 ? Color.Lerp(new Color(1, .8f, .2f), Color.white, (.5f + .5f * Mathf.Sin(Time.unscaledTime * 3.2f))) : new Color(.6f, .75f, 1);
                    Ui.Bar(new Rect(x + 58, y + 50, w - 72, 6), sk, skc, new Color(.08f, .08f, .16f));
                    if (extra != "") Ui.Text(new Rect(x + 58, y + 54, w - 60, 18), extra, Ui.Small, new Color(.85f, .88f, 1), 1);
                    if (p.Ch == CharId.DVD2) for (int d = 0; d < p.AmmoMax && d < 8; d++) Ui.Rect(new Rect(x + w - 16 - d * 9, y + 58, 6, 6), d < p.Ammo ? new Color(.75f, .45f, 1) : new Color(1, 1, 1, .12f));
                    if (p.Ch == CharId.DVD) for (int d = 0; d < 6; d++) Ui.Rect(new Rect(x + w - 16 - d * 9, y + 58, 6, 6), d < p.Stk ? new Color(1, .3f, .85f) : new Color(1, 1, 1, .12f));
                }
            }
            // 王冠模式：分数条 / 复活倒计时；事件；幽灵提示
            if (s.Mode == GameMode.Crown && n > 0)
            {
                float gap = n > 6 ? 6 : 12, w = (1240 - (n - 1) * gap) / n, yo = n > 6 ? -24 : 0;
                for (int k = 0; k < n; k++)
                {
                    var p = list[k]; float x = 20 + k * (w + gap);
                    Ui.Bar(new Rect(x + 4, 84 + yo, w - 8, 7), p.Score / Sim.CROWN_GOAL, new Color(1, .85f, .3f), new Color(.08f, .08f, .16f));
                    Ui.Text(new Rect(x, 90 + yo, w, 22), (s.CrownHolder == p.Slot ? "♛ " : "") + Mathf.FloorToInt(p.Score) + " / " + (int)Sim.CROWN_GOAL, Ui.SmallC, new Color(1, .9f, .5f), 1);
                    if (!p.Alive && p.RespawnT > 0) Ui.Text(new Rect(x, 50, w, 22), "复活 " + p.RespawnT.ToString("0.0"), Ui.SmallC, Color.white, 1);
                }
            }
            if (s.EventId >= 0 && s.EventT > 0 && s.State == GameState.Play)
                Ui.Text(new Rect(0, 116, 1280, 30), "事件 · " + Sim.EventNames[s.EventId] + "  " + Mathf.CeilToInt(s.EventT) + "s", Ui.LabelC, Art.C(Sim.EventColors[s.EventId]), 2);
            foreach (var p in s.Players)
                if (!p.Alive && p.Ghost && view.LocalSlot == p.Slot || (!p.Alive && p.Ghost && net == null && p.Kind == SlotMode.Human && s.State == GameState.Play))
                { Ui.Text(new Rect(0, 640, 1280, 30), $"P{p.Slot + 1} 变成幽灵了：移动键飘，攻击键吹气把活人推下去！", Ui.LabelC, new Color(.85f, .9f, 1), 2); break; }
            // 岩浆倒计时
            float lavaLeft = client != null ? client.LavaLeft : (s.State == GameState.Play ? (s.Mode == GameMode.Crown ? Sim.CROWN_LAVA_AT : LAVA_AT) - s.RoundT : -99);
            if (lavaLeft > 0 && lavaLeft < 15 && s.State == GameState.Play) Ui.Text(new Rect(0, 86, 1280, 30), "岩浆 " + Mathf.CeilToInt(lavaLeft) + " 秒后上涨", Ui.LabelC, new Color(1, .55f, .25f), 2);
            // 大字横幅
            string big = null, small = null; Color bc = Color.white;
            if (s.State == GameState.Countdown) { big = s.Timer > 1.6f ? $"第 {s.RoundNo} 局" : Mathf.CeilToInt(s.Timer / .55f).ToString(); small = $"{ModeName(s.Mode)} · 地图：{Maps.All[Mathf.Clamp(s.CurMap, 0, Maps.All.Length - 1)].Name} · 先赢 {WIN_ROUNDS} 局者夺冠"; }
            else if (s.State == GameState.Play && s.Banner != "") { big = s.Banner; bc = new Color(1, .77f, .2f); }
            else if (s.State == GameState.RoundEnd || s.State == GameState.MatchEnd)
            {
                Player wp = null; foreach (var p in s.Players) if (p.Slot == s.WinnerSlot) wp = p;
                if (s.State == GameState.RoundEnd && s.Mode == GameMode.Team && wp != null) { big = TeamNames[wp.Slot % 2] + " 赢下本局"; bc = Art.C(TeamColors[wp.Slot % 2]); }
                else if (s.State == GameState.RoundEnd) { big = wp != null ? $"P{wp.Slot + 1} {wp.C.Name} 赢下本局" : "同归于尽！"; bc = wp != null ? Art.C(wp.C.Color) : Color.white; }
                else { DrawMatchEnd(s, wp); }
            }
            if (big != null) UiFx.Banner(big, small, bc); // 第十版：横幅展开 + 大字落定 + 毛笔底纹
            if (paused) DrawPause();
        }

        /// <summary>画面外的人：屏幕边缘显示彩色箭头 + 距离。</summary>
        void DrawOffscreen(Sim s)
        {
            if (s.State != GameState.Play && s.State != GameState.Countdown) return;
            // 第十一版：箭头只在「顶部角色卡以下、黑边以内、小地图以外」的区域里沿屏幕边走，不压 HUD
            float yTop = ArrowTop(s), yBot = 720 - Mathf.Max(GodView.LetterboxPx, 0) - 34;
            var vr = view.ViewRect; var c0 = new Vector2(vr.center.x, vr.center.y);
            foreach (var p in s.Players)
            {
                if (!p.Alive) continue;
                if (p.Cx > vr.xMin + 10 && p.Cx < vr.xMax - 10 && p.Cy > vr.yMin + 10 && p.Cy < vr.yMax - 10) continue;
                var gc = Ui.ScreenToDesign(view.ToGui(c0.x, c0.y)); var gp = Ui.ScreenToDesign(view.ToGui(p.Cx, p.Cy));
                Vector2 d = gp - gc; if (d.sqrMagnitude < 1) continue;
                Vector2 e = EdgeArrow(s, d, 600f, yTop, yBot);
                var tc = s.Mode == GameMode.Team ? Art.C(TeamColors[p.Slot % 2]) : Art.C(SlotColors[p.Slot]);
                float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + 90;
                var keep = GUI.matrix; GUIUtility.RotateAroundPivot(ang, e * Ui.Scale + Ui.Offset);
                Ui.Text(new Rect(e.x - 20, e.y - 20, 40, 40), "▲", Ui.Big, tc, 2); GUI.matrix = keep;
                float dist = Vector2.Distance(new Vector2(p.Cx, p.Cy), view.LocalFocus != null ? new Vector2(view.LocalFocus.Cx, view.LocalFocus.Cy) : c0) / T;
                Vector2 tp = e - d.normalized * 38;
                Ui.Text(new Rect(tp.x - 50, tp.y - 11, 100, 22), "P" + (p.Slot + 1) + " " + Mathf.RoundToInt(dist) + "m", Ui.SmallC, Color.Lerp(tc, Color.white, .4f), 1.5f);
            }
            foreach (var it in s.ItemsList) // 终极球在镜头外：屏幕边缘一个闪动的彩虹箭头
            {
                if (it.Type != Items.Ult) continue; float ux = it.X + 13, uy = it.Y + 13;
                if (ux > vr.xMin + 10 && ux < vr.xMax - 10 && uy > vr.yMin + 10 && uy < vr.yMax - 10) continue;
                var gc = Ui.ScreenToDesign(view.ToGui(c0.x, c0.y)); var gp = Ui.ScreenToDesign(view.ToGui(ux, uy));
                Vector2 d = gp - gc; if (d.sqrMagnitude < 1) continue;
                Vector2 e = EdgeArrow(s, d, 590f, yTop + 6, yBot - 6);
                var rc = Color.HSVToRGB((Time.unscaledTime * .4f) % 1, .55f, 1);
                float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + 90;
                var keep = GUI.matrix; GUIUtility.RotateAroundPivot(ang, e * Ui.Scale + Ui.Offset);
                Ui.Text(new Rect(e.x - 26, e.y - 26, 52, 52), "▲", Ui.Big, rc, 3); GUI.matrix = keep;
                Vector2 tp = e - d.normalized * 44;
                Ui.Text(new Rect(tp.x - 60, tp.y - 11, 120, 22), "★终极球", Ui.SmallC, rc, 1.5f);
            }
        }

        /// <summary>屏幕边箭头能到的最高处：顶部角色卡（王冠模式还有分数条）下面再留出箭头和字的位置。</summary>
        static float ArrowTop(Sim s)
        {
            int n = 0; foreach (var _ in s.Players) n++;
            float hud = n > 6 ? 60 : 82; if (s.Mode == GameMode.Crown) hud = 114;
            return Mathf.Max(hud + 34, GodView.LetterboxPx + 34);
        }
        /// <summary>从屏幕中心沿方向 d 走到允许区域的边上（左右 ±xr，上 yTop，下 yBot）；落在大地图小地图上时挪到它左边。</summary>
        static Vector2 EdgeArrow(Sim s, Vector2 d, float xr, float yTop, float yBot)
        {
            float k = 1;
            if (Mathf.Abs(d.x) > .01f) k = Mathf.Min(k, xr / Mathf.Abs(d.x));
            if (d.y < -.01f) k = Mathf.Min(k, (360 - yTop) / -d.y);
            else if (d.y > .01f) k = Mathf.Min(k, (yBot - 360) / d.y);
            var e = new Vector2(640, 360) + d * Mathf.Max(0, k);
            if (s.CurMap >= 0 && s.CurMap < Maps.All.Length && Maps.All[s.CurMap].Big)
            {
                float mw = 230, mh = mw * s.ROWS / Mathf.Max(1, s.COLS), mx = 1280 - mw - 12, my = 720 - mh - 12;
                if (e.x > mx - 40 && e.y > my - 40) { if (d.x > 0 && Mathf.Abs(d.x) * (my - 40 - 360) > Mathf.Abs(d.y) * (mx - 40 - 640)) e.x = mx - 40; else e.y = my - 40; }
            }
            return e;
        }

        readonly List<(Player p, float x, float y)> tagList = new List<(Player p, float x, float y)>(); readonly List<Vector3> tagPos = new List<Vector3>();
        Texture2D miniTex; int miniVer = -1, miniMap = -1; Color32[] miniBuf;
        /// <summary>大地图右下角的小地图：地形轮廓 + 每人一个彩色点 + 当前镜头框。</summary>
        void DrawMinimap(Sim s)
        {
            if (s.CurMap < 0 || !Maps.All[s.CurMap].Big || (s.State != GameState.Play && s.State != GameState.Countdown)) return;
            int C = s.COLS, R = s.ROWS;
            if (miniTex == null || miniTex.width != C || miniTex.height != R) { miniTex = new Texture2D(C, R, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point }; miniBuf = new Color32[C * R]; miniVer = -1; }
            if (miniVer != s.TileVersion || miniMap != s.CurMap)
            {
                miniVer = s.TileVersion; miniMap = s.CurMap;
                for (int r = 0; r < R; r++) for (int c = 0; c < C; c++)
                    { byte v = s.Tiles[r * C + c]; miniBuf[(R - 1 - r) * C + c] = v == 0 ? new Color32(8, 10, 24, 150) : v == 2 ? new Color32(120, 126, 160, 230) : new Color32(190, 196, 225, 230); }
                miniTex.SetPixels32(miniBuf); miniTex.Apply(false);
            }
            float mw = 230, mh = mw * R / C, mx = 1280 - mw - 12, my = 720 - mh - 12;
            Ui.Rect(new Rect(mx - 3, my - 3, mw + 6, mh + 6), new Color(.02f, .02f, .06f, .8f));
            GUI.DrawTexture(new Rect(mx, my, mw, mh), miniTex);
            float sx = mw / s.W, sy = mh / s.H;
            if (s.LavaY < s.H) Ui.Rect(new Rect(mx, my + s.LavaY * sy, mw, mh - s.LavaY * sy), new Color(1, .35f, .1f, .55f));
            var vr = view.ViewRect;
            float x0 = Mathf.Clamp(vr.xMin * sx, 0, mw), x1 = Mathf.Clamp(vr.xMax * sx, 0, mw), y0 = Mathf.Clamp(vr.yMin * sy, 0, mh), y1 = Mathf.Clamp(vr.yMax * sy, 0, mh);
            var fc = new Color(1, 1, 1, .5f);
            Ui.Rect(new Rect(mx + x0, my + y0, x1 - x0, 1), fc); Ui.Rect(new Rect(mx + x0, my + y1 - 1, x1 - x0, 1), fc);
            Ui.Rect(new Rect(mx + x0, my + y0, 1, y1 - y0), fc); Ui.Rect(new Rect(mx + x1 - 1, my + y0, 1, y1 - y0), fc);
            foreach (var p in s.Players)
            {
                if (!p.Alive) continue;
                var tc = s.Mode == GameMode.Team ? Art.C(TeamColors[p.Slot % 2]) : Art.C(SlotColors[p.Slot]);
                float px = mx + Mathf.Clamp(p.Cx * sx, 0, mw), py = my + Mathf.Clamp(p.Cy * sy, 0, mh); bool me = view.LocalFocus == p;
                Ui.Rect(new Rect(px - (me ? 5 : 4), py - (me ? 5 : 4), me ? 10 : 8, me ? 10 : 8), me ? Color.white : new Color(0, 0, 0, .8f));
                Ui.Rect(new Rect(px - 3, py - 3, 6, 6), tc);
            }
        }

        void DrawMatchEnd(Sim s, Player wp)
        {
            // 第十版结算：放射光 + 聚光 + 彩带，排行按胜场/击杀/伤害排序，第一名金边
            Ui.Rect(new Rect(-400, -200, 2080, 1120), new Color(0, 0, 0, .5f));
            bool team = s.Mode == GameMode.Team && wp != null;
            var wc = team ? Art.C(TeamColors[wp.Slot % 2]) : wp != null ? Art.C(wp.C.Color) : Color.white;
            var icon = new Rect(580, 150, 120, 120);
            if (wp != null) UiFx.Victory(icon, wc);
            Ui.Text(new Rect(0, 50, 1280, 100), team ? TeamNames[wp.Slot % 2] + " 夺冠！" : wp != null ? $"P{wp.Slot + 1} {wp.C.Name} 夺冠！" : "平局", Ui.Title, wc, 5, new Color(.08f, .03f, .02f, 1));
            if (wp != null) DrawCharIcon(icon, wp.Ch, true);
            Player mvpK = null, mvpD = null; foreach (var p in s.Players) { if (mvpK == null || p.Kills > mvpK.Kills) mvpK = p; if (mvpD == null || p.DamageDealt > mvpD.DamageDealt) mvpD = p; }
            var ps = new List<Player>(s.Players);
            ps.Sort((a2, b2) => s.Wins[b2.Slot] != s.Wins[a2.Slot] ? s.Wins[b2.Slot] - s.Wins[a2.Slot] : b2.Kills != a2.Kills ? b2.Kills - a2.Kills : b2.DamageDealt.CompareTo(a2.DamageDealt));
            bool two = ps.Count > 5; int per = two ? (ps.Count + 1) / 2 : ps.Count;
            for (int k = 0; k < ps.Count; k++)
            {
                var p = ps[k]; float rw = two ? 430 : 640, x = two ? (k < per ? 200 : 650) : 320, y = 296 + (k % per) * 36;
                var row = new Rect(x, y, rw, 30); UiFx.RankRow(row, Art.C(p.C.Color), k);
                Ui.Text(new Rect(x + 12, y + 2, 40, 26), "#" + (k + 1), Ui.Small, k == 0 ? new Color(1, .85f, .35f) : new Color(.75f, .78f, .9f), 1);
                Ui.Text(new Rect(x + 52, y + 2, rw - 60, 26), (s.Mode == GameMode.Team ? "[" + TeamNames[p.Slot % 2] + "] " : "") + $"P{p.Slot + 1} <b>{p.C.Name}</b>   胜 {s.Wins[p.Slot]}   杀 {p.Kills}   伤害 {Mathf.RoundToInt(p.DamageDealt)}" + (p == mvpD ? "  <color=#ffd84a>★输出王</color>" : "") + (p == mvpK && p.Kills > 0 ? "  <color=#ff8a8a>☠击杀王</color>" : ""), two ? Ui.Small : Ui.Label, Art.C(p.C.Color), 1);
            }
            if (client != null) Ui.Text(new Rect(190, 590, 900, 30), "等待房主选择再来一局…", Ui.LabelC, new Color(.8f, .84f, 1), 1);
            else
            {
                if (Ui.Btn(new Rect(410, 590, 210, 50), "再来一局 (Enter)", true)) Rematch();
                if (host != null) { if (Ui.Btn(new Rect(660, 590, 210, 50), "回到房间")) { host.BackToLobby(); scr = Scr.Lobby; NewAttract(); } }
                else if (Ui.Btn(new Rect(660, 590, 210, 50), "返回菜单")) ToMenu();
            }
        }

        void DrawPause()
        {
            Ui.Rect(new Rect(-400, -200, 2080, 1120), new Color(0, 0, 0, .55f));
            GUI.Box(new Rect(440, 200, 400, 300), "", Ui.Panel);
            Ui.Text(new Rect(440, 215, 400, 60), net == null ? "暂停" : "菜单", Ui.Big, Color.white, 3);
            if (Ui.Btn(new Rect(490, 290, 300, 50), "继续")) paused = false;
            if (net == null) { if (Ui.Btn(new Rect(490, 355, 300, 50), "返回主菜单 (M)")) ToMenu(); }
            else
            {
                if (host != null && Ui.Btn(new Rect(490, 355, 300, 50), "回到房间（结束本场）")) { paused = false; host.BackToLobby(); scr = Scr.Lobby; NewAttract(); }
                if (Ui.Btn(new Rect(490, 420, 300, 50), host != null ? "解散房间" : "离开房间")) ToMenu();
            }
            if (net != null) Ui.Text(new Rect(440, 470, 400, 30), "联机时游戏不会暂停", Ui.SmallC, new Color(.7f, .74f, .9f), 1);
        }
    }
}
