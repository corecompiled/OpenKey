using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OpenKey.Gui.ViewModels;

/// <summary>
/// Minimal INotifyPropertyChanged base.
/// <para>
/// Hand-rolled rather than pulling in an MVVM toolkit: this is the only thing such a package would
/// be used for here, and the source generators most of them rely on interact badly with the AOT
/// build. Twenty lines is cheaper than the dependency.
/// </para>
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}
