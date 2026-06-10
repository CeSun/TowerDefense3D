using Avalonia.Controls;

namespace TowerDefense;

/// <summary>
/// Desktop window shell. All navigation lives in <see cref="MainView"/>,
/// which is shared with the Android single-view path.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
