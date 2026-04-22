namespace Loupedeck.VoxRingPlugin.Actions;

using System.Runtime.Versioning;
using Loupedeck.VoxRingPlugin.Helpers;
using Loupedeck.VoxRingPlugin.Models;

// Edit copies the transcript to clipboard so the user can paste it into any text editor.
// A proper in-place editor (WebSettingsService endpoint or SDK modal) is the future path,
// but clipboard-bridge is immediately useful and costs nothing to implement.
[SupportedOSPlatform("windows")]
public class EditAction : PluginDynamicCommand
{
    public EditAction()
        : base(displayName: "Edit", description: "Copy transcript to clipboard for editing in any text app.", groupName: "4 Text Tools")
    {
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
        PluginResources.ReadImage("edit.svg");

    protected override void RunCommand(String actionParameter)
    {
        var text = VoxRingState.CurrentTranscript;
        if (string.IsNullOrWhiteSpace(text))
            return;

        ClipboardHelper.SetText(text);
        PluginLog.Info($"Edit: copied {text.Length} chars to clipboard");
        this.ActionImageChanged();
    }

    protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
        string.IsNullOrEmpty(VoxRingState.CurrentTranscript) ? "Edit" : $"Edit{Environment.NewLine}({VoxRingState.CurrentTranscript.Length} ch)";
}
