using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Mizon.Windows.Services;

/// <summary>Reads the Windows master volume of an individual render endpoint.</summary>
internal static class AudioEndpointVolumeReader
{
    private static readonly ConcurrentDictionary<string, byte> LoggedFailures = new(StringComparer.Ordinal);

    public static int? TryGetVolumePercentage(string deviceId)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioEndpointVolume? endpointVolume = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            var endpointId = GetEndpointId(deviceId);
            Marshal.ThrowExceptionForHR(enumerator.GetDevice(endpointId, out device));
            var interfaceId = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref interfaceId, ClsCtx.All, IntPtr.Zero, out var endpointVolumeObject));
            endpointVolume = (IAudioEndpointVolume)endpointVolumeObject;
            Marshal.ThrowExceptionForHR(endpointVolume.GetMasterVolumeLevelScalar(out var level));
            return (int)Math.Round(Math.Clamp(level, 0f, 1f) * 100f, MidpointRounding.AwayFromZero);
        }
        catch (COMException exception)
        {
            LogFailureOnce(deviceId, "Unable to read endpoint volume", exception);
            return null;
        }
        catch (ArgumentException exception)
        {
            LogFailureOnce(deviceId, "Audio endpoint rejected device id", exception);
            return null;
        }
        catch (InvalidCastException exception)
        {
            LogFailureOnce(deviceId, "Audio endpoint volume interface is unavailable", exception);
            return null;
        }
        finally
        {
            if (endpointVolume is not null) Marshal.ReleaseComObject(endpointVolume);
            if (device is not null) Marshal.ReleaseComObject(device);
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }
    }

    private static void LogFailureOnce(string deviceId, string message, Exception exception)
    {
        if (LoggedFailures.TryAdd(deviceId, 0))
            AppLog.Error($"{message} for '{deviceId}'.", exception);
    }

    private static string GetEndpointId(string deviceInterfaceId)
    {
        var parts = deviceInterfaceId.Split('#');
        return parts.Length >= 3 && string.Equals(parts[1], "MMDEVAPI", StringComparison.OrdinalIgnoreCase)
            ? parts[2]
            : deviceInterfaceId;
    }

    private enum ClsCtx { All = 23 }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), ClassInterface(ClassInterfaceType.None)]
    private class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid interfaceId, ClsCtx classContext, IntPtr activationParameters, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
        [PreserveSig] int OpenPropertyStore(int storageAccessMode, out IntPtr properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int GetChannelCount(out uint channelCount);
        [PreserveSig] int SetMasterVolumeLevel(float levelInDecibels, IntPtr eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, IntPtr eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelInDecibels);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channelNumber, float levelInDecibels, IntPtr eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channelNumber, float level, IntPtr eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channelNumber, out float levelInDecibels);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channelNumber, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool isMuted, IntPtr eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool isMuted);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(IntPtr eventContext);
        [PreserveSig] int VolumeStepDown(IntPtr eventContext);
        [PreserveSig] int QueryHardwareSupport(out uint hardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float volumeMinInDecibels, out float volumeMaxInDecibels, out float volumeIncrementInDecibels);
    }
}
