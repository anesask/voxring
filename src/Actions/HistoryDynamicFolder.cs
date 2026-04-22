namespace Loupedeck.VoxRingPlugin.Actions;

using System.Collections.Generic;
using Loupedeck.VoxRingPlugin.Models;

public class HistoryDynamicFolder : PluginDynamicFolder
{
    public HistoryDynamicFolder()
    {
        this.DisplayName = "History";
        this.GroupName = "2 Send";
        this.Description = "Browse and resend past transcripts.";
    }

    public override IEnumerable<string> GetButtonPressActionNames(DeviceType deviceType)
    {
        var names = new List<string> { PluginDynamicFolder.NavigateUpActionName };
        for (var i = 0; i < VoxRingState.TranscriptHistory.Count; i++)
            names.Add(this.CreateCommandName(i.ToString()));
        return names;
    }

    public override BitmapImage GetButtonImage(PluginImageSize imageSize)
    {
        try { return PluginResources.ReadImage("history.svg"); }
        catch { return base.GetButtonImage(imageSize); }
    }

    public override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        if (!int.TryParse(actionParameter, out var idx)
            || idx >= VoxRingState.TranscriptHistory.Count)
            return base.GetCommandImage(actionParameter, imageSize);

        try
        {
            var text = VoxRingState.TranscriptHistory[idx];
            using var builder = new BitmapBuilder(imageSize);
            builder.Clear(BitmapColor.Black);
            var preview = text.Length > 50 ? text.Substring(0, 50) : text;
            builder.DrawText(preview, BitmapColor.White, 16);
            return builder.ToImage();
        }
        catch { return base.GetCommandImage(actionParameter, imageSize); }
    }

    public override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize)
    {
        if (!int.TryParse(actionParameter, out var idx)
            || idx >= VoxRingState.TranscriptHistory.Count)
            return actionParameter;

        var text = VoxRingState.TranscriptHistory[idx];
        const int Max = 22;
        return text.Length > Max ? text.Substring(0, Max) + "..." : text;
    }

    public override void RunCommand(string actionParameter)
    {
        if (!int.TryParse(actionParameter, out var idx)
            || idx >= VoxRingState.TranscriptHistory.Count)
            return;

        var text = VoxRingState.TranscriptHistory[idx];
        VoxRingState.CurrentTranscript = text;
        VoxRingState.FormattedOutputs.Clear();
        PluginLog.Info($"History: restored entry {idx}");
    }
}
