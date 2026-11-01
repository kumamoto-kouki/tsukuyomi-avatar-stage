using System;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

/// <summary>
/// Console のログ（エディタ・Play 中とも）とコンパイル結果を Logs/claude-console.log に書き出す。
/// エディタの外（Claude など）から、スタックトレース付きでエラーを読めるようにするため。
/// </summary>
[InitializeOnLoad]
static class ClaudeLogBridge
{
    static readonly string LogPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "claude-console.log"));
    static readonly object Lock = new();

    static ClaudeLogBridge()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
        Write($"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} domain reload (Unity {Application.unityVersion}) =====");

        Application.logMessageReceivedThreaded -= OnLog;
        Application.logMessageReceivedThreaded += OnLog;
        CompilationPipeline.assemblyCompilationFinished -= OnCompiled;
        CompilationPipeline.assemblyCompilationFinished += OnCompiled;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    static void OnLog(string message, string stackTrace, LogType type)
    {
        var text = $"[{DateTime.Now:HH:mm:ss.fff}] [{type}] {message}";
        if (type is LogType.Error or LogType.Exception or LogType.Assert or LogType.Warning && !string.IsNullOrEmpty(stackTrace))
            text += Environment.NewLine + stackTrace.TrimEnd();
        Write(text);
    }

    static void OnCompiled(string assembly, CompilerMessage[] messages)
    {
        var errors = 0;
        foreach (var m in messages)
        {
            if (m.type == CompilerMessageType.Error) errors++;
            if (m.type is CompilerMessageType.Error or CompilerMessageType.Warning)
                Write($"[{DateTime.Now:HH:mm:ss.fff}] [Compile{m.type}] {m.file}({m.line},{m.column}): {m.message}");
        }
        Write($"[{DateTime.Now:HH:mm:ss.fff}] [Compile] {Path.GetFileName(assembly)}: {(errors == 0 ? "OK" : $"{errors} error(s)")}");
    }

    static void OnPlayMode(PlayModeStateChange state) => Write($"[{DateTime.Now:HH:mm:ss.fff}] [PlayMode] {state}");

    static void Write(string line)
    {
        lock (Lock)
        {
            try
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // ログの書き込み失敗でエディタを止めない
            }
        }
    }
}
