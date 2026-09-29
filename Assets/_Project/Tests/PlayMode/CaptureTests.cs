using System;
using System.Collections;
using LanesOfVietnam.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace LanesOfVietnam.Tests
{
    /// <summary>
    /// FrameCapture (PLAN §7): the real game, in play mode, frozen at a named
    /// moment and written to disk.
    ///
    /// <code>tools/capture.sh seed=3,tick=700,x=0,out=captures/frame.png</code>
    ///
    /// It is a play-mode test rather than an editor script because that runs
    /// the game's own Awake/Update/render path — not a re-implementation of it
    /// that can drift. Skipped unless <c>-lovCapture</c> is on the command line,
    /// so an ordinary test run does not write files.
    /// </summary>
    public class CaptureTests
    {
        private const string Scene = "Assets/_Project/Scenes/Main.unity";

        private static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        [UnityTest, Category("Capture"), Timeout(600000)]
        public IEnumerator Capture()
        {
            string spec = Arg("-lovCapture");
            if (spec == null)
            {
                Assert.Ignore("no -lovCapture on the command line");
                yield break;
            }
            CaptureSettings.Active = CaptureSettings.Parse(spec);
            CaptureRunner.Done = false;
            try
            {
#if UNITY_EDITOR
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Scene, new LoadSceneParameters(LoadSceneMode.Single));
#else
                yield return SceneManager.LoadSceneAsync("Main");
#endif
                float t0 = Time.realtimeSinceStartup;
                while (!CaptureRunner.Done)
                {
                    Assert.Less(Time.realtimeSinceStartup - t0, 120f, "capture did not finish in 120 s");
                    yield return null;
                }
                Assert.IsNull(CaptureRunner.Error, CaptureRunner.Error);
            }
            finally
            {
                CaptureSettings.Active = null;
            }
        }
    }
}
