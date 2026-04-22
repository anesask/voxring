namespace Loupedeck.VoxRingPlugin.Actions;

using Loupedeck.VoxRingPlugin.Destinations;
using Loupedeck.VoxRingPlugin.Models;

public class RegenerateAction : PluginDynamicCommand
{
    private new VoxRingPlugin Plugin => base.Plugin as VoxRingPlugin;

    public RegenerateAction()
        : base(displayName: "Regenerate", description: "Re-run AI formatting for the current destination.", groupName: "4 Text Tools")
    {
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
        PluginResources.ReadImage("refresh.svg");

    protected override void RunCommand(String actionParameter)
    {
        // TODO: discard cached output for current destination and re-invoke ClaudeApi.ReformatAsync
        var dest = DestinationRegistry.Current;
        if (dest != null)
            VoxRingState.FormattedOutputs.Remove(dest.Name);

        PluginLog.Info("Regenerate: stub - cleared cache for current destination");
        this.ActionImageChanged();
    }

    protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
        $"Regen{Environment.NewLine}{DestinationRegistry.Current?.Name ?? "-"}";
}
