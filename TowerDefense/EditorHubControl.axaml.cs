using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace TowerDefense;

public partial class EditorHubControl : UserControl
{
    // ==================== Callbacks ====================
    public Action? OnBack { get; set; }
    public Action? OnOpenMapEditor { get; set; }
    public Action? OnOpenTowerEditor { get; set; }
    public Action? OnOpenMonsterEditor { get; set; }

    public EditorHubControl()
    {
        InitializeComponent();
        BuildCards();
    }

    private void BuildCards()
    {
        CardGrid.Children.Clear();

        // Map Editor card
        CardGrid.Children.Add(CreateCard(
            "🗺", Loc.Get("EditorHub.MapEditor"),
            Loc.Get("EditorHub.MapEditorDesc"),
            Brush.Parse("#CC2d5a27"), Brush.Parse("#CC3a7a3a"),
            () => OnOpenMapEditor?.Invoke()));

        // Tower Editor card
        CardGrid.Children.Add(CreateCard(
            "🗼", Loc.Get("EditorHub.TowerEditor"),
            Loc.Get("EditorHub.TowerEditorDesc"),
            Brush.Parse("#CC2d3a5a"), Brush.Parse("#CC4a5a8a"),
            () => OnOpenTowerEditor?.Invoke()));

        // Monster Editor card
        CardGrid.Children.Add(CreateCard(
            "👾", Loc.Get("EditorHub.MonsterEditor"),
            Loc.Get("EditorHub.MonsterEditorDesc"),
            Brush.Parse("#CC5a3a2d"), Brush.Parse("#CC7a5a3a"),
            () => OnOpenMonsterEditor?.Invoke()));
    }

    private static Border CreateCard(string icon, string title, string desc,
        IBrush bg, IBrush hoverBg, Action onClick)
    {
        var card = new Border
        {
            Width = 220,
            Height = 180,
            Margin = new Avalonia.Thickness(12),
            CornerRadius = new Avalonia.CornerRadius(12),
            Background = bg,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };

        var sp = new StackPanel
        {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Spacing = 10,
        };

        var iconBlock = new TextBlock
        {
            Text = icon,
            FontSize = 48,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        var descBlock = new TextBlock
        {
            Text = desc,
            FontSize = 11,
            Foreground = Brush.Parse("#AAA"),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(8, 0),
        };

        sp.Children.Add(iconBlock);
        sp.Children.Add(titleBlock);
        sp.Children.Add(descBlock);
        card.Child = sp;

        card.PointerEntered += (_, _) => card.Background = hoverBg;
        card.PointerExited += (_, _) => card.Background = bg;
        card.PointerPressed += (_, _) => onClick();

        return card;
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        OnBack?.Invoke();
    }
}
