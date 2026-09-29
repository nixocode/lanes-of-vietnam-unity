using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace LanesOfVietnam.Tools
{
    /// <summary>
    /// WebGL builds from the command line, and the size report PLAN §2 is
    /// judged against.
    ///
    /// <code>
    /// tools/unity.sh -nographics -executeMethod LanesOfVietnam.Tools.Build.WebGL
    /// tools/unity.sh -nographics -executeMethod LanesOfVietnam.Tools.Build.WebGL -lovScene Assets/_Project/Scenes/Main.unity -lovOut Builds/web -lovDev
    /// tools/unity.sh -nographics -executeMethod LanesOfVietnam.Tools.Build.EmptyFloor
    /// </code>
    ///
    /// Every build prints each file it produced and the total, and writes the
    /// same numbers to <c>size.json</c> beside the build, so a size claim is a
    /// file anyone can read rather than a number someone remembers.
    /// </summary>
    public static class Build
    {
        public const string MainScene = "Assets/_Project/Scenes/Main.unity";
        public const string EmptyScene = "Assets/_Project/Scenes/Empty.unity";

        /// <summary>
        /// PLAN §10 step 0: the size of an empty URP scene, which is the floor
        /// every other build is measured against. Whatever this costs is the
        /// engine, not the game.
        /// </summary>
        public static void EmptyFloor()
        {
            MakeEmptyScene();
            Run(EmptyScene, "Builds/empty-floor", development: false);
        }

        public static void WebGL()
        {
            string scene = Arg("-lovScene") ?? MainScene;
            string outDir = Arg("-lovOut") ?? "Builds/web";
            bool dev = Environment.GetCommandLineArgs().Contains("-lovDev");
            Run(scene, outDir, dev);
        }

        private static void Run(string scene, string outDir, bool development)
        {
            if (!File.Exists(scene)) Fail($"no scene at {scene}");
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            if (s.result != BuildResult.Succeeded)
            {
                Fail($"build {s.result}: {s.totalErrors} errors — see the log");
            }
            Report(outDir, scene, s);
        }

        /// <summary>Print and save every file's size. Compressed, because that is the download.</summary>
        private static void Report(string outDir, string scene, BuildSummary s)
        {
            var files = Directory.GetFiles(outDir, "*", SearchOption.AllDirectories)
                                 .Select(f => new FileInfo(f))
                                 .OrderByDescending(f => f.Length)
                                 .ToArray();
            long total = files.Sum(f => f.Length);
            // What a browser must fetch before the first frame: the loader and
            // the three compressed payloads. Everything else (template art,
            // streamed content) is outside that.
            long initial = files.Where(f => f.DirectoryName.EndsWith("Build"))
                                .Sum(f => f.Length);

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"scene\": \"{scene}\",\n");
            sb.Append($"  \"built\": \"{DateTime.UtcNow:O}\",\n");
            sb.Append($"  \"unity\": \"{Application.unityVersion}\",\n");
            sb.Append($"  \"buildSeconds\": {s.totalTime.TotalSeconds:F1},\n");
            sb.Append($"  \"initialBytes\": {initial},\n");
            sb.Append($"  \"totalBytes\": {total},\n");
            sb.Append("  \"files\": {\n");
            for (int i = 0; i < files.Length; i++)
            {
                string rel = Path.GetRelativePath(outDir, files[i].FullName).Replace('\\', '/');
                sb.Append($"    \"{rel}\": {files[i].Length}{(i < files.Length - 1 ? "," : "")}\n");
                Debug.Log($"[LOV] build   {files[i].Length / 1048576.0,8:F2} MB  {rel}");
            }
            sb.Append("  }\n}\n");
            File.WriteAllText(Path.Combine(outDir, "size.json"), sb.ToString());
            Debug.Log($"[LOV] build   initial {initial / 1048576.0:F2} MB, total {total / 1048576.0:F2} MB, "
                      + $"{s.totalTime.TotalSeconds:F0} s — {outDir}/size.json");
        }

        /// <summary>A camera and a light, nothing else. The floor.</summary>
        private static void MakeEmptyScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(EmptyScene));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera", typeof(Camera), typeof(UniversalAdditionalCameraData));
            cam.tag = "MainCamera";
            var sun = new GameObject("Sun", typeof(Light));
            sun.GetComponent<Light>().type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            EditorSceneManager.SaveScene(scene, EmptyScene);
        }

        internal static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        internal static void Fail(string why)
        {
            Debug.LogError($"[LOV] {why}");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            throw new Exception(why);
        }
    }
}
