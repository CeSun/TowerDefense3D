using Aura3D.Avalonia;
using Aura3D.Core;
using Aura3D.Core.Geometries;
using Aura3D.Core.Nodes;
using Aura3D.Core.Resources;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Numerics;
using DrawingColor = System.Drawing.Color;

namespace TowerDefense;

public partial class MapListControl : UserControl
{
    // ==================== State ====================
    private string _mapsDir = string.Empty;
    private MapData _previewMap = MapData.CreateDefault();
    private int _selectedMapNum;
    private readonly Dictionary<int, Button> _mapCards = new();
    private bool _sceneReady;

    // ==================== Scene Cache ====================
    private Node? _groundNode;
    private readonly List<Node> _pathNodes = new();
    private readonly List<Node> _gridDotNodes = new();
    private Node? _entryMarkerNode;
    private Node? _exitMarkerNode;
    private PlaneGeometry? _planeGeo;
    private BoxGeometry? _boxGeo;
    private SphereGeometry? _sphereGeo;
    private Material? _groundMat;
    private Material? _buildableMat;
    private Material? _pathMat;

    // ==================== Callbacks ====================
    /// <summary>Fired when user clicks Edit. Args: (mapData, filePath)</summary>
    public Action<MapData, string>? OnEditMap { get; set; }
    public Action? OnMainMenu { get; set; }

    public MapListControl()
    {
        InitializeComponent();
    }

    // ==================== Initialization ====================

    public void Initialize(string mapsDir)
    {
        _mapsDir = mapsDir;
        EnsureMapsDir();
        BuildMapGrid();
    }

    /// <summary>Refresh the map grid (called when returning from editor).</summary>
    public void Refresh()
    {
        BuildMapGrid();
    }

    private void EnsureMapsDir()
    {
        if (!Directory.Exists(_mapsDir))
            Directory.CreateDirectory(_mapsDir);
        var defaultPath = Path.Combine(_mapsDir, "1.json");
        if (!File.Exists(defaultPath))
            MapData.CreateDefault().SaveToFile(defaultPath);
    }

    private int GetNextLevelNumber()
    {
        int max = 0;
        if (Directory.Exists(_mapsDir))
        {
            foreach (var file in Directory.GetFiles(_mapsDir, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (int.TryParse(name, out int num) && num > max)
                    max = num;
            }
        }
        return max + 1;
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
        return numbers;
    }

    // ==================== Map Card Grid ====================

    private void BuildMapGrid()
    {
        MapCardGrid.Children.Clear();
        _mapCards.Clear();

        var numbers = GetLevelNumbers();
        foreach (var num in numbers)
        {
            var card = new Button
            {
                Width = 120,
                Height = 90,
                Margin = new Avalonia.Thickness(6),
                CornerRadius = new Avalonia.CornerRadius(8),
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                Background = Brush.Parse("#CC2d5a27"),
                BorderBrush = Brushes.LimeGreen,
                BorderThickness = new Avalonia.Thickness(0),
                Content = $"Level {num}",
                Tag = num,
            };
            card.Click += (_, _) =>
            {
                if (card.Tag is int n)
                    SelectMapCard(n);
            };
            _mapCards[num] = card;
            MapCardGrid.Children.Add(card);
        }

        _selectedMapNum = 0;
        UpdateMapCardStyles();
        ClearMapDetails();

        MapListStatus.Text = numbers.Count == 0
            ? "No maps yet — create a new one!"
            : $"{numbers.Count} maps available";
    }

    private void SelectMapCard(int num)
    {
        _selectedMapNum = num;
        UpdateMapCardStyles();

        var filePath = Path.Combine(_mapsDir, num + ".json");
        if (!File.Exists(filePath))
        {
            ClearMapDetails();
            DetailMapName.Text = $"Level {num}";
            DetailMapStatus.Text = "File not found";
            return;
        }

        var map = MapData.LoadFromFile(filePath);
        if (map == null)
        {
            ClearMapDetails();
            DetailMapName.Text = $"Level {num}";
            DetailMapStatus.Text = "Failed to load";
            return;
        }

        // Update 3D preview
        _previewMap = map;
        RebuildPreviewScene();

        // Update detail panel
        DetailMapName.Text = $"Level {num}";
        DetailMapGrid.Text = $"Grid: {map.GridCols} × {map.GridRows}";
        DetailMapWaves.Text = $"Waves: {map.Waves.Count}";
        int totalEnemies = map.Waves.Sum(w => w.Entries.Sum(e => e.Count));
        DetailMapEnemies.Text = $"Enemies: {totalEnemies}";
        int wpCount = (map.StartCell != null ? 1 : 0) + map.PathWaypoints.Count + (map.EndCell != null ? 1 : 0);
        DetailMapPath.Text = $"Waypoints: {wpCount}";
        DetailMapStatus.Text = map.IsComplete ? "✅ Complete" : "⚠ Incomplete";

        EditMapBtn.IsVisible = true;
        DeleteMapBtn.IsVisible = true;
    }

    private void UpdateMapCardStyles()
    {
        foreach (var (n, card) in _mapCards)
        {
            if (n == _selectedMapNum)
            {
                card.BorderBrush = Brushes.Gold;
                card.BorderThickness = new Avalonia.Thickness(3);
                card.Background = Brush.Parse("#CC3a7a3a");
            }
            else
            {
                card.BorderBrush = Brushes.LimeGreen;
                card.BorderThickness = new Avalonia.Thickness(0);
                card.Background = Brush.Parse("#CC2d5a27");
            }
        }
    }

    private void ClearMapDetails()
    {
        DetailMapName.Text = "Select a Map";
        DetailMapGrid.Text = "Grid: -";
        DetailMapWaves.Text = "Waves: -";
        DetailMapEnemies.Text = "Enemies: -";
        DetailMapPath.Text = "Path: -";
        DetailMapStatus.Text = "";
        EditMapBtn.IsVisible = false;
        DeleteMapBtn.IsVisible = false;
    }

    // ==================== Map Actions ====================

    private void OnEditMapClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedMapNum <= 0) return;
        var filePath = Path.Combine(_mapsDir, _selectedMapNum + ".json");
        OnEditMap?.Invoke(_previewMap, filePath);
    }

    private void OnDeleteMap(object? sender, RoutedEventArgs e)
    {
        if (_selectedMapNum <= 0) return;

        var numbers = GetLevelNumbers();
        if (numbers.Count <= 1)
        {
            MapListStatus.Text = "Cannot delete the last level.";
            return;
        }

        var filePath = Path.Combine(_mapsDir, _selectedMapNum + ".json");
        if (File.Exists(filePath))
            File.Delete(filePath);

        _selectedMapNum = 0;
        BuildMapGrid();
        MapListStatus.Text = "Level deleted.";
    }

    private void OnNewMap(object? sender, RoutedEventArgs e)
    {
        var next = GetNextLevelNumber();
        var map = new MapData
        {
            Name = next.ToString(),
            GridCols = 20,
            GridRows = 12,
            CellSize = 1f,
            StartCell = new WaypointCell(0, 6),
            EndCell = new WaypointCell(19, 6),
            PathWaypoints = { new(9, 6) },
            Waves =
            {
                new()
                {
                    DelayBeforeWave = 3f,
                    Entries = { new("Basic", 5, 1.0f) },
                },
            },
        };
        var filePath = Path.Combine(_mapsDir, next + ".json");
        map.SaveToFile(filePath);
        OnEditMap?.Invoke(map, filePath);
    }

    private void OnMainMenuClick(object? sender, RoutedEventArgs e)
    {
        OnMainMenu?.Invoke();
    }

    // ==================== 3D Preview Scene ====================

    private void OnPreviewSceneInit(object? sender, InitializedRoutedEventArgs e)
    {
        var view = (sender as Aura3DView)!;

        _groundNode = null;
        _entryMarkerNode = null;
        _exitMarkerNode = null;
        _pathNodes.Clear();
        _gridDotNodes.Clear();

        view.MainCamera.Position = new Vector3(10, 16, 20);
        view.MainCamera.RotationDegrees = new Vector3(-55, 0, 0);
        view.Scene.Background = Texture.CreateFromColor(DrawingColor.DarkSlateGray);

        // CSM shadow quality
        view.PipelineSettings.CsmShadowMapResolution = 2048;
        view.PipelineSettings.CsmCascadeCount = 4;
        view.PipelineSettings.CsmSplitLambda = 0.6f;

        _boxGeo = new BoxGeometry();
        _sphereGeo = new SphereGeometry();
        _planeGeo = new PlaneGeometry();

        _groundMat = CreateMaterial(DrawingColor.DarkGreen);
        _buildableMat = CreateMaterial(DrawingColor.FromArgb(255, 34, 139, 34));
        _pathMat = CreateMaterial(DrawingColor.SandyBrown);

        var dl = new DirectionalLight
        {
            RotationDegrees = new Vector3(-40, -20, 0),
            LightColor = DrawingColor.White,
            CastShadow = true,
            ShadowConfig = new DirectionalLightShadowMapConfig
            {
                Width = 30, Height = 30,
                NearPlane = 0.5f, FarPlane = 60,
            }
        };
        view.AddNode(dl);
        view.Scene.MainDirectionalLight = dl;

        var ambient = new DirectionalLight();
        ambient.RotationDegrees = new Vector3(20, 150, 0);
        ambient.LightColor = DrawingColor.FromArgb(255, 80, 80, 100);
        view.AddNode(ambient);

        _sceneReady = true;
        RebuildPreviewScene();
    }

    private void OnPreviewSceneUpdated(object? sender, UpdateRoutedEventArgs args)
    {
        PreviewView.RequestNextFrameRendering();
    }

    private void RebuildPreviewScene()
    {
        if (!_sceneReady) return;
        var view = PreviewView;

        // Clear
        if (_groundNode != null) { view.Remove(_groundNode); _groundNode = null; }
        foreach (var n in _pathNodes) view.Remove(n);
        _pathNodes.Clear();
        foreach (var n in _gridDotNodes) view.Remove(n);
        _gridDotNodes.Clear();
        if (_entryMarkerNode != null) { view.Remove(_entryMarkerNode); _entryMarkerNode = null; }
        if (_exitMarkerNode != null) { view.Remove(_exitMarkerNode); _exitMarkerNode = null; }

        // Ground
        var ground = new Mesh
        {
            Geometry = _planeGeo!,
            Material = _groundMat!,
        };
        ground.Scale = new Vector3(_previewMap.GridCols + 2, 1, _previewMap.GridRows + 2);
        ground.Position = new Vector3(_previewMap.GridCols / 2f, -0.05f, _previewMap.GridRows / 2f);
        _groundNode = ground;
        view.AddNode(ground);

        // Grid dots and path
        var pathSet = ComputePathCells();
        for (int col = 0; col < _previewMap.GridCols; col++)
        {
            for (int row = 0; row < _previewMap.GridRows; row++)
            {
                if (pathSet.Contains((col, row)))
                {
                    var tile = new Mesh
                    {
                        Geometry = _planeGeo!,
                        Material = _pathMat!,
                    };
                    tile.Scale = new Vector3(0.9f, 1, 0.9f);
                    tile.Position = CellToWorld(col, row) + new Vector3(0, 0.03f, 0);
                    _pathNodes.Add(tile);
                    view.AddNode(tile);
                }
                else
                {
                    var dot = new Mesh
                    {
                        Geometry = _boxGeo!,
                        Material = _buildableMat!,
                    };
                    dot.Scale = new Vector3(0.12f, 0.02f, 0.12f);
                    dot.Position = CellToWorld(col, row) + new Vector3(0, 0.02f, 0);
                    _gridDotNodes.Add(dot);
                    view.AddNode(dot);
                }
            }
        }

        // Start marker
        if (_previewMap.StartCell != null)
        {
            var s = _previewMap.StartCell;
            _entryMarkerNode = new Mesh
            {
                Geometry = _boxGeo!,
                Material = CreateMaterial(DrawingColor.LimeGreen),
            };
            _entryMarkerNode.Scale = new Vector3(0.2f, 0.08f, 0.8f);
            _entryMarkerNode.Position = CellToWorld(s.Col, s.Row) + new Vector3(-0.5f, 0.06f, 0);
            view.AddNode(_entryMarkerNode);
        }

        // End marker
        if (_previewMap.EndCell != null)
        {
            var e = _previewMap.EndCell;
            _exitMarkerNode = new Mesh
            {
                Geometry = _boxGeo!,
                Material = CreateMaterial(DrawingColor.Red),
            };
            _exitMarkerNode.Scale = new Vector3(0.2f, 0.08f, 0.8f);
            _exitMarkerNode.Position = CellToWorld(e.Col, e.Row) + new Vector3(0.5f, 0.06f, 0);
            view.AddNode(_exitMarkerNode);
        }

        // Camera
        view.MainCamera.Position = new Vector3(
            _previewMap.GridCols / 2f,
            Math.Max(_previewMap.GridCols, _previewMap.GridRows) * 1.3f,
            _previewMap.GridRows + 4);
    }

    private HashSet<(int, int)> ComputePathCells()
    {
        var set = new HashSet<(int, int)>();
        var wps = new List<WaypointCell>();
        if (_previewMap.StartCell != null) wps.Add(_previewMap.StartCell);
        wps.AddRange(_previewMap.PathWaypoints);
        if (_previewMap.EndCell != null) wps.Add(_previewMap.EndCell);

        for (int w = 1; w < wps.Count; w++)
        {
            var from = wps[w - 1];
            var to = wps[w];
            int stepX = Math.Sign(to.Col - from.Col);
            int stepZ = Math.Sign(to.Row - from.Row);
            int cx = from.Col, cz = from.Row;
            while (cx != to.Col) { cx += stepX; set.Add((cx, cz)); }
            while (cz != to.Row) { cz += stepZ; set.Add((cx, cz)); }
        }
        foreach (var wp in wps) set.Add((wp.Col, wp.Row));
        return set;
    }

    private Vector3 CellToWorld(int col, int row) => new(col + 0.5f, 0, row + 0.5f);

    private static Material CreateMaterial(DrawingColor color) => new()
    {
        BlendMode = BlendMode.Opaque,
        DoubleSided = true,
        Channels = { new() { Name = "BaseColor", Texture = Texture.CreateFromColor(color) } }
    };
}
