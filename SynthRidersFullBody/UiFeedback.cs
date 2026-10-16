using UnityEngine;

namespace SynthRidersFullBody;

/// <summary>操作確定時だけ短い振動と合成クリック音を返します。失敗をUI・追跡へ伝播させません。</summary>
internal sealed class UiFeedback
{
    private GameObject? _root;
    private AudioSource? _source;
    private AudioClip? _clip;
    private bool _audioUnavailable;

    /// <summary>入力判定が確定した1回の操作に対し、対応する手へだけ通知します。</summary>
    public void Play(ModConfig config, Tracking tracking, UiHand hand)
    {
        if (config.UiHaptics && hand != UiHand.None)
            try { tracking.SendUiHaptic(hand == UiHand.Left); } catch { /* Unsupported haptics must not block input. */ }
        if (config.UiSound && !_audioUnavailable)
            try { PlaySound(); } catch { _audioUnavailable = true; }
    }

    /// <summary>生成した音源と波形をUI再生成・MOD終了時に解放します。</summary>
    public void Reset()
    {
        try { if (_root != null) UnityEngine.Object.Destroy(_root); } catch { }
        try { if (_clip != null) UnityEngine.Object.Destroy(_clip); } catch { }
        _root = null;
        _source = null;
        _clip = null;
        _audioUnavailable = false;
    }

    /// <summary>他の音源・音量設定を変更せず、短い2Dクリックを低音量で再生します。</summary>
    private void PlaySound()
    {
        if (_root == null)
        {
            _root = new GameObject("SynthRidersFullBody.UiFeedback");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _source = _root.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0;
            _source.volume = .12f;
            _clip = AudioClip.Create("FullBodyUiClick", 1440, 1, 48000, false);
            var samples = new float[1440];
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / 48000f;
                var fade = MathF.Sin(MathF.PI * i / (samples.Length - 1));
                samples[i] = .35f * fade * fade * MathF.Sin(2 * MathF.PI * 880 * t);
            }
            if (!_clip.SetData(samples, 0)) throw new InvalidOperationException("UI audio data unavailable.");
        }
        _source!.PlayOneShot(_clip);
    }
}
