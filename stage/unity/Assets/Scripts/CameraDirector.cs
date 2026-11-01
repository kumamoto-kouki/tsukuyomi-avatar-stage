using UnityEngine;

/// <summary>
/// セクションごとのカメラショットに合わせてカメラを動かす。目標の位置へなめらかに追いかける。
/// VRM 1.0 のモデルは +Z を向いているので、+Z 側から見るのが正面。
/// </summary>
public class CameraDirector
{
    readonly Camera _camera;
    readonly AvatarProfile _avatar;
    readonly Vector3 _head;
    readonly Vector3 _bodyCenter;
    Vector3 _position;
    Vector3 _lookAt;
    bool _initialized;

    /// <summary>目標へ追いかける速さ（大きいほどきびきび動く）</summary>
    public float followSpeed = 1.5f;

    public CameraDirector(Camera camera, AvatarProfile avatar, Vector3 headPosition, Vector3 footPosition)
    {
        _camera = camera;
        _avatar = avatar;
        _head = headPosition;
        // 全身ショットで画面の中心にする点（足元と頭の間、少し上寄り）
        _bodyCenter = Vector3.Lerp(footPosition, headPosition, 0.55f);
    }

    public void Update(SongProfile.CameraShot shot, float progress, float time)
    {
        Desired(shot, progress, time, out var position, out var lookAt);
        if (!_initialized)
        {
            _position = position;
            _lookAt = lookAt;
            _initialized = true;
        }
        var k = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
        _position = Vector3.Lerp(_position, position, k);
        _lookAt = Vector3.Lerp(_lookAt, lookAt, k);
        _camera.transform.position = _position;
        _camera.transform.LookAt(_lookAt);
    }

    void Desired(SongProfile.CameraShot shot, float progress, float time, out Vector3 position, out Vector3 lookAt)
    {
        var d = _avatar.cameraDistance;
        var face = _head + Vector3.down * _avatar.cameraTargetBelowHead;
        var t = Smooth(progress);
        float distance, yaw;
        switch (shot)
        {
            case SongProfile.CameraShot.PushIn:
                distance = Mathf.Lerp(d * 1.8f, d, t);
                lookAt = Vector3.Lerp(_bodyCenter, face, t);
                yaw = 0f;
                break;
            case SongProfile.CameraShot.Orbit:
                distance = d * 1.2f;
                lookAt = Vector3.Lerp(face, _bodyCenter, 0.3f);
                yaw = Mathf.Lerp(-30f, 30f, t);
                break;
            case SongProfile.CameraShot.CloseUp:
                distance = d * 0.6f;
                lookAt = _head + Vector3.down * (_avatar.cameraTargetBelowHead * 0.3f);
                yaw = Mathf.Sin(time * 0.4f) * 8f;
                break;
            case SongProfile.CameraShot.PullBack:
                distance = Mathf.Lerp(d, d * 2.0f, t);
                lookAt = Vector3.Lerp(face, _bodyCenter, t);
                yaw = 0f;
                break;
            default: // Wide
                distance = d * 2.0f;
                lookAt = _bodyCenter;
                yaw = 0f;
                break;
        }
        position = lookAt + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0.05f, distance);
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);
}
