using Avalonia.Controls;
using System.Reflection;

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

        // Use ApplicationData as the writable maps directory (works cross-platform: Desktop + Android).
        // This is Android's internal app-private storage — no permissions required.
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _mapsDir = Path.Combine(appData, "Maps");

        // Extract built-in maps from embedded resources on first run (or if directory is missing).
        ExtractEmbeddedMaps();

        // Fall back to desktop dev paths if extraction didn't yield anything.
        if (!Directory.Exists(_mapsDir) || Directory.GetFiles(_mapsDir, "*.json").Length == 0)
        {
            var fallback = Path.Combine(AppContext.BaseDirectory, "Maps");
            if (Directory.Exists(fallback))
            {
                _mapsDir = fallback;
            }
            else
            {
                var projectDir = FindProjectMapsDir();
                if (projectDir != null)
                    _mapsDir = projectDir;
            }
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

    /// <summary>
    /// Extract built-in map JSON files from embedded resources to the app data directory.
    /// This ensures maps bundled in the APK are available on first launch (Android) and
    /// provides a clean writable copy on Desktop as well.
    /// </summary>
    private void ExtractEmbeddedMaps()
    {
        // Already populated — skip extraction to avoid overwriting user changes.
        if (Directory.Exists(_mapsDir) && Directory.GetFiles(_mapsDir, "*.json").Length > 0)
            return;

        var assembly = typeof(MapData).Assembly;
        var prefix = $"{assembly.GetName().Name}.Maps.";

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(prefix) || !resourceName.EndsWith(".json"))
                continue;

            // Extract just the filename (e.g. "1.json")
            var fileName = resourceName.Substring(prefix.Length);

            try
            {
                Directory.CreateDirectory(_mapsDir);

                var targetPath = Path.Combine(_mapsDir, fileName);
                if (File.Exists(targetPath))
                    continue; // Don't overwrite existing files.

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                using var fileStream = File.Create(targetPath);
                stream.CopyTo(fileStream);
            }
            catch
            {
                // Best-effort: skip files that fail (e.g. permissions, disk full).
            }
        }
    }

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

        // Ensure Level 1 exists
        var defaultPath = Path.Combine(_mapsDir, "1.json");
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
        }

        _gameView.SetMapsDirectory(_mapsDir);
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
        }

        _gameView.SetMapsDirectory(_mapsDir);
        _gameView.SetSelectedLevel(map.Name);

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
