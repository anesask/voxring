namespace Loupedeck.VoxRingPlugin.Actions;

using Loupedeck.VoxRingPlugin.Models;

public class ToggleTranslateAction : PluginDynamicCommand
{
    private new VoxRingPlugin Plugin => base.Plugin as VoxRingPlugin;

    // Cycle order: off → EN → DE → FR → ES → off
    private static readonly string[] Cycle = ["", "en", "de", "fr", "es"];

    public ToggleTranslateAction()
        : base("Translate Output", "Translate output to another language via AI. Cycles: off, English, German, French, Spanish.", "4 Text Tools")
    {
    }

    protected override void RunCommand(String actionParameter)
    {
        var current = VoxRingState.TranslateTargetLanguage ?? "";
        var idx = Array.IndexOf(Cycle, current);
        var next = Cycle[(idx + 1) % Cycle.Length];
        Plugin?.SaveTranslateTargetLanguage(next);
        this.ActionImageChanged();
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
    {
        try { return PluginResources.ReadImage("translate.svg"); }
        catch { return base.GetCommandImage(actionParameter, imageSize); }
    }

    protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
    {
        var lang = VoxRingState.TranslateTargetLanguage;
        if (string.IsNullOrEmpty(lang)) return string.Empty;
        return $"→{lang.ToUpper()}";
    }
}
