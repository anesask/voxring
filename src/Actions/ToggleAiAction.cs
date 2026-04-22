namespace Loupedeck.VoxRingPlugin.Actions;

using Loupedeck.VoxRingPlugin.Models;

public class ToggleAiAction : PluginDynamicCommand
{
    private new VoxRingPlugin Plugin => base.Plugin as VoxRingPlugin;

    public ToggleAiAction()
        : base("Toggle AI", "Enable or disable AI formatting. When on, text is shaped for each destination automatically.", "4 Text Tools")
    {
    }

    protected override void RunCommand(String actionParameter)
    {
        VoxRingState.UseAi = !VoxRingState.UseAi;
        Plugin?.SaveUseAi(VoxRingState.UseAi);
        this.ActionImageChanged();
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
    {
        try { return PluginResources.ReadImage("toggle-ai.svg"); }
        catch { return base.GetCommandImage(actionParameter, imageSize); }
    }

    protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
        VoxRingState.UseAi ? string.Empty : "AI Off";
}
