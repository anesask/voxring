namespace Loupedeck.VoxRingPlugin.Actions;

using System.Runtime.Versioning;
using Loupedeck.VoxRingPlugin.Destinations;
using Loupedeck.VoxRingPlugin.Models;
using Loupedeck.VoxRingPlugin.Services.Ai;

[SupportedOSPlatform("windows")]
public abstract class QuickSendActionBase : PluginDynamicCommand
{
    protected new VoxRingPlugin Plugin => base.Plugin as VoxRingPlugin;

    private System.Threading.Timer _pulseTimer;
    private System.Threading.Timer _autoStopTimer;
    private System.Threading.Timer _tickTimer;
    private DateTime _recordingStartUtc;

    /// <summary>Name of the target destination (must match IDestination.Name).</summary>
    protected abstract string DestinationName { get; }

    /// <summary>Embedded-resource filename of the icon to show.</summary>
    protected abstract string IconResourceName { get; }

    protected QuickSendActionBase(string displayName, string description, string groupName)
        : base(displayName, description, groupName)
    {
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
        PluginResources.ReadImage(IconResourceName);

    // Tap-toggle fallback when hardware doesn't deliver press/release events.
    protected override async void RunCommand(String actionParameter)
    {
        if (VoxRingState.IsProcessing || VoxRingState.IsDownloadingModels)
            return;
        if (VoxRingState.IsRecording
            && (VoxRingState.CurrentRecordingMode != RecordingMode.QuickSend
                || VoxRingState.ActiveQuickSendTarget != DestinationName))
            return;
        if (!VoxRingState.IsRecording)
        {
            if (!TryGateReadiness()) return;
            StartRecording();
        }
        else
        {
            await StopTranscribeAndSendAsync(autoStopped: false);
        }
    }

    // True push-to-talk for Quick-Send: hold to record, release to send.
    protected override Boolean ProcessButtonEvent2(String actionParameter, DeviceButtonEvent2 buttonEvent)
    {
        switch (buttonEvent.EventType)
        {
            case DeviceButtonEventType.Press:
                if (VoxRingState.IsProcessing || VoxRingState.IsDownloadingModels)
                    return true;
                if (VoxRingState.IsRecording)
                    return true;
                if (!TryGateReadiness())
                    return true;
                StartRecording();
                return true;
            case DeviceButtonEventType.Release:
                if (VoxRingState.IsRecording
                    && VoxRingState.CurrentRecordingMode == RecordingMode.QuickSend
                    && VoxRingState.ActiveQuickSendTarget == DestinationName)
                    _ = StopTranscribeAndSendAsync(autoStopped: false);
                return true;
        }
        return true;
    }

    /// <summary>
    /// Short-circuits Quick Send when the destination isn't ready (no webhook URL, no AI key, etc.).
    /// Shows the reason in the button label and fires an error haptic so the user notices.
    /// </summary>
    private bool TryGateReadiness()
    {
        var dest = DestinationRegistry.All.FirstOrDefault(d => d.Name == DestinationName);
        var readiness = DestinationReadiness.Check(dest);
        if (readiness.Ready) return true;

        VoxRingState.LastSendResult = readiness.ShortLabel;
        Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
        PluginLog.Warning($"QuickSend[{DestinationName}] blocked: {readiness.Reason}");
        this.ActionImageChanged();
        return false;
    }

    private void StartRecording()
    {
        Plugin.AudioRecorder.StartRecording();
        VoxRingState.IsRecording = true;
        VoxRingState.CurrentRecordingMode = RecordingMode.QuickSend;
        VoxRingState.ActiveQuickSendTarget = DestinationName;
        if (!VoxRingState.AppendMode)
            VoxRingState.CurrentTranscript = string.Empty;
        VoxRingState.FormattedOutputs.Clear();
        VoxRingState.LastSendResult = null;

        Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticRecordStart);
        PluginLog.Info($"QuickSend[{DestinationName}] started");
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
        PluginLog.Info($"QuickSend[{DestinationName}] auto-stopped at {VoxRingState.MaxRecordingSeconds}s");
        await StopTranscribeAndSendAsync(autoStopped: true);
    }

    private async Task StopTranscribeAndSendAsync(bool autoStopped)
    {
        StopTimers();

        var wavData = Plugin.AudioRecorder.StopRecording();
        VoxRingState.IsRecording = false;
        VoxRingState.CurrentRecordingMode = RecordingMode.None;
        VoxRingState.ActiveQuickSendTarget = null;
        VoxRingState.IsProcessing = true;
        this.ActionImageChanged();

        try
        {
            var transcript = await Plugin.SpeechRecognition.RecognizeFromWavAsync(wavData);
            VoxRingState.SetTranscript(transcript);
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticTranscriptionComplete);
            PluginLog.Info($"QuickSend[{DestinationName}] transcript: {transcript}");

            if (string.IsNullOrEmpty(transcript))
            {
                VoxRingState.LastSendResult = "Empty";
                return;
            }

            var destination = DestinationRegistry.All.FirstOrDefault(d => d.Name == DestinationName);
            if (destination == null)
            {
                VoxRingState.LastSendResult = $"No {DestinationName}";
                Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
                return;
            }

            if (!destination.IsAvailable)
            {
                VoxRingState.LastSendResult = $"{DestinationName} off";
                Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
                PluginLog.Warning($"QuickSend[{DestinationName}] unavailable - check config");
                return;
            }

            var textToSend = await ResolveTextAsync(destination);
            var success = await destination.SendAsync(textToSend);

            if (success)
            {
                Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendSuccess);
                VoxRingState.LastSendResult = $"Sent to {DestinationName}";
                PluginLog.Info($"QuickSend[{DestinationName}] sent");
            }
            else
            {
                Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
                VoxRingState.LastSendResult = $"Failed: {DestinationName}";
            }
        }
        catch (Exception ex)
        {
            VoxRingState.IsProcessingAi = false;
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
            VoxRingState.LastSendResult = "Error";
            PluginLog.Error($"QuickSend[{DestinationName}] error: {ex.Message}");
        }
        finally
        {
            VoxRingState.IsProcessing = false;
            this.ActionImageChanged();
        }
    }

    private async Task<string> ResolveTextAsync(IDestination destination)
    {
        if (string.IsNullOrEmpty(destination.AiPrompt))
            return VoxRingState.CurrentTranscript;

        var provider = AiProviderRegistry.Current;
        if (!provider.IsAvailable)
        {
            PluginLog.Warning($"{destination.Name}: {provider.DisplayName} not configured - sending raw");
            return VoxRingState.CurrentTranscript;
        }

        VoxRingState.IsProcessingAi = true;
        this.ActionImageChanged();

        try
        {
            var formatted = await provider.ReformatAsync(
                VoxRingState.CurrentTranscript, destination.AiPrompt, VoxRingState.SelectedLanguage);
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
        if (VoxRingState.IsRecording
            && VoxRingState.CurrentRecordingMode == RecordingMode.QuickSend
            && VoxRingState.ActiveQuickSendTarget == DestinationName)
        {
            var elapsed = (int)(DateTime.UtcNow - _recordingStartUtc).TotalSeconds;
            var remaining = Math.Max(0, VoxRingState.MaxRecordingSeconds - elapsed);
            return $"Talk{Environment.NewLine}{remaining}s";
        }

        if (VoxRingState.IsProcessingAi && VoxRingState.LastSendResult == null)
            return "Formatting...";

        // Surface a "disabled" state while idle: if the destination isn't ready (no webhook URL,
        // no AI key, or placeholder provider selected), show the short reason on the button face.
        if (!VoxRingState.IsRecording && string.IsNullOrEmpty(VoxRingState.LastSendResult))
        {
            var dest = DestinationRegistry.All.FirstOrDefault(d => d.Name == DestinationName);
            var readiness = DestinationReadiness.Check(dest);
            if (!readiness.Ready)
                return $"{DestinationName}{Environment.NewLine}{readiness.ShortLabel}";
        }

        // Idle and ready: icon-only, matching Dictate/PTT
        return string.Empty;
    }
}
