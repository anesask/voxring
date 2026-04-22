namespace Loupedeck.VoxRingPlugin.Actions;


using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
public class QuickEmailAction : QuickSendActionBase
{
    public QuickEmailAction()
        : base("Email", "Hold to record, release to open email draft", "3 Quick Send") { }

    protected override string DestinationName => "Email";
    protected override string IconResourceName => "mode-email.svg";
}
