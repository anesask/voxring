namespace Loupedeck.VoxRingPlugin.Actions;

using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Loupedeck.VoxRingPlugin.Destinations;
using Loupedeck.VoxRingPlugin.Models;
using Loupedeck.VoxRingPlugin.Services.Ai;

[SupportedOSPlatform("windows")]
public class SendToAction : PluginDynamicCommand
{
    private new VoxRingPlugin Plugin => base.Plugin as VoxRingPlugin;

    private static readonly string[] DestinationNames = new[]
    {
        "Clipboard", "Type Out", "Email", "Slack", "Discord", "Teams", "Calendar",
    };

    public SendToAction()
        : base(displayName: "Send to", description: "Send the current transcript to a chosen destination.", groupName: "2 Send")
    {
        foreach (var name in DestinationNames)
            this.AddParameter(name, $"Send to {name}", this.GroupName);

        VoxRingState.TranscriptChanged += OnTranscriptChanged;
    }

    private void OnTranscriptChanged()
    {
        foreach (var name in DestinationNames)
            this.ActionImageChanged(name);
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
    {
        var dest = DestinationRegistry.All.FirstOrDefault(d => d.Name == actionParameter);
        var iconName = dest?.IconName ?? "send.svg";
        try { return PluginResources.ReadImage(iconName); }
        catch { return PluginResources.ReadImage("send.svg"); }
    }

    protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
    {
        if (VoxRingState.IsProcessingAi)
            return "Formatting...";

        if (!string.IsNullOrEmpty(VoxRingState.LastSendResult))
        {
            var isOtherSuccess = VoxRingState.LastSendResult.StartsWith("Sent to", StringComparison.Ordinal)
                && !VoxRingState.LastSendResult.Contains(actionParameter, StringComparison.OrdinalIgnoreCase);
            if (!isOtherSuccess)
                return VoxRingState.LastSendResult;
        }

        var transcript = VoxRingState.CurrentTranscript;
        if (!string.IsNullOrEmpty(transcript))
        {
            const int MaxChars = 20;
            var preview = transcript.Length > MaxChars
                ? transcript.Substring(0, MaxChars).TrimEnd() + "..."
                : transcript;
            return preview;
        }

        return $"Send to{Environment.NewLine}{actionParameter}";
    }

    protected override void RunCommand(String actionParameter)
    {
        var dest = DestinationRegistry.All.FirstOrDefault(d => d.Name == actionParameter);
        if (dest == null)
        {
            PluginLog.Warning($"SendTo: unknown destination '{actionParameter}'");
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
            return;
        }
        _ = SendAsync(dest);
    }

    private async Task SendAsync(IDestination destination)
    {
        if (string.IsNullOrEmpty(VoxRingState.CurrentTranscript))
        {
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
            VoxRingState.LastSendResult = "No recording";
            this.ActionImageChanged(destination.Name);
            return;
        }

        if (VoxRingState.IsProcessing || VoxRingState.IsProcessingAi)
        {
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
            return;
        }

        var readiness = DestinationReadiness.Check(destination);
        if (!readiness.Ready)
        {
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
            VoxRingState.LastSendResult = readiness.ShortLabel;
            PluginLog.Warning($"SendTo[{destination.Name}] blocked: {readiness.Reason}");
            this.ActionImageChanged(destination.Name);
            return;
        }

        if (destination.Category == DestinationCategory.Ai)
        {
            var aiList = DestinationRegistry.Ai;
            for (var i = 0; i < aiList.Count; i++)
            {
                if (aiList[i].Name == destination.Name)
                {
                    VoxRingState.SelectedAiIndex = i;
                    break;
                }
            }
            VoxRingState.ActiveCategory = DestinationCategory.Ai;
        }

        try
        {
            var textToSend = await ResolveTextAsync(destination);
            PluginLog.Info($"SendTo[{destination.Name}]: {textToSend}");

            var success = await destination.SendAsync(textToSend);
            if (success)
            {
                Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendSuccess);
                VoxRingState.LastSendResult = $"Sent to {destination.Name}";
            }
            else
            {
                Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
                VoxRingState.LastSendResult = $"Failed: {destination.Name}";
            }
        }
        catch (Exception ex)
        {
            VoxRingState.IsProcessingAi = false;
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
            VoxRingState.LastSendResult = "Error";
            PluginLog.Error($"SendTo[{destination.Name}] error: {ex.Message}");
        }

        this.ActionImageChanged(destination.Name);
    }

    private async Task<string> ResolveTextAsync(IDestination destination)
    {
        if (string.IsNullOrEmpty(destination.AiPrompt) || !VoxRingState.UseAi)
            return VoxRingState.CurrentTranscript;

        var provider = AiProviderRegistry.Current;
        if (!provider.IsAvailable)
        {
            PluginLog.Warning($"{destination.Name}: {provider.DisplayName} not configured - sending raw");
            return VoxRingState.CurrentTranscript;
        }

        if (VoxRingState.FormattedOutputs.TryGetValue(destination.Name, out var cached)
            && !string.IsNullOrEmpty(cached))
            return cached;

        VoxRingState.IsProcessingAi = true;
        this.ActionImageChanged(destination.Name);
        try
        {
            var prompt = VoxRingState.GetEffectivePrompt(destination.Name, destination.AiPrompt);
            var formatted = await provider.ReformatAsync(
                VoxRingState.CurrentTranscript, prompt, VoxRingState.SelectedLanguage);
            VoxRingState.FormattedOutputs[destination.Name] = formatted;
            return formatted;
        }
        finally
        {
            VoxRingState.IsProcessingAi = false;
        }
    }
}
