using System;
using UniVRM10;
using UnityEngine;

/// <summary>
/// 曲ごとに変わる設定。音声ファイル、テンポ、セクション（イントロ・A メロ…）ごとの動き・表情・カメラ。
/// 曲を差し替えるときは、このアセットを複製して値を変える。
/// </summary>
[CreateAssetMenu(menuName = "Tsukuyomi Stage/Song Profile", fileName = "SongProfile")]
public class SongProfile : ScriptableObject
{
    public enum CameraShot
    {
        Wide,       // 全身が入る引き
        PushIn,     // セクションの間にゆっくり寄る
        Orbit,      // 横から回り込む
        CloseUp,    // 顔に寄る
        PullBack,   // セクションの間にゆっくり引く
    }

    [Serializable]
    public class Section
    {
        public string name = "";
        [Tooltip("曲の頭から何拍目に始まるか（1 小節 = 4 拍）")]
        public float startBeat;
        [Tooltip("体の動きの大きさ（0 = 止まる、1 = 標準）")]
        public float motion = 1f;
        public ExpressionPreset expression = ExpressionPreset.neutral;
        [Range(0f, 1f)] public float expressionWeight;
        public CameraShot camera = CameraShot.Wide;
    }

    [Header("音声（リポジトリのルートからの相対パス、または絶対パス）")]
    [Tooltip("スピーカーから流す音（伴奏と歌のミックス）")]
    public string songWavPath = "music/mix/song01_mix.wav";
    [Tooltip("口パクの元にする歌声だけの音。空なら再生中の音で口パクする")]
    public string lipSyncWavPath = "voice/renders/song01_vocal.wav";

    [Header("テンポ")]
    public float bpm = 110f;

    [Header("セクション（startBeat の小さい順に並べる）")]
    public Section[] sections = Array.Empty<Section>();

    public float BeatsAt(float seconds) => seconds * bpm / 60f;

    /// <summary>指定した拍のセクションと、そのセクション内の進み具合（0〜1）</summary>
    public (Section section, float progress) SectionAt(float beat, float totalBeats)
    {
        if (sections.Length == 0) return (null, 0f);
        var i = 0;
        while (i + 1 < sections.Length && sections[i + 1].startBeat <= beat) i++;
        var start = sections[i].startBeat;
        var end = i + 1 < sections.Length ? sections[i + 1].startBeat : totalBeats;
        var progress = end > start ? Mathf.Clamp01((beat - start) / (end - start)) : 1f;
        return (sections[i], progress);
    }
}
