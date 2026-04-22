namespace Loupedeck.VoxRingPlugin.Actions;

using Loupedeck.VoxRingPlugin.Models;

public class LanguageToggleAction : PluginDynamicCommand
{
    private new VoxRingPlugin Plugin => base.Plugin as VoxRingPlugin;

    // Cycle order: auto → English → Deutsch → auto …
    private static readonly string[] Cycle = { "auto", "en", "de" };

    public LanguageToggleAction()
        : base(displayName: "Language Toggle", description: "Cycle recognition language: Auto-detect, English, Deutsch.", groupName: "6 Settings")
    {
    }

    protected override void RunCommand(String actionParameter)
    {
        var current = VoxRingState.SelectedLanguage ?? "auto";
        var index = Array.IndexOf(Cycle, current);
        var next = Cycle[index < 0 ? 0 : (index + 1) % Cycle.Length];
        Plugin?.SaveLanguage(next);
        this.ActionImageChanged();
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
    {
        var code = (VoxRingState.SelectedLanguage ?? "auto") switch
        {
            "en" => "EN",
            "de" => "DE",
            _ => "AUTO",
        };

        // "EN" / "DE" are 2 chars — go big. "AUTO" is 4 chars — scale down so it fits the slot.
        var fontSize = code.Length <= 2 ? 56 : 38;

        using var builder = new BitmapBuilder(imageSize);
        builder.Clear(BitmapColor.Black);
        builder.DrawText(code, BitmapColor.White, fontSize);
        return builder.ToImage();
    }

    protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => string.Empty;
}
