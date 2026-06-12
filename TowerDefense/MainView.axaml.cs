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
    private TowerListControl? _towerListView;
    private MonsterListControl? _monsterListView;
    private EditorHubControl? _editorHub;
    private MapData _currentMapData = null!;
    private string _dataDir = string.Empty;
    private string _mapsDir = string.Empty;
    private string _mapsFilePath = string.Empty;
    private string _towersFilePath = string.Empty;
    private string _enemiesFilePath = string.Empty;

    public MainView()
    {
        InitializeComponent();

        // All game data lives under a single TowerDefense folder (easy to find and clean up).
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _dataDir = Path.Combine(appData, "TowerDefense");
        _mapsDir = Path.Combine(_dataDir, "Maps");
        _mapsFilePath = Path.Combine(_mapsDir, "maps.json");
        _towersFilePath = Path.Combine(_dataDir, "Towers", "towers.json");
        _enemiesFilePath = Path.Combine(_dataDir, "Enemies", "enemies.json");

        // Extract built-in configs from embedded resources on first run.
        ExtractEmbeddedFile(_mapsFilePath, "maps.json");
        ExtractEmbeddedFile(_towersFilePath, "towers.json");
        ExtractEmbeddedFile(_enemiesFilePath, "enemies.json");

        EnsureFallbackMaps();

        // Load all definitions into the global registries.
        LoadCustomEnemies();
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

    // ==================== File Extraction ====================

    /// <summary>
    /// Extract a single embedded resource file to the target path (skip if already present).
    /// Matches the resource whose name ends with <paramref name="resourceSuffix"/>.
    /// </summary>
    private static void ExtractEmbeddedFile(string targetPath, string resourceSuffix)
    {
        if (File.Exists(targetPath)) return;

        var assembly = typeof(MapData).Assembly;

        try
        {
            foreach (var resourceName in assembly.GetManifestResourceNames())
            {
                if (!resourceName.EndsWith(resourceSuffix))
                    continue;

                var dir = Path.GetDirectoryName(targetPath);
                if (dir != null) Directory.CreateDirectory(dir);

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                using var fileStream = File.Create(targetPath);
                stream.CopyTo(fileStream);
                return;
            }
        }
        catch
        {
            // Best-effort: skip files that fail.
        }
    }

    private void EnsureFallbackMaps()
    {
        if (File.Exists(_mapsFilePath)) return;

        // Fall back to desktop dev paths if no maps file found
        var fallback = Path.Combine(AppContext.BaseDirectory, "Maps", "maps.json");
        if (File.Exists(fallback))
        {
            _mapsFilePath = fallback;
            _mapsDir = Path.GetDirectoryName(fallback)!;
            return;
        }

        var projectDir = FindProjectMapsDir();
        if (projectDir != null)
        {
            _mapsDir = projectDir;
            _mapsFilePath = Path.Combine(projectDir, "maps.json");
            if (File.Exists(_mapsFilePath)) return;
        }

        // Create default maps file with a starter map
        Directory.CreateDirectory(_mapsDir);
        MapData.SaveListToFile(_mapsFilePath, new[] { MapData.CreateDefault() });
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

    // ==================== Navigation ====================

    private void ShowMenu()
    {
        if (_menuView == null)
        {
            _menuView = new MenuView();
            _menuView.OnPlayGame = () => ShowGame();
            _menuView.OnOpenEditor = () => ShowEditorHub();
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

    private void ShowEditorHub()
    {
        if (_editorHub == null)
        {
            _editorHub = new EditorHubControl();
            _editorHub.OnBack = () => ShowMenu();
            _editorHub.OnOpenMapEditor = () => ShowEditor();
            _editorHub.OnOpenTowerEditor = () => ShowTowerList();
            _editorHub.OnOpenMonsterEditor = () => ShowMonsterList();
        }

        ContentArea.Content = _editorHub;
    }

    private void ShowEditor()
    {
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

        _monsterEditorView.Initialize(_enemiesFilePath);
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
        ExtractEmbeddedFile(_mapsFilePath, "maps.json");
        ExtractEmbeddedFile(_towersFilePath, "towers.json");
        ExtractEmbeddedFile(_enemiesFilePath, "enemies.json");
        EnsureFallbackMaps();
        LoadCustomEnemies();
        LoadCustomTowers();

        if (_menuView != null)
            _menuView.MapCount = CountMaps();
    }

    private void ShowTowerList()
    {
        if (_towerListView == null)
        {
            _towerListView = new TowerListControl();
            _towerListView.OnBack = () => ShowMenu();
            _towerListView.OnEditTower = (tower, filePath) => OpenTowerInEditor(tower, filePath);
            _towerListView.Initialize(_towersFilePath);
        }
        else
        {
            _towerListView.Refresh();
        }

        ContentArea.Content = _towerListView;
    }

    private void OpenTowerInEditor(TowerData tower, string filePath)
    {
        if (_towerEditorView == null)
        {
            _towerEditorView = new TowerEditorView();
            _towerEditorView.OnBack = () => ShowTowerList();
        }
        else
        {
            _towerEditorView.OnBack = () => ShowTowerList();
        }

        _towerEditorView.LoadForEdit(tower, filePath);
        ContentArea.Content = _towerEditorView;
    }

    private void ShowMonsterList()
    {
        if (_monsterListView == null)
        {
            _monsterListView = new MonsterListControl();
            _monsterListView.OnBack = () => ShowMenu();
            _monsterListView.OnEditMonster = (enemy, filePath) => OpenMonsterInEditor(enemy, filePath);
            _monsterListView.Initialize(_enemiesFilePath);
        }
        else
        {
            _monsterListView.Refresh();
        }

        ContentArea.Content = _monsterListView;
    }

    private void OpenMonsterInEditor(EnemyData enemy, string filePath)
    {
        if (_monsterEditorView == null)
        {
            _monsterEditorView = new MonsterEditorView();
            _monsterEditorView.OnBack = () => ShowMonsterList();
        }
        else
        {
            _monsterEditorView.OnBack = () => ShowMonsterList();
        }

        _monsterEditorView.LoadForEdit(enemy, filePath);
        ContentArea.Content = _monsterEditorView;
    }

    private void ShowTowerEditor()
    {
        if (_towerEditorView == null)
        {
            _towerEditorView = new TowerEditorView();
            _towerEditorView.OnBack = () => ShowMenu();
        }

        _towerEditorView.Initialize(_towersFilePath);
        ContentArea.Content = _towerEditorView;
    }

    // ==================== Definition Loading ====================

    private void LoadCustomEnemies()
    {
        EnemyDefinition.All.Clear();
        var list = EnemyData.LoadListFromFile(_enemiesFilePath);
        foreach (var data in list)
            EnemyDefinition.All[data.Name] = data.ToDefinition();
    }

    private void LoadCustomTowers()
    {
        TowerDefinition.All.Clear();
        var list = TowerData.LoadListFromFile(_towersFilePath);
        foreach (var data in list)
            TowerDefinition.All[data.Name] = data.ToDefinition();
    }

    private int CountMaps()
    {
        var maps = MapData.LoadListFromFile(_mapsFilePath);
        return maps.Count;
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
