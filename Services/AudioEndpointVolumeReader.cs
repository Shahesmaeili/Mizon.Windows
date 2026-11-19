using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Mizon.Windows.Services;

internal readonly record struct AudioEndpointVolumeState(int Percentage, bool IsMuted)
{
    internal bool IsSilent => IsMuted || Percentage == 0;
}

/// <summary>Reads and adjusts the Windows master volume of an individual render endpoint.</summary>
internal static partial class AudioEndpointVolumeReader
{
    private static readonly ConcurrentDictionary<string, byte> LoggedFailures = new(StringComparer.Ordinal);

    private static readonly Guid EnumeratorClass = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid EnumeratorInterface = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid EndpointVolumeInterface = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    private const uint ClassContextAll = 23;
    private const int ChangedApartmentMode = unchecked((int)0x80010106);
    // IUnknown occupies slots 0–2. These indices match mmdeviceapi.h and endpointvolume.h.
    private const int GetDeviceSlot = 5;
    private const int ActivateSlot = 3;
    private const int SetMasterVolumeLevelScalarSlot = 7;
    private const int GetMasterVolumeLevelScalarSlot = 9;
    private const int SetMuteSlot = 14;
    private const int GetMuteSlot = 15;

    public static int? TryGetVolumePercentage(string deviceId) => TryGetState(deviceId)?.Percentage;

    public static AudioEndpointVolumeState? TryGetState(string deviceId) => AccessEndpoint(deviceId, null);

    public static AudioEndpointVolumeState? TrySetVolumePercentage(string deviceId, int percentage) =>
        percentage is >= 0 and <= 100 ? AccessEndpoint(deviceId, percentage) : null;

    private static unsafe AudioEndpointVolumeState? AccessEndpoint(string deviceId, int? requestedPercentage)
    {
        nint enumerator = 0, device = 0, endpointVolume = 0;
        var apartment = CoInitializeEx(0, 0); // COINIT_MULTITHREADED

        try
        {
            // An existing STA can use Core Audio too. Only a successful call here
            // adds an initialization reference that must be released.
            if (apartment != ChangedApartmentMode) Marshal.ThrowExceptionForHR(apartment);
            Marshal.ThrowExceptionForHR(CoCreateInstance(in EnumeratorClass, 0, ClassContextAll, in EnumeratorInterface, out enumerator));

            var getDevice = (delegate* unmanaged[Stdcall]<nint, char*, nint*, int>)Method(enumerator, GetDeviceSlot);
            fixed (char* endpointId = GetEndpointId(deviceId))
                Marshal.ThrowExceptionForHR(getDevice(enumerator, endpointId, &device));

            var volumeId = EndpointVolumeInterface;
            var activate = (delegate* unmanaged[Stdcall]<nint, Guid*, uint, nint, nint*, int>)Method(device, ActivateSlot);
            Marshal.ThrowExceptionForHR(activate(device, &volumeId, ClassContextAll, 0, &endpointVolume));

            var getMute = (delegate* unmanaged[Stdcall]<nint, int*, int>)Method(endpointVolume, GetMuteSlot);
            int muted = 0;
            Marshal.ThrowExceptionForHR(getMute(endpointVolume, &muted));

            if (requestedPercentage is { } requested)
            {
                var setLevel = (delegate* unmanaged[Stdcall]<nint, float, nint, int>)Method(endpointVolume, SetMasterVolumeLevelScalarSlot);
                Marshal.ThrowExceptionForHR(setLevel(endpointVolume, requested / 100f, 0));
                if (requested > 0 && muted != 0)
                {
                    var setMute = (delegate* unmanaged[Stdcall]<nint, int, nint, int>)Method(endpointVolume, SetMuteSlot);
                    Marshal.ThrowExceptionForHR(setMute(endpointVolume, 0, 0));
                    muted = 0;
                }
            }

            var getLevel = (delegate* unmanaged[Stdcall]<nint, float*, int>)Method(endpointVolume, GetMasterVolumeLevelScalarSlot);
            float level = 0;
            Marshal.ThrowExceptionForHR(getLevel(endpointVolume, &level));
            var percentage = (int)Math.Round(Math.Clamp(level, 0f, 1f) * 100f, MidpointRounding.AwayFromZero);
            return new(percentage, muted != 0);
        }
        catch (Exception error) when (error is COMException or ArgumentException or UnauthorizedAccessException or InvalidCastException)
        {
            LogFailureOnce(deviceId, requestedPercentage.HasValue ? "Unable to set endpoint volume" : "Unable to read endpoint volume", error);
            return null;
        }
        finally
        {
            if (endpointVolume != 0) Marshal.Release(endpointVolume);
            if (device != 0) Marshal.Release(device);
            if (enumerator != 0) Marshal.Release(enumerator);
            if (apartment >= 0) CoUninitialize();
        }
    }

    private static unsafe nint Method(nint instance, int slot) => (*(nint**)instance)[slot];

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

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

}
