namespace Loupedeck.VoxRingPlugin.Destinations;

using System.Runtime.Versioning;
using Loupedeck.VoxRingPlugin.Helpers;
using Loupedeck.VoxRingPlugin.Models;

[SupportedOSPlatform("windows")]
public class ClipboardDestination : IDestination
{
    public string Name => "Clipboard";
    public string Description => "Copy voice to clipboard";
    public bool IsAvailable => true;
    public DestinationCategory Category => DestinationCategory.Raw;
    public string AiPrompt => null;

    public Task<bool> SendAsync(string text)
    {
        try
        {
            var success = ClipboardHelper.SetText(text);
            if (success)
                PluginLog.Info($"Copied {text.Length} chars to clipboard");
            else
                PluginLog.Error("Clipboard: SetText returned false");
            return Task.FromResult(success);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Clipboard failed: {ex.Message}");
            return Task.FromResult(false);
        }
    }
}
