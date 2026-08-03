using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OpenKey.Gui.Views;

/// <summary>
/// The OpenKey mark. Size it by setting <c>Width</c> and <c>Height</c> at the call site — the
/// geometry is a 24-unit master scaled by a <c>Viewbox</c>.
/// </summary>
public partial class Brandmark : UserControl
{
    public Brandmark() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
