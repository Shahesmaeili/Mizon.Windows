using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace Mizon.Windows
{
    public sealed class BleHeartRateClient : IDisposable
    {

        // Heart Rate Service (یا Custom Service)
        public static readonly Guid ServiceUuid =
            Guid.Parse("eed6d5cc-c3b2-4d7b-8c6b-7acbf7965bb6");

        public static readonly Guid CharacteristicUuid =
            Guid.Parse("f1e2d3c4-b5a6-7890-1234-56789abcdef0");


        private BluetoothLEDevice _device;
        private GattCharacteristic _characteristic;

        public event Action<int>? HeartRateReceived;
        public event Action<string>? Log;

        // =============================
        // Scan + Connect
        // =============================
        public async Task<bool> ConnectAsync()
        {
            Log?.Invoke("Scanning for BLE devices....");

            var devices = await DeviceInformation.FindAllAsync(
                BluetoothLEDevice.GetDeviceSelector());

            var watch = devices.FirstOrDefault(d =>
                d.Name.Contains("Watch", StringComparison.OrdinalIgnoreCase));

            if (watch == null)
            {
                Log?.Invoke("Watch not found");
                return false;
            }

            _device = await BluetoothLEDevice.FromIdAsync(watch.Id);
            if (_device == null)
            {
                Log?.Invoke("Failed to connect");
                return false;
            }

            Log?.Invoke($"Connected to {_device.Name}");

            return await SetupGattAsync();
        }

        // =============================
        // GATT Setup
        // =============================
        private async Task<bool> SetupGattAsync()
        {
            var servicesResult = await _device.GetGattServicesAsync();
            if (servicesResult.Status != GattCommunicationStatus.Success)
                return false;

            var service = servicesResult.Services
                .FirstOrDefault(s => s.Uuid == ServiceUuid);

            if (service == null)
            {
                Log?.Invoke("Service not found");
                return false;
            }

            var charResult = await service.GetCharacteristicsAsync();
            _characteristic = charResult.Characteristics
                .FirstOrDefault(c => c.Uuid == CharacteristicUuid);

            if (_characteristic == null)
            {
                Log?.Invoke("Characteristic not found");
                return false;
            }

            _characteristic.ValueChanged += OnValueChanged;

            var status = await _characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);

            Log?.Invoke("Subscribed to notifications");
            return status == GattCommunicationStatus.Success;
        }

        // =============================
        // Receive Data
        // =============================
        private void OnValueChanged(
            GattCharacteristic sender,
            GattValueChangedEventArgs args)
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);

            // Example: single byte BPM
            byte bpm = reader.ReadByte();

            HeartRateReceived?.Invoke(bpm);
        }

        // =============================
        // Cleanup
        // =============================
        public void Dispose()
        {
            if (_characteristic != null)
                _characteristic.ValueChanged -= OnValueChanged;

            _device?.Dispose();
        }
    }
}
