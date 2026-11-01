using System.IO;
using System.Threading.Tasks;
using UniVRM10;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// VRM を読み込み、wav に合わせて口パクと体の揺れをさせるステージ。
/// モデルと音声はリポジトリ内の avatar/ と voice/ から実行時に読み込む（モデルは git 管理外）。
/// </summary>
public class SingingStage : MonoBehaviour
{
    [Header("ファイル（リポジトリのルートからの相対パス、または絶対パス）")]
    public string vrmPath = "avatar/models/ずんだもん2025モデルセット ver.A1/VRM/Zundamon_2025_VRM10A.vrm";
    public string wavPath = "voice/renders/zundamon_test.wav";

    [Header("口パク")]
    [Tooltip("音量(RMS)をこの倍率で口の開きに変換")]
    public float mouthGain = 8f;
    [Tooltip("この音量以下は口を閉じる")]
    public float mouthThreshold = 0.01f;
    [Range(0f, 1f)] public float mouthSmoothing = 0.5f;

    [Header("体の揺れ")]
    public float swayDegrees = 4f;
    public float swaySpeed = 1.6f;

    [Header("投影")]
    [Tooltip("ビルドしたアプリで 2 台目のディスプレイがあれば全画面でそちらに出す")]
    public bool useSecondDisplay = true;
    public float startDelaySeconds = 1f;

    Camera _camera;
    AudioSource _audio;
    Vrm10Instance _vrm;
    Transform _spine;
    Transform _head;
    Quaternion _rootRotation;
    float _mouth;
    float _nextBlink;
    float _blinkTimer = -1f;
    readonly float[] _samples = new float[1024];

    async void Start()
    {
        SetupCamera();
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;

        var root = ProjectRoot();
        _vrm = await LoadVrm(Resolve(root, vrmPath));
        _audio.clip = await LoadWav(Resolve(root, wavPath));
        if (_vrm == null || _audio.clip == null) return;

        var animator = _vrm.GetComponent<Animator>();
        if (animator != null && animator.isHuman)
        {
            _spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            _head = animator.GetBoneTransform(HumanBodyBones.Head);
        }
        _rootRotation = _vrm.transform.rotation;
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
        var target = _head != null ? _head.position : _vrm.transform.position + Vector3.up * 1.2f;
        target.y -= 0.35f;
        _camera.transform.position = target + new Vector3(0f, 0.05f, 2.4f);
        _camera.transform.LookAt(target);
    }

    void Update()
    {
        if (_vrm == null) return;
        var expression = _vrm.Runtime.Expression;

        var target = 0f;
        if (_audio.isPlaying)
        {
            _audio.GetOutputData(_samples, 0);
            var sum = 0f;
            foreach (var s in _samples) sum += s * s;
            var rms = Mathf.Sqrt(sum / _samples.Length);
            target = rms < mouthThreshold ? 0f : Mathf.Clamp01((rms - mouthThreshold) * mouthGain);
        }
        _mouth = Mathf.Lerp(target, _mouth, mouthSmoothing);
        expression.SetWeight(ExpressionKey.Aa, _mouth);

        UpdateBlink(expression);

        var t = Time.time * swaySpeed;
        _vrm.transform.rotation = _rootRotation * Quaternion.Euler(0f, Mathf.Sin(t * 0.5f) * swayDegrees * 0.5f, 0f);
    }

    void LateUpdate()
    {
        if (_vrm == null) return;
        var t = Time.time * swaySpeed;
        if (_spine != null) _spine.localRotation *= Quaternion.Euler(0f, 0f, Mathf.Sin(t) * swayDegrees);
        if (_head != null) _head.localRotation *= Quaternion.Euler(Mathf.Sin(t * 2f) * swayDegrees * 0.5f * _mouth, 0f, 0f);
    }

    void UpdateBlink(Vrm10RuntimeExpression expression)
    {
        if (_blinkTimer < 0f && Time.time >= _nextBlink) _blinkTimer = 0f;
        if (_blinkTimer < 0f) return;

        _blinkTimer += Time.deltaTime;
        const float duration = 0.15f;
        expression.SetWeight(ExpressionKey.Blink, Mathf.Sin(Mathf.Clamp01(_blinkTimer / duration) * Mathf.PI));
        if (_blinkTimer >= duration)
        {
            expression.SetWeight(ExpressionKey.Blink, 0f);
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
