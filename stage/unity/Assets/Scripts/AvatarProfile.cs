using UniVRM10;
using UnityEngine;

/// <summary>
/// アバター（VRM モデル）ごとに変わる設定。モデルを差し替えるときは、このアセットを複製して値を変える。
/// </summary>
[CreateAssetMenu(menuName = "Tsukuyomi Stage/Avatar Profile", fileName = "AvatarProfile")]
public class AvatarProfile : ScriptableObject
{
    [Header("モデル（リポジトリのルートからの相対パス、または絶対パス）")]
    public string vrmPath = "avatar/models/ずんだもん2025モデルセット ver.A1/VRM/Zundamon_2025_VRM10A.vrm";

    [Header("ポーズ")]
    [Tooltip("T ポーズから腕を下ろす角度")]
    public float armDownDegrees = 70f;

    [Header("カメラ・ライト")]
    [Tooltip("カメラから頭までの距離")]
    public float cameraDistance = 1.6f;
    [Tooltip("頭からどれだけ下を画面の中心にするか（頭身の低いモデルは小さく）")]
    public float cameraTargetBelowHead = 0.25f;
    [Tooltip("ディレクショナルライトの明るさ（白飛びするなら下げる）")]
    public float lightIntensity = 0.8f;

    [Header("表情")]
    [Tooltip("口パクに使う表情（プリセット）")]
    public ExpressionPreset mouthExpression = ExpressionPreset.aa;
    [Tooltip("プリセット以外の表情名を使うときだけ入力（入力するとこちらが優先）")]
    public string customMouthExpression = "";
    [Tooltip("口の開きの最大値（モデルの口が開きすぎるなら下げる）")]
    [Range(0f, 1f)] public float mouthMaxWeight = 1f;
    public ExpressionPreset blinkExpression = ExpressionPreset.blink;

    public ExpressionKey MouthKey => string.IsNullOrEmpty(customMouthExpression)
        ? ExpressionKey.CreateFromPreset(mouthExpression)
        : ExpressionKey.CreateCustom(customMouthExpression);

    public ExpressionKey BlinkKey => ExpressionKey.CreateFromPreset(blinkExpression);
}
