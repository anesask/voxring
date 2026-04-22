namespace Loupedeck.VoxRingPlugin.Actions;

using Loupedeck.VoxRingPlugin.Services;

public class OpenSettingsAction : PluginDynamicCommand
{
    private new VoxRingPlugin Plugin => base.Plugin as VoxRingPlugin;

    public OpenSettingsAction()
        : base("Settings", "Open VoxRing settings: microphone, speech engine, AI keys, and more.", "6 Settings")
    {
    }

    protected override void RunCommand(String actionParameter)
    {
        WebSettingsService.Instance.OpenInBrowser();
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize) =>
        PluginResources.ReadImage("settings.svg");

    protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
    {
        return "Settings";
    }
}
