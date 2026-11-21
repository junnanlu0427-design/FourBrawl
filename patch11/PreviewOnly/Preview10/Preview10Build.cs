#if UNITY_EDITOR
// 只用于第十版预览：把项目构建到 <项目>/PreviewBuild/FourBrawl.exe（不碰正式构建目录）。
using UnityEditor;
using UnityEngine;
namespace Brawl.EditorTools
{
    public static class Preview10Build
    {
        public static void Build()
        {
            var scenes = new System.Collections.Generic.List<string> { "Assets/Brawl/Scenes/Main.unity" };
            var r = BuildPipeline.BuildPlayer(scenes.ToArray(), System.IO.Path.GetFullPath("PreviewBuild/FourBrawl.exe"), BuildTarget.StandaloneWindows64, BuildOptions.None);
            Debug.Log("Preview10Build: " + r.summary.result);
            EditorApplication.Exit(r.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }
    }
}
#endif
