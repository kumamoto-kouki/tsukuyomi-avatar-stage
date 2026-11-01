using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UniVRM10;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// VRM を読み込み、曲を再生して、歌声に合わせた口パク・拍に合わせた動き・セクションごとの表情とカメラを付けるステージ。
/// モデルと音声はリポジトリ内の avatar/・voice/・music/ から実行時に読み込む（モデルは git 管理外）。
/// モデルごとの設定は <see cref="AvatarProfile"/>、曲ごとの設定は <see cref="SongProfile"/> にまとめてある。
/// 動かすのは VRM 標準の Humanoid ボーンと標準の表情だけなので、モデルを差し替えてもそのまま使える。
/// </summary>
public class SingingStage : MonoBehaviour
{
    const string DefaultAvatar = "AvatarProfiles/Zundamon2025";
    const string DefaultSong = "SongProfiles/Song01";

    [Header("アバター・曲（未指定なら Resources の既定）")]
    public AvatarProfile avatar;
    public SongProfile song;

    [Header("口パク")]
    [Tooltip("音量(RMS)をこの倍率で口の開きに変換")]
    public float mouthGain = 8f;
    [Tooltip("この音量以下は口を閉じる")]
    public float mouthThreshold = 0.01f;
    [Range(0f, 1f)] public float mouthSmoothing = 0.5f;

    [Header("拍に合わせた動き（セクションの motion が 1 のときの大きさ）")]
    [Tooltip("体の上下（メートル）")]
    public float bounceMeters = 0.015f;
    [Tooltip("体の左右の揺れ（度）")]
    public float swayDegrees = 4f;
    [Tooltip("頭のうなずき（度）")]
    public float nodDegrees = 5f;
    [Tooltip("腕の振り（度）")]
    public float armSwingDegrees = 8f;
    [Tooltip("ひじの曲げ（度）")]
    public float elbowBendDegrees = 15f;

    [Header("カメラ")]
    [Tooltip("カメラが目標へ追いかける速さ")]
    public float cameraFollowSpeed = 1.5f;

    [Header("投影")]
    [Tooltip("ビルドしたアプリで 2 台目のディスプレイがあれば全画面でそちらに出す")]
    public bool useSecondDisplay = true;
    public float startDelaySeconds = 1f;

    const int Window = 1024;

    Camera _camera;
    CameraDirector _director;
    AudioSource _audio;
    Vrm10Instance _vrm;
    Vrm10RuntimeControlRig _rig;
    Transform _hips;
    Vector3 _hipsRestPosition;
    ExpressionKey _mouthKey;
    ExpressionKey _blinkKey;
    float[] _lipSamples;
    int _lipChannels;
    int _lipFrequency;
    float _songTime;
    float _totalBeats;
    float _motion;
    float _mouth;
    float _nextBlink;
    float _blinkTimer = -1f;
    readonly Dictionary<ExpressionPreset, float> _emotions = new();
    readonly float[] _samples = new float[Window];

    async void Start()
    {
        if (avatar == null) avatar = Resources.Load<AvatarProfile>(DefaultAvatar);
        if (song == null) song = Resources.Load<SongProfile>(DefaultSong);
        if (avatar == null || song == null)
        {
            Debug.LogError($"AvatarProfile / SongProfile が見つかりません: Resources/{DefaultAvatar}, Resources/{DefaultSong}");
            return;
        }
        _mouthKey = avatar.MouthKey;
        _blinkKey = avatar.BlinkKey;

        SetupCamera();
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;

        var root = ProjectRoot();
        _vrm = await LoadVrm(Resolve(root, avatar.vrmPath));
        _audio.clip = await LoadWav(Resolve(root, song.songWavPath));
        if (_vrm == null || _audio.clip == null) return;
        _totalBeats = song.BeatsAt(_audio.clip.length);

        if (!string.IsNullOrEmpty(song.lipSyncWavPath))
        {
            var lip = await LoadWav(Resolve(root, song.lipSyncWavPath));
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
        _hips = _rig?.GetBoneTransform(HumanBodyBones.Hips);
        if (_hips != null) _hipsRestPosition = _hips.localPosition;
        foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type == LightType.Directional) light.intensity = avatar.lightIntensity;
        }

        var head = _rig?.GetBoneTransform(HumanBodyBones.Head);
        var headPosition = head != null ? head.position : _vrm.transform.position + Vector3.up * 1.2f;
        _director = new CameraDirector(_camera, avatar, headPosition);

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

    void Update()
    {
        if (_vrm == null || _director == null) return;

        // 曲の再生位置。始まる前は 0、終わったら最後で止まる
        if (_audio.isPlaying) _songTime = _audio.time;
        else if (_songTime > 0f) _songTime = _audio.clip.length;
        var beat = song.BeatsAt(_songTime);
        var (section, progress) = song.SectionAt(beat, _totalBeats);

        var k = 1f - Mathf.Exp(-3f * Time.deltaTime);
        _motion = Mathf.Lerp(_motion, (section?.motion ?? 0f) * avatar.motionScale, k);

        var expression = _vrm.Runtime.Expression;
        UpdateMouth(expression);
        var emotion = UpdateEmotion(expression, section, k);
        UpdateBlink(expression, 1f - emotion);
        UpdatePose(beat);

        _director.followSpeed = cameraFollowSpeed;
        _director.Update(section?.camera ?? SongProfile.CameraShot.Wide, progress, Time.time);
    }

    void UpdateMouth(Vrm10RuntimeExpression expression)
    {
        var target = 0f;
        if (_audio.isPlaying)
        {
            var rms = _lipSamples != null ? LipSyncRms() : OutputRms();
            target = rms < mouthThreshold ? 0f : Mathf.Clamp01((rms - mouthThreshold) * mouthGain);
        }
        _mouth = Mathf.Lerp(target, _mouth, mouthSmoothing);
        expression.SetWeight(_mouthKey, _mouth * avatar.mouthMaxWeight);
    }

    /// <summary>セクションの表情へなめらかに切り替える。いちばん強い表情の重みを返す</summary>
    float UpdateEmotion(Vrm10RuntimeExpression expression, SongProfile.Section section, float k)
    {
        var active = section != null && section.expression != ExpressionPreset.neutral ? section.expression : ExpressionPreset.custom;
        if (active != ExpressionPreset.custom && !_emotions.ContainsKey(active)) _emotions[active] = 0f;

        var strongest = 0f;
        foreach (var preset in new List<ExpressionPreset>(_emotions.Keys))
        {
            var target = preset == active ? section.expressionWeight * avatar.emotionMaxWeight : 0f;
            var weight = Mathf.Lerp(_emotions[preset], target, k);
            _emotions[preset] = weight;
            expression.SetWeight(ExpressionKey.CreateFromPreset(preset), weight);
            strongest = Mathf.Max(strongest, weight);
        }
        return strongest;
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
    /// Vrm10Instance が LateUpdate でこれを元のボーンへ反映する。モデルは +Z 向き。
    /// </summary>
    void UpdatePose(float beat)
    {
        if (_rig == null) return;
        var m = _motion;
        var onBeat = Mathf.Abs(Mathf.Sin(beat * Mathf.PI));   // 1 拍ごとに 0→1→0
        var side = Mathf.Sin(beat * Mathf.PI);                 // 2 拍で左右に 1 往復

        if (_hips != null) _hips.localPosition = _hipsRestPosition + Vector3.down * (bounceMeters * m * onBeat);
        SetBone(HumanBodyBones.Spine, Quaternion.Euler(0f, side * swayDegrees * 0.5f * m, side * swayDegrees * m));
        SetBone(HumanBodyBones.Head, Quaternion.Euler(onBeat * nodDegrees * m, 0f, -side * swayDegrees * 0.5f * m));

        // 左腕は -X 側、右腕は +X 側。Z 軸回りに回して下ろし、X 軸回りに前後へ交互に振る。ひじは前へ曲げる
        var swing = side * armSwingDegrees * m;
        var down = avatar.armDownDegrees - onBeat * armSwingDegrees * 0.3f * m;
        SetBone(HumanBodyBones.LeftUpperArm, Quaternion.Euler(swing, 0f, down));
        SetBone(HumanBodyBones.RightUpperArm, Quaternion.Euler(-swing, 0f, -down));
        var elbow = elbowBendDegrees * (0.5f + 0.5f * m);
        SetBone(HumanBodyBones.LeftLowerArm, Quaternion.Euler(0f, elbow, 0f));
        SetBone(HumanBodyBones.RightLowerArm, Quaternion.Euler(0f, -elbow, 0f));
    }

    void SetBone(HumanBodyBones bone, Quaternion rotation)
    {
        var t = _rig.GetBoneTransform(bone);
        if (t != null) t.localRotation = rotation;
    }

    void UpdateBlink(Vrm10RuntimeExpression expression, float scale)
    {
        if (_blinkTimer < 0f && Time.time >= _nextBlink) _blinkTimer = 0f;
        if (_blinkTimer < 0f) return;

        _blinkTimer += Time.deltaTime;
        const float duration = 0.15f;
        expression.SetWeight(_blinkKey, Mathf.Sin(Mathf.Clamp01(_blinkTimer / duration) * Mathf.PI) * scale);
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
