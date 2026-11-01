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
///   screenshot [path]     … Game 画面を保存（既定: Logs/claude-screenshot.png、Play 中のみ）
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
            case "screenshot":
                var path = string.IsNullOrEmpty(arg) ? Path.Combine("Logs", "claude-screenshot.png") : arg;
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log($"[ClaudeCommand] screenshot requested: {Path.GetFullPath(path)}");
                break;
            default:
                Debug.LogWarning($"[ClaudeCommand] unknown command: {command}");
                break;
        }
    }
}
