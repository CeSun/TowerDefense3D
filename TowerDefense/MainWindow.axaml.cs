using Avalonia.Controls;

namespace TowerDefense;

public partial class MainWindow : Window
{
    private MenuView? _menuView;
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
            var projectDir = FindProjectMapsDir();
            if (projectDir != null)
                _mapsDir = projectDir;
        }

        EnsureMapsDir();

        // Check for command-line args to skip the menu
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("--game"))
        {
            ShowGame();
        }
        else if (args.Contains("--editor"))
        {
            ShowEditor();
        }
        else if (args.Contains("--play"))
        {
            // --play <mapfile> — directly play a specific map
            var idx = Array.IndexOf(args, "--play");
            if (idx + 1 < args.Length)
            {
                var mapPath = args[idx + 1];
                if (!Path.IsPathRooted(mapPath))
                    mapPath = Path.Combine(_mapsDir, mapPath);
                ShowGame(mapPath);
            }
            else
            {
                ShowGame();
            }
        }
        else
        {
            // Default: show the main menu
            ShowMenu();
        }
    }

    // ==================== Maps Directory ====================

    private static string? FindProjectMapsDir()
    {
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

    private void EnsureMapsDir()
    {
        if (!Directory.Exists(_mapsDir))
            Directory.CreateDirectory(_mapsDir);

        // Ensure a default map exists
        var defaultPath = Path.Combine(_mapsDir, "default.json");
        if (!File.Exists(defaultPath))
        {
            MapData.CreateDefault().SaveToFile(defaultPath);
        }
    }

    private int CountMaps()
    {
        if (!Directory.Exists(_mapsDir)) return 0;
        return Directory.GetFiles(_mapsDir, "*.json").Length;
    }

    private string[] GetMapFiles()
    {
        if (!Directory.Exists(_mapsDir)) return Array.Empty<string>();
        return Directory.GetFiles(_mapsDir, "*.json");
    }

    // ==================== Navigation ====================

    private void ShowMenu()
    {
        if (_menuView == null)
        {
            _menuView = new MenuView();
            _menuView.OnPlayGame = () => ShowGame();
            _menuView.OnOpenEditor = () => ShowEditor();
        }

        _menuView.MapCount = CountMaps();
        ContentArea.Content = _menuView;
    }

    private void ShowGame(string? mapFilePath = null)
    {
        if (_gameView == null)
        {
            _gameView = new GameView();
            _gameView.OnMainMenu = () => ShowMenu();
            _gameView.OnBackToEditor = () => ShowEditor();
        }

        _gameView.SetMapsDirectory(_mapsDir);

        if (mapFilePath != null && File.Exists(mapFilePath))
        {
            // Load specific map file
            var map = MapData.LoadFromFile(mapFilePath);
            if (map != null)
            {
                _gameView.LoadMap(map);
                _gameView.SelectMapFile(mapFilePath);
            }
            else
            {
                _gameView.LoadMap(MapData.CreateDefault());
            }
        }
        else
        {
            // Load first available map or default
            var mapFiles = GetMapFiles();
            if (mapFiles.Length > 0)
            {
                var map = MapData.LoadFromFile(mapFiles[0]);
                _gameView.LoadMap(map ?? MapData.CreateDefault());
                _gameView.SelectMapFile(mapFiles[0]);
            }
            else
            {
                _gameView.LoadMap(MapData.CreateDefault());
            }
        }

        // Pure game mode: show map selector, hide editor-specific buttons
        _gameView.SetPureGameMode(true);

        ContentArea.Content = _gameView;
    }

    /// <summary>
    /// Launch game view from the editor (test map).
    /// </summary>
    private void ShowGameFromEditor(MapData map)
    {
        if (_gameView == null)
        {
            _gameView = new GameView();
            _gameView.OnMainMenu = () => ShowMenu();
            _gameView.OnBackToEditor = () => ShowEditor();
        }

        _gameView.SetMapsDirectory(_mapsDir);
        _gameView.LoadMap(map);

        // Editor-launched mode: show "Back to Editor", hide map selector
        _gameView.SetPureGameMode(false);

        ContentArea.Content = _gameView;
    }

    private void ShowEditor()
    {
        if (_editorView == null)
        {
            _editorView = new MapEditorView();
            _editorView.OnPlayMap = OnPlayMap;
            _editorView.OnMainMenu = () => ShowMenu();
            _editorView.Initialize(_mapsDir);
        }

        ContentArea.Content = _editorView;
    }

    private void OnPlayMap(MapData map)
    {
        _currentMapData = map;
        ShowGameFromEditor(map);
    }
}
