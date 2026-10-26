using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace Mizon.Windows.Services;

internal sealed record BluetoothBatteryDevice(
    string Name,
    byte? BatteryLevel,
    IReadOnlyCollection<string> ContainerIds,
    string? BluetoothAddress);
internal sealed record GattBatteryDevice(
    string Name,
    byte BatteryLevel,
    IReadOnlyCollection<string> ContainerIds,
    string? BluetoothAddress);

/// <summary>
/// Resolves Bluetooth battery information separately from render endpoints.
/// Windows exposes them as different DeviceInformation instances.
/// </summary>
internal static class BluetoothBatteryReader
{
    private const string BatteryLifeProperty = "System.Devices.BatteryLife";
    private const string ContainerIdProperty = "System.Devices.ContainerId";
    private const string DeviceInstanceIdProperty = "System.Devices.DeviceInstanceId";
    private static readonly string[] RequestedProperties =
    [
        BatteryLifeProperty,
        ContainerIdProperty,
        DeviceInstanceIdProperty
    ];

    public static async Task<IReadOnlyList<BluetoothBatteryDevice>> GetPairedDevicesAsync()
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(
                BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                RequestedProperties);
            var gattBatteryDevices = await GetGattBatteryDevicesAsync();

            var results = new List<BluetoothBatteryDevice>();
            foreach (var device in devices)
            {
                var bluetoothDevice = await BluetoothDevice.FromIdAsync(device.Id);
                var bluetoothAddress = bluetoothDevice is null
                    ? null
                    : bluetoothDevice.BluetoothAddress.ToString("X12");

                results.Add(new BluetoothBatteryDevice(
                    device.Name,
                    ReadBatteryLevel(device) ?? ResolveGattBatteryLevel(device.Name, GetContainerIds(device), bluetoothAddress, gattBatteryDevices),
                    GetContainerIds(device),
                    bluetoothAddress));
            }

            AppLog.Information($"Paired Bluetooth battery devices. Count={results.Count}. Devices=[{string.Join(" | ", results.Select(device => $"{device.Name}: {device.BatteryLevel?.ToString() ?? "unavailable"}, Address={device.BluetoothAddress ?? "unavailable"}, Correlations=[{string.Join(',', device.ContainerIds)}]"))}].");
            return results;
        }
        catch (Exception exception)
        {
            AppLog.Error("Paired Bluetooth battery enumeration failed.", exception);
            return [];
        }
    }

    private static async Task<IReadOnlyList<GattBatteryDevice>> GetGattBatteryDevicesAsync()
    {
        var batteryServiceSelector = GattDeviceService.GetDeviceSelectorFromUuid(GattServiceUuids.Battery);
        var services = await DeviceInformation.FindAllAsync(
            batteryServiceSelector,
            new[] { ContainerIdProperty, DeviceInstanceIdProperty });
        var results = new List<GattBatteryDevice>();

        foreach (var serviceInformation in services)
        {
            var service = await GattDeviceService.FromIdAsync(serviceInformation.Id);
            if (service is null)
                continue;

            var characteristicsResult = await service.GetCharacteristicsAsync();
            if (characteristicsResult.Status != GattCommunicationStatus.Success)
                continue;

            var batteryCharacteristic = characteristicsResult.Characteristics
                .FirstOrDefault(characteristic => characteristic.Uuid == GattCharacteristicUuids.BatteryLevel);
            if (batteryCharacteristic is null)
                continue;

            var batteryResult = await batteryCharacteristic.ReadValueAsync();
            if (batteryResult.Status != GattCommunicationStatus.Success)
                continue;

            var reader = DataReader.FromBuffer(batteryResult.Value);
            var bluetoothLeDevice = await BluetoothLEDevice.FromIdAsync(service.DeviceId);
            results.Add(new GattBatteryDevice(
                string.IsNullOrWhiteSpace(bluetoothLeDevice?.Name)
                    ? serviceInformation.Name
                    : bluetoothLeDevice.Name,
                reader.ReadByte(),
                GetContainerIds(serviceInformation).Append(service.DeviceId).ToArray(),
                TryGetGattBluetoothAddress(service.DeviceId)));
        }

        AppLog.Information($"GATT battery services. Count={results.Count}. Services=[{string.Join(" | ", results.Select(result => $"{result.Name}: {result.BatteryLevel}, Containers=[{string.Join(',', result.ContainerIds)}]"))}].");
        return results;
    }

    private static byte? ResolveGattBatteryLevel(
        string bluetoothDeviceName,
        IReadOnlyCollection<string> bluetoothContainerIds,
        string? bluetoothAddress,
        IReadOnlyList<GattBatteryDevice> gattBatteryDevices)
    {
        var matchingServices = gattBatteryDevices.Where(service =>
            service.ContainerIds.Intersect(bluetoothContainerIds, StringComparer.OrdinalIgnoreCase).Any() ||
            service.ContainerIds.Any(serviceId => bluetoothContainerIds.Any(bluetoothId => HaveSameBluetoothAddress(serviceId, bluetoothId))) ||
            (!string.IsNullOrWhiteSpace(bluetoothAddress) &&
             string.Equals(service.BluetoothAddress, bluetoothAddress, StringComparison.OrdinalIgnoreCase)) ||
            HaveSameDeviceName(service.Name, bluetoothDeviceName));

        return matchingServices.Select(service => (byte?)service.BatteryLevel).DefaultIfEmpty().Min();
    }

    public static async Task<DeviceInformation?> GetAudioDeviceDetailsAsync(string deviceId)
    {
        try
        {
            return await DeviceInformation.CreateFromIdAsync(deviceId, RequestedProperties);
        }
        catch (ArgumentException exception)
        {
            AppLog.Error($"Audio endpoint details could not be read for '{deviceId}'.", exception);
            return null;
        }
        catch (System.Runtime.InteropServices.COMException exception)
        {
            AppLog.Error($"Audio endpoint details could not be read for '{deviceId}'.", exception);
            return null;
        }
    }

    public static byte? ResolveBatteryLevel(
        DeviceInformation? audioDevice,
        string audioDeviceName,
        IReadOnlyList<BluetoothBatteryDevice> bluetoothDevices)
    {
        if (audioDevice is not null)
        {
            var directBatteryLevel = ReadBatteryLevel(audioDevice);
            if (directBatteryLevel.HasValue)
                return directBatteryLevel;

            var audioContainerIds = GetContainerIds(audioDevice);
            var containerMatch = bluetoothDevices.FirstOrDefault(bluetoothDevice =>
                bluetoothDevice.BatteryLevel.HasValue &&
                bluetoothDevice.ContainerIds.Intersect(audioContainerIds, StringComparer.OrdinalIgnoreCase).Any());

            if (containerMatch is not null)
                return containerMatch.BatteryLevel;
        }

        var normalizedAudioName = NormalizeName(audioDeviceName);
        var nameMatch = bluetoothDevices.FirstOrDefault(bluetoothDevice =>
        {
            if (!bluetoothDevice.BatteryLevel.HasValue)
                return false;

            var normalizedBluetoothName = NormalizeName(bluetoothDevice.Name);
            return normalizedAudioName.Contains(normalizedBluetoothName, StringComparison.OrdinalIgnoreCase) ||
                   normalizedBluetoothName.Contains(normalizedAudioName, StringComparison.OrdinalIgnoreCase);
        });

        return nameMatch?.BatteryLevel;
    }

    private static byte? ReadBatteryLevel(DeviceInformation device)
    {
        if (!device.Properties.TryGetValue(BatteryLifeProperty, out var value) || value is null)
            return null;

        return value switch
        {
            byte byteValue => byteValue,
            ushort ushortValue when ushortValue <= 100 => (byte)ushortValue,
            uint uintValue when uintValue <= 100 => (byte)uintValue,
            int intValue when intValue is >= 0 and <= 100 => (byte)intValue,
            _ => null
        };
    }

    private static IReadOnlyCollection<string> GetContainerIds(DeviceInformation device) =>
        new[] { ContainerIdProperty, DeviceInstanceIdProperty }
            .Where(propertyName => device.Properties.TryGetValue(propertyName, out var value) && value is not null)
            .Select(propertyName => device.Properties[propertyName]?.ToString())
            .OfType<string>()
            .ToArray();

    private static bool HaveSameBluetoothAddress(string left, string right)
    {
        var leftAddress = TryGetBluetoothAddress(left);
        var rightAddress = TryGetBluetoothAddress(right);
        return leftAddress is not null && string.Equals(leftAddress, rightAddress, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetBluetoothAddress(string value)
    {
        var match = Regex.Match(value, "DEV_([0-9A-F]{12})", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? TryGetGattBluetoothAddress(string deviceId)
    {
        var match = Regex.Match(
            deviceId,
            "-(?<address>(?:[0-9a-f]{2}:){5}[0-9a-f]{2})#GATT",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["address"].Value.Replace(":", string.Empty) : null;
    }

    private static bool HaveSameDeviceName(string left, string right)
    {
        var normalizedLeft = NormalizeName(left);
        var normalizedRight = NormalizeName(right);
        return !string.IsNullOrEmpty(normalizedLeft) &&
               !string.IsNullOrEmpty(normalizedRight) &&
               (normalizedLeft.Contains(normalizedRight, StringComparison.OrdinalIgnoreCase) ||
                normalizedRight.Contains(normalizedLeft, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeName(string value)
    {
        var openParenthesis = value.IndexOf('(');
        var closeParenthesis = value.LastIndexOf(')');
        if (openParenthesis >= 0 && closeParenthesis > openParenthesis)
            value = value[(openParenthesis + 1)..closeParenthesis];

        return value.Trim();
    }
}
