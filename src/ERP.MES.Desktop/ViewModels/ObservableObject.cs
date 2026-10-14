using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ERP.MES.Desktop.ViewModels;

/// <summary>
/// Minimal change-notification base for view models in the Desktop (presentation) project. It uses only
/// base-library types, so view models can be unit tested without opening a window.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
