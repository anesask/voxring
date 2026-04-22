namespace Loupedeck.VoxRingPlugin.Destinations;

using System.Net.Http;
using System.Text;
using System.Text.Json;
using Loupedeck.VoxRingPlugin.Models;

public class DiscordWebhookDestination : IDestination
{
    private static readonly HttpClient Http = new();

    public string Name => "Discord";
    public string Description => "Send to Discord via webhook";
    public bool IsAvailable => !string.IsNullOrEmpty(VoxRingState.DiscordWebhookUrl);
    public DestinationCategory Category => DestinationCategory.Ai;
    public string AiPrompt => "Format this speech transcript as a concise Discord message. Use Discord markdown: **bold**, *italic*, `code`, > quote. Keep it casual. Output only the message text, nothing else.";

    public async Task<bool> SendAsync(string text)
    {
        if (string.IsNullOrEmpty(VoxRingState.DiscordWebhookUrl))
        {
            PluginLog.Error("Discord webhook URL not configured");
            return false;
        }

        try
        {
            var payload = JsonSerializer.Serialize(new { content = text });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await Http.PostAsync(VoxRingState.DiscordWebhookUrl, content);

            if (response.IsSuccessStatusCode)
            {
                PluginLog.Info("Sent to Discord successfully");
                return true;
            }

            var body = await response.Content.ReadAsStringAsync();
            PluginLog.Error($"Discord webhook returned {(int)response.StatusCode}: {body}");
            return false;
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Discord send failed: {ex.Message}");
            return false;
        }
    }
}
