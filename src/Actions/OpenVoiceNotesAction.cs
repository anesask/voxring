namespace Loupedeck.VoxRingPlugin.Actions;

using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using Loupedeck.VoxRingPlugin.Models;

[SupportedOSPlatform("windows")]
public class OpenVoiceNotesAction : PluginDynamicCommand
{
    public OpenVoiceNotesAction()
        : base(displayName: "Open Voice Notes", description: "Open the folder where Voice Notes are saved.", groupName: "5 Controls")
    {
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
        PluginResources.ReadImage("folder-open.svg");

    protected override void RunCommand(String actionParameter)
    {
        try
        {
            var dir = VoxRingState.EffectiveVoiceNoteSavePath;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            PluginLog.Info($"Opened Voice Notes folder: {dir}");
        }
        catch (Exception ex)
        {
            PluginLog.Error($"OpenVoiceNotes failed: {ex.Message}");
        }
    }

    protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
        string.Empty;
}
