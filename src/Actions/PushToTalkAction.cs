namespace Loupedeck.VoxRingPlugin.Actions;

using System.Runtime.Versioning;
using Loupedeck.VoxRingPlugin.Destinations;
using Loupedeck.VoxRingPlugin.Models;

[SupportedOSPlatform("windows")]
public class PushToTalkAction : PluginDynamicCommand
{
    private new VoxRingPlugin Plugin => base.Plugin as VoxRingPlugin;

    private System.Threading.Timer _pulseTimer;
    private System.Threading.Timer _autoStopTimer;
    private System.Threading.Timer _tickTimer;
    private DateTime _recordingStartUtc;

    // Hybrid press detection: below this threshold a press-release counts as a "click" and
    // recording stays active (tap-toggle). Above, classic push-to-talk: release stops and sends.
    private const int MinHoldMs = 300;
    private DateTime _pressedAtUtc;
    private bool _secondTapWhileRecording;

    public PushToTalkAction()
        : base(displayName: "Push to Talk", description: "Hold to record and release to send. Quick-tap to start, tap again to stop.", groupName: "1 Voice")
    {
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
        PluginResources.ReadImage("radio.svg");

    protected override async void RunCommand(String actionParameter)
    {
        if (VoxRingState.IsProcessing)
            return;

        if (VoxRingState.IsDownloadingModels)
        {
            PluginLog.Warning("Models still downloading - cannot record yet");
            return;
        }

        if (VoxRingState.IsRecording && VoxRingState.CurrentRecordingMode != RecordingMode.PushToTalk)
        {
            PluginLog.Warning("Push to Talk: another recording is in progress - ignored");
            return;
        }

        if (!VoxRingState.IsRecording)
            StartRecording();
        else
            await StopTranscribeAndSendAsync(autoStopped: false);
    }

    // Hybrid Press/Release handling:
    //   - Press (not recording)       -> start recording, record press time
    //   - Press (already recording)   -> mark as "second tap" so the following Release stops regardless of hold time
    //   - Release, held < MinHoldMs   -> user quick-clicked; leave recording on, wait for a second tap
    //   - Release, held >= MinHoldMs  -> true push-to-talk; stop + transcribe + send
    //   - Release, second-tap flag    -> stop + transcribe + send
    protected override Boolean ProcessButtonEvent2(String actionParameter, DeviceButtonEvent2 buttonEvent)
    {
        PluginLog.Info($"PTT button event: {buttonEvent.EventType}");
        switch (buttonEvent.EventType)
        {
            case DeviceButtonEventType.Press:
                if (VoxRingState.IsProcessing || VoxRingState.IsDownloadingModels)
                    return true;
                if (VoxRingState.IsRecording && VoxRingState.CurrentRecordingMode != RecordingMode.PushToTalk)
                    return true;

                if (!VoxRingState.IsRecording)
                {
                    _pressedAtUtc = DateTime.UtcNow;
                    _secondTapWhileRecording = false;
                    StartRecording();
                }
                else
                {
                    // Already recording from a previous click: treat this press as a stop-request.
                    _secondTapWhileRecording = true;
                }
                return true;

            case DeviceButtonEventType.Release:
                if (!VoxRingState.IsRecording || VoxRingState.CurrentRecordingMode != RecordingMode.PushToTalk)
                    return true;

                if (_secondTapWhileRecording)
                {
                    _secondTapWhileRecording = false;
                    _ = StopTranscribeAndSendAsync(autoStopped: false);
                    return true;
                }

                var heldMs = (DateTime.UtcNow - _pressedAtUtc).TotalMilliseconds;
                if (heldMs < MinHoldMs)
                {
                    PluginLog.Info($"PTT quick-click ({heldMs:F0}ms < {MinHoldMs}ms): keep recording, waiting for second tap to send");
                    return true;
                }

                _ = StopTranscribeAndSendAsync(autoStopped: false);
                return true;
        }
        // Consume all other button events so they don't fall through to RunCommand's toggle.
        return true;
    }

    private void StartRecording()
    {
        Plugin.AudioRecorder.StartRecording();
        VoxRingState.IsRecording = true;
        VoxRingState.CurrentRecordingMode = RecordingMode.PushToTalk;
        if (!VoxRingState.AppendMode)
            VoxRingState.CurrentTranscript = string.Empty;
        VoxRingState.FormattedOutputs.Clear();
        VoxRingState.LastSendResult = null;

        Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticRecordStart);
        PluginLog.Info("Push to Talk started");
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

        this.ActionImageChanged();
    }

    private async Task AutoStopAsync()
    {
        if (!VoxRingState.IsRecording)
            return;

        Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticRecordAutoStop);
        PluginLog.Info($"Push to Talk auto-stopped at {VoxRingState.MaxRecordingSeconds}s limit");
        await StopTranscribeAndSendAsync(autoStopped: true);
    }

    private async Task StopTranscribeAndSendAsync(bool autoStopped)
    {
        StopTimers();

        var wavData = Plugin.AudioRecorder.StopRecording();
        VoxRingState.IsRecording = false;
        VoxRingState.CurrentRecordingMode = RecordingMode.None;
        VoxRingState.IsProcessing = true;
        this.ActionImageChanged();

        PluginLog.Info($"Push to Talk stopped{(autoStopped ? " (auto)" : "")}, {wavData.Length} bytes");

        try
        {
            // Transcribe
            var transcript = await Plugin.SpeechRecognition.RecognizeFromWavAsync(wavData);
            VoxRingState.SetTranscript(transcript);
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticTranscriptionComplete);
            PluginLog.Info($"PTT transcript: {transcript}");

            if (string.IsNullOrEmpty(transcript))
            {
                VoxRingState.LastSendResult = "Empty";
                return;
            }

            // Send to current mode
            var destination = DestinationRegistry.Current;
            if (destination == null)
            {
                VoxRingState.LastSendResult = "No mode";
                Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
                return;
            }

            var textToSend = await ResolveTextAsync(destination);
            var success = await destination.SendAsync(textToSend);

            if (success)
            {
                Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendSuccess);
                VoxRingState.LastSendResult = $"Sent to {destination.Name}";
                PluginLog.Info($"PTT sent to {destination.Name}");
            }
            else
            {
                Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
                VoxRingState.LastSendResult = $"Failed: {destination.Name}";
            }
        }
        catch (Exception ex)
        {
            VoxRingState.IsProcessingAi = false;
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
            VoxRingState.LastSendResult = "Error";
            PluginLog.Error($"Push to Talk failed: {ex.Message}");
        }
        finally
        {
            VoxRingState.IsProcessing = false;
            this.ActionImageChanged();
        }
    }

    private async Task<string> ResolveTextAsync(IDestination destination)
    {
        if (string.IsNullOrEmpty(destination.AiPrompt) || !VoxRingState.UseAi)
            return VoxRingState.CurrentTranscript;

        if (!VoxRingState.IsClaudeAvailable)
        {
            PluginLog.Warning($"{destination.Name}: Claude API key not set - sending raw transcript");
            return VoxRingState.CurrentTranscript;
        }

        VoxRingState.IsProcessingAi = true;
        this.ActionImageChanged();

        try
        {
            var prompt = VoxRingState.GetEffectivePrompt(destination.Name, destination.AiPrompt);
            var formatted = await Plugin.ClaudeApi.ReformatAsync(
                VoxRingState.CurrentTranscript, prompt, VoxRingState.SelectedLanguage);
            VoxRingState.FormattedOutputs[destination.Name] = formatted;
            return formatted;
        }
        finally
        {
            VoxRingState.IsProcessingAi = false;
        }
    }

    private void StopTimers()
    {
        _pulseTimer?.Dispose();
        _pulseTimer = null;
        _autoStopTimer?.Dispose();
        _autoStopTimer = null;
        _tickTimer?.Dispose();
        _tickTimer = null;
    }

    protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
    {
        if (VoxRingState.IsRecording && VoxRingState.CurrentRecordingMode == RecordingMode.PushToTalk)
        {
            var elapsed = (int)(DateTime.UtcNow - _recordingStartUtc).TotalSeconds;
            var remaining = Math.Max(0, VoxRingState.MaxRecordingSeconds - elapsed);
            return $"Talk{Environment.NewLine}{remaining}s";
        }

        if (VoxRingState.IsProcessingAi)
            return "Formatting...";

        if (VoxRingState.IsProcessing)
            return "Processing...";

        // Idle: icon-only (matches Dictate). Status messages auto-clear on next recording.
        return string.Empty;
    }
}
