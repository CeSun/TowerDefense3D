using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TowerDefense;

public partial class MenuView : UserControl
{
    /// <summary>Called when user wants to enter game mode.</summary>
    public Action? OnPlayGame { get; set; }

    /// <summary>Called when user wants to enter the map editor.</summary>
    public Action? OnOpenEditor { get; set; }

    /// <summary>Called when user wants to enter the monster editor.</summary>
    public Action? OnOpenMonsterEditor { get; set; }

    /// <summary>Called when user wants to enter the tower editor.</summary>
    public Action? OnOpenTowerEditor { get; set; }

    /// <summary>Called when user wants to reset all data.</summary>
    public Action? OnResetData { get; set; }

    /// <summary>Optional: the number of available maps, shown as a hint.</summary>
    public int MapCount
    {
        set => MapCountLabel.Text = value > 0
            ? string.Format(Loc.Get("Menu.MapCount"), value)
            : Loc.Get("Menu.NoMaps");
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

    private void OnOpenMonsterEditorClick(object? sender, RoutedEventArgs e)
    {
        OnOpenMonsterEditor?.Invoke();
    }

    private void OnOpenTowerEditorClick(object? sender, RoutedEventArgs e)
    {
        OnOpenTowerEditor?.Invoke();
    }

    private void OnResetDataClick(object? sender, RoutedEventArgs e)
    {
        OnResetData?.Invoke();
    }

    private void OnSwitchToEnglish(object? sender, RoutedEventArgs e)
    {
        Loc.Switch("en");
    }

    private void OnSwitchToChinese(object? sender, RoutedEventArgs e)
    {
        Loc.Switch("zh-CN");
    }
}
