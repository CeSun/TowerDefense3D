using Aura3D.Avalonia;
using Aura3D.Core;
using Aura3D.Core.Geometries;
using Aura3D.Core.Nodes;
using Aura3D.Core.Resources;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Diagnostics;
using System.Numerics;
using DrawingColor = System.Drawing.Color;

namespace TowerDefense;

public partial class GameView : UserControl
{
    // ==================== Core ====================
    private GameManager _gm = null!;
    private string _selectedTowerName = "Arrow Tower";
    private bool _isPlacing;
    private readonly Dictionary<string, Button> _towerButtons = new();
    private Node? _ghostNode;

    // ==================== Scene Nodes ====================
    private Node? _groundNode;
    private readonly List<Node> _pathNodes = new();
    private readonly List<Node> _mapNodes = new(); // all map-specific nodes for cleanup
    private readonly Dictionary<int, Node> _enemyNodes = new();
    private readonly Dictionary<int, Border> _enemyHpBars = new();
    private readonly Dictionary<int, Border> _enemyHpBgs = new();
    private readonly Dictionary<int, Node> _towerNodes = new();
    private readonly Dictionary<int, Node> _projectileNodes = new();

    // ==================== Geometry Cache ====================
    private BoxGeometry? _boxGeo;
    private SphereGeometry? _sphereGeo;
    private CylinderGeometry? _cylinderGeo;
    private PlaneGeometry? _planeGeo;

    // ==================== Material Cache ====================
    private Material? _groundMat;
    private Material? _buildableMat;
    private Material? _pathMat;

    // ==================== UI State ====================
    private bool _gameOverShown;
    private bool _sceneInitialized;
    private bool _isActive = true; // false when view is detached; skips game updates
    private string _mapsDir = string.Empty;
    private int _highestUnlockedLevel = 1;
    private int _selectedLevelNum;
    private bool _editorTestMode;
    private readonly Dictionary<int, Button> _levelCards = new();

    // Callbacks
    public Action? OnMainMenu { get; set; }
    public Action? OnBackToEditor { get; set; }

    public GameView()
    {
        InitializeComponent();
        _gm = new GameManager();

        _gm.TowerAdded += OnTowerAdded;
        _gm.TowerRemoved += OnTowerRemoved;
        _gm.EnemyAdded += OnEnemyAdded;
        _gm.EnemyRemoved += OnEnemyRemoved;
        _gm.ProjectileAdded += OnProjectileAdded;
        _gm.ProjectileRemoved += OnProjectileRemoved;
        _gm.GameStateChanged += OnGameStateChanged;
        _gm.GameReset += OnGameReset;

        UpdateTowerButtonHighlight();
    }

    // ==================== Level Grid ====================

    /// <summary>
    /// Set the maps directory and build the level select grid.
    /// </summary>
    public void SetMapsDirectory(string mapsDir)
    {
        _mapsDir = mapsDir;
        var save = SaveData.Load(_mapsDir);
        _highestUnlockedLevel = save.HighestUnlockedLevel;
        BuildLevelGrid();
    }

    /// <summary>
    /// Auto-select a level in the grid (called from editor to pre-select the edited level).
    /// </summary>
    public void SetSelectedLevel(string name)
    {
        if (int.TryParse(name, out int num))
        {
            _editorTestMode = true;
            BuildLevelGrid();
            SelectLevel(num);
        }
    }

    private List<int> GetLevelNumbers()
    {
        var numbers = new List<int>();
        if (Directory.Exists(_mapsDir))
        {
            foreach (var file in Directory.GetFiles(_mapsDir, "*.json"))
            {
                var fname = Path.GetFileNameWithoutExtension(file);
                if (int.TryParse(fname, out int num))
                    numbers.Add(num);
            }
        }
        numbers.Sort();
        if (numbers.Count == 0) numbers.Add(1);
        return numbers;
    }

    private void BuildLevelGrid()
    {
        LevelGrid.Children.Clear();
        _levelCards.Clear();

        var numbers = GetLevelNumbers();
        foreach (var num in numbers)
        {
            bool unlocked = num <= _highestUnlockedLevel || _editorTestMode;
            var card = new Button
            {
                Width = 110, Height = 90,
                Margin = new Avalonia.Thickness(6),
                CornerRadius = new Avalonia.CornerRadius(8),
                FontSize = 14,
                FontWeight = Avalonia.Media.FontWeight.Bold,
                Foreground = Avalonia.Media.Brushes.White,
                Content = unlocked ? $"Level {num}" : $"🔒\nLevel {num}",
                Tag = num,
            };
            card.Click += (_, _) => SelectLevel(num);

            if (unlocked)
            {
                card.Background = Avalonia.Media.Brush.Parse("#CC2d5a27");
                card.BorderBrush = Avalonia.Media.Brushes.LimeGreen;
                card.BorderThickness = new Avalonia.Thickness(0);
            }
            else
            {
                card.Background = Avalonia.Media.Brush.Parse("#CC3a3a4a");
                card.BorderBrush = Avalonia.Media.Brush.Parse("#555");
                card.BorderThickness = new Avalonia.Thickness(1);
            }

            _levelCards[num] = card;
            LevelGrid.Children.Add(card);
        }

        // Reset selection
        _selectedLevelNum = 0;
        DetailLevelNum.Text = "Select a Level";
        DetailWaves.Text = "";
        DetailEnemies.Text = "";
        DetailPath.Text = "";
        DetailStatus.Text = "";
        LevelStartBtn.IsVisible = false;
    }

    private void SelectLevel(int num)
    {
        _selectedLevelNum = num;
        UpdateLevelCardStyles();
        UpdateLevelDetails(num);
    }

    private void UpdateLevelCardStyles()
    {
        foreach (var (n, card) in _levelCards)
        {
            bool selected = n == _selectedLevelNum;
            bool unlocked = n <= _highestUnlockedLevel || _editorTestMode;

            if (selected)
            {
                card.BorderBrush = Avalonia.Media.Brushes.Gold;
                card.BorderThickness = new Avalonia.Thickness(3);
                card.Background = Avalonia.Media.Brush.Parse("#CC3a7a3a");
            }
            else if (unlocked)
            {
                card.BorderBrush = Avalonia.Media.Brushes.LimeGreen;
                card.BorderThickness = new Avalonia.Thickness(0);
                card.Background = Avalonia.Media.Brush.Parse("#CC2d5a27");
            }
            else
            {
                card.BorderBrush = Avalonia.Media.Brush.Parse("#555");
                card.BorderThickness = new Avalonia.Thickness(1);
                card.Background = Avalonia.Media.Brush.Parse("#CC3a3a4a");
            }
        }
    }

    private void UpdateLevelDetails(int num)
    {
        var filePath = Path.Combine(_mapsDir, num + ".json");
        if (!File.Exists(filePath))
        {
            DetailLevelNum.Text = $"Level {num}";
            DetailWaves.Text = "File not found";
            DetailStatus.Text = "";
            LevelStartBtn.IsVisible = false;
            return;
        }

        var map = MapData.LoadFromFile(filePath);
        if (map == null)
        {
            DetailLevelNum.Text = $"Level {num}";
            DetailWaves.Text = "Failed to load";
            LevelStartBtn.IsVisible = false;
            return;
        }

        DetailLevelNum.Text = $"Level {num}";
        DetailWaves.Text = $"Waves: {map.Waves.Count}";
        int totalEnemies = map.Waves.Sum(w => w.Entries.Sum(e => e.Count));
        DetailEnemies.Text = $"Total Enemies: {totalEnemies}";
        int waypointCount = (map.StartCell != null ? 1 : 0) + map.PathWaypoints.Count + (map.EndCell != null ? 1 : 0);
        DetailPath.Text = $"Grid: {map.GridCols}×{map.GridRows} | Waypoints: {waypointCount}";

        bool unlocked = num <= _highestUnlockedLevel || _editorTestMode;
        if (!unlocked)
        {
            DetailStatus.Text = $"🔒 Locked — beat Level {_highestUnlockedLevel} first";
            LevelStartBtn.IsVisible = false;
        }
        else if (_editorTestMode && num > _highestUnlockedLevel)
        {
            DetailStatus.Text = map.IsComplete
                ? "🧪 Editor test — playable for testing only"
                : "🧪 Editor test — ⚠️ Incomplete";
            LevelStartBtn.IsVisible = true;
        }
        else
        {
            DetailStatus.Text = map.IsComplete ? "✅ Ready" : "⚠️ Incomplete (missing start/end/waves)";
            LevelStartBtn.IsVisible = true;
        }

        // Preview the map in 3D
        LoadMap(map);
    }

    /// <summary>
    /// Reset the view to a clean level-select state (no running game, no editor test mode).
    /// Called when navigating to GameView from the main menu.
    /// </summary>
    public void EnsureLevelSelectState()
    {
        _editorTestMode = false;
        _gameOverShown = false;
        GameHudPanel.IsVisible = false;
        GameOverPanel.IsVisible = false;
        LevelSelectPanel.IsVisible = true;
        TowerBtnPanel.IsVisible = false;
        ClearAllGameNodes();
        _gm.Reset();
        ClearPlacement();
        LevelSelectStatus.Text = "Choose a level to begin";
    }

    /// <summary>
    /// Skip level select and jump directly into gameplay (used by editor Test Map).
    /// </summary>
    public void StartTestPlay(MapData map)
    {
        _editorTestMode = true;
        LevelSelectPanel.IsVisible = false;
        GameHudPanel.IsVisible = true;
        GameOverPanel.IsVisible = false;
        _gameOverShown = false;

        ClearAllGameNodes();
        _gm.LoadMap(map);
        if (_sceneInitialized)
        {
            ClearMapScene();
            BuildMapScene(AuraView);
        }
        _gm.Reset();
        _gm.StartGame();

        TowerBtnPanel.IsVisible = true;
        BuildTowerButtons();
        ClearPlacement();
        StatusText.Text = "Editor Test — Select a tower and place it!";
    }

    private void OnLevelStartClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedLevelNum <= 0) return;
        if (_selectedLevelNum > _highestUnlockedLevel && !_editorTestMode) return;

        var filePath = Path.Combine(_mapsDir, _selectedLevelNum + ".json");
        if (!File.Exists(filePath)) return;
        var map = MapData.LoadFromFile(filePath);
        if (map == null) return;

        // Hide level select, show game HUD
        LevelSelectPanel.IsVisible = false;
        GameHudPanel.IsVisible = true;
        GameOverPanel.IsVisible = false;
        _gameOverShown = false;

        // Load and start
        ClearAllGameNodes();
        _gm.LoadMap(map);
        if (_sceneInitialized)
        {
            ClearMapScene();
            BuildMapScene(AuraView);
        }
        _gm.Reset();
        _gm.StartGame();

        TowerBtnPanel.IsVisible = true;
        BuildTowerButtons();
        ClearPlacement();
        StatusText.Text = $"Level {_selectedLevelNum} — Select a tower and place it!";
    }

    private void OnBackToLevelSelect(object? sender, RoutedEventArgs e)
    {
        ReturnToLevelSelect();
    }

    private void ReturnToLevelSelect()
    {
        if (_editorTestMode)
        {
            _editorTestMode = false;
            ClearAllGameNodes();
            _gm.Reset();
            GameHudPanel.IsVisible = false;
            GameOverPanel.IsVisible = false;
            _gameOverShown = false;
            OnBackToEditor?.Invoke();
            return;
        }

        _editorTestMode = false;
        GameHudPanel.IsVisible = false;
        GameOverPanel.IsVisible = false;
        _gameOverShown = false;
        LevelSelectPanel.IsVisible = true;

        ClearAllGameNodes();
        _gm.Reset();
        ClearPlacement();

        // Refresh grid (may have new unlocks)
        BuildLevelGrid();
        LevelSelectStatus.Text = "Choose a level to begin";
    }

    // ==================== Map Loading ====================

    /// <summary>
    /// Load a map into the game view, rebuilding the 3D scene.
    /// </summary>
    public void LoadMap(MapData map)
    {
        _gm.LoadMap(map);

        if (_sceneInitialized)
        {
            ClearMapScene();
            BuildMapScene(AuraView);
        }
    }

    // ==================== Scene Initialization ====================

    private void OnSceneInitialized(object? sender, InitializedRoutedEventArgs e)
    {
        var view = (sender as Aura3DView)!;
        view.AutoRequestNextFrameRendering = false;

        // Clear stale node references from any previous scene
        _pathNodes.Clear();
        _mapNodes.Clear();
        _groundNode = null;

        // Camera setup — top-down angled view
        view.MainCamera.Position = new Vector3(10, 16, 20);
        view.MainCamera.RotationDegrees = new Vector3(-55, 0, 0);
        view.Scene.Background = Texture.CreateFromColor(DrawingColor.DarkSlateGray);

        // CSM shadow quality: increase per-cascade resolution and cascade count
        view.PipelineSettings.CsmShadowMapResolution = 2048;
        view.PipelineSettings.CsmCascadeCount = 4;
        view.PipelineSettings.CsmSplitLambda = 0.6f;
        view.PointerMoved += OnPointerMoved;

        // Create geometries (reusable)
        _boxGeo = new BoxGeometry();
        _sphereGeo = new SphereGeometry();
        _cylinderGeo = new CylinderGeometry();
        _planeGeo = new PlaneGeometry();

        // Create materials (reusable)
        _groundMat = CreateColorMaterial(DrawingColor.DarkGreen);
        _buildableMat = CreateColorMaterial(DrawingColor.FromArgb(255, 34, 139, 34));
        _pathMat = CreateColorMaterial(DrawingColor.SandyBrown);

        // Main directional light with CSM shadows
        var dl = new DirectionalLight
        {
            RotationDegrees = new Vector3(-40, -20, 0),
            LightColor = DrawingColor.White,
            CastShadow = true,
            ShadowConfig = new DirectionalLightShadowMapConfig
            {
                Width = 30,
                Height = 30,
                NearPlane = 0.5f,
                FarPlane = 60,
            }
        };
        view.AddNode(dl);
        view.Scene.MainDirectionalLight = dl;

        // Ambient fill light
        var ambientLight = new DirectionalLight();
        ambientLight.RotationDegrees = new Vector3(20, 150, 0);
        ambientLight.LightColor = DrawingColor.FromArgb(255, 80, 80, 100);
        view.AddNode(ambientLight);

        // Build the map scene
        BuildMapScene(view);

        _sceneInitialized = true;
        view.RequestNextFrameRendering();
    }

    // ==================== Map Scene Building ====================

    private void ClearMapScene()
    {
        var view = AuraView;

        // Remove ground
        if (_groundNode != null)
        {
            TryRemoveNode(view, _groundNode);
            _groundNode = null;
        }

        // Remove path nodes
        foreach (var node in _pathNodes)
            TryRemoveNode(view, node);
        _pathNodes.Clear();

        // Remove other map nodes (grid dots, markers)
        foreach (var node in _mapNodes)
            TryRemoveNode(view, node);
        _mapNodes.Clear();

        // Clear game object nodes
        foreach (var (_, node) in _enemyNodes) TryRemoveNode(view, node);
        foreach (var (_, hp) in _enemyHpBars) HpBarCanvas.Children.Remove(hp);
        foreach (var (_, bg) in _enemyHpBgs) HpBarCanvas.Children.Remove(bg);
        foreach (var (_, node) in _towerNodes) TryRemoveNode(view, node);
        foreach (var (_, node) in _projectileNodes) TryRemoveNode(view, node);

        _enemyNodes.Clear();
        _enemyHpBars.Clear();
        _enemyHpBgs.Clear();
        _towerNodes.Clear();
        _projectileNodes.Clear();
    }

    private static void TryRemoveNode(Aura3DView view, Node node)
    {
        try { view.Remove(node); } catch (InvalidOperationException) { /* already removed */ }
    }

    private void BuildMapScene(Aura3DView view)
    {
        BuildGround(view);
        BuildPath(view);
        BuildGridCells(view);
        BuildMarkers(view);

        // Adjust camera for new map size
        view.MainCamera.Position = new Vector3(
            _gm.GridCols / 2f,
            Math.Max(_gm.GridCols, _gm.GridRows) * 1.3f,
            _gm.GridRows + 4);
    }

    private void BuildGround(Aura3DView view)
    {
        var groundMesh = new Mesh
        {
            Geometry = _planeGeo!,
            Material = _groundMat!,
            Name = "Ground",
        };
        groundMesh.Scale = new Vector3(_gm.GridCols + 2, 1, _gm.GridRows + 2);
        groundMesh.Position = new Vector3(_gm.GridCols / 2f, -0.05f, _gm.GridRows / 2f);

        _groundNode = groundMesh;
        view.AddNode(_groundNode);
    }

    private void BuildPath(Aura3DView view)
    {
        for (int col = 0; col < _gm.GridCols; col++)
        {
            for (int row = 0; row < _gm.GridRows; row++)
            {
                if (!_gm.PathCells[col, row]) continue;

                var cellMesh = new Mesh
                {
                    Geometry = _planeGeo!,
                    Material = _pathMat!,
                };
                cellMesh.Scale = new Vector3(0.9f, 1, 0.9f);
                cellMesh.Position = _gm.GridToWorld(col, row) + new Vector3(0, 0.02f, 0);

                _pathNodes.Add(cellMesh);
                view.AddNode(cellMesh);
            }
        }
    }

    private void BuildGridCells(Aura3DView view)
    {
        for (int col = 0; col < _gm.GridCols; col++)
        {
            for (int row = 0; row < _gm.GridRows; row++)
            {
                if (_gm.PathCells[col, row]) continue;

                var dot = new Mesh
                {
                    Geometry = _boxGeo!,
                    Material = _buildableMat!,
                };
                dot.Scale = new Vector3(0.15f, 0.02f, 0.15f);
                dot.Position = _gm.GridToWorld(col, row) + new Vector3(0, 0.02f, 0);
                _mapNodes.Add(dot);
                view.AddNode(dot);
            }
        }
    }

    private void BuildMarkers(Aura3DView view)
    {
        // Entry marker — position at first path waypoint
        var entryMarker = new Mesh
        {
            Geometry = _boxGeo!,
            Material = CreateColorMaterial(DrawingColor.LimeGreen),
        };
        entryMarker.Scale = new Vector3(0.4f, 0.15f, 1.2f);
        var firstWp = _gm.PathPositions.Count > 0 ? _gm.PathPositions[0] : Vector3.Zero;
        entryMarker.Position = firstWp + new Vector3(-0.3f, 0.04f, 0);
        _mapNodes.Add(entryMarker);
        view.AddNode(entryMarker);

        // Exit marker
        var exitMarker = new Mesh
        {
            Geometry = _boxGeo!,
            Material = CreateColorMaterial(DrawingColor.Red),
        };
        exitMarker.Scale = new Vector3(0.4f, 0.15f, 1.2f);
        var lastWp = _gm.PathPositions.Count > 0 ? _gm.PathPositions[^1] : Vector3.Zero;
        exitMarker.Position = lastWp + new Vector3(0.3f, 0.04f, 0);
        _mapNodes.Add(exitMarker);
        view.AddNode(exitMarker);
    }

    // ==================== Tower Nodes ====================

    /// <summary>Build a tower model from its shape list. If <paramref name="translucent"/> is true,
    /// materials use alpha blending for a ghost preview.</summary>
    private Node BuildTowerModel(TowerDefinition def, bool translucent)
    {
        var node = new Node();
        var shapes = def.Shapes ?? new List<TowerShapeData>();

        Material MakeMat(System.Drawing.Color c) => translucent
            ? CreateTranslucentMaterial(c)
            : CreateColorMaterial(c);

        // Auto-stack shapes along Y axis
        float currentY = 0;
        foreach (var s in shapes)
        {
            var mat = MakeMat(s.GetColor());

            Mesh mesh;
            if (s.Type == "Box") mesh = new Mesh { Geometry = _boxGeo!, Material = mat };
            else if (s.Type == "Sphere") mesh = new Mesh { Geometry = _sphereGeo!, Material = mat };
            else mesh = new Mesh { Geometry = _cylinderGeo!, Material = mat }; // Cylinder or Cone fallback

            mesh.Scale = new Vector3(s.ScaleX, s.ScaleY, s.ScaleZ);

            float y = currentY + s.ScaleY / 2 + s.OffsetY;
            mesh.Position = new Vector3(s.OffsetX, y, s.OffsetZ);
            mesh.RotationDegrees = new Vector3(0, s.RotationY, 0);

            currentY += s.ScaleY + s.OffsetY;

            node.AddChild(mesh, AttachToParentRule.KeepLocal);
        }

        return node;
    }

    private void OnTowerAdded(TowerInstance tower)
    {
        var view = AuraView;
        if (view.Scene == null) return;

        var towerNode = BuildTowerModel(tower.Def, translucent: false);
        towerNode.Position = tower.Position + new Vector3(0, 0.01f, 0);
        view.AddNode(towerNode);
        _towerNodes[tower.Id] = towerNode;
    }

    private void OnTowerRemoved(TowerInstance tower)
    {
        if (_towerNodes.TryGetValue(tower.Id, out var node))
        {
            AuraView.Remove(node);
            _towerNodes.Remove(tower.Id);
        }
    }

    // ==================== Enemy Nodes ====================

    private void OnEnemyAdded(EnemyInstance enemy)
    {
        var view = AuraView;
        if (view.Scene == null) return;

        var enemyMesh = new Mesh
        {
            Geometry = _sphereGeo!,
            Material = CreateColorMaterial(enemy.Def.Color),
        };
        enemyMesh.Scale = new Vector3(enemy.Def.Radius * 2);
        enemyMesh.Position = enemy.Position + new Vector3(0, enemy.Def.Radius + 0.05f, 0);
        view.AddNode(enemyMesh);
        _enemyNodes[enemy.Id] = enemyMesh;

        // HP bar — Avalonia controls on overlay canvas
        var hpBg = new Border
        {
            Background = Avalonia.Media.Brushes.DarkGray,
            Width = 40,
            Height = 5,
            CornerRadius = new Avalonia.CornerRadius(2),
        };
        var hpFill = new Border
        {
            Background = Avalonia.Media.Brushes.LimeGreen,
            Width = 36,
            Height = 3,
            CornerRadius = new Avalonia.CornerRadius(1),
            Margin = new Avalonia.Thickness(2, 1, 2, 1),
        };
        HpBarCanvas.Children.Add(hpBg);
        HpBarCanvas.Children.Add(hpFill);
        _enemyHpBgs[enemy.Id] = hpBg;
        _enemyHpBars[enemy.Id] = hpFill;
    }

    private void OnEnemyRemoved(EnemyInstance enemy)
    {
        if (_enemyNodes.TryGetValue(enemy.Id, out var node))
        {
            AuraView.Remove(node);
            _enemyNodes.Remove(enemy.Id);
        }
        if (_enemyHpBars.TryGetValue(enemy.Id, out var hp))
        {
            HpBarCanvas.Children.Remove(hp);
            _enemyHpBars.Remove(enemy.Id);
        }
        if (_enemyHpBgs.TryGetValue(enemy.Id, out var bg))
        {
            HpBarCanvas.Children.Remove(bg);
            _enemyHpBgs.Remove(enemy.Id);
        }
    }

    // ==================== Projectile Nodes ====================

    private void OnProjectileAdded(ProjectileData proj)
    {
        var view = AuraView;
        if (view.Scene == null) return;

        var projMesh = new Mesh
        {
            Geometry = _sphereGeo!,
            Material = CreateColorMaterial(proj.Color),
        };
        projMesh.Scale = new Vector3(0.16f);
        projMesh.Position = proj.Position;
        view.AddNode(projMesh);
        _projectileNodes[proj.Id] = projMesh;
    }

    private void OnProjectileRemoved(ProjectileData proj)
    {
        if (_projectileNodes.TryGetValue(proj.Id, out var node))
        {
            AuraView.Remove(node);
            _projectileNodes.Remove(proj.Id);
        }
    }

    // ==================== Game State Changed (UI Update) ====================

    private void OnGameStateChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            GoldText.Text = $"Gold: {_gm.Gold}";
            LivesText.Text = $"Lives: {_gm.Lives}";
            WaveText.Text = _gm.HasStarted
                ? $"Wave: {_gm.CurrentWave}/{_gm.TotalWaves}"
                : "Wave: -";
            StatusText.Text = $"Enemies: {_gm.Enemies.Count} | Towers: {_gm.Towers.Count}";

            if (_gm.IsGameOver && !_gameOverShown)
            {
                ShowGameOver();
            }
        });
    }

    // ==================== Frame Update ====================

    /// <summary>Stop game updates when removed from visual tree.</summary>
    protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _isActive = false;
    }

    /// <summary>Resume rendering when re-attached.</summary>
    protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isActive = true;
        if (_sceneInitialized)
            AuraView.RequestNextFrameRendering();
    }

    private void OnSceneUpdated(object? sender, UpdateRoutedEventArgs args)
    {
        if (!_isActive)
            return; // Detached from visual tree — don't drive game logic.

        var view = (sender as Aura3DView)!;

        if (_gm.HasStarted)
            _gm.Update(args.DeltaTime);

        // Sync enemy positions
        foreach (var enemy in _gm.Enemies)
        {
            if (_enemyNodes.TryGetValue(enemy.Id, out var node))
            {
                node.Position = enemy.Position + new Vector3(0, enemy.Def.Radius + 0.05f, 0);
            }
        }

        // Project HP bars from 3D world to screen overlay
        var cam = AuraView.MainCamera;
        foreach (var enemy in _gm.Enemies)
        {
            var barWorld = enemy.Position + new Vector3(0, enemy.Def.Radius * 2 + 0.40f, 0);
            var screen = cam.WorldToScreen(barWorld);
            if (screen == null) continue;

            float sx = screen.Value.X;
            float sy = screen.Value.Y;

            if (_enemyHpBgs.TryGetValue(enemy.Id, out var bg))
            {
                Canvas.SetLeft(bg, sx - 20);
                Canvas.SetTop(bg, sy - 2);
            }
            if (_enemyHpBars.TryGetValue(enemy.Id, out var hp))
            {
                float ratio = enemy.HP / enemy.MaxHP;
                int fillW = (int)(36 * ratio);
                if (fillW != (int)hp.Width)
                {
                    hp.Width = fillW;
                    hp.Background = ratio < 0.3f ? Avalonia.Media.Brushes.Red
                        : ratio < 0.6f ? Avalonia.Media.Brushes.Yellow
                        : Avalonia.Media.Brushes.LimeGreen;
                }
                Canvas.SetLeft(hp, sx - 20);
                Canvas.SetTop(hp, sy - 2);
            }
        }

        // Sync projectile positions
        foreach (var proj in _gm.Projectiles)
        {
            if (_projectileNodes.TryGetValue(proj.Id, out var node))
            {
                node.Position = proj.Position;
            }
        }

        view.RequestNextFrameRendering();
    }

    // ==================== Player Input ====================

    private void OnObjectPicked(object? sender, ObjectPickedEventArgs args)
    {
        if (!_isPlacing || !_gm.HasStarted) return;

        var worldPos = args.WorldPosition;
        var grid = _gm.WorldToGrid(worldPos);

        var nodeName = args.Node.Name ?? "?";
        var dbg = $"Picked [{nodeName}] @ world({worldPos.X:F1},{worldPos.Y:F1},{worldPos.Z:F1})";

        if (grid == null)
        {
            StatusText.Text = $"{dbg} — out of map bounds";
            return;
        }

        var (col, row) = grid.Value;

        if (_gm.TryPlaceTower(col, row, _selectedTowerName))
        {
            StatusText.Text = $"Placed {_selectedTowerName} at ({col},{row}) | Gold: {_gm.Gold}";
            ClearPlacement();
        }
        else if (_gm.PathCells[col, row])
        {
            StatusText.Text = $"{dbg} → Cell ({col},{row}) is on path!";
        }
        else if (_gm.OccupiedCells[col, row])
        {
            StatusText.Text = $"{dbg} → Cell ({col},{row}) occupied!";
        }
        else
        {
            var def = TowerDefinition.Resolve(_selectedTowerName);
            StatusText.Text = $"Not enough gold! Need {def.Cost}";
            ClearPlacement();
        }
    }

    // ==================== Tower Selection ====================

    /// <summary>
    /// Build tower buttons dynamically from <see cref="TowerDefinition.All"/>.
    /// Called when game starts (tower registry is already populated).
    /// </summary>
    private void BuildTowerButtons()
    {
        // Clear old buttons
        _towerButtons.Clear();
        // Remove all children except the "TOWERS" label (first child)
        while (TowerBtnPanel.Children.Count > 1)
            TowerBtnPanel.Children.RemoveAt(TowerBtnPanel.Children.Count - 1);

        // Sort towers by cost
        var towers = TowerDefinition.All.Values.OrderBy(t => t.Cost).ToList();

        // Emoji icons for the first 8 towers
        var icons = new[] { "🏹", "❄️", "💣", "☠️", "🎯", "🧨", "☀️", "🗼" };

        for (int i = 0; i < towers.Count; i++)
        {
            var def = towers[i];
            var icon = i < icons.Length ? icons[i] : "🗼";
            var name = def.Name;

            // Derive a button color from the tower's color
            var bgColor = System.Drawing.Color.FromArgb(
                (byte)(def.Color.R / 2 + 40),
                (byte)(def.Color.G / 2 + 40),
                (byte)(def.Color.B / 2 + 40));
            var bgHex = $"#CC{bgColor.R:X2}{bgColor.G:X2}{bgColor.B:X2}";

            var btn = new Button
            {
                Content = $"{icon} {name} ({def.Cost}g)",
                Background = Avalonia.Media.Brush.Parse(bgHex),
                Foreground = Avalonia.Media.Brushes.White,
                FontSize = 13,
                FontWeight = Avalonia.Media.FontWeight.Bold,
                Padding = new Avalonia.Thickness(14, 8),
                CornerRadius = new Avalonia.CornerRadius(6),
                Width = 180,
                Tag = name,
            };
            btn.Click += (_, _) => OnSelectTower(name, def);

            _towerButtons[name] = btn;
            TowerBtnPanel.Children.Add(btn);
        }
    }

    private void OnSelectTower(string name, TowerDefinition def)
    {
        _selectedTowerName = name;
        _isPlacing = true;
        UpdateTowerButtonHighlight();
        RebuildGhost();

        var desc = def.AoeRadius > 0 ? "Continuous AOE burn"
            : def.MultiShotCount > 1 ? $"Fires {def.MultiShotCount} shots in a fan"
            : def.CritChance > 0 ? $"Long range, {def.CritChance*100:F0}% crit ×{def.CritMultiplier:F1}"
            : def.DotDamage > 0 ? "Damage over time"
            : def.SlowAmount > 0 ? "Slows enemies"
            : def.SplashRadius > 0 ? "Splash damage"
            : "Basic tower";
        StatusText.Text = $"Selected: {name} ({def.Cost}g) — {desc}";
    }

    private void ClearPlacement()
    {
        _isPlacing = false;
        UpdateTowerButtonHighlight();
        RemoveGhost();
    }

    // ==================== Ghost Preview ====================

    private void EnsureGhost()
    {
        if (_ghostNode != null) return;
        _ghostNode = BuildTowerModel(TowerDefinition.Resolve(_selectedTowerName), translucent: true);
        _ghostNode.Position = new Vector3(0, -999, 0); // hidden
        AuraView.AddNode(_ghostNode);
    }

    private void RemoveGhost()
    {
        if (_ghostNode != null)
        {
            AuraView.Remove(_ghostNode);
            _ghostNode = null;
        }
    }

    private void RebuildGhost()
    {
        RemoveGhost();
        EnsureGhost();
    }

    private int _ghostCol, _ghostRow;

    private void OnPointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (!_isPlacing || AuraView.Scene == null) return;

        var pos = e.GetPosition(AuraView);
        var pick = AuraView.PickClosestAt(pos.X, pos.Y);
        if (pick == null) return;

        var grid = _gm.WorldToGrid(pick.WorldPosition);
        if (grid == null) return;
        var (col, row) = grid.Value;

        if (col != _ghostCol || row != _ghostRow)
        {
            _ghostCol = col;
            _ghostRow = row;
            EnsureGhost();
            _ghostNode!.Position = _gm.GridToWorld(col, row);
        }
    }

    private Material CreateTranslucentMaterial(DrawingColor color)
    {
        var halfAlpha = DrawingColor.FromArgb(128, color.R, color.G, color.B);
        return new Material
        {
            BlendMode = BlendMode.Translucent,
            DoubleSided = true,
            Channels =
            {
                new()
                {
                    Name = "BaseColor",
                    Texture = Texture.CreateFromColor(halfAlpha),
                }
            }
        };
    }

    // ==================== Game Controls ====================

    /// <summary>
    /// Back button during gameplay. Returns to level select, or to editor if in test mode.
    /// </summary>
    private void OnUnifiedBack(object? sender, RoutedEventArgs e)
    {
        if (_editorTestMode)
        {
            _editorTestMode = false;
            ClearAllGameNodes();
            _gm.Reset();
            GameHudPanel.IsVisible = false;
            GameOverPanel.IsVisible = false;
            _gameOverShown = false;
            OnBackToEditor?.Invoke();
        }
        else
        {
            ReturnToLevelSelect();
        }
    }

    private void OnMainMenuClick(object? sender, RoutedEventArgs e)
    {
        _editorTestMode = false;
        ClearAllGameNodes();
        _sceneInitialized = false;
        _pathNodes.Clear();
        _mapNodes.Clear();
        _groundNode = null;
        OnMainMenu?.Invoke();
    }

    private void OnReset(object? sender, RoutedEventArgs e)
    {
        // Restart current level
        ClearAllGameNodes();
        _gm.Reset();
        _gm.StartGame();
        GameOverPanel.IsVisible = false;
        _gameOverShown = false;
        GameHudPanel.IsVisible = true;
        TowerBtnPanel.IsVisible = true;
        BuildTowerButtons();
        ClearPlacement();
        StatusText.Text = $"Level {_selectedLevelNum} — Select a tower and place it!";
    }

    private void ClearAllGameNodes()
    {
        foreach (var (_, node) in _enemyNodes) TryRemoveNode(AuraView, node);
        foreach (var (_, hp) in _enemyHpBars) HpBarCanvas.Children.Remove(hp);
        foreach (var (_, bg) in _enemyHpBgs) HpBarCanvas.Children.Remove(bg);
        foreach (var (_, node) in _towerNodes) TryRemoveNode(AuraView, node);
        foreach (var (_, node) in _projectileNodes) TryRemoveNode(AuraView, node);

        _enemyNodes.Clear();
        _enemyHpBars.Clear();
        _enemyHpBgs.Clear();
        _towerNodes.Clear();
        _projectileNodes.Clear();
        RemoveGhost();
    }

    private void OnGameReset()
    {
        // Additional cleanup if needed (already handled in OnReset)
    }

    private void UpdateTowerButtonHighlight()
    {
        foreach (var (name, btn) in _towerButtons)
        {
            bool isSelected = _isPlacing && _selectedTowerName == name;
            btn.BorderThickness = new Avalonia.Thickness(isSelected ? 3 : 0);
            btn.BorderBrush = Avalonia.Media.Brushes.White;
        }
    }

    // ==================== Game Over ====================

    private void ShowGameOver()
    {
        _gameOverShown = true;
        GameOverPanel.IsVisible = true;
        GameOverNextBtn.IsVisible = false;

        if (_editorTestMode)
        {
            // Editor test: only show Back to Editor, no Retry/Next
            GameOverReplayBtn.IsVisible = false;
            GameOverNextBtn.IsVisible = false;

            if (_gm.IsVictory)
            {
                GameOverTitle.Text = "🏆 VICTORY!";
                GameOverTitle.Foreground = Avalonia.Media.Brushes.Gold;
                GameOverSubtext.Text = $"Editor test passed! Final gold: {_gm.Gold}";
            }
            else
            {
                GameOverTitle.Text = "💀 DEFEATED";
                GameOverTitle.Foreground = Avalonia.Media.Brushes.Red;
                GameOverSubtext.Text = $"The enemy broke through! Reached wave {_gm.CurrentWave}/{_gm.TotalWaves}";
            }
            return;
        }

        GameOverReplayBtn.IsVisible = true;

        if (_gm.IsVictory)
        {
            GameOverTitle.Text = "🏆 VICTORY!";
            GameOverTitle.Foreground = Avalonia.Media.Brushes.Gold;
            GameOverSubtext.Text = $"All waves defeated! Final gold: {_gm.Gold}";

            // Unlock next level
            if (_selectedLevelNum >= _highestUnlockedLevel)
            {
                _highestUnlockedLevel = _selectedLevelNum + 1;
                new SaveData(_highestUnlockedLevel).Save(_mapsDir);
                GameOverSubtext.Text += $"\nLevel {_highestUnlockedLevel} unlocked!";
            }

            // Show "Next Level" button if next level exists
            var nextPath = Path.Combine(_mapsDir, (_selectedLevelNum + 1) + ".json");
            if (File.Exists(nextPath))
                GameOverNextBtn.IsVisible = true;
        }
        else
        {
            GameOverTitle.Text = "💀 DEFEATED";
            GameOverTitle.Foreground = Avalonia.Media.Brushes.Red;
            GameOverSubtext.Text = $"The enemy broke through! Reached wave {_gm.CurrentWave}/{_gm.TotalWaves}";
        }
    }

    private void OnNextLevelClick(object? sender, RoutedEventArgs e)
    {
        var next = _selectedLevelNum + 1;
        var filePath = Path.Combine(_mapsDir, next + ".json");
        if (!File.Exists(filePath)) return;

        var map = MapData.LoadFromFile(filePath);
        if (map == null) return;

        _selectedLevelNum = next;
        ClearAllGameNodes();
        _gm.LoadMap(map);
        if (_sceneInitialized)
        {
            ClearMapScene();
            BuildMapScene(AuraView);
        }
        _gm.Reset();
        _gm.StartGame();

        GameOverPanel.IsVisible = false;
        _gameOverShown = false;
        GameHudPanel.IsVisible = true;
        TowerBtnPanel.IsVisible = true;
        BuildTowerButtons();
        ClearPlacement();
        StatusText.Text = $"Level {next} — Select a tower and place it!";
    }

    // ==================== Helpers ====================

    private Material CreateColorMaterial(DrawingColor color)
    {
        return new Material
        {
            BlendMode = BlendMode.Opaque,
            DoubleSided = true,
            Channels =
            {
                new()
                {
                    Name = "BaseColor",
                    Texture = Texture.CreateFromColor(color),
                }
            }
        };
    }

}
