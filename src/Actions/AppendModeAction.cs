namespace Loupedeck.VoxRingPlugin.Actions;

using Loupedeck.VoxRingPlugin.Models;

public class AppendModeAction : PluginDynamicCommand
{
    public AppendModeAction()
        : base(displayName: "Append Mode", description: "Append the next recording to the current transcript instead of replacing it.", groupName: "4 Text Tools")
    {
    }

    protected override void RunCommand(String actionParameter)
    {
        VoxRingState.AppendMode = !VoxRingState.AppendMode;
        this.ActionImageChanged();
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
    {
        try { return PluginResources.ReadImage("append.svg"); }
        catch { return base.GetCommandImage(actionParameter, imageSize); }
    }

    protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
        VoxRingState.AppendMode ? "Appending" : string.Empty;
}
