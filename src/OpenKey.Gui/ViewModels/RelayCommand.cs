using System.Windows.Input;

namespace OpenKey.Gui.ViewModels;

/// <summary>
/// Minimal <see cref="ICommand"/> for the window's key bindings. Buttons call view-model methods
/// directly, so nothing here needs parameters or dynamic enablement.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute();
}
