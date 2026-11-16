using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Platform;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GivreClone.App.Services;

/// <summary>Sons d'interface + ambiance via un mixeur NAudio (float 44.1k stéréo, superposables). Fichiers absents = silence.</summary>
public static class AudioService
{
    public const string Click    = "click";
    public const string Nav      = "nav";
    public const string ModalIn  = "modal_open";
    public const string ModalOut = "modal_close";
    public const string Launch   = "launch";
    public const string Success  = "success";
    public const string Error    = "error";
    private const string ErrorFallback = "error_fallback";
    public const string ToggleOn  = "toggle_on";
    public const string ToggleOff = "toggle_off";
    public const string Notify   = "notify";
    public const string Dismiss  = "dismiss";   // retrait d'une notification (whoosh discret, distinct du clic)

    private static readonly WaveFormat MixFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    private static readonly object _gate = new();
    private static readonly Dictionary<string, byte[]> _cache = new();

    private static IDisposable? _output;   // PcmWaveOut : sortie maison AOT-safe
    private static MixingSampleProvider? _mixer;
    private static ISampleProvider? _ambientInput;
    private static float[]? _ambientSamples;

    public static bool Sfx { get; private set; } = true;
    public static float SfxVolume { get; private set; } = 1f;   // 0..1, appliqué à chaque son d'interface
    public static bool Ambient { get; private set; }

    private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

    public static void ApplyFromConfig()
    {
        Sfx = AppConfig.Current.Sfx;
        SfxVolume = Clamp01(AppConfig.Current.SfxVolume);
        Ambient = AppConfig.Current.Ambient;
        if (Ambient) StartAmbient();
    }

    public static void SetSfx(bool on)
    {
        Sfx = on;
        AppConfig.Current.Sfx = on;
        AppConfig.Save();
        // bascule jouée par OnAnyToggle global (évite le double-son)
        Logger.Info($"Interface sounds {(on ? "enabled" : "disabled")}");
    }

    /// <summary>Règle le volume des sons d'interface (0..1). Appliqué immédiatement aux sons suivants + persisté.</summary>
    public static void SetSfxVolume(float volume)
    {
        SfxVolume = Clamp01(volume);
        AppConfig.Current.SfxVolume = SfxVolume;
        AppConfig.Save();
    }

    public static void SetAmbient(bool on)
    {
        Ambient = on;
        AppConfig.Current.Ambient = on;
        AppConfig.Save();
        if (on) StartAmbient(); else StopAmbient();
        Logger.Info($"Ambient sound {(on ? "enabled" : "disabled")}");
    }

    public static void PlayError()
    {
        var uri = new Uri($"avares://GivreClone/Assets/{Error}.wav");
        PlayUi(AssetLoader.Exists(uri) ? Error : ErrorFallback);
    }

    // Sons d'interface (one-shot, superposables, hors thread UI)
    public static void PlayUi(string name)
    {
        if (!Sfx || string.IsNullOrEmpty(name)) return;
        if (SfxVolume <= 0f) return;   // volume à 0 = muet

        // Exécution en tâche de fond pour ne jamais bloquer le Dispatcher UI (initialisation waveOut, I/O ou lock)
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var bytes = LoadBytes(name);
                if (bytes is null || bytes.Length == 0) return;
                lock (_gate)
                {
                    EnsureMixer();
                    var reader = new WaveFileReader(new MemoryStream(bytes));
                    // Chaque son d'interface passe par un VolumeSampleProvider -> volume réglable (Réglages). 1 = amplitude d'origine.
                    _mixer!.AddMixerInput(new VolumeSampleProvider(ToMixFormat(reader.ToSampleProvider())) { Volume = SfxVolume });
                }
            }
            catch (Exception ex) { Logger.Warn($"UI sound '{name}' failed: {ex.Message}"); }
        });
    }

    // Ambiance (boucle)
    private static void StartAmbient()
    {
        try
        {
            lock (_gate)
            {
                EnsureMixer();
                StopAmbientCore();
                EnsureAmbientLoaded();
                if (_ambientSamples is null || _ambientSamples.Length == 0) { Logger.Warn("ambient.wav introuvable"); return; }
                _ambientInput = new LoopBufferProvider(_ambientSamples, MixFormat);
                _mixer!.AddMixerInput(_ambientInput);
            }
        }
        catch (Exception ex) { Logger.Error($"Ambient failed: {ex}"); }
    }

    private static void EnsureAmbientLoaded()
    {
        if (_ambientSamples is not null) return;
        var uri = new Uri("avares://GivreClone/Assets/ambient.wav");
        if (!AssetLoader.Exists(uri)) { _ambientSamples = Array.Empty<float>(); return; }
        using var s = AssetLoader.Open(uri);
        var reader = new WaveFileReader(s);
        var sp = ToMixFormat(reader.ToSampleProvider());
        var buffer = new List<float>(1 << 20);
        var tmp = new float[8192];
        int n;
        while ((n = sp.Read(tmp, 0, tmp.Length)) > 0)
            for (int i = 0; i < n; i++) buffer.Add(tmp[i]);
        _ambientSamples = buffer.ToArray();
    }

    private static void StopAmbient() { lock (_gate) { StopAmbientCore(); } }

    private static void StopAmbientCore()
    {
        try { if (_ambientInput is not null && _mixer is not null) _mixer.RemoveMixerInput(_ambientInput); }
        catch { }
        _ambientInput = null;
    }

    // Plomberie
    private static void EnsureMixer()
    {
        if (_output is not null) return;
        _mixer = new MixingSampleProvider(MixFormat) { ReadFully = true };
        // WaveOutEvent plante sous NativeAOT (WaveHeaderUnprepared, header recopié). Voir PcmWaveOut.
        var pcm = new PcmWaveOut(new SampleToWaveProvider16(_mixer));
        pcm.Play();
        _output = pcm;
    }

    private static ISampleProvider ToMixFormat(ISampleProvider sp)
    {
        if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);
        if (sp.WaveFormat.SampleRate != MixFormat.SampleRate) sp = new WdlResamplingSampleProvider(sp, MixFormat.SampleRate);
        return sp;
    }

    private static byte[]? LoadBytes(string name)
    {
        if (_cache.TryGetValue(name, out var cached)) return cached.Length == 0 ? null : cached;
        var uri = new Uri($"avares://GivreClone/Assets/{name}.wav");
        if (!AssetLoader.Exists(uri)) { _cache[name] = Array.Empty<byte>(); return null; }
        using var s = AssetLoader.Open(uri);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        var arr = ms.ToArray();
        _cache[name] = arr;
        return arr;
    }

    private sealed class LoopBufferProvider : ISampleProvider
    {
        private readonly float[] _samples;
        private int _pos;
        public LoopBufferProvider(float[] samples, WaveFormat format) { _samples = samples; WaveFormat = format; }
        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            if (_samples.Length == 0) { for (int i = 0; i < count; i++) buffer[offset + i] = 0f; return count; }
            for (int i = 0; i < count; i++)
            {
                buffer[offset + i] = _samples[_pos++];
                if (_pos >= _samples.Length) _pos = 0;
            }
            return count;
        }
    }
}
