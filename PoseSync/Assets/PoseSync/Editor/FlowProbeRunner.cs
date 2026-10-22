using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PoseSync.EditorTools
{
    /// <summary>
    /// Booth シーンを Play して FlowProbe で体験フローを自動実行し、見本と撮影時の位置の差をログに出す。
    /// バッチモード: Unity.exe -batchmode -projectPath ... -executeMethod PoseSync.EditorTools.FlowProbeRunner.RunFromCommandLine
    /// (-quit は付けない。終わったら自動で終了する)
    /// </summary>
    [InitializeOnLoad]
    public static class FlowProbeRunner
    {
        const string Flag = "PoseSync.FlowProbe.Active";
        const string ExitFlag = "PoseSync.FlowProbe.ExitWhenDone";

        static FlowProbeRunner()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += Poll;
        }

        [MenuItem("PoseSync/Run Flow Probe")]
        public static void Run() => Begin(false);

        public static void RunFromCommandLine() => Begin(true);

        static void Begin(bool exitWhenDone)
        {
            EditorSceneManager.OpenScene("Assets/PoseSync/Scenes/Booth.unity");
            SessionState.SetBool(Flag, true);
            SessionState.SetBool(ExitFlag, exitWhenDone);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Flag, false))
                new GameObject("FlowProbe").AddComponent<FlowProbe>();
        }

        static void Poll()
        {
            if (!SessionState.GetBool(Flag, false) || !EditorApplication.isPlaying || !FlowProbe.Done) return;
            SessionState.SetBool(Flag, false);
            EditorApplication.ExitPlaymode();
            if (SessionState.GetBool(ExitFlag, false)) EditorApplication.Exit(0);
        }
    }
}
