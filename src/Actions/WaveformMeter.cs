namespace Loupedeck.VoxRingPlugin.Actions;

using System.Runtime.Versioning;
using Loupedeck.VoxRingPlugin.Helpers;
using Loupedeck.VoxRingPlugin.Models;

[SupportedOSPlatform("windows")]
public class WaveformMeter : PluginDynamicAdjustment
{
    private new VoxRingPlugin Plugin => base.Plugin as VoxRingPlugin;

    private const int BufferSize = 32; // number of historical samples to display
    private readonly float[] _buffer = new float[BufferSize];
    private int _writeIndex;
    private bool _wasRecording;
    private System.Threading.Timer _updateTimer;

    public WaveformMeter()
        : base(displayName: "Waveform", description: "Live waveform display during recording.", groupName: "7 Meters", hasReset: false)
    {
    }

    protected override bool OnLoad()
    {
        // 10 Hz tick while recording; on stop, emit one final invalidate to clear the display.
        // Idle state fires no invalidations to avoid flooding the SDK IPC.
        _updateTimer = new System.Threading.Timer(_ =>
        {
            var rec = VoxRingState.IsRecording;
            if (rec)
            {
                SamplePeak();
                MeterWatchdog.Fire(nameof(WaveformMeter), this.AdjustmentValueChanged);
            }
            else if (_wasRecording)
            {
                Array.Clear(_buffer, 0, _buffer.Length);
                _writeIndex = 0;
                MeterWatchdog.Fire(nameof(WaveformMeter), this.AdjustmentValueChanged);
            }
            _wasRecording = rec;
        }, null, 100, 100);
        return base.OnLoad();
    }

    protected override bool OnUnload()
    {
        _updateTimer?.Dispose();
        _updateTimer = null;
        return base.OnUnload();
    }

    private void SamplePeak()
    {
        if (Plugin?.AudioRecorder == null) return;

        // Normalize 16-bit peak (0..32768) to 0..1
        var sample = Plugin.AudioRecorder.CurrentPeak / 32768f;
        if (sample < 0) sample = 0;
        if (sample > 1) sample = 1;

        _buffer[_writeIndex] = sample;
        _writeIndex = (_writeIndex + 1) % BufferSize;
    }

    protected override void RunCommand(String actionParameter) { }
    protected override void ApplyAdjustment(String actionParameter, Int32 diff) { }

    protected override String GetAdjustmentValue(String actionParameter) =>
        VoxRingState.IsRecording ? string.Empty : Environment.NewLine + "Waveform";

    protected override BitmapImage GetAdjustmentImage(String actionParameter, PluginImageSize imageSize)
    {
        // Idle: show the icon
        if (!VoxRingState.IsRecording)
            return PluginResources.ReadImage("audio-lines.svg");

        using var builder = new BitmapBuilder(imageSize);
        builder.Clear(BitmapColor.Black);

        var w = builder.Width;
        var h = builder.Height;
        var barWidth = Math.Max(1, w / BufferSize);
        var centerY = h / 2;

        // Draw waveform bars left-to-right: oldest sample first, newest on the right
        for (var i = 0; i < BufferSize; i++)
        {
            var sampleIndex = (_writeIndex + i) % BufferSize;
            var amp = _buffer[sampleIndex];
            var halfH = (int)(amp * h * 0.45f);
            if (halfH < 1) halfH = 1;

            var x = i * barWidth;
            builder.FillRectangle(x, centerY - halfH, barWidth - 1, halfH * 2, BitmapColor.White);
        }

        return builder.ToImage();
    }
}
