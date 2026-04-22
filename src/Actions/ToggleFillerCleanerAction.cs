namespace Loupedeck.VoxRingPlugin.Actions;

using Loupedeck.VoxRingPlugin.Models;

public class ToggleFillerCleanerAction : PluginDynamicCommand
{
    private new VoxRingPlugin Plugin => base.Plugin as VoxRingPlugin;

    public ToggleFillerCleanerAction()
        : base("Clean Filler Words", "Remove filler words (um, uh, hmm, er, äh) from transcripts automatically.", "4 Text Tools")
    {
    }

    protected override void RunCommand(String actionParameter)
    {
        VoxRingState.UseFillerWordCleaner = !VoxRingState.UseFillerWordCleaner;
        Plugin?.SaveUseFillerWordCleaner(VoxRingState.UseFillerWordCleaner);
        this.ActionImageChanged();
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
    {
        try { return PluginResources.ReadImage("filler-cleaner.svg"); }
        catch { return base.GetCommandImage(actionParameter, imageSize); }
    }

    protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
        VoxRingState.UseFillerWordCleaner ? "Clean" : string.Empty;
}
