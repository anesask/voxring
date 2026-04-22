namespace Loupedeck.VoxRingPlugin.Services;

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Loupedeck.VoxRingPlugin.Models;

/// <summary>
/// Unified text-to-speech facade with two backends:
///
/// <list type="bullet">
/// <item><b>Piper</b> — neural TTS, offline, much better voice quality. Auto-used if the binary and
/// at least one voice model are present under <c>%LOCALAPPDATA%\VoxRing\tts\</c>.</item>
/// <item><b>SAPI</b> — Windows Speech API via COM interop (NOT System.Speech.Synthesis, which is
/// broken on .NET 8). Always available on Windows. Uses whichever voices are installed —
/// installing Windows 11 neural voices transparently upgrades this path.</item>
/// </list>
///
/// Active backend is picked at first call: Piper if available, else SAPI. If Piper fails mid-call
/// (missing model, bad exit code), we fall back to SAPI so the user isn't left in silence.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TtsService
{
    // ---------- SAPI flags ----------
    private const int SVSFlagsAsync = 1;            // speak on a SAPI thread; return immediately
    private const int SVSFPurgeBeforeSpeak = 2;     // cancel anything currently queued

    // Resolved paths — prefer the bundled (in-plugin) location, fall back to the downloaded one.
    private static string PiperExe => PiperInstaller.ActivePiperExe;
    private static string PiperDir => PiperInstaller.ActivePiperDir;
    private static string VoicesDir => PiperInstaller.ActiveVoicesDir;

    private static readonly Lazy<TtsService> _instance = new(() => new TtsService());
    public static TtsService Instance => _instance.Value;

    // ---------- SAPI state ----------
    private readonly object _sapiLock = new();
    private dynamic _sapi;
    private bool _sapiAvailable;

    // ---------- Piper state ----------
    private readonly object _piperLock = new();
    private CancellationTokenSource _piperCts;
    private WaveOutEvent _piperPlayer;

    public enum TtsBackend { None, Sapi, Piper }

    public TtsBackend ActiveBackend
    {
        get
        {
            if (IsPiperAvailable) return TtsBackend.Piper;
            if (_sapiAvailable) return TtsBackend.Sapi;
            return TtsBackend.None;
        }
    }

    public bool IsAvailable => ActiveBackend != TtsBackend.None;

    private static bool IsPiperAvailable =>
        !string.IsNullOrEmpty(PiperExe) && File.Exists(PiperExe)
        && !string.IsNullOrEmpty(VoicesDir) && Directory.Exists(VoicesDir)
        && Directory.GetFiles(VoicesDir, "*.onnx").Length > 0;

    private TtsService()
    {
        TryInitializeSapi();
        PluginLog.Info($"TTS: SAPI={_sapiAvailable}, Piper={IsPiperAvailable} -> active={ActiveBackend}");
        if (IsPiperAvailable)
        {
            PluginLog.Info($"TTS Piper source: bin={PiperInstaller.PiperBinarySource}, en={PiperInstaller.EnVoiceSource}, de={PiperInstaller.DeVoiceSource}");
        }
    }

    private void TryInitializeSapi()
    {
        try
        {
            var type = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (type == null)
            {
                PluginLog.Warning("TTS: SAPI.SpVoice ProgID not found");
                return;
            }
            _sapi = Activator.CreateInstance(type);
            _sapi.Rate = 0;
            _sapi.Volume = 90;
            _sapiAvailable = true;
        }
        catch (Exception ex)
        {
            _sapiAvailable = false;
            PluginLog.Warning(ex, "TTS: SAPI init failed");
        }
    }

    public void Speak(string text, string languageOverride = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var lang = string.IsNullOrEmpty(languageOverride) ? VoxRingState.SelectedLanguage : languageOverride;
        switch (ActiveBackend)
        {
            case TtsBackend.Piper:
                PiperSpeak(text, lang);
                break;
            case TtsBackend.Sapi:
                SapiSpeak(text, lang);
                break;
            default:
                PluginLog.Warning("TTS: no backend available");
                break;
        }
    }

    public void Stop()
    {
        SapiStop();
        PiperStop();
    }

    // =====================================================
    //                        SAPI
    // =====================================================

    private void SapiSpeak(string text, string lang = null)
    {
        if (!_sapiAvailable) return;
        lock (_sapiLock)
        {
            try
            {
                SelectSapiVoiceForLanguage(lang ?? VoxRingState.SelectedLanguage);
                _sapi.Speak(text, SVSFlagsAsync | SVSFPurgeBeforeSpeak);
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "TTS(SAPI): Speak failed");
            }
        }
    }

    private void SapiStop()
    {
        if (!_sapiAvailable) return;
        lock (_sapiLock)
        {
            try { _sapi.Speak(string.Empty, SVSFPurgeBeforeSpeak); }
            catch (Exception ex) { PluginLog.Warning(ex, "TTS(SAPI): Stop failed"); }
        }
    }

    // SAPI stores its language attribute as a hex LANGID string (e.g. "409" = en-US).
    // We map VoxRingState.SelectedLanguage ("en" | "de" | "auto") to a set of candidate LANGIDs
    // and pick the first installed voice whose Language attribute contains any of them.
    private void SelectSapiVoiceForLanguage(string lang)
    {
        if (string.IsNullOrEmpty(lang) || lang == "auto") return;

        var wanted = lang.ToLowerInvariant() switch
        {
            "en" => new[] { "409", "809", "c09", "1009", "1409", "1809" }, // en-US, en-GB, en-AU, en-CA, en-NZ, en-ZA
            "de" => new[] { "407", "807", "c07", "1007", "1407" },         // de-DE, de-CH, de-AT, de-LU, de-LI
            _ => null,
        };
        if (wanted == null) return;

        try
        {
            var voices = _sapi.GetVoices();
            int count = voices.Count;
            for (int i = 0; i < count; i++)
            {
                var voice = voices.Item(i);
                string langAttr;
                try { langAttr = (string)voice.GetAttribute("Language"); }
                catch { continue; }
                if (string.IsNullOrEmpty(langAttr)) continue;

                foreach (var id in wanted)
                {
                    if (langAttr.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _sapi.Voice = voice;
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            PluginLog.Warning(ex, "TTS(SAPI): voice selection failed");
        }
    }

    // =====================================================
    //                       Piper
    // =====================================================

    private void PiperSpeak(string text, string lang = null)
    {
        var modelPath = ChoosePiperVoice(lang ?? VoxRingState.SelectedLanguage);
        if (modelPath == null)
        {
            PluginLog.Warning("TTS(Piper): no voice model found, falling back to SAPI");
            SapiSpeak(text, lang);
            return;
        }

        PiperStop(); // cancel any in-flight playback first
        CancellationTokenSource cts;
        lock (_piperLock)
        {
            _piperCts = cts = new CancellationTokenSource();
        }

        Task.Run(() => PiperSpeakInternal(text, modelPath, cts.Token, lang));
    }

    // Pick the first voice model whose filename prefix matches the selected language (e.g. en_US-... / de_DE-...).
    // Falls back to the first model in the folder if nothing matches.
    private static string ChoosePiperVoice(string lang)
    {
        if (string.IsNullOrEmpty(VoicesDir) || !Directory.Exists(VoicesDir)) return null;
        var models = Directory.GetFiles(VoicesDir, "*.onnx");
        if (models.Length == 0) return null;

        var prefix = lang?.ToLowerInvariant() switch
        {
            "en" => "en",
            "de" => "de",
            _ => null,
        };
        if (prefix != null)
        {
            foreach (var m in models)
            {
                if (Path.GetFileName(m).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return m;
            }
        }
        return models[0];
    }

    private void PiperSpeakInternal(string text, string modelPath, CancellationToken ct, string lang = null)
    {
        string tempWav = Path.Combine(Path.GetTempPath(), $"voxring-tts-{Guid.NewGuid():N}.wav");
        try
        {
            var psi = new ProcessStartInfo(PiperExe)
            {
                Arguments = $"--model \"{modelPath}\" --output_file \"{tempWav}\"",
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = PiperDir,  // so Piper finds espeak-ng-data next to the exe
            };
            using var p = Process.Start(psi);
            if (p == null)
            {
                PluginLog.Warning("TTS(Piper): Process.Start returned null, falling back to SAPI");
                SapiSpeak(text, lang);
                return;
            }

            p.StandardInput.Write(text);
            p.StandardInput.Close();
            if (!p.WaitForExit(15000))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                PluginLog.Warning("TTS(Piper): timeout after 15s, falling back to SAPI");
                SapiSpeak(text, lang);
                return;
            }
            if (ct.IsCancellationRequested) return;
            if (p.ExitCode != 0)
            {
                PluginLog.Warning($"TTS(Piper): exit {p.ExitCode}: {p.StandardError.ReadToEnd()}, falling back to SAPI");
                SapiSpeak(text, lang);
                return;
            }
            if (!File.Exists(tempWav))
            {
                PluginLog.Warning("TTS(Piper): no output file produced, falling back to SAPI");
                SapiSpeak(text, lang);
                return;
            }

            using var reader = new WaveFileReader(tempWav);
            using var player = new WaveOutEvent();
            lock (_piperLock) { _piperPlayer = player; }

            var done = new ManualResetEventSlim(false);
            player.PlaybackStopped += (_, _) => done.Set();
            player.Init(reader);
            player.Play();

            while (!ct.IsCancellationRequested && player.PlaybackState == PlaybackState.Playing)
            {
                done.Wait(100);
            }
            try { player.Stop(); } catch { }
        }
        catch (Exception ex)
        {
            PluginLog.Warning(ex, "TTS(Piper): internal error, falling back to SAPI");
            try { SapiSpeak(text, lang); } catch { }
        }
        finally
        {
            lock (_piperLock) { _piperPlayer = null; }
            try { if (File.Exists(tempWav)) File.Delete(tempWav); } catch { }
        }
    }

    private void PiperStop()
    {
        CancellationTokenSource cts;
        WaveOutEvent player;
        lock (_piperLock)
        {
            cts = _piperCts;
            player = _piperPlayer;
            _piperCts = null;
        }
        try { cts?.Cancel(); } catch { }
        try { player?.Stop(); } catch { }
    }
}
