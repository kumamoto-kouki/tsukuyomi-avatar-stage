using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// エディタの外（Claude など）から Unity を操作するための窓口。
/// Logs/claude-command.txt に 1 行 1 コマンドを書くと、エディタが拾って実行し、ファイルを消す。
/// 結果は Debug.Log に出るので、ClaudeLogBridge 経由で Logs/claude-console.log から読める。
///
/// コマンド:
///   refresh               … アセットを再読み込み（スクリプトの変更をコンパイル）
///   play / stop           … Play モードの開始・終了
///   open-scene &lt;path&gt;     … シーンを開く（例: Assets/Scenes/Stage.unity）
///   build-scene           … Tools/Build Stage Scene と同じ
///   setup                 … プロジェクト設定を整える（Run In Background を有効にし、エディタが裏にあっても Play を止めない）
///   screenshot [path]     … メインカメラの映像を PNG で保存（既定: Logs/claude-screenshot.png）。Game 画面が非表示でも撮れる
/// </summary>
[InitializeOnLoad]
static class ClaudeCommandBridge
{
    static readonly string CommandPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "claude-command.txt"));
    static double _nextPoll;

    static ClaudeCommandBridge()
    {
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < _nextPoll) return;
        _nextPoll = EditorApplication.timeSinceStartup + 0.5;
        if (!File.Exists(CommandPath)) return;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(CommandPath);
            File.Delete(CommandPath);
        }
        catch (IOException)
        {
            return; // 書き込み中。次の回で拾う
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            try
            {
                Run(line);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ClaudeCommand] {line} failed: {e}");
            }
        }
    }

    static void Run(string line)
    {
        var space = line.IndexOf(' ');
        var command = space < 0 ? line : line[..space];
        var arg = space < 0 ? "" : line[(space + 1)..].Trim();
        Debug.Log($"[ClaudeCommand] {line}");

        switch (command)
        {
            case "refresh":
                AssetDatabase.Refresh();
                break;
            case "play":
                EditorApplication.isPlaying = true;
                break;
            case "stop":
                EditorApplication.isPlaying = false;
                break;
            case "open-scene":
                EditorSceneManager.OpenScene(arg);
                break;
            case "build-scene":
                StageSceneBuilder.Build();
                break;
            case "setup":
                PlayerSettings.runInBackground = true;
                AssetDatabase.SaveAssets();
                Debug.Log("[ClaudeCommand] runInBackground = true");
                break;
            case "screenshot":
                var path = Path.GetFullPath(string.IsNullOrEmpty(arg) ? Path.Combine("Logs", "claude-screenshot.png") : arg);
                CaptureCamera(path);
                Debug.Log($"[ClaudeCommand] screenshot saved: {path}");
                break;
            default:
                Debug.LogWarning($"[ClaudeCommand] unknown command: {command}");
                break;
        }
    }

    /// <summary>Game 画面の表示状態に関係なく、メインカメラを RenderTexture に描いて保存する</summary>
    static void CaptureCamera(string path, int width = 1280, int height = 720)
    {
        var camera = Camera.main;
        if (camera == null) throw new InvalidOperationException("Main Camera がありません");
        var rt = RenderTexture.GetTemporary(width, height, 24);
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        try
        {
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(rt);
        }
    }
}
