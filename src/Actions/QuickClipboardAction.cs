namespace Loupedeck.VoxRingPlugin.Actions;


using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
public class QuickClipboardAction : QuickSendActionBase
{
    public QuickClipboardAction()
        : base("Clipboard", "Hold to record, release to copy to clipboard", "3 Quick Send") { }

    protected override string DestinationName => "Clipboard";
    protected override string IconResourceName => "mode-clipboard.svg";
}
