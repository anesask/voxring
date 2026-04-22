namespace Loupedeck.VoxRingPlugin.Actions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Loupedeck.VoxRingPlugin.Destinations;
using Loupedeck.VoxRingPlugin.Models;
using Loupedeck.VoxRingPlugin.Services.Ai;

/// <summary>
/// "Send" as a native <see cref="PluginDynamicFolder"/>.
///
/// <para>User taps the Send button on the ring or Console. The device navigates INTO the folder
/// and shows one button per AI destination (Email, Slack, Discord, Teams, Calendar) plus a
/// "back" button. Tapping a destination commits the send; the folder stays open so the user
/// can NavigateUp when they're done.</para>
///
/// <para>Replaces the earlier cycling <c>SendAction</c>. The folder pattern gives the user a
/// visible list instead of cycle-by-tap, which matches what Logi hardware renders natively.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public class SendDynamicFolder : PluginDynamicFolder
{
    private new VoxRingPlugin Plugin => base.Plugin as VoxRingPlugin;

    public SendDynamicFolder()
    {
        this.DisplayName = "Send";
        this.GroupName = "2 Send";
        this.Description = "AI-formatted send to 9 destinations.";
    }

    public override IEnumerable<string> GetButtonPressActionNames(DeviceType deviceType)
    {
        var names = new List<string> { PluginDynamicFolder.NavigateUpActionName };
        foreach (var dest in DestinationRegistry.Ai)
        {
            names.Add(this.CreateCommandName(dest.Name));
        }
        return names;
    }

    public override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize)
    {
        // Show feedback on the button that was just tapped.
        if (!string.IsNullOrEmpty(VoxRingState.LastSendResult)
            && VoxRingState.LastSendDestination == actionParameter)
        {
            return VoxRingState.LastSendResult;
        }

        return actionParameter;
    }

    public override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        var dest = DestinationRegistry.All.FirstOrDefault(d => d.Name == actionParameter);
        var iconName = dest?.IconName ?? "send.svg";
        try { return PluginResources.ReadImage(iconName); }
        catch { return PluginResources.ReadImage("send.svg"); }
    }

    public override BitmapImage GetButtonImage(PluginImageSize imageSize)
    {
        // Icon that represents the folder itself in the sidebar (parent view).
        try { return PluginResources.ReadImage("send.svg"); }
        catch { return base.GetButtonImage(imageSize); }
    }

    public override void RunCommand(string actionParameter)
    {
        var destination = DestinationRegistry.All.FirstOrDefault(d => d.Name == actionParameter);
        if (destination == null)
        {
            PluginLog.Warning($"Send folder: unknown destination '{actionParameter}'");
            return;
        }
        _ = SendAsync(destination);
    }

    private async Task SendAsync(IDestination destination)
    {
        if (string.IsNullOrEmpty(VoxRingState.CurrentTranscript))
        {
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
            VoxRingState.LastSendDestination = destination.Name;
            VoxRingState.LastSendResult = "No recording";
            PluginLog.Warning("Send folder: no transcript to send");
            this.CommandImageChanged(destination.Name);
            return;
        }

        if (VoxRingState.IsProcessing || VoxRingState.IsProcessingAi)
        {
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
            PluginLog.Info("Send folder: tap ignored, still processing");
            return;
        }

        var readiness = DestinationReadiness.Check(destination);
        if (!readiness.Ready)
        {
            Plugin.PluginEvents.RaiseEvent(VoxRingPlugin.HapticSendFailure);
            VoxRingState.LastSendDestination = destination.Name;
            VoxRingState.LastSendResult = readiness.ShortLabel;
            PluginLog.Warning($"Send folder blocked: {readiness.Reason}");
            this.CommandImageChanged(destination.Name);
            return;
        }

        // Sync the dial and active category so Regenerate + the AI dial agree with this choice.
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

        try
        {
            var textToSend = await ResolveTextAsync(destination);
            PluginLog.Info($"Send folder -> {destination.Name}: {textToSend}");

            var success = await destination.SendAsync(textToSend);
            VoxRingState.LastSendDestination = destination.Name;
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
            VoxRingState.LastSendDestination = destination.Name;
            VoxRingState.LastSendResult = "Error";
            PluginLog.Error($"Send folder error: {ex.Message}");
        }

        this.CommandImageChanged(destination.Name);
    }

    private async Task<string> ResolveTextAsync(IDestination destination)
    {
        if (string.IsNullOrEmpty(destination.AiPrompt) || !VoxRingState.UseAi)
            return VoxRingState.CurrentTranscript;

        var aiProvider = AiProviderRegistry.Current;
        if (!aiProvider.IsAvailable)
        {
            PluginLog.Warning($"{destination.Name}: {aiProvider.DisplayName} not configured - sending raw transcript");
            return VoxRingState.CurrentTranscript;
        }

        if (VoxRingState.FormattedOutputs.TryGetValue(destination.Name, out var cached)
            && !string.IsNullOrEmpty(cached))
            return cached;

        VoxRingState.IsProcessingAi = true;
        try
        {
            var prompt = VoxRingState.GetEffectivePrompt(destination.Name, destination.AiPrompt);
            var formatted = await aiProvider.ReformatAsync(
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
