using System;
using System.Runtime.InteropServices;

namespace DuoWall.App.Helpers;

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject { }

[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
    int GetDevice(string pwstrId, out IMMDevice ppDevice);
    int RegisterEndpointNotificationCallback(IntPtr pClient);
    int UnregisterEndpointNotificationCallback(IntPtr pClient);
}

[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);
    int GetId(out string ppstrId);
    int GetState(out int pdwState);
}

[Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    int RegisterControlChangeNotify(IntPtr pNotify);
    int UnregisterControlChangeNotify(IntPtr pNotify);
    int GetChannelCount(out int pnChannelCount);
    int SetMasterVolumeLevel(float fLevelDB, Guid pguidEventContext);
    int SetMasterVolumeLevelScalar(float fLevel, Guid pguidEventContext);
    int GetMasterVolumeLevel(out float pfLevelDB);
    int GetMasterVolumeLevelScalar(out float pfLevel);
    int SetChannelVolumeLevel(int nChannel, float fLevelDB, Guid pguidEventContext);
    int SetChannelVolumeLevelScalar(int nChannel, float fLevel, Guid pguidEventContext);
    int GetChannelVolumeLevel(int nChannel, out float pfLevelDB);
    int GetChannelVolumeLevelScalar(int nChannel, out float pfLevel);
    int SetMute(bool bMute, Guid pguidEventContext);
    int GetMute(out bool pbMute);
    int GetVolumeStepInfo(out int pnStep, out int pnStepCount);
    int VolumeStepUp(Guid pguidEventContext);
    int VolumeStepDown(Guid pguidEventContext);
    int QueryHardwareSupport(out int pdwHardwareSupportMask);
    int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
}

/// <summary>
/// Lightweight Win32 CoreAudio wrapper for querying and setting the Windows default playback master volume and mute state.
/// </summary>
public static class AudioVolumeHelper
{
    private static readonly Guid AudioEndpointVolumeIid = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    private static IAudioEndpointVolume? GetDefaultVolumeEndpoint()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            // dataFlow: 0 = eRender, role: 1 = eMultimedia
            int hr = enumerator.GetDefaultAudioEndpoint(0, 1, out var dev);
            if (hr != 0 || dev == null) return null;

            var iid = AudioEndpointVolumeIid;
            hr = dev.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var ep);
            if (hr != 0 || ep == null) return null;

            return ep as IAudioEndpointVolume;
        }
        catch
        {
            return null;
        }
    }

    public static float GetMasterVolumePercent()
    {
        try
        {
            var ep = GetDefaultVolumeEndpoint();
            if (ep == null) return 50f;

            ep.GetMasterVolumeLevelScalar(out float level);
            return Math.Clamp(level * 100f, 0f, 100f);
        }
        catch
        {
            return 50f;
        }
    }

    public static bool SetMasterVolumePercent(float percent)
    {
        try
        {
            var ep = GetDefaultVolumeEndpoint();
            if (ep == null) return false;

            float scalar = Math.Clamp(percent / 100f, 0f, 1f);
            ep.SetMasterVolumeLevelScalar(scalar, Guid.Empty);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool GetIsMuted()
    {
        try
        {
            var ep = GetDefaultVolumeEndpoint();
            if (ep == null) return false;

            ep.GetMute(out bool muted);
            return muted;
        }
        catch
        {
            return false;
        }
    }

    public static bool ToggleMute()
    {
        try
        {
            var ep = GetDefaultVolumeEndpoint();
            if (ep == null) return false;

            ep.GetMute(out bool muted);
            ep.SetMute(!muted, Guid.Empty);
            return !muted;
        }
        catch
        {
            return false;
        }
    }
}
