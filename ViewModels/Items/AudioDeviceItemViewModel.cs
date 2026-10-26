using Windows.Devices.Enumeration;

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
    public int? VolumePercentage
    {
        get => field;
        set
        {
            if (SetField(ref field, value))
                OnPropertyChanged(nameof(HasVolumePercentage));
        }
    }

    public bool HasBatteryLevel => BatteryLevel is >= 0 and <= 100;
    public bool HasVolumePercentage => VolumePercentage is >= 0 and <= 100;

    public DeviceInformation DeviceInformation { get => field; set => SetField(ref field, value); } = null!;
}
