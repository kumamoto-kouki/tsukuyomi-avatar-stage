using System.IO;
using System.Threading.Tasks;
using UniVRM10;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// VRM を読み込み、曲を再生して、歌声トラックの音量に合わせて口パクと体の揺れをさせるステージ。
/// モデルと音声はリポジトリ内の avatar/・voice/・music/ から実行時に読み込む（モデルは git 管理外）。
/// モデルごとの設定は <see cref="AvatarProfile"/> にまとめてある。
/// </summary>
public class SingingStage : MonoBehaviour
{
    const string DefaultProfile = "AvatarProfiles/Zundamon2025";

    [Header("アバター（未指定なら Resources/" + DefaultProfile + "）")]
    public AvatarProfile avatar;

    [Header("音声（リポジトリのルートからの相対パス、または絶対パス）")]
    [Tooltip("スピーカーから流す音（伴奏と歌のミックス）")]
    public string songWavPath = "music/mix/song01_mix.wav";
    [Tooltip("口パクの元にする歌声だけの音。空なら再生中の音で口パクする")]
    public string lipSyncWavPath = "voice/renders/song01_vocal.wav";

    [Header("口パク")]
    [Tooltip("音量(RMS)をこの倍率で口の開きに変換")]
    public float mouthGain = 8f;
    [Tooltip("この音量以下は口を閉じる")]
    public float mouthThreshold = 0.01f;
    [Range(0f, 1f)] public float mouthSmoothing = 0.5f;

    [Header("体の揺れ")]
    public float swayDegrees = 3f;
    public float swaySpeed = 1.6f;

    [Header("投影")]
    [Tooltip("ビルドしたアプリで 2 台目のディスプレイがあれば全画面でそちらに出す")]
    public bool useSecondDisplay = true;
    public float startDelaySeconds = 1f;

    const int Window = 1024;

    Camera _camera;
    AudioSource _audio;
    Vrm10Instance _vrm;
    Vrm10RuntimeControlRig _rig;
    ExpressionKey _mouthKey;
    ExpressionKey _blinkKey;
    float[] _lipSamples;
    int _lipChannels;
    int _lipFrequency;
    float _mouth;
    float _nextBlink;
    float _blinkTimer = -1f;
    readonly float[] _samples = new float[Window];

    async void Start()
    {
        if (avatar == null) avatar = Resources.Load<AvatarProfile>(DefaultProfile);
        if (avatar == null)
        {
            Debug.LogError($"AvatarProfile が見つかりません: Resources/{DefaultProfile}");
            return;
        }
        _mouthKey = avatar.MouthKey;
        _blinkKey = avatar.BlinkKey;

        SetupCamera();
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;

        var root = ProjectRoot();
        _vrm = await LoadVrm(Resolve(root, avatar.vrmPath));
        _audio.clip = await LoadWav(Resolve(root, songWavPath));
        if (_vrm == null || _audio.clip == null) return;

        if (!string.IsNullOrEmpty(lipSyncWavPath))
        {
            var lip = await LoadWav(Resolve(root, lipSyncWavPath));
            if (lip != null && lip.GetData(_lipSamples = new float[lip.samples * lip.channels], 0))
            {
                _lipChannels = lip.channels;
                _lipFrequency = lip.frequency;
            }
            else
            {
                _lipSamples = null;
            }
        }

        _rig = _vrm.Runtime.ControlRig;
        foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type == LightType.Directional) light.intensity = avatar.lightIntensity;
        }
        FrameCamera();
        _nextBlink = Time.time + 2f;
        _audio.PlayDelayed(startDelaySeconds);
    }

    void SetupCamera()
    {
        _camera = Camera.main;
        if (_camera == null)
        {
            _camera = new GameObject("Main Camera").AddComponent<Camera>();
            _camera.tag = "MainCamera";
        }
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = Color.black;
        _camera.fieldOfView = 30f;

        if (useSecondDisplay && !Application.isEditor && Display.displays.Length > 1)
        {
            Display.displays[1].Activate();
            _camera.targetDisplay = 1;
        }
    }

    void FrameCamera()
    {
        // VRM 1.0 のモデルは +Z を向いているので、+Z 側から見る
        var head = _rig?.GetBoneTransform(HumanBodyBones.Head);
        var target = head != null ? head.position : _vrm.transform.position + Vector3.up * 1.2f;
        target.y -= avatar.cameraTargetBelowHead;
        _camera.transform.position = target + new Vector3(0f, 0.05f, avatar.cameraDistance);
        _camera.transform.LookAt(target);
    }

    void Update()
    {
        if (_vrm == null) return;
        var expression = _vrm.Runtime.Expression;

        var target = 0f;
        if (_audio.isPlaying)
        {
            var rms = _lipSamples != null ? LipSyncRms() : OutputRms();
            target = rms < mouthThreshold ? 0f : Mathf.Clamp01((rms - mouthThreshold) * mouthGain);
        }
        _mouth = Mathf.Lerp(target, _mouth, mouthSmoothing);
        expression.SetWeight(_mouthKey, _mouth * avatar.mouthMaxWeight);

        UpdateBlink(expression);
        UpdatePose();
    }

    /// <summary>再生位置に対応する歌声トラックの区間の音量</summary>
    float LipSyncRms()
    {
        var center = (int)(_audio.time * _lipFrequency) * _lipChannels;
        var start = Mathf.Max(0, center - Window / 2 * _lipChannels);
        var end = Mathf.Min(_lipSamples.Length, center + Window / 2 * _lipChannels);
        if (end <= start) return 0f;
        var sum = 0f;
        for (var i = start; i < end; i++) sum += _lipSamples[i] * _lipSamples[i];
        return Mathf.Sqrt(sum / (end - start));
    }

    float OutputRms()
    {
        _audio.GetOutputData(_samples, 0);
        var sum = 0f;
        foreach (var s in _samples) sum += s * s;
        return Mathf.Sqrt(sum / _samples.Length);
    }

    /// <summary>
    /// ControlRig（正規化ボーン：T ポーズで回転ゼロ、軸はモデル空間と同じ）に毎フレーム「絶対値」で姿勢を入れる。
    /// Vrm10Instance が LateUpdate でこれを元のボーンへ反映する。
    /// </summary>
    void UpdatePose()
    {
        if (_rig == null) return;
        var t = Time.time * swaySpeed;
        // モデルは +Z 向き。左腕は -X 側、右腕は +X 側に伸びているので、Z 軸回りに逆向きに回して下ろす
        SetBone(HumanBodyBones.LeftUpperArm, Quaternion.Euler(0f, 0f, avatar.armDownDegrees));
        SetBone(HumanBodyBones.RightUpperArm, Quaternion.Euler(0f, 0f, -avatar.armDownDegrees));
        SetBone(HumanBodyBones.Spine, Quaternion.Euler(0f, 0f, Mathf.Sin(t) * swayDegrees));
        SetBone(HumanBodyBones.Head, Quaternion.Euler(Mathf.Sin(t * 2f) * swayDegrees * _mouth, 0f, -Mathf.Sin(t) * swayDegrees * 0.5f));
    }

    void SetBone(HumanBodyBones bone, Quaternion rotation)
    {
        var t = _rig.GetBoneTransform(bone);
        if (t != null) t.localRotation = rotation;
    }

    void UpdateBlink(Vrm10RuntimeExpression expression)
    {
        if (_blinkTimer < 0f && Time.time >= _nextBlink) _blinkTimer = 0f;
        if (_blinkTimer < 0f) return;

        _blinkTimer += Time.deltaTime;
        const float duration = 0.15f;
        expression.SetWeight(_blinkKey, Mathf.Sin(Mathf.Clamp01(_blinkTimer / duration) * Mathf.PI));
        if (_blinkTimer >= duration)
        {
            expression.SetWeight(_blinkKey, 0f);
            _blinkTimer = -1f;
            _nextBlink = Time.time + Random.Range(2f, 5f);
        }
    }

    static async Task<Vrm10Instance> LoadVrm(string path)
    {
        if (!File.Exists(path))
        {
            Debug.LogError($"VRM が見つかりません: {path}");
            return null;
        }
        return await Vrm10.LoadPathAsync(path, canLoadVrm0X: true, showMeshes: true);
    }

    static async Task<AudioClip> LoadWav(string path)
    {
        if (!File.Exists(path))
        {
            Debug.LogError($"wav が見つかりません: {path}");
            return null;
        }
        using var request = UnityWebRequestMultimedia.GetAudioClip(new System.Uri(path).AbsoluteUri, AudioType.WAV);
        var handler = (DownloadHandlerAudioClip)request.downloadHandler;
        handler.streamAudio = false;
        handler.compressed = false; // GetData で波形を読めるように
        var op = request.SendWebRequest();
        while (!op.isDone) await Task.Yield();
        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"wav を読み込めません: {request.error}");
            return null;
        }
        return DownloadHandlerAudioClip.GetContent(request);
    }

    static string ProjectRoot()
    {
        // エディタ: <repo>/stage/unity/Assets、ビルド: <exe>_Data。どちらも上にたどって avatar/ がある所をルートとする
        var dir = new DirectoryInfo(Application.dataPath);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "avatar"))) dir = dir.Parent;
        return dir?.FullName ?? Application.dataPath;
    }

    static string Resolve(string root, string path) => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(root, path));
}
