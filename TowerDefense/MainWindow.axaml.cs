using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TowerDefense;

public partial class MainWindow : Window
{
    private GameView? _gameView;
    private MapEditorView? _editorView;
    private MapData _currentMapData = null!;
    private string _mapsDir = string.Empty;

    public MainWindow()
    {
        InitializeComponent();

        // Determine maps directory (next to the executable or in project during dev)
        _mapsDir = Path.Combine(AppContext.BaseDirectory, "Maps");

        // Fall back to a relative path during development
        if (!Directory.Exists(_mapsDir))
        {
            // Try the project source directory
            var projectDir = FindProjectMapsDir();
            if (projectDir != null)
                _mapsDir = projectDir;
        }

        // Start in editor mode
        SwitchToEditor();
    }

    private static string? FindProjectMapsDir()
    {
        // Walk up from base directory to find TowerDefense/Maps
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            var mapsPath = Path.Combine(dir, "TowerDefense", "Maps");
            if (Directory.Exists(mapsPath))
                return mapsPath;
            mapsPath = Path.Combine(dir, "Maps");
            if (Directory.Exists(mapsPath))
                return mapsPath;
            dir = Path.GetDirectoryName(dir);
            if (dir == null) break;
        }
        return null;
    }

    private void SwitchToEditor()
    {
        if (_editorView == null)
        {
            _editorView = new MapEditorView();
            _editorView.OnPlayMap = OnPlayMap;
            _editorView.Initialize(_mapsDir);
        }

        ContentArea.Content = _editorView;
        _currentMapData = _editorView.GetCurrentMap();
        UpdateToolbarHighlight(false);
    }

    private void SwitchToPlay()
    {
        if (_gameView == null)
        {
            _gameView = new GameView();
            _gameView.OnBackToEditor = () => SwitchToEditor();
        }

        _gameView.LoadMap(_currentMapData);
        ContentArea.Content = _gameView;
        UpdateToolbarHighlight(true);
    }

    private void OnPlayMap(MapData map)
    {
        _currentMapData = map;
        SwitchToPlay();
    }

    private void OnSwitchToPlay(object? sender, RoutedEventArgs e)
    {
        _currentMapData = _editorView?.GetCurrentMap() ?? MapData.CreateDefault();
        SwitchToPlay();
    }

    private void OnSwitchToEditor(object? sender, RoutedEventArgs e)
    {
        SwitchToEditor();
    }

    private void UpdateToolbarHighlight(bool isPlayMode)
    {
        PlayModeBtn.Background = isPlayMode
            ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0xCC, 0x2d, 0x7a, 0x27))
            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0xCC, 0x2d, 0x5a, 0x27));
        PlayModeBtn.BorderThickness = isPlayMode
            ? new Avalonia.Thickness(2, 2, 2, 2)
            : new Avalonia.Thickness(0);
        PlayModeBtn.BorderBrush = Avalonia.Media.Brushes.White;

        EditorModeBtn.Background = !isPlayMode
            ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0xCC, 0x5a, 0x8a, 0x5a))
            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0xCC, 0x4a, 0x6a, 0x8a));
        EditorModeBtn.BorderThickness = !isPlayMode
            ? new Avalonia.Thickness(2, 2, 2, 2)
            : new Avalonia.Thickness(0);
        EditorModeBtn.BorderBrush = Avalonia.Media.Brushes.White;

        CurrentMapLabel.Text = $"Map: {_currentMapData.Name}";
    }
}
