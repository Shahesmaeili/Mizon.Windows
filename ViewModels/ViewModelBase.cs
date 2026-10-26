using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Mizon.Windows.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected void SetProperty<T>(T value, [CallerMemberName] string? propertyName = null)
    {
        var prop = GetType().GetProperty(propertyName!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop is null) return;

        var current = prop.GetValue(this);
        if (!Equals(current, value))
        {
            prop.SetValue(this, value);
            OnPropertyChanged(propertyName);
        }
    }


    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
