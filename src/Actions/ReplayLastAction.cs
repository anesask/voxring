namespace Loupedeck.VoxRingPlugin.Actions;

using System.Diagnostics;
using System.IO;
using Loupedeck.VoxRingPlugin.Models;

public class ReplayLastAction : PluginDynamicCommand
{
    private new VoxRingPlugin Plugin => base.Plugin as VoxRingPlugin;

    private string _lastPlayedName;
    private System.Threading.Timer _clearLabelTimer;

    public ReplayLastAction()
        : base(displayName: "Replay Last", description: "Play the last Voice Note in your default audio player.", groupName: "1 Voice")
    {
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
        PluginResources.ReadImage("history.svg");

    protected override void RunCommand(String actionParameter)
    {
        try
        {
            var dir = VoxRingState.EffectiveVoiceNoteSavePath;
            if (!Directory.Exists(dir))
            {
                PluginLog.Warning($"Replay Last: folder does not exist: {dir}");
                Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
                ShowLabel("No notes", persist: false);
                return;
            }

            var latest = new DirectoryInfo(dir)
                .GetFiles("*.wav")
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault();

            if (latest == null)
            {
                PluginLog.Warning($"Replay Last: no .wav files in {dir}");
                Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
                ShowLabel("No notes", persist: false);
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = latest.FullName, UseShellExecute = true });

            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendSuccess);
            PluginLog.Info($"Replay Last: opened {latest.FullName}");
            _lastPlayedName = Path.GetFileNameWithoutExtension(latest.Name);
            ShowLabel("Playing...", persist: true);
        }
        catch (Exception ex)
        {
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
            PluginLog.Error($"Replay Last failed: {ex.Message}");
            ShowLabel("Error", persist: false);
        }
    }

    private void ShowLabel(string label, bool persist)
    {
        _lastPlayedName = label;
        this.ActionImageChanged();

        _clearLabelTimer?.Dispose();
        _clearLabelTimer = new System.Threading.Timer(
            _ => { _lastPlayedName = null; this.ActionImageChanged(); },
            null, persist ? 3000 : 2000, System.Threading.Timeout.Infinite);
    }

    protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
    {
        if (!string.IsNullOrEmpty(_lastPlayedName))
            return _lastPlayedName;

        return "Replay";
    }
}
