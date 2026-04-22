namespace Loupedeck.VoxRingPlugin.Actions;

using System.Runtime.Versioning;
using Loupedeck.VoxRingPlugin.Helpers;
using Loupedeck.VoxRingPlugin.Models;

[SupportedOSPlatform("windows")]
public class WordCountMeter : PluginDynamicAdjustment
{
    private System.Threading.Timer _updateTimer;
    private int _lastLength = -1;
    private DateTime _lastChangeUtc = DateTime.MinValue;
    private bool _isIdle = true;

    // After this much time without transcript changes, the meter resets to its idle visual.
    // Underlying VoxRingState.CurrentTranscript is NOT touched — other actions (Send, Edit, Replay) still see it.
    private const int IdleResetSeconds = 60;

    public WordCountMeter()
        : base(displayName: "Word Count", description: "Word and character count for the last transcript. Resets after 60 seconds idle.", groupName: "7 Meters", hasReset: false)
    {
    }

    protected override bool OnLoad()
    {
        // 2 Hz polling; only invalidate when the transcript content changes OR when the idle threshold is crossed.
        _updateTimer = new System.Threading.Timer(_ =>
        {
            var transcript = VoxRingState.CurrentTranscript ?? string.Empty;
            if (transcript.Length != _lastLength)
            {
                _lastLength = transcript.Length;
                _lastChangeUtc = DateTime.UtcNow;
                _isIdle = transcript.Length == 0;
                MeterWatchdog.Fire(nameof(WordCountMeter), this.AdjustmentValueChanged);
            }
            else if (!_isIdle && transcript.Length > 0
                     && (DateTime.UtcNow - _lastChangeUtc).TotalSeconds >= IdleResetSeconds)
            {
                _isIdle = true;
                MeterWatchdog.Fire(nameof(WordCountMeter), this.AdjustmentValueChanged);
            }
        }, null, 500, 500);
        return base.OnLoad();
    }

    protected override bool OnUnload()
    {
        _updateTimer?.Dispose();
        _updateTimer = null;
        return base.OnUnload();
    }

    protected override void RunCommand(String actionParameter) { }
    protected override void ApplyAdjustment(String actionParameter, Int32 diff) { }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split(new[] { ' ', '\t', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries).Length;
    }

    protected override String GetAdjustmentValue(String actionParameter)
    {
        if (_isIdle || string.IsNullOrWhiteSpace(VoxRingState.CurrentTranscript))
            return Environment.NewLine + "Word Count";
        return Environment.NewLine + "Last Record";
    }

    protected override BitmapImage GetAdjustmentImage(String actionParameter, PluginImageSize imageSize)
    {
        var transcript = VoxRingState.CurrentTranscript ?? string.Empty;
        var words = CountWords(transcript);
        if (_isIdle || words == 0)
            return PluginResources.ReadImage("whole-word.svg");

        var chars = transcript.Length;
        // Rough speaking rate ~150 wpm = 2.5 words/sec
        var seconds = words / 2.5;
        var durationLabel = seconds < 60
            ? $"~{seconds:F0}s"
            : $"~{(int)(seconds / 60)}m{(int)(seconds % 60):00}s";

        var label = $"{words} {(words == 1 ? "word" : "words")}{Environment.NewLine}{chars} chars{Environment.NewLine}{durationLabel}";

        using var builder = new BitmapBuilder(imageSize);
        builder.Clear(BitmapColor.Black);
        builder.DrawText(label, BitmapColor.White, 18);
        return builder.ToImage();
    }
}
