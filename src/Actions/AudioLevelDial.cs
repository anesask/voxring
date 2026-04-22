namespace Loupedeck.VoxRingPlugin.Actions;

using System.Runtime.Versioning;
using Loupedeck.VoxRingPlugin.Helpers;
using Loupedeck.VoxRingPlugin.Models;

[SupportedOSPlatform("windows")]
public class AudioLevelDial : PluginDynamicAdjustment
{
    private new VoxRingPlugin Plugin => base.Plugin as VoxRingPlugin;

    private System.Threading.Timer _updateTimer;
    private bool _wasRecording;

    public AudioLevelDial()
        : base(displayName: "Audio Level", description: "Live microphone level during recording.", groupName: "7 Meters", hasReset: false)
    {
    }

    protected override bool OnLoad()
    {
        // 5 Hz refresh while recording; one trailing invalidate on stop to swap back to the idle icon.
        _updateTimer = new System.Threading.Timer(_ =>
        {
            var rec = VoxRingState.IsRecording;
            if (rec || _wasRecording)
                MeterWatchdog.Fire(nameof(AudioLevelDial), this.AdjustmentValueChanged);
            _wasRecording = rec;
        }, null, 200, 200);
        return base.OnLoad();
    }

    protected override bool OnUnload()
    {
        _updateTimer?.Dispose();
        _updateTimer = null;
        return base.OnUnload();
    }

    protected override BitmapImage GetAdjustmentImage(String actionParameter, PluginImageSize imageSize)
    {
        // While recording: render the value + bar centered into the image slot.
        if (VoxRingState.IsRecording && Plugin?.AudioRecorder != null)
        {
            var peakDb = Plugin.AudioRecorder.CurrentPeakDb;
            var label = $"{peakDb:F0}dB{Environment.NewLine}{BuildBar(peakDb)}";

            using var builder = new BitmapBuilder(imageSize);
            builder.Clear(BitmapColor.Black);
            builder.DrawText(label, BitmapColor.White, 24);
            return builder.ToImage();
        }

        return PluginResources.ReadImage("activity.svg");
    }

    // Display widget: no scroll, no press behavior.
    protected override void RunCommand(String actionParameter) { }
    protected override void ApplyAdjustment(String actionParameter, Int32 diff) { }

    protected override String GetAdjustmentValue(String actionParameter)
    {
        // Value is drawn in the image slot while recording; keep text slot blank for centered look.
        if (VoxRingState.IsRecording)
            return string.Empty;
        // Leading newline adds breathing room between icon and label in idle state
        return Environment.NewLine + "Audio Level";
    }

    /// <summary>6-cell ASCII bar from dB level. -60 dB = empty, 0 dB = full.</summary>
    private static string BuildBar(double peakDb)
    {
        var normalized = Math.Max(0, Math.Min(1.0, (peakDb + 60) / 60));
        var filled = (int)Math.Round(normalized * 6);
        return new string('|', filled) + new string('.', 6 - filled);
    }
}
