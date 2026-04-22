namespace Loupedeck.VoxRingPlugin.Actions;


using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
public class QuickCalendarAction : QuickSendActionBase
{
    public QuickCalendarAction()
        : base("Calendar", "Hold to record, release to create calendar event", "3 Quick Send") { }

    protected override string DestinationName => "Calendar";
    protected override string IconResourceName => "mode-calendar.svg";
}
