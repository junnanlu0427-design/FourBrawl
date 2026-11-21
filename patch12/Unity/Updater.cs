// 自动更新：启动时去 GitHub Releases 看有没有更新的版本；有就后台下载，
// 回到主菜单时自动退出 → 解压覆盖 → 重新打开游戏。完全免费，不需要账号。
// 发新版：把 BUILD 加 1，构建后把 zip 上传成标签为 v<BUILD> 的 Release。
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace Brawl.View
{
    public sealed class Updater : MonoBehaviour
    {
        public const int BUILD = 12;                       // 当前版本号（第十二版）
        public const string OWNER = "junnanlu0427-design", REPO = "FourBrawl";
        public enum St { Idle, Checking, UpToDate, Downloading, Ready, Failed, Disabled }
        public St State = St.Idle;
        public int Remote; public float Progress; public string Error = "";
        string zipPath; UnityWebRequest dl;

        public static bool Supported => !Application.isEditor && Application.platform == RuntimePlatform.WindowsPlayer;

        void Start()
        {
            if (!Supported || PlayerPrefs.GetInt("autoupdate", 1) == 0) { State = St.Disabled; return; }
            StartCoroutine(Check());
        }

        public string Line()
        {
            switch (State)
            {
                case St.Checking: return "正在检查更新…";
                case St.UpToDate: return "已是最新版 v" + BUILD;
                case St.Downloading: return "正在下载新版本 v" + Remote + "… " + Mathf.RoundToInt(Progress * 100) + "%";
                case St.Ready: return "新版本 v" + Remote + " 已下载好";
                case St.Failed: return "检查更新失败（不影响游戏）";
                default: return "v" + BUILD;
            }
        }

        void Update() { if (State == St.Downloading && dl != null) Progress = dl.downloadProgress; }

        IEnumerator Check()
        {
            State = St.Checking;
            using (var req = UnityWebRequest.Get("https://api.github.com/repos/" + OWNER + "/" + REPO + "/releases/latest"))
            {
                req.SetRequestHeader("User-Agent", "FourBrawl-Updater"); req.SetRequestHeader("Accept", "application/vnd.github+json"); req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { State = req.responseCode == 404 ? St.UpToDate : St.Failed; Error = req.error; yield break; }
                string json = req.downloadHandler.text;
                var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?(\\d+)");
                var url = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+\\.zip)\"");
                if (!tag.Success || !url.Success) { State = St.UpToDate; yield break; }
                Remote = int.Parse(tag.Groups[1].Value);
                if (Remote <= BUILD) { State = St.UpToDate; yield break; }
                zipPath = Path.Combine(Application.temporaryCachePath, "FourBrawl-v" + Remote + "-" + Process.GetCurrentProcess().Id + ".zip");
                State = St.Downloading;
                dl = new UnityWebRequest(url.Groups[1].Value, "GET", new DownloadHandlerFile(zipPath) { removeFileOnAbort = true }, null);
                dl.SetRequestHeader("User-Agent", "FourBrawl-Updater");
                yield return dl.SendWebRequest();
                bool ok = dl.result == UnityWebRequest.Result.Success; Error = dl.error; dl.Dispose(); dl = null;
                State = ok && File.Exists(zipPath) && new FileInfo(zipPath).Length > 1000000 ? St.Ready : St.Failed;
                zipReady = State == St.Ready;
            }
        }

        bool zipReady, launched;
        /// <summary>直接关掉游戏（没点更新、点了稍后、倒计时里关窗口）也要把已下载的新版装上，下次打开就是新版。</summary>
        void OnApplicationQuit() { if (zipReady && !launched) Launch(false); }

        /// <summary>退出游戏，由 PowerShell 脚本等游戏关掉后解压覆盖并重新打开。</summary>
        public void ApplyAndRestart()
        {
            if (State != St.Ready && !zipReady) return;
            if (Launch(true)) Application.Quit();
        }

        bool Launch(bool restart)
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                string dir = Path.GetDirectoryName(exe), script = Path.Combine(Application.temporaryCachePath, "fb_update_" + Process.GetCurrentProcess().Id + ".ps1");
                const string ps =
                    "param($ProcId, $Zip, $Dest, $Exe, $Restart = 1)\r\n" +
                    "$log = Join-Path ([IO.Path]::GetDirectoryName($Zip)) 'fb_update.log'\r\n" +
                    "function L($m) { Add-Content -LiteralPath $log -Value ((Get-Date -Format 'HH:mm:ss') + ' ' + $m) -Encoding UTF8 }\r\n" +
                    "L ('start dest=' + $Dest + ' exe=' + $Exe)\r\n" +
                    "for ($i = 0; $i -lt 200 -and (Get-Process -Id $ProcId -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 300 }\r\n" +
                    "Start-Sleep -Milliseconds 800\r\n" +
                    "$tmp = Join-Path ([IO.Path]::GetDirectoryName($Zip)) ('fb_new_' + $ProcId)\r\n" +
                    "try {\r\n" +
                    "  if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force }\r\n" +
                    "  Expand-Archive -LiteralPath $Zip -DestinationPath $tmp -Force -ErrorAction Stop\r\n" +
                    "  $name = [IO.Path]::GetFileName($Exe)\r\n" +
                    "  $found = Get-ChildItem -LiteralPath $tmp -Recurse -Filter $name | Select-Object -First 1\r\n" +
                    "  if (-not $found) { $found = Get-ChildItem -LiteralPath $tmp -Recurse -Filter '*.exe' | Where-Object { $_.Name -notlike 'UnityCrashHandler*' } | Select-Object -First 1 }\r\n" +
                    "  if (-not $found) { L 'no exe in zip'; throw 'no exe' }\r\n" +
                    "  $src = $found.DirectoryName; L ('copy from ' + $src)\r\n" +
                    "  Get-ChildItem -LiteralPath $Dest -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object { $_.IsReadOnly = $false }\r\n" +
                    "  robocopy $src $Dest /E /IS /IT /R:20 /W:1 /NFL /NDL /NJH /NJS | Out-Null; $rc = $LASTEXITCODE; L ('robocopy exit ' + $rc)\r\n" +
                    "  if ($rc -ge 8) { Copy-Item -Path (Join-Path $src '*') -Destination $Dest -Recurse -Force -ErrorAction Continue; L 'fallback Copy-Item done' }\r\n" +
                    "  if ($found.Name -ne $name) { Copy-Item -LiteralPath (Join-Path $Dest $found.Name) -Destination $Exe -Force -ErrorAction SilentlyContinue; Copy-Item -LiteralPath (Join-Path $src ($found.BaseName + '_Data')) -Destination (Join-Path $Dest ([IO.Path]::GetFileNameWithoutExtension($name) + '_Data')) -Recurse -Force -ErrorAction SilentlyContinue; L 'renamed exe/data' }\r\n" +
                    "  $dll = 'FourBrawl_Data\\Managed\\Assembly-CSharp.dll'; $a = Join-Path $src $dll; $b = Join-Path $Dest ([IO.Path]::GetFileNameWithoutExtension($name) + '_Data\\Managed\\Assembly-CSharp.dll')\r\n" +
                    "  if ((Test-Path -LiteralPath $a) -and (Test-Path -LiteralPath $b) -and ((Get-FileHash -LiteralPath $a).Hash -ne (Get-FileHash -LiteralPath $b).Hash)) { L 'verify mismatch, retry copy'; Start-Sleep 2; Copy-Item -Path (Join-Path $src '*') -Destination $Dest -Recurse -Force -ErrorAction Continue }\r\n" +
                    "  if ((Test-Path -LiteralPath $b) -and (Get-FileHash -LiteralPath $a).Hash -eq (Get-FileHash -LiteralPath $b).Hash) { L 'verify ok' } else { L 'verify FAILED' }\r\n" +
                    "  Remove-Item -LiteralPath $Zip -Force -ErrorAction SilentlyContinue; Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue\r\n" +
                    "} catch { L ('error ' + $_) }\r\n" +
                    "if ($Restart -eq 1) { Start-Process -FilePath $Exe -WorkingDirectory $Dest; L 'restarted' } else { L 'applied on quit (no restart)' }\r\n";
                File.WriteAllText(script, ps, new UTF8Encoding(true));
                var psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " + Q(script) + " -ProcId " + Process.GetCurrentProcess().Id + " -Zip " + Q(zipPath) + " -Dest " + Q(dir) + " -Exe " + Q(exe) + " -Restart " + (restart ? 1 : 0))
                { UseShellExecute = false, CreateNoWindow = true };
                Process.Start(psi); launched = true;
                return true;
            }
            catch (Exception e) { State = St.Failed; Error = e.Message; UnityEngine.Debug.LogException(e); return false; }
        }
        static string Q(string s) => "\"" + s.Replace("\"", "") + "\"";
    }
}
