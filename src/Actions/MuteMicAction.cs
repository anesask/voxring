namespace Loupedeck.VoxRingPlugin.Actions;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

[SupportedOSPlatform("windows")]
public class MuteMicAction : PluginDynamicCommand
{
    public MuteMicAction()
        : base(displayName: "Mute Mic", description: "Toggle your system microphone mute on or off.", groupName: "5 Controls")
    {
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
    {
        try { return PluginResources.ReadImage(IsMuted() ? "mic-off.svg" : "dictate.svg"); }
        catch { return PluginResources.ReadImage("dictate.svg"); }
    }

    protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) => string.Empty;

    protected override void RunCommand(String actionParameter)
    {
        RunOnSta(() =>
        {
            var vol = GetEndpointVolume();
            if (vol == null) return;
            try
            {
                vol.GetMute(out var muted);
                var empty = Guid.Empty;
                vol.SetMute(!muted, ref empty);
                PluginLog.Info($"Mic mute toggled to {!muted}");
            }
            finally { Marshal.ReleaseComObject(vol); }
        });
        this.ActionImageChanged();
    }

    private static bool IsMuted()
    {
        var result = false;
        RunOnSta(() =>
        {
            var vol = GetEndpointVolume();
            if (vol == null) return;
            try { vol.GetMute(out result); }
            finally { Marshal.ReleaseComObject(vol); }
        });
        return result;
    }

    private static IAudioEndpointVolume GetEndpointVolume()
    {
        object enumeratorObj = null;
        try
        {
            var enumType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), true);
            enumeratorObj = Activator.CreateInstance(enumType);
            var enumerator = (IMMDeviceEnumerator)enumeratorObj;

            enumerator.GetDefaultAudioEndpoint(1 /* eCapture */, 1 /* eCommunications */, out var device);
            if (device == null) return null;

            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var volObj);
            Marshal.ReleaseComObject(device);

            return volObj as IAudioEndpointVolume;
        }
        catch (Exception ex)
        {
            PluginLog.Error($"MuteMic: failed to get endpoint volume: {ex.Message}");
            return null;
        }
        finally
        {
            if (enumeratorObj != null) Marshal.ReleaseComObject(enumeratorObj);
        }
    }

    private static void RunOnSta(Action action)
    {
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { PluginLog.Error($"MuteMic STA error: {ex.Message}"); } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(3000);
    }

    // --- Minimal Core Audio COM interfaces (no NAudio dependency) ---

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int GetChannelCount(out uint pnChannelCount);
        [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
        [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }
}
