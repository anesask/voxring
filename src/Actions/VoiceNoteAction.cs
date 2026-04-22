namespace Loupedeck.VoxRingPlugin.Actions;

using System.IO;
using System.Runtime.Versioning;
using Loupedeck.VoxRingPlugin.Models;

[SupportedOSPlatform("windows")]
public class VoiceNoteAction : PluginDynamicCommand
{
    private new VoxRingPlugin Plugin => base.Plugin as VoxRingPlugin;

    private System.Threading.Timer _pulseTimer;
    private System.Threading.Timer _autoStopTimer;
    private System.Threading.Timer _tickTimer;
    private System.Threading.Timer _clearSavedTimer;
    private DateTime _recordingStartUtc;
    private string _savedLabel;

    public VoiceNoteAction()
        : base(displayName: "Voice Note", description: "Record audio and save as a WAV file. No transcription.", groupName: "1 Voice")
    {
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
        PluginResources.ReadImage("voice-note.svg");

    protected override void RunCommand(String actionParameter)
    {
        if (VoxRingState.IsProcessing)
            return;

        if (VoxRingState.IsRecording && VoxRingState.CurrentRecordingMode != RecordingMode.VoiceNote)
        {
            PluginLog.Warning("Voice Note: another recording is in progress - ignored");
            return;
        }

        if (!VoxRingState.IsRecording)
            StartRecording();
        else
            StopAndSave(autoStopped: false);
    }

    private void StartRecording()
    {
        // 48 kHz for playback quality - Voice Notes aren't transcribed, so no need for 16 kHz.
        Plugin.AudioRecorder.StartRecording(sampleRate: 48000);
        VoxRingState.IsRecording = true;
        VoxRingState.CurrentRecordingMode = RecordingMode.VoiceNote;
        _savedLabel = null;

        Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticRecordStart);
        PluginLog.Info("Voice Note started");

        _recordingStartUtc = DateTime.UtcNow;

        _pulseTimer = new System.Threading.Timer(
            _ => Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticRecordPulse),
            null, 2000, 2000);

        _tickTimer = new System.Threading.Timer(
            _ => this.ActionImageChanged(),
            null, 1000, 1000);

        var maxMs = VoxRingState.MaxRecordingSeconds * 1000;
        _autoStopTimer = new System.Threading.Timer(
            _ => StopAndSave(autoStopped: true),
            null, maxMs, System.Threading.Timeout.Infinite);

        this.ActionImageChanged();
    }

    private void StopAndSave(bool autoStopped)
    {
        StopTimers();

        var wavData = Plugin.AudioRecorder.StopRecording();
        VoxRingState.IsRecording = false;
        VoxRingState.CurrentRecordingMode = RecordingMode.None;

        if (autoStopped)
        {
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticRecordAutoStop);
            PluginLog.Info($"Voice Note auto-stopped at {VoxRingState.MaxRecordingSeconds}s limit");
        }

        try
        {
            var dir = VoxRingState.EffectiveVoiceNoteSavePath;
            Directory.CreateDirectory(dir);

            var fileName = BuildFileName();
            var path = Path.Combine(dir, fileName);

            // Avoid collisions when the pattern lacks enough precision
            var counter = 1;
            while (File.Exists(path))
            {
                var stem = Path.GetFileNameWithoutExtension(fileName);
                path = Path.Combine(dir, $"{stem}_{counter}.wav");
                counter++;
            }

            File.WriteAllBytes(path, wavData);

            _savedLabel = "Saved!";
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendSuccess);
            PluginLog.Info($"Voice Note saved: {path} ({wavData.Length} bytes)");

            _clearSavedTimer?.Dispose();
            _clearSavedTimer = new System.Threading.Timer(
                _ => { _savedLabel = null; this.ActionImageChanged(); },
                null, 3000, System.Threading.Timeout.Infinite);
        }
        catch (Exception ex)
        {
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
            PluginLog.Error($"Voice Note save failed: {ex.Message}");
            _savedLabel = "Error";
        }

        this.ActionImageChanged();
    }

    private static string BuildFileName()
    {
        var pattern = string.IsNullOrWhiteSpace(VoxRingState.VoiceNoteFilenamePattern)
            ? VoxRingState.DefaultVoiceNoteFilenamePattern
            : VoxRingState.VoiceNoteFilenamePattern;

        string raw;
        try { raw = DateTime.Now.ToString(pattern); }
        catch { raw = DateTime.Now.ToString(VoxRingState.DefaultVoiceNoteFilenamePattern); }

        // Strip Windows-invalid filename chars (colons from HH:mm:ss, etc.)
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(raw.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c));

        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = DateTime.Now.ToString(VoxRingState.DefaultVoiceNoteFilenamePattern);

        return sanitized + ".wav";
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
        if (VoxRingState.IsRecording && VoxRingState.CurrentRecordingMode == RecordingMode.VoiceNote)
        {
            var elapsed = (int)(DateTime.UtcNow - _recordingStartUtc).TotalSeconds;
            var remaining = Math.Max(0, VoxRingState.MaxRecordingSeconds - elapsed);
            return $"Rec{Environment.NewLine}{remaining}s";
        }

        if (!string.IsNullOrEmpty(_savedLabel))
            return _savedLabel;

        return string.Empty;
    }
}
