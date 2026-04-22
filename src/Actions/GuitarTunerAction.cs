namespace Loupedeck.VoxRingPlugin.Actions;

using System.Runtime.Versioning;
using Loupedeck.VoxRingPlugin.Services;

/// <summary>
/// Guitar tuner — Dynamic Folder with exactly 4 slots (Back + 3):
///   [← Back]  [▼ Flat]  [Note · tap=Start/Stop]  [▲ Sharp]
///
/// Group "Tuner" sorts after "Tools" in Logi Options+.
/// CC-only: check plugin log for "GuitarTuner: deviceType = X (N)" then uncomment the filter.
/// </summary>
[SupportedOSPlatform("windows")]
public class GuitarTunerFolder : PluginDynamicFolder
{
    private const string CmdSpacer1 = "_1";
    private const string CmdSpacer2 = "_2";
    private const string CmdFlat    = "Flat";
    private const string CmdNote    = "Note";
    private const string CmdSharp   = "Sharp";

    private static readonly BitmapColor GreenBg = new(0, 140, 60);
    private static readonly BitmapColor RedBg   = new(180, 40, 40);
    private static readonly BitmapColor StartBg = new(0, 90, 180);
    private static readonly BitmapColor DimBg   = new(28, 28, 28);
    private static readonly BitmapColor MidGray = new(90, 90, 90);
    private static readonly BitmapColor IdleFg  = new(110, 110, 110);

    private static bool _deviceTypeLogged;

    public GuitarTunerFolder()
    {
        this.DisplayName  = "Guitar Tuner";
        this.GroupName    = "8 Utilities";
        this.Description  = "Guitar tuner for standard E A D G B E tuning. Enter the folder, tap Note to start listening, tune each string to green.";
        GuitarTunerService.StateChanged += OnServiceStateChanged;
    }

    private void OnServiceStateChanged()
    {
        this.CommandImageChanged(CmdFlat);
        this.CommandImageChanged(CmdNote);
        this.CommandImageChanged(CmdSharp);
    }

    public override IEnumerable<string> GetButtonPressActionNames(DeviceType deviceType)
    {
        if (!_deviceTypeLogged)
        {
            PluginLog.Info($"GuitarTuner: deviceType = {deviceType} ({(int)deviceType})");
            _deviceTypeLogged = true;
        }

        // CC-only filter — Creative Console device types are MxCreativeDialpad and MxCreativeKeypad.
        // Check the plugin log line above for which value fires on your hardware, then uncomment:
        // if (deviceType != DeviceType.MxCreativeDialpad) return Array.Empty<string>();

        return new List<string>
        {
            PluginDynamicFolder.NavigateUpActionName,
            this.CreateCommandName(CmdSpacer1),
            this.CreateCommandName(CmdSpacer2),
            this.CreateCommandName(CmdFlat),
            this.CreateCommandName(CmdNote),
            this.CreateCommandName(CmdSharp),
        };
    }

    public override BitmapImage GetButtonImage(PluginImageSize imageSize)
    {
        try { return PluginResources.ReadImage("guitar-tuner.svg"); }
        catch { return base.GetButtonImage(imageSize); }
    }

    public override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) =>
        string.Empty;

    public override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        using var b = new BitmapBuilder(imageSize);

        var note      = GuitarTunerService.Note;
        var cents     = GuitarTunerService.CentsOff;
        var active    = GuitarTunerService.IsActive;
        var holdGreen = active && DateTime.UtcNow < GuitarTunerService.InTuneHoldUntil;
        var inTune    = active && note != "---" && Math.Abs(cents) <= 8;

        switch (actionParameter)
        {
            case CmdFlat:
            {
                bool flat = active && note != "---" && cents < -8;
                b.Clear(flat ? RedBg : DimBg);
                b.DrawText("▼", flat ? BitmapColor.White : (active ? MidGray : new BitmapColor(45, 45, 45)), 32);
                return b.ToImage();
            }

            case CmdNote:
            {
                bool showGreen = inTune || holdGreen;
                b.Clear(showGreen ? GreenBg : (active ? BitmapColor.Black : StartBg));

                if (!active)
                    b.DrawText("Start", BitmapColor.White, 20);
                else if (note == "---")
                    b.DrawText("---", MidGray, 22);
                else if (showGreen)
                    b.DrawText(note + "\nTuned", BitmapColor.White, 20);
                else
                {
                    var sign = cents >= 0 ? "+" : "";
                    b.DrawText($"{note}\n{sign}{cents:F0}¢", BitmapColor.White, 20);
                }
                return b.ToImage();
            }

            case CmdSharp:
            {
                bool sharp = active && note != "---" && cents > 8;
                b.Clear(sharp ? RedBg : DimBg);
                b.DrawText("▲", sharp ? BitmapColor.White : (active ? MidGray : new BitmapColor(45, 45, 45)), 32);
                return b.ToImage();
            }

            case CmdSpacer1:
            case CmdSpacer2:
                b.Clear(BitmapColor.Black);
                return b.ToImage();
        }

        return base.GetCommandImage(actionParameter, imageSize);
    }

    public override void RunCommand(string actionParameter)
    {
        if (actionParameter == PluginDynamicFolder.NavigateUpActionName)
        {
            GuitarTunerService.Stop();
            return;
        }

        if (actionParameter == CmdNote)
        {
            if (GuitarTunerService.IsActive)
                GuitarTunerService.Stop();
            else
                GuitarTunerService.Start();

            this.CommandImageChanged(CmdFlat);
            this.CommandImageChanged(CmdNote);
            this.CommandImageChanged(CmdSharp);
        }
    }
}
