namespace Loupedeck.VoxRingPlugin.Actions;


using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
public class QuickTypeAction : QuickSendActionBase
{
    public QuickTypeAction()
        : base("Type Out", "Hold to record, release to type into active window", "3 Quick Send") { }

    protected override string DestinationName => "Type Out";
    protected override string IconResourceName => "mode-type.svg";
}
