namespace Loupedeck.VoxRingPlugin.Actions;

using Loupedeck.VoxRingPlugin.Models;

public class CycleCaseTransformAction : PluginDynamicCommand
{
    private new VoxRingPlugin Plugin => base.Plugin as VoxRingPlugin;

    public CycleCaseTransformAction()
        : base("Case Transform", "Cycle transcript case: Normal, UPPER, lower, Title Case.", "4 Text Tools")
    {
    }

    protected override void RunCommand(String actionParameter)
    {
        VoxRingState.SelectedCaseTransform = VoxRingState.SelectedCaseTransform switch
        {
            CaseTransform.None  => CaseTransform.Upper,
            CaseTransform.Upper => CaseTransform.Lower,
            CaseTransform.Lower => CaseTransform.Title,
            CaseTransform.Title => CaseTransform.None,
            _                   => CaseTransform.None
        };
        Plugin?.SaveCaseTransform(VoxRingState.SelectedCaseTransform);
        this.ActionImageChanged();
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
    {
        try { return PluginResources.ReadImage("case-transform.svg"); }
        catch { return base.GetCommandImage(actionParameter, imageSize); }
    }

    protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
        VoxRingState.SelectedCaseTransform switch
        {
            CaseTransform.Upper => "UPPER",
            CaseTransform.Lower => "lower",
            CaseTransform.Title => "Title",
            _                   => string.Empty
        };
}
