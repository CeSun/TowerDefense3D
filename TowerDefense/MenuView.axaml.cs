using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TowerDefense;

public partial class MenuView : UserControl
{
    /// <summary>Called when user wants to enter game mode.</summary>
    public Action? OnPlayGame { get; set; }

    /// <summary>Called when user wants to enter the map editor.</summary>
    public Action? OnOpenEditor { get; set; }

    /// <summary>Optional: the number of available maps, shown as a hint.</summary>
    public int MapCount
    {
        set => MapCountLabel.Text = value > 0
            ? $"{value} map{(value > 1 ? "s" : "")} available"
            : "No maps found — create one in the Editor first";
    }

    public MenuView()
    {
        InitializeComponent();
    }

    private void OnPlayGameClick(object? sender, RoutedEventArgs e)
    {
        OnPlayGame?.Invoke();
    }

    private void OnOpenEditorClick(object? sender, RoutedEventArgs e)
    {
        OnOpenEditor?.Invoke();
    }
}
