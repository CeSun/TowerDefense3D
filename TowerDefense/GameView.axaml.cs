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
    private TowerType _selectedTower = TowerType.Arrow;
    private bool _isPlacing;
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

    // Callback to return to editor (set by MainWindow)
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

        // Camera setup — top-down angled view
        view.MainCamera.Position = new Vector3(10, 16, 20);
        view.MainCamera.RotationDegrees = new Vector3(-55, 0, 0);
        view.Scene.Background = Texture.CreateFromColor(DrawingColor.DarkSlateGray);
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
            view.Remove(_groundNode);
            _groundNode = null;
        }

        // Remove path nodes
        foreach (var node in _pathNodes)
            view.Remove(node);
        _pathNodes.Clear();

        // Remove other map nodes (grid dots, markers)
        foreach (var node in _mapNodes)
            view.Remove(node);
        _mapNodes.Clear();

        // Clear game object nodes
        foreach (var (_, node) in _enemyNodes) view.Remove(node);
        foreach (var (_, hp) in _enemyHpBars) HpBarCanvas.Children.Remove(hp);
        foreach (var (_, bg) in _enemyHpBgs) HpBarCanvas.Children.Remove(bg);
        foreach (var (_, node) in _towerNodes) view.Remove(node);
        foreach (var (_, node) in _projectileNodes) view.Remove(node);

        _enemyNodes.Clear();
        _enemyHpBars.Clear();
        _enemyHpBgs.Clear();
        _towerNodes.Clear();
        _projectileNodes.Clear();
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

    /// <summary>Build a tower model from its definition. If <paramref name="translucent"/> is true,
    /// materials use alpha blending for a ghost preview.</summary>
    private Node BuildTowerModel(TowerDefinition def, bool translucent)
    {
        var node = new Node();
        var mat = translucent
            ? CreateTranslucentMaterial(def.Color)
            : CreateColorMaterial(def.Color);
        var darkMat = translucent
            ? CreateTranslucentMaterial(Darken(def.Color, 0.6f))
            : CreateColorMaterial(Darken(def.Color, 0.6f));
        var lightMat = translucent
            ? CreateTranslucentMaterial(Lighten(def.Color, 0.4f))
            : CreateColorMaterial(Lighten(def.Color, 0.4f));

        // Common base
        var baseMesh = new Mesh { Geometry = _boxGeo!.Clone(), Material = darkMat };
        baseMesh.Scale = new Vector3(0.55f, 0.1f, 0.55f);
        baseMesh.Position = new Vector3(0, 0.1f, 0);
        node.AddChild(baseMesh, AttachToParentRule.KeepLocal);

        // Common body cylinder
        var bodyMesh = new Mesh { Geometry = _cylinderGeo!.Clone(), Material = mat };
        bodyMesh.Scale = new Vector3(0.3f, 0.4f, 0.3f);
        bodyMesh.Position = new Vector3(0, 0.5f, 0);
        node.AddChild(bodyMesh, AttachToParentRule.KeepLocal);

        switch (def.Type)
        {
            case TowerType.Arrow:
                {
                    var topMesh = new Mesh { Geometry = _cylinderGeo!.Clone(), Material = darkMat };
                    topMesh.Scale = new Vector3(0.08f, 0.25f, 0.08f);
                    topMesh.Position = new Vector3(0, 1.0f, 0);
                    node.AddChild(topMesh, AttachToParentRule.KeepLocal);
                    break;
                }
            case TowerType.Sniper:
                {
                    var barrel = new Mesh { Geometry = _cylinderGeo!.Clone(), Material = darkMat };
                    barrel.Scale = new Vector3(0.1f, 0.5f, 0.1f);
                    barrel.Position = new Vector3(0, 1.1f, 0);
                    node.AddChild(barrel, AttachToParentRule.KeepLocal);

                    var ring = new Mesh { Geometry = _cylinderGeo!.Clone(), Material = lightMat };
                    ring.Scale = new Vector3(0.18f, 0.06f, 0.18f);
                    ring.Position = new Vector3(0, 1.35f, 0);
                    node.AddChild(ring, AttachToParentRule.KeepLocal);
                    break;
                }
            case TowerType.MultiShot:
                {
                    float[] angles = { 0, -30, 30 };
                    float radius = 0.1f;
                    foreach (var ang in angles)
                    {
                        var rad = ang * MathF.PI / 180f;
                        var barrel = new Mesh { Geometry = _cylinderGeo!.Clone(), Material = darkMat };
                        barrel.Scale = new Vector3(0.07f, 0.22f, 0.07f);
                        barrel.Position = new Vector3(
                            MathF.Sin(rad) * radius,
                            1.05f,
                            MathF.Cos(rad) * radius
                        );
                        barrel.RotationDegrees = new Vector3(ang * 0.4f, 0, 0);
                        node.AddChild(barrel, AttachToParentRule.KeepLocal);
                    }
                    break;
                }
            case TowerType.Poison:
                {
                    var poisonBall = new Mesh { Geometry = _sphereGeo!.Clone(), Material = lightMat };
                    poisonBall.Scale = new Vector3(0.28f, 0.28f, 0.28f);
                    poisonBall.Position = new Vector3(0, 0.95f, 0);
                    node.AddChild(poisonBall, AttachToParentRule.KeepLocal);

                    var spike = new Mesh { Geometry = _cylinderGeo!.Clone(), Material = darkMat };
                    spike.Scale = new Vector3(0.05f, 0.18f, 0.05f);
                    spike.Position = new Vector3(0, 1.2f, 0);
                    node.AddChild(spike, AttachToParentRule.KeepLocal);
                    break;
                }
            case TowerType.Sun:
                {
                    var sunCore = new Mesh { Geometry = _sphereGeo!.Clone(), Material = lightMat };
                    sunCore.Scale = new Vector3(0.35f, 0.35f, 0.35f);
                    sunCore.Position = new Vector3(0, 0.95f, 0);
                    node.AddChild(sunCore, AttachToParentRule.KeepLocal);

                    var glowRing = new Mesh { Geometry = _cylinderGeo!.Clone(), Material = mat };
                    glowRing.Scale = new Vector3(0.42f, 0.04f, 0.42f);
                    glowRing.Position = new Vector3(0, 0.8f, 0);
                    node.AddChild(glowRing, AttachToParentRule.KeepLocal);
                    break;
                }
            default:
                {
                    var topMesh = new Mesh { Geometry = _sphereGeo!.Clone(), Material = mat };
                    topMesh.Scale = new Vector3(0.22f, 0.22f, 0.22f);
                    topMesh.Position = new Vector3(0, 0.95f, 0);
                    node.AddChild(topMesh, AttachToParentRule.KeepLocal);
                    break;
                }
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
            Geometry = _sphereGeo!.Clone(),
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
            Geometry = _sphereGeo!.Clone(),
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

    private void OnSceneUpdated(object? sender, UpdateRoutedEventArgs args)
    {
        var view = (sender as Aura3DView)!;

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

        if (_gm.TryPlaceTower(col, row, _selectedTower))
        {
            StatusText.Text = $"Placed {TowerDefinition.Get(_selectedTower).Name} at ({col},{row}) | Gold: {_gm.Gold}";
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
            StatusText.Text = $"Not enough gold! Need {TowerDefinition.Get(_selectedTower).Cost}";
            ClearPlacement();
        }
    }

    // ==================== Tower Selection ====================

    private void OnSelectArrow(object? sender, RoutedEventArgs e)
    {
        _selectedTower = TowerType.Arrow;
        _isPlacing = true;
        UpdateTowerButtonHighlight();
        RebuildGhost();
        StatusText.Text = "Selected: Arrow Tower (50g) — Click map to place";
    }

    private void OnSelectCannon(object? sender, RoutedEventArgs e)
    {
        _selectedTower = TowerType.Cannon;
        _isPlacing = true;
        UpdateTowerButtonHighlight();
        RebuildGhost();
        StatusText.Text = "Selected: Cannon Tower (100g) — Click map to place";
    }

    private void OnSelectIce(object? sender, RoutedEventArgs e)
    {
        _selectedTower = TowerType.Ice;
        _isPlacing = true;
        UpdateTowerButtonHighlight();
        RebuildGhost();
        StatusText.Text = "Selected: Ice Tower (75g) — Click map to place";
    }

    private void OnSelectMulti(object? sender, RoutedEventArgs e)
    {
        _selectedTower = TowerType.MultiShot;
        _isPlacing = true;
        UpdateTowerButtonHighlight();
        RebuildGhost();
        StatusText.Text = "Selected: Multi-Shot (150g) — Fires 3 arrows in a fan";
    }

    private void OnSelectSniper(object? sender, RoutedEventArgs e)
    {
        _selectedTower = TowerType.Sniper;
        _isPlacing = true;
        UpdateTowerButtonHighlight();
        RebuildGhost();
        StatusText.Text = "Selected: Sniper (130g) — Long range, 30% crit ×3";
    }

    private void OnSelectPoison(object? sender, RoutedEventArgs e)
    {
        _selectedTower = TowerType.Poison;
        _isPlacing = true;
        UpdateTowerButtonHighlight();
        RebuildGhost();
        StatusText.Text = "Selected: Poison (100g) — Damage over time";
    }

    private void OnSelectSun(object? sender, RoutedEventArgs e)
    {
        _selectedTower = TowerType.Sun;
        _isPlacing = true;
        UpdateTowerButtonHighlight();
        RebuildGhost();
        StatusText.Text = "Selected: Sun (175g) — Continuous AOE burn";
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
        _ghostNode = BuildTowerModel(TowerDefinition.Get(_selectedTower), translucent: true);
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

    private void OnStartGame(object? sender, RoutedEventArgs e)
    {
        if (!_gm.HasStarted)
        {
            _gm.StartGame();
            StartBtn.IsEnabled = false;
            StartBtn.Content = "⚔ Game Running...";
            TowerBtnPanel.IsVisible = true;
            StatusText.Text = "Game started! Select a tower and place it";
        }
    }

    private void OnReset(object? sender, RoutedEventArgs e)
    {
        ClearAllGameNodes();
        _gm.Reset();

        // Reset UI
        StartBtn.IsEnabled = true;
        StartBtn.Content = "▶ Start Game";
        GameOverPanel.IsVisible = false;
        _gameOverShown = false;
        TowerBtnPanel.IsVisible = false;
        ClearPlacement();
        StatusText.Text = "Press Start Game to begin";
    }

    private void OnBackToEditorClick(object? sender, RoutedEventArgs e)
    {
        ClearAllGameNodes();
        OnBackToEditor?.Invoke();
    }

    private void ClearAllGameNodes()
    {
        foreach (var (_, node) in _enemyNodes) AuraView.Remove(node);
        foreach (var (_, hp) in _enemyHpBars) HpBarCanvas.Children.Remove(hp);
        foreach (var (_, bg) in _enemyHpBgs) HpBarCanvas.Children.Remove(bg);
        foreach (var (_, node) in _towerNodes) AuraView.Remove(node);
        foreach (var (_, node) in _projectileNodes) AuraView.Remove(node);

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
        var showSelected = _isPlacing;

        ArrowBtn.BorderThickness = new Avalonia.Thickness(showSelected && _selectedTower == TowerType.Arrow ? 3 : 0);
        ArrowBtn.BorderBrush = Avalonia.Media.Brushes.White;

        CannonBtn.BorderThickness = new Avalonia.Thickness(showSelected && _selectedTower == TowerType.Cannon ? 3 : 0);
        CannonBtn.BorderBrush = Avalonia.Media.Brushes.White;

        IceBtn.BorderThickness = new Avalonia.Thickness(showSelected && _selectedTower == TowerType.Ice ? 3 : 0);
        IceBtn.BorderBrush = Avalonia.Media.Brushes.White;

        MultiBtn.BorderThickness = new Avalonia.Thickness(showSelected && _selectedTower == TowerType.MultiShot ? 3 : 0);
        MultiBtn.BorderBrush = Avalonia.Media.Brushes.White;

        SniperBtn.BorderThickness = new Avalonia.Thickness(showSelected && _selectedTower == TowerType.Sniper ? 3 : 0);
        SniperBtn.BorderBrush = Avalonia.Media.Brushes.White;

        PoisonBtn.BorderThickness = new Avalonia.Thickness(showSelected && _selectedTower == TowerType.Poison ? 3 : 0);
        PoisonBtn.BorderBrush = Avalonia.Media.Brushes.White;

        SunBtn.BorderThickness = new Avalonia.Thickness(showSelected && _selectedTower == TowerType.Sun ? 3 : 0);
        SunBtn.BorderBrush = Avalonia.Media.Brushes.White;
    }

    // ==================== Game Over ====================

    private void ShowGameOver()
    {
        _gameOverShown = true;
        GameOverPanel.IsVisible = true;

        if (_gm.IsVictory)
        {
            GameOverTitle.Text = "🏆 VICTORY!";
            GameOverTitle.Foreground = Avalonia.Media.Brushes.Gold;
            GameOverSubtext.Text = $"All waves defeated! Final gold: {_gm.Gold}";
        }
        else
        {
            GameOverTitle.Text = "💀 DEFEATED";
            GameOverTitle.Foreground = Avalonia.Media.Brushes.Red;
            GameOverSubtext.Text = $"The enemy broke through! Reached wave {_gm.CurrentWave}/{_gm.TotalWaves}";
        }
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

    private static DrawingColor Darken(DrawingColor c, float factor)
    {
        return DrawingColor.FromArgb(
            c.A,
            (byte)(c.R * factor),
            (byte)(c.G * factor),
            (byte)(c.B * factor));
    }

    private static DrawingColor Lighten(DrawingColor c, float amount)
    {
        return DrawingColor.FromArgb(
            c.A,
            (byte)Math.Min(255, c.R + (255 - c.R) * amount),
            (byte)Math.Min(255, c.G + (255 - c.G) * amount),
            (byte)Math.Min(255, c.B + (255 - c.B) * amount));
    }
}
