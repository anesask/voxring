namespace Loupedeck.VoxRingPlugin.Actions;


using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
public class QuickTeamsAction : QuickSendActionBase
{
    public QuickTeamsAction()
        : base("Teams", "Hold to record, release to post to Teams", "3 Quick Send") { }

    protected override string DestinationName => "Teams";
    protected override string IconResourceName => "mode-teams.svg";
}
