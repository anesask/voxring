namespace Loupedeck.VoxRingPlugin.Actions;

using System.Runtime.Versioning;
using Loupedeck.VoxRingPlugin.Models;
using Loupedeck.VoxRingPlugin.Services;

[SupportedOSPlatform("windows")]
public class ReadBackAction : PluginDynamicCommand
{
    public ReadBackAction()
        : base(displayName: "Read Back", description: "Speak the current transcript aloud. Tap again to stop.", groupName: "1 Voice")
    {
    }

    protected override void RunCommand(String actionParameter)
    {
        var transcript = VoxRingState.CurrentTranscript;
        if (string.IsNullOrWhiteSpace(transcript))
        {
            PluginLog.Info("Read Back: no transcript to read");
            return;
        }

        if (!TtsService.Instance.IsAvailable)
        {
            PluginLog.Warning("Read Back: TTS unavailable (SAPI init failed)");
            return;
        }

        TtsService.Instance.Speak(transcript);
        PluginLog.Info($"Read Back: speaking {transcript.Length} chars");
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
    {
        try { return PluginResources.ReadImage("book-open.svg"); }
        catch { return base.GetCommandImage(actionParameter, imageSize); }
    }

    protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => string.Empty;
}
