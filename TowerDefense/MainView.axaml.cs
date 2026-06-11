using Avalonia.Controls;
using System.Reflection;

namespace TowerDefense;

/// <summary>
/// Root navigation container shared by Desktop (via MainWindow) and Android (directly as MainView).
/// Handles map directory setup and switching between Menu / Game / Editor views.
/// </summary>
public partial class MainView : UserControl
{
    private MenuView? _menuView;
    private GameView? _gameView;
    private MapListControl? _mapListView;
    private MapEditorView? _editorView;
    private MonsterEditorView? _monsterEditorView;
    private TowerEditorView? _towerEditorView;
    private MapData _currentMapData = null!;
    private string _dataDir = string.Empty;
    private string _mapsDir = string.Empty;

    public MainView()
    {
        InitializeComponent();

        // All game data lives under a single TowerDefense folder (easy to find and clean up).
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _dataDir = Path.Combine(appData, "TowerDefense");
        _mapsDir = Path.Combine(_dataDir, "Maps");

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

        // Load custom enemy definitions so they are available for gameplay.
        LoadCustomEnemies();

        // Load tower definitions from built-in + custom JSON files.
        LoadCustomTowers();

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
            ShowMenu();
        }
    }

    // ==================== Maps Directory ====================

    /// <summary>
    /// Extract built-in map JSON files from embedded resources to the app data directory.
    /// </summary>
    private void ExtractEmbeddedMaps()
    {
        if (Directory.Exists(_mapsDir) && Directory.GetFiles(_mapsDir, "*.json").Length > 0)
            return;

        var assembly = typeof(MapData).Assembly;
        var prefix = $"{assembly.GetName().Name}.Maps.";

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(prefix) || !resourceName.EndsWith(".json"))
                continue;

            var fileName = resourceName.Substring(prefix.Length);

            try
            {
                Directory.CreateDirectory(_mapsDir);

                var targetPath = Path.Combine(_mapsDir, fileName);
                if (File.Exists(targetPath))
                    continue;

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                using var fileStream = File.Create(targetPath);
                stream.CopyTo(fileStream);
            }
            catch
            {
                // Best-effort: skip files that fail.
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

        var defaultPath = Path.Combine(_mapsDir, "1.json");
        if (!File.Exists(defaultPath))
        {
            MapData.CreateDefault().SaveToFile(defaultPath);
        }
    }

    private int CountMaps()
    {
        if (!Directory.Exists(_mapsDir)) return 0;
        return Directory.GetFiles(_mapsDir, "*.json")
            .Count(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out _));
    }

    // ==================== Navigation ====================

    private void ShowMenu()
    {
        if (_menuView == null)
        {
            _menuView = new MenuView();
            _menuView.OnPlayGame = () => ShowGame();
            _menuView.OnOpenEditor = () => ShowEditor();
            _menuView.OnOpenMonsterEditor = () => ShowMonsterEditor();
            _menuView.OnOpenTowerEditor = () => ShowTowerEditor();
            _menuView.OnResetData = () => ResetAllData();
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
        _gameView.EnsureLevelSelectState();
        ContentArea.Content = _gameView;
    }

    private void ShowGameFromEditor(MapData map)
    {
        if (_gameView == null)
        {
            _gameView = new GameView();
            _gameView.OnMainMenu = () => ShowMenu();
        }

        _gameView.SetMapsDirectory(_mapsDir);
        _gameView.EnsureLevelSelectState();
        _gameView.SetSelectedLevel(map.Name);

        ContentArea.Content = _gameView;
    }

    private void ShowEditor()
    {
        // Show map list first
        if (_mapListView == null)
        {
            _mapListView = new MapListControl();
            _mapListView.OnMainMenu = () => ShowMenu();
            _mapListView.OnEditMap = (map, filePath) => OpenMapInEditor(map, filePath);
            _mapListView.Initialize(_mapsDir);
        }
        else
        {
            _mapListView.Refresh();
        }

        ContentArea.Content = _mapListView;
    }

    private void ShowMonsterEditor()
    {
        if (_monsterEditorView == null)
        {
            _monsterEditorView = new MonsterEditorView();
            _monsterEditorView.OnBack = () => ShowMenu();
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var enemiesDir = Path.Combine(_dataDir, "Enemies");
        _monsterEditorView.Initialize(enemiesDir);
        ContentArea.Content = _monsterEditorView;
    }

    /// <summary>
    /// Delete all data (maps, enemies, towers, save) and re-extract built-in defaults.
    /// </summary>
    private void ResetAllData()
    {
        try
        {
            if (Directory.Exists(_dataDir))
                Directory.Delete(_dataDir, recursive: true);
        }
        catch
        {
            // Best-effort — some files may be locked.
        }

        // Re-extract all built-in configs and reload registries
        ExtractEmbeddedMaps();
        EnsureMapsDir();            // fallback default map if no embedded maps
        LoadCustomEnemies();        // extracts embedded + loads all enemies
        LoadCustomTowers();         // extracts embedded + loads all towers

        // Refresh menu map count
        if (_menuView != null)
            _menuView.MapCount = CountMaps();
    }

    private void ShowTowerEditor()
    {
        if (_towerEditorView == null)
        {
            _towerEditorView = new TowerEditorView();
            _towerEditorView.OnBack = () => ShowMenu();
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var towersDir = Path.Combine(_dataDir, "Towers");
        _towerEditorView.Initialize(towersDir);
        ContentArea.Content = _towerEditorView;
    }

    /// <summary>
    /// Extract built-in enemy configs from embedded resources, then load all enemies
    /// (built-in + custom) into <see cref="EnemyDefinition.All"/>.
    /// </summary>
    private void LoadCustomEnemies()
    {
        var enemiesDir = Path.Combine(_dataDir, "Enemies");

        // Extract built-in enemy JSON files from embedded resources on first run.
        ExtractEmbeddedEnemies(enemiesDir);

        EnemyDefinition.All.Clear();

        // Load all enemies from the single Enemies/ directory (built-in + custom).
        LoadEnemiesFromDir(enemiesDir);
    }

    /// <summary>
    /// Extract built-in enemy configs to the given directory (skip if already present).
    /// </summary>
    private static void ExtractEmbeddedEnemies(string enemiesDir)
    {
        var assembly = typeof(MapData).Assembly;
        var prefix = $"{assembly.GetName().Name}.Enemies.";

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(prefix) || !resourceName.EndsWith(".json"))
                continue;

            var fileName = resourceName.Substring(prefix.Length);
            try
            {
                Directory.CreateDirectory(enemiesDir);
                var targetPath = Path.Combine(enemiesDir, fileName);
                if (File.Exists(targetPath))
                    continue;

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                using var fileStream = File.Create(targetPath);
                stream.CopyTo(fileStream);
            }
            catch
            {
                // Best-effort.
            }
        }
    }

    /// <summary>Load all .json enemy configs from a directory into <see cref="EnemyDefinition.All"/>.</summary>
    private static void LoadEnemiesFromDir(string dir)
    {
        if (!Directory.Exists(dir))
            return;

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name == "_save") continue;

            var data = EnemyData.LoadFromFile(file);
            if (data != null)
                EnemyDefinition.All[data.Name] = data.ToDefinition();
        }
    }

    /// <summary>
    /// Extract built-in tower configs from embedded resources, then load all towers
    /// (built-in + custom) into <see cref="TowerDefinition.All"/>.
    /// </summary>
    private void LoadCustomTowers()
    {
        var towersDir = Path.Combine(_dataDir, "Towers");

        // Extract built-in tower JSON files from embedded resources on first run.
        ExtractEmbeddedTowers(towersDir);

        TowerDefinition.All.Clear();

        // Load all towers from the single Towers/ directory (built-in + custom).
        LoadTowersFromDir(towersDir);
    }

    /// <summary>
    /// Extract built-in tower configs to the given directory (skip if already present).
    /// </summary>
    private static void ExtractEmbeddedTowers(string towersDir)
    {
        var assembly = typeof(MapData).Assembly;
        var prefix = $"{assembly.GetName().Name}.Towers.Builtin.";

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(prefix) || !resourceName.EndsWith(".json"))
                continue;

            var fileName = resourceName.Substring(prefix.Length);
            try
            {
                Directory.CreateDirectory(towersDir);
                var targetPath = Path.Combine(towersDir, fileName);
                if (File.Exists(targetPath))
                    continue;

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                using var fileStream = File.Create(targetPath);
                stream.CopyTo(fileStream);
            }
            catch
            {
                // Best-effort.
            }
        }
    }

    /// <summary>Load all .json tower configs from a directory into <see cref="TowerDefinition.All"/>.</summary>
    private static void LoadTowersFromDir(string dir)
    {
        if (!Directory.Exists(dir))
            return;

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name == "_save") continue;

            var data = TowerData.LoadFromFile(file);
            if (data != null)
                TowerDefinition.All[data.Name] = data.ToDefinition();
        }
    }

    private void OpenMapInEditor(MapData map, string filePath)
    {
        _currentMapData = map;

        if (_editorView == null)
        {
            _editorView = new MapEditorView();
            _editorView.OnBack = () => ShowEditor();
            _editorView.OnPlayMap = OnPlayMap;
        }

        _editorView.LoadForEdit(map, filePath);
        ContentArea.Content = _editorView;
    }

    private void OnPlayMap(MapData map)
    {
        _currentMapData = map;

        if (_gameView == null)
        {
            _gameView = new GameView();
            _gameView.OnMainMenu = () => ShowMenu();
        }
        _gameView.OnBackToEditor = () =>
        {
            if (_editorView != null)
                ContentArea.Content = _editorView;
        };

        _gameView.SetMapsDirectory(_mapsDir);
        _gameView.StartTestPlay(map);
        ContentArea.Content = _gameView;
    }
}
