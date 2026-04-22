namespace Loupedeck.VoxRingPlugin.Actions;

using Loupedeck.VoxRingPlugin.Models;

public class CancelAction : PluginDynamicCommand
{
    public CancelAction()
        : base(displayName: "Cancel", description: "Clear the current transcript and reset to idle.", groupName: "5 Controls")
    {
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
        PluginResources.ReadImage("cancel.svg");

    protected override void RunCommand(String actionParameter)
    {
        VoxRingState.CurrentTranscript = string.Empty;
        VoxRingState.FormattedOutputs.Clear();
        VoxRingState.LastSendResult = null;
        PluginLog.Info("Cancel: cleared transcript and AI cache");
        this.ActionImageChanged();
    }

    protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => "Cancel";
}
