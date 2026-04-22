namespace Loupedeck.VoxRingPlugin.Actions;

using Loupedeck.VoxRingPlugin.Models;

public class ToggleNoiseGateAction : PluginDynamicCommand
{
    private new VoxRingPlugin Plugin => base.Plugin as VoxRingPlugin;

    public ToggleNoiseGateAction()
        : base("Noise Gate", "Filter background noise before transcription. Recommended for noisy environments.", "4 Text Tools")
    {
    }

    protected override void RunCommand(String actionParameter)
    {
        VoxRingState.UseNoiseGate = !VoxRingState.UseNoiseGate;
        Plugin?.SaveUseNoiseGate(VoxRingState.UseNoiseGate);
        this.ActionImageChanged();
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
    {
        try { return PluginResources.ReadImage("noise-gate.svg"); }
        catch { return base.GetCommandImage(actionParameter, imageSize); }
    }

    protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
        VoxRingState.UseNoiseGate ? string.Empty : "Gate Off";
}
