// 第十版素材读取：Resources/Brawl10 下的贴图、字体、音效。找不到素材时一律返回程序生成的替代品，不会报错。
using System.Collections.Generic;
using UnityEngine;

namespace Brawl.View
{
    public static class Res10
    {
        const string ROOT = "Brawl10/";
        static readonly Dictionary<string, Sprite> spr = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Texture2D> tex = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        static Font brush, title; static bool fontTried;

        /// <summary>读贴图（如 "Fx/sigil7"）。没有就返回 null。</summary>
        public static Texture2D Tex(string path)
        {
            Texture2D t; if (tex.TryGetValue(path, out t)) return t;
            t = Resources.Load<Texture2D>(ROOT + path); if (t != null) { t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Bilinear; }
            tex[path] = t; return t;
        }
        /// <summary>读精灵（中心为轴心）。没有就用 fallback。</summary>
        public static Sprite Spr(string path, Sprite fallback)
        {
            Sprite s; if (spr.TryGetValue(path, out s)) return s ?? fallback;
            var t = Tex(path); s = t != null ? Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(.5f, .5f), 1, 0, SpriteMeshType.FullRect) : null;
            spr[path] = s; return s ?? fallback;
        }
        /// <summary>书法字体（马善政楷书，OFL）——神谕、居合等大字。</summary>
        public static Font Brush { get { LoadFonts(); return brush; } }
        /// <summary>标题字体（站酷庆科黄油体，OFL）——界面标题、横幅、按钮。</summary>
        public static Font TitleFont { get { LoadFonts(); return title; } }
        static void LoadFonts()
        {
            if (fontTried) return; fontTried = true;
            brush = Resources.Load<Font>(ROOT + "Fonts/MaShanZheng");
            title = Resources.Load<Font>(ROOT + "Fonts/ZcoolQingKe");
        }
        /// <summary>字体里缺字（比如玩家起的名字）时退回系统字体。</summary>
        public static bool Covers(Font f, string s)
        {
            if (f == null || string.IsNullOrEmpty(s)) return false;
            string key = (f == brush ? "b|" : f == title ? "t|" : f.name + "|") + s;
            bool ok; if (covers.TryGetValue(key, out ok)) return ok;
            if (covers.Count > 4000) covers.Clear();
            ok = true;
            try { foreach (var ch in s) if (ch > ' ' && !f.HasCharacter(ch)) { ok = false; break; } } catch { ok = false; }
            covers[key] = ok; return ok;
        }
        static readonly Dictionary<string, bool> covers = new Dictionary<string, bool>();

        // ---------------- 音效 ----------------
        static AudioSource[] voices; static int next; static SfxPlayer main; static float lastAny;
        public static AudioClip Clip(string name)
        {
            AudioClip c; if (clips.TryGetValue(name, out c)) return c;
            c = Resources.Load<AudioClip>(ROOT + "Sfx/" + name); clips[name] = c; return c;
        }
        /// <summary>播放第十版音效（跟随设置里的音量）。</summary>
        public static void Play(string name, float vol = 1)
        {
            var c = Clip(name); if (c == null) return;
            if (voices == null)
            {
                var go = new GameObject("Brawl10Sfx"); Object.DontDestroyOnLoad(go); voices = new AudioSource[4];
                for (int i = 0; i < voices.Length; i++) { voices[i] = go.AddComponent<AudioSource>(); voices[i].playOnAwake = false; }
            }
            if (main == null) main = Object.FindFirstObjectByType<SfxPlayer>();
            float v = main != null ? main.Volume : .7f; if (v <= 0) return;
            var src = voices[next++ % voices.Length]; src.Stop(); src.clip = c; src.volume = Mathf.Clamp01(v * vol); src.Play();
            lastAny = Time.unscaledTime;
        }

        // ---------------- 程序生成的替代贴图 ----------------
        static Sprite crescent;
        /// <summary>新月形刀光（朝 +x，内缘锐利、尾部渐隐）——挥砍用。</summary>
        public static Sprite Crescent
        {
            get
            {
                if (crescent != null) return crescent;
                int n = 128; var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                    {
                        float u = (x + .5f) / n * 2 - 1, v = (y + .5f) / n * 2 - 1;
                        float d1 = Mathf.Sqrt(u * u + v * v), d2 = Mathf.Sqrt((u + .28f) * (u + .28f) + v * v);
                        float outer = Mathf.Clamp01((.92f - d1) * 30), inner = Mathf.Clamp01((d2 - .86f) * 18);
                        float ang = Mathf.Atan2(v, u), tail = Mathf.Clamp01(1 - Mathf.Abs(ang) / 1.5f);
                        float a = outer * inner * Mathf.Pow(tail, .8f);
                        float core = Mathf.Clamp01(1 - Mathf.Abs(d1 - .88f) / .05f) * tail;
                        a = Mathf.Clamp01(a * .8f + core * .5f);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                    }
                t.SetPixels32(px); t.Apply();
                crescent = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 1, 0, SpriteMeshType.FullRect);
                return crescent;
            }
        }

        // 常用素材（缺素材时退回 Art 里的程序图）
        public static Sprite Sigil => Spr("Fx/sigil7", Art.RingThin);
        public static Sprite RuneRing => Spr("Fx/runering", Art.RingThin);
        public static Sprite Rays => Spr("Fx/rays", Art.Soft);
        public static Sprite Pillar => Spr("Fx/pillar", Art.Soft);
        public static Sprite Flare => Spr("Fx/flare", Art.Star4);
        public static Sprite WingBig => Spr("Fx/wing", Art.Wing);
        public static Sprite Slash => Spr("Fx/slash", Art.Spark);
        public static Sprite Feather => Spr("Fx/feather", NewCharArt.Feather ?? Art.Spark);
        public static Sprite Bird(int frame) => Spr(frame == 0 ? "Fx/bird0" : "Fx/bird1", Art.Wing);
    }
}
