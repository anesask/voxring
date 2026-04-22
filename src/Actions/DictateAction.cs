namespace Loupedeck.VoxRingPlugin.Actions;

using System.Runtime.Versioning;
using NAudio.Wave;
using Loupedeck.VoxRingPlugin.Models;
using Loupedeck.VoxRingPlugin.Services;
using Loupedeck.VoxRingPlugin.Services.Ai;
using Loupedeck.VoxRingPlugin.Helpers;

[SupportedOSPlatform("windows")]
public class DictateAction : PluginDynamicCommand
{
    private new VoxRingPlugin Plugin => base.Plugin as VoxRingPlugin;

    private System.Threading.Timer _pulseTimer;
    private System.Threading.Timer _autoStopTimer;
    private System.Threading.Timer _autoStopWarningTimer;
    private System.Threading.Timer _tickTimer;
    private System.Threading.Timer _emptyClearTimer;
    private System.Threading.Timer _langClearTimer;
    private DateTime _recordingStartUtc;

    // Bug-fix tuning knobs for Dictate as documented in FEATURES.md.
    // Kept conservative so reflex double-taps are rejected but normal tap-stop after a short utterance goes through.
    private const int MinRecordingMs = 150;            // taps within this window of start are swallowed
    private const int AutoStopWarningSec = 5;          // seconds before auto-stop we fire a warning haptic
    private const int EmptyDisplayMs = 3000;           // how long "No speech" label stays visible

    private DateTime _emptyShownUntilUtc  = DateTime.MinValue;
    private DateTime _langShownUntilUtc   = DateTime.MinValue;
    private string   _displayLang         = null;

    public DictateAction()
        : base(displayName: "Dictate", description: "Tap to start recording. Tap again to stop and transcribe. AI formats the result for your chosen destination.", groupName: "1 Voice")
    {
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
        PluginResources.ReadImage("dictate.svg");

    // Tap-toggle only. We deliberately do NOT override ProcessButtonEvent2 here: on some
    // hardware, returning true from ProcessButtonEvent2 did not consistently suppress the
    // follow-up RunCommand dispatch, so a single click caused start + immediate stop (and with
    // the MinRecordingMs guard, the stop was swallowed and recording stayed on). Sticking to
    // RunCommand gives us one reliable entry point per physical tap.
    protected override async void RunCommand(String actionParameter)
    {
        await HandleTapAsync();
    }

    private async Task HandleTapAsync()
    {
        if (VoxRingState.IsProcessing)
        {
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
            PluginLog.Info("Dictate: tap ignored - busy transcribing");
            return;
        }

        if (VoxRingState.IsDownloadingModels)
        {
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
            PluginLog.Warning("Models still downloading - cannot record yet");
            return;
        }

        // Another action owns the current recording - don't touch it
        if (VoxRingState.IsRecording && VoxRingState.CurrentRecordingMode != RecordingMode.Dictate)
        {
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
            PluginLog.Warning("Dictate: another recording is in progress - ignored");
            return;
        }

        if (!VoxRingState.IsRecording)
            StartRecording();
        else
            await StopAndTranscribeAsync(autoStopped: false);
    }

    private bool IsMicrophoneAvailable(out string reason)
    {
        try
        {
            var count = WaveInEvent.DeviceCount;
            if (count == 0)
            {
                reason = "No microphone detected";
                return false;
            }

            var idx = VoxRingState.SelectedMicrophoneIndex;
            if (idx >= 0 && idx >= count)
            {
                reason = $"Selected mic index {idx} out of range (only {count} device{(count == 1 ? "" : "s")})";
                return false;
            }

            reason = null;
            return true;
        }
        catch (Exception ex)
        {
            reason = $"Mic check failed: {ex.Message}";
            return false;
        }
    }

    private void StartRecording()
    {
        // Mic pre-flight: refuse loud rather than silently recording 0 bytes.
        if (!IsMicrophoneAvailable(out var micReason))
        {
            VoxRingState.LastSendResult = "No mic";
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
            PluginLog.Error($"Dictate: cannot start recording - {micReason}");
            this.ActionImageChanged();
            return;
        }

        Plugin.AudioRecorder.StartRecording();
        VoxRingState.IsRecording = true;
        VoxRingState.CurrentRecordingMode = RecordingMode.Dictate;
        if (!VoxRingState.AppendMode)
            VoxRingState.CurrentTranscript = string.Empty;
        VoxRingState.FormattedOutputs.Clear();
        VoxRingState.LastSendResult = null;

        _emptyShownUntilUtc = DateTime.MinValue; // clear any stale labels
        _langShownUntilUtc  = DateTime.MinValue;
        _displayLang        = null;
        _emptyClearTimer?.Dispose();
        _emptyClearTimer = null;

        Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticRecordStart);
        PluginLog.Info("Recording started");
        _recordingStartUtc = DateTime.UtcNow;

        _pulseTimer = new System.Threading.Timer(
            _ => Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticRecordPulse),
            null, 2000, 2000);

        _tickTimer = new System.Threading.Timer(
            _ => this.ActionImageChanged(),
            null, 1000, 1000);

        var maxMs = VoxRingState.MaxRecordingSeconds * 1000;
        _autoStopTimer = new System.Threading.Timer(
            _ => _ = AutoStopAsync(),
            null, maxMs, System.Threading.Timeout.Infinite);

        // Warn-haptic 5s before auto-stop so a user who drifted away feels "wrap it up".
        var warningMs = Math.Max(0, maxMs - AutoStopWarningSec * 1000);
        if (warningMs > 0)
        {
            _autoStopWarningTimer = new System.Threading.Timer(
                _ => Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticRecordAutoStop),
                null, warningMs, System.Threading.Timeout.Infinite);
        }

        this.ActionImageChanged();
    }

    private async Task AutoStopAsync()
    {
        if (!VoxRingState.IsRecording)
            return;

        Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticRecordAutoStop);
        PluginLog.Info($"Recording auto-stopped at {VoxRingState.MaxRecordingSeconds}s limit");
        await StopAndTranscribeAsync(autoStopped: true);
    }

    private async Task StopAndTranscribeAsync(bool autoStopped)
    {
        // Minimum-duration guard: a stop-tap that lands within MinRecordingMs of start is almost
        // always an accidental double-tap. Swallow it and keep recording.
        if (!autoStopped)
        {
            var elapsedMs = (DateTime.UtcNow - _recordingStartUtc).TotalMilliseconds;
            if (elapsedMs < MinRecordingMs)
            {
                PluginLog.Info($"Dictate: stop ignored (only {elapsedMs:F0}ms elapsed, min {MinRecordingMs}ms)");
                Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
                return;
            }
        }

        StopTimers();

        var wavData = Plugin.AudioRecorder.StopRecording();
        VoxRingState.IsRecording = false;
        VoxRingState.CurrentRecordingMode = RecordingMode.None;
        VoxRingState.IsProcessing = true;
        this.ActionImageChanged();

        PluginLog.Info($"Recording stopped{(autoStopped ? " (auto)" : "")}, {wavData.Length} bytes captured");

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var transcript = await Plugin.SpeechRecognition.RecognizeFromWavAsync(wavData);
            sw.Stop();

            if (!string.IsNullOrWhiteSpace(transcript))
            {
                // Pipeline order matters: filler → case → translate.
                // Case transform runs on the source language so it applies correctly to German nouns etc.
                // Translate runs last so it operates on the clean, case-adjusted source text.
                // Future: kick off background AI pre-format here so formatted output is ready by the
                // time the user opens the Send folder (hides the AI latency from the critical path).
                if (VoxRingState.UseFillerWordCleaner)
                    transcript = FillerWordFilter.Apply(transcript);
                transcript = CaseTransformer.Apply(transcript, VoxRingState.SelectedCaseTransform);

                var targetLang = VoxRingState.TranslateTargetLanguage;
                if (!string.IsNullOrEmpty(targetLang))
                {
                    var provider = AiProviderRegistry.Current;
                    if (provider.IsAvailable)
                    {
                        var langName = LanguageCodeToName(targetLang);
                        var translated = await provider.ReformatAsync(transcript,
                            $"Translate the following text to {langName}. Return only the translated text, no explanations.",
                            targetLang);
                        if (!string.IsNullOrWhiteSpace(translated))
                            transcript = translated;
                    }
                    else
                    {
                        PluginLog.Warning("Translate: no AI provider configured, skipping");
                    }
                }
            }

            // Whisper emits a language tag per segment; Vosk doesn't detect language at all.
            // The 4-second flash gives the user confirmation of which language was recognized
            // without staying on screen long enough to obscure the next idle state.
            var lang = VoxRingState.DetectedLanguage;
            if (!string.IsNullOrEmpty(lang))
            {
                _displayLang = lang.ToUpper();
                _langShownUntilUtc = DateTime.UtcNow.AddSeconds(4);
                _langClearTimer?.Dispose();
                _langClearTimer = new System.Threading.Timer(
                    _ => this.ActionImageChanged(),
                    null, 4100, System.Threading.Timeout.Infinite);
            }

            VoxRingState.SetTranscript(transcript);

            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticTranscriptionComplete);
            PluginLog.Info($"Transcript ({sw.ElapsedMilliseconds}ms): {transcript}");

            if (string.IsNullOrWhiteSpace(transcript))
            {
                // Visible feedback: "No speech" for a few seconds, then clear automatically.
                _emptyShownUntilUtc = DateTime.UtcNow.AddMilliseconds(EmptyDisplayMs);
                _emptyClearTimer?.Dispose();
                _emptyClearTimer = new System.Threading.Timer(
                    _ => this.ActionImageChanged(),
                    null, EmptyDisplayMs + 50, System.Threading.Timeout.Infinite);
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Transcription failed: {ex.Message}");
            if (!VoxRingState.AppendMode)
                VoxRingState.CurrentTranscript = string.Empty;
        }
        finally
        {
            VoxRingState.IsProcessing = false;
            this.ActionImageChanged();
        }
    }

    private static string LanguageCodeToName(string code) => code switch
    {
        "en" => "English",
        "de" => "German",
        "fr" => "French",
        "es" => "Spanish",
        "it" => "Italian",
        "pt" => "Portuguese",
        "nl" => "Dutch",
        "pl" => "Polish",
        "ru" => "Russian",
        "zh" => "Chinese",
        _    => code
    };

    protected override bool OnUnload()
    {
        StopTimers();
        _emptyClearTimer?.Dispose();
        _emptyClearTimer = null;
        _langClearTimer?.Dispose();
        _langClearTimer = null;
        return base.OnUnload();
    }

    private void StopTimers()
    {
        _pulseTimer?.Dispose();
        _pulseTimer = null;
        _autoStopTimer?.Dispose();
        _autoStopTimer = null;
        _autoStopWarningTimer?.Dispose();
        _autoStopWarningTimer = null;
        _tickTimer?.Dispose();
        _tickTimer = null;
    }

    protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
    {
        if (VoxRingState.IsDownloadingModels)
            return VoxRingState.ModelDownloadStatus ?? "Downloading...";

        if (VoxRingState.IsRecording && VoxRingState.CurrentRecordingMode == RecordingMode.Dictate)
        {
            var elapsed = (int)(DateTime.UtcNow - _recordingStartUtc).TotalSeconds;
            var remaining = Math.Max(0, VoxRingState.MaxRecordingSeconds - elapsed);
            return $"Listen{Environment.NewLine}{remaining}s";
        }

        if (VoxRingState.IsProcessing)
            return "Transcribing...";

        // Recently finished with no speech detected: flash a label for a few seconds.
        if (DateTime.UtcNow < _emptyShownUntilUtc)
            return $"No speech{Environment.NewLine}detected";

        // Show detected language briefly after transcription
        if (DateTime.UtcNow < _langShownUntilUtc && _displayLang != null)
            return _displayLang;

        // Idle: icon-only (no text label)
        return string.Empty;
    }
}
