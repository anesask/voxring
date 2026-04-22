namespace Loupedeck.VoxRingPlugin.Actions;


using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
public class QuickSlackAction : QuickSendActionBase
{
    public QuickSlackAction()
        : base("Slack", "Hold to record, release to post to Slack", "3 Quick Send") { }

    protected override string DestinationName => "Slack";
    protected override string IconResourceName => "mode-slack.svg";
}
