using Windows.Devices.Enumeration;
using Mizon.Windows.Services;

namespace Mizon.Windows.ViewModels.Items;

public sealed class AudioDeviceItemViewModel : Mizon.Windows.ViewModels.ViewModelBase
{
    public string Id { get; init; } = null!;
    public string Title { get => field; set => SetField(ref field, value); } = null!;
    public byte? BatteryLevel
    {
        get => field;
        set
        {
            if (SetField(ref field, value))
                OnPropertyChanged(nameof(HasBatteryLevel));
        }
    }
    internal AudioEndpointVolumeState? OutputVolume
    {
        get => field;
        set => SetField(ref field, value);
    }

    internal long VolumeRevision { get; set; }

    public bool HasBatteryLevel => BatteryLevel is >= 0 and <= 100;

    public DeviceInformation DeviceInformation { get => field; set => SetField(ref field, value); } = null!;
}
