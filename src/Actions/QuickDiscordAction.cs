namespace Loupedeck.VoxRingPlugin.Actions;


using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
public class QuickDiscordAction : QuickSendActionBase
{
    public QuickDiscordAction()
        : base("Discord", "Hold to record, release to post to Discord", "3 Quick Send") { }

    protected override string DestinationName => "Discord";
    protected override string IconResourceName => "mode-discord.svg";
}
