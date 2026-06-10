using Aura3D.Avalonia;
using Aura3D.Core;
using Aura3D.Core.Geometries;
using Aura3D.Core.Nodes;
using Aura3D.Core.Resources;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System.Numerics;
using DrawingColor = System.Drawing.Color;

namespace TowerDefense;

public partial class MapEditorView : UserControl
{
    // ==================== State ====================
    private MapData _mapData = MapData.CreateDefault();
    private string? _currentFilePath;
    private string _mapsDir = string.Empty;

    // ==================== Scene ====================
    private Node? _groundNode;
    private readonly List<Node> _pathNodes = new();
    private readonly List<Node> _gridDotNodes = new();
    private readonly List<Node> _waypointMarkerNodes = new();
    private Node? _entryMarkerNode;
    private Node? _exitMarkerNode;
    private bool _sceneReady;

    // ==================== Geometry & Material Cache ====================
    private BoxGeometry? _boxGeo;
    private SphereGeometry? _sphereGeo;
    private PlaneGeometry? _planeGeo;
    private Material? _groundMat;
    private Material? _buildableMat;
    private Material? _pathMat;

    // ==================== Callback ====================
    public Action<MapData>? OnPlayMap { get; set; }

    // ==================== Initialization ====================

    public MapEditorView()
    {
        InitializeComponent();

        // Populate enemy type dropdown
        EnemyTypeCombo.ItemsSource = new[] { "Basic", "Fast", "Tank" };
        EnemyTypeCombo.SelectedIndex = 0;
    }

    /// <summary>
    /// Set the maps directory and load the map list.
    /// </summary>
    public void Initialize(string mapsDir)
    {
        _mapsDir = mapsDir;
        EnsureMapsDir();
        RefreshMapList();
        // Load the first map (or default)
        LoadMap(MapData.CreateDefault());
    }

    private void EnsureMapsDir()
    {
        if (!Directory.Exists(_mapsDir))
            Directory.CreateDirectory(_mapsDir);

        // Ensure default.json exists
        var defaultPath = Path.Combine(_mapsDir, "default.json");
        if (!File.Exists(defaultPath))
        {
            MapData.CreateDefault().SaveToFile(defaultPath);
        }
    }

    private void RefreshMapList()
    {
        MapListCombo.Items.Clear();
        if (Directory.Exists(_mapsDir))
        {
            foreach (var file in Directory.GetFiles(_mapsDir, "*.json"))
            {
                MapListCombo.Items.Add(Path.GetFileNameWithoutExtension(file));
            }
        }
        if (MapListCombo.Items.Count == 0)
            MapListCombo.Items.Add("default");
        MapListCombo.SelectedIndex = 0;
    }

    // ==================== Map Loading ====================

    private void LoadMap(MapData map)
    {
        _mapData = map;
        _currentFilePath = null;
        SyncUIFromMap();
        RebuildMapScene();
    }

    private void LoadMapFromFile(string filePath)
    {
        var map = MapData.LoadFromFile(filePath);
        if (map != null)
        {
            _mapData = map;
            _currentFilePath = filePath;
            SyncUIFromMap();
            RebuildMapScene();
        }
    }

    private void SyncUIFromMap()
    {
        GridColsInput.Value = _mapData.GridCols;
        GridRowsInput.Value = _mapData.GridRows;
        RefreshWaypointList();
        RefreshWaveList();
        RefreshEntryList();
    }

    // ==================== Scene Initialization ====================

    private void OnEditorSceneInit(object? sender, InitializedRoutedEventArgs e)
    {
        var view = (sender as Aura3DView)!;
        view.MainCamera.Position = new Vector3(
            _mapData.GridCols / 2f,
            Math.Max(_mapData.GridCols, _mapData.GridRows) * 1.3f,
            _mapData.GridRows + 4);
        view.MainCamera.RotationDegrees = new Vector3(-55, 0, 0);
        view.Scene.Background = Texture.CreateFromColor(DrawingColor.DarkSlateGray);

        // Pointer events for editing
        view.PointerPressed += OnEditorPointerPressed;

        // Create geometries
        _boxGeo = new BoxGeometry();
        _sphereGeo = new SphereGeometry();
        _planeGeo = new PlaneGeometry();

        // Create materials
        _groundMat = CreateMaterial(DrawingColor.DarkGreen);
        _buildableMat = CreateMaterial(DrawingColor.FromArgb(255, 34, 139, 34));
        _pathMat = CreateMaterial(DrawingColor.SandyBrown);

        // Main directional light with CSM shadows (match GameView)
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
        var ambient = new DirectionalLight();
        ambient.RotationDegrees = new Vector3(20, 150, 0);
        ambient.LightColor = DrawingColor.FromArgb(255, 80, 80, 100);
        view.AddNode(ambient);

        _sceneReady = true;
        RebuildMapScene();
    }

    private void OnEditorSceneUpdated(object? sender, UpdateRoutedEventArgs args)
    {
        EditorView.RequestNextFrameRendering();
    }

    // ==================== Scene Rebuild ====================

    private void RebuildMapScene()
    {
        if (!_sceneReady) return;
        var view = EditorView;

        ClearMapNodes();
        BuildGround(view);
        BuildGridDots(view);
        BuildPathTiles(view);
        BuildWaypointMarkers(view);
        BuildMarkers(view);

        // Adjust camera for new map size
        view.MainCamera.Position = new Vector3(
            _mapData.GridCols / 2f,
            Math.Max(_mapData.GridCols, _mapData.GridRows) * 1.3f,
            _mapData.GridRows + 4);

    }

    private void ClearMapNodes()
    {
        var view = EditorView;

        if (_groundNode != null) { view.Remove(_groundNode); _groundNode = null; }

        foreach (var n in _pathNodes) view.Remove(n);
        _pathNodes.Clear();

        foreach (var n in _gridDotNodes) view.Remove(n);
        _gridDotNodes.Clear();

        foreach (var n in _waypointMarkerNodes) view.Remove(n);
        _waypointMarkerNodes.Clear();

        if (_entryMarkerNode != null) { view.Remove(_entryMarkerNode); _entryMarkerNode = null; }
        if (_exitMarkerNode != null) { view.Remove(_exitMarkerNode); _exitMarkerNode = null; }
    }

    private void BuildGround(Aura3DView view)
    {
        var mesh = new Mesh
        {
            Geometry = _planeGeo!,
            Material = _groundMat!,
            Name = "EditorGround",
        };
        mesh.Scale = new Vector3(_mapData.GridCols + 2, 1, _mapData.GridRows + 2);
        mesh.Position = new Vector3(_mapData.GridCols / 2f, -0.05f, _mapData.GridRows / 2f);
        _groundNode = mesh;
        view.AddNode(mesh);
    }

    private void BuildGridDots(Aura3DView view)
    {
        for (int col = 0; col < _mapData.GridCols; col++)
        {
            for (int row = 0; row < _mapData.GridRows; row++)
            {
                if (IsPathCell(col, row)) continue;

                var dot = new Mesh
                {
                    Geometry = _boxGeo!,
                    Material = _buildableMat!,
                    Name = $"GridCell_{col}_{row}",
                };
                dot.Scale = new Vector3(0.12f, 0.02f, 0.12f);
                dot.Position = CellToWorld(col, row) + new Vector3(0, 0.02f, 0);
                _gridDotNodes.Add(dot);
                view.AddNode(dot);
            }
        }
    }

    private void BuildPathTiles(Aura3DView view)
    {
        for (int col = 0; col < _mapData.GridCols; col++)
        {
            for (int row = 0; row < _mapData.GridRows; row++)
            {
                if (!IsPathCell(col, row)) continue;

                var tile = new Mesh
                {
                    Geometry = _planeGeo!,
                    Material = _pathMat!,
                    Name = $"PathTile_{col}_{row}",
                };
                tile.Scale = new Vector3(0.9f, 1, 0.9f);
                tile.Position = CellToWorld(col, row) + new Vector3(0, 0.03f, 0);
                _pathNodes.Add(tile);
                view.AddNode(tile);
            }
        }
    }

    private void BuildWaypointMarkers(Aura3DView view)
    {
        var wps = _mapData.PathWaypoints;
        for (int i = 0; i < wps.Count; i++)
        {
            var color = GetWaypointColor(i, wps.Count);
            var node = new Node { Name = $"Waypoint_{i}" };

            // Flat base disc
            var disc = new Mesh
            {
                Geometry = _boxGeo!,
                Material = CreateMaterial(color),
            };
            disc.Scale = new Vector3(0.5f, 0.03f, 0.5f);
            node.AddChild(disc, AttachToParentRule.KeepLocal);

            // Center sphere for visibility
            var sphere = new Mesh
            {
                Geometry = _sphereGeo!,
                Material = CreateMaterial(color),
            };
            sphere.Scale = new Vector3(0.20f);
            sphere.Position = new Vector3(0, 0.10f, 0);
            node.AddChild(sphere, AttachToParentRule.KeepLocal);

            var wp = wps[i];
            node.Position = CellToWorld(wp.Col, wp.Row) + new Vector3(0, 0.06f, 0);
            _waypointMarkerNodes.Add(node);
            view.AddNode(node);
        }
    }

    private void BuildMarkers(Aura3DView view)
    {
        var wps = _mapData.PathWaypoints;
        if (wps.Count == 0) return;

        // Entry marker
        var entryWp = wps[0];
        var entryPos = CellToWorld(entryWp.Col, entryWp.Row) + new Vector3(-0.5f, 0.06f, 0);
        _entryMarkerNode = new Mesh
        {
            Geometry = _boxGeo!,
            Material = CreateMaterial(DrawingColor.LimeGreen),
            Name = "EntryMarker",
        };
        _entryMarkerNode.Scale = new Vector3(0.2f, 0.08f, 0.8f);
        _entryMarkerNode.Position = entryPos;
        view.AddNode(_entryMarkerNode);

        // Exit marker
        var exitWp = wps[^1];
        var exitPos = CellToWorld(exitWp.Col, exitWp.Row) + new Vector3(0.5f, 0.06f, 0);
        _exitMarkerNode = new Mesh
        {
            Geometry = _boxGeo!,
            Material = CreateMaterial(DrawingColor.Red),
            Name = "ExitMarker",
        };
        _exitMarkerNode.Scale = new Vector3(0.2f, 0.08f, 0.8f);
        _exitMarkerNode.Position = exitPos;
        view.AddNode(_exitMarkerNode);
    }

    // ==================== Pointer Input ====================

    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_sceneReady) return;

        var view = EditorView;
        var pos = e.GetPosition(view);

        if (e.GetCurrentPoint(view).Properties.IsRightButtonPressed)
        {
            // Right click — try to delete a waypoint
            HandleRightClick(pos);
            return;
        }

        // Left click — handled by ObjectPicked event for ground/grid clicks
    }

    private void OnEditorObjectPicked(object? sender, ObjectPickedEventArgs args)
    {
        if (!_sceneReady) return;

        var nodeName = args.Node.Name ?? "";

        // Check if clicked a waypoint marker — select it
        if (nodeName.StartsWith("Waypoint_"))
        {
            if (int.TryParse(nodeName.Replace("Waypoint_", ""), out int index))
            {
                WaypointList.SelectedIndex = index;
                GridStatusText.Text = $"Selected waypoint {index} @ ({_mapData.PathWaypoints[index].Col}, {_mapData.PathWaypoints[index].Row}). Press Delete to remove.";
            }
            return;
        }

        // Check if clicked a grid cell or path tile
        var worldPos = args.WorldPosition;
        var grid = WorldToCell(worldPos);
        if (grid == null) return;

        var (col, row) = grid.Value;

        // Check if clicking on an existing waypoint to select it
        int existingIndex = FindWaypointAt(col, row);
        if (existingIndex >= 0)
        {
            WaypointList.SelectedIndex = existingIndex;
            GridStatusText.Text = $"Selected waypoint {existingIndex} @ ({col}, {row})";
            return;
        }

        // Add new waypoint: find best insertion point
        int insertAt = FindBestInsertionPoint(col, row);
        var wp = new WaypointCell(col, row);

        if (insertAt < 0)
        {
            _mapData.PathWaypoints.Add(wp);
        }
        else
        {
            _mapData.PathWaypoints.Insert(insertAt, wp);
        }

        RefreshWaypointList();
        RebuildMapScene();
        GridStatusText.Text = $"Added waypoint @ ({col}, {row})";
    }

    private void HandleRightClick(Avalonia.Point pos)
    {
        var pick = EditorView.PickClosestAt(pos.X, pos.Y);
        if (pick == null) return;

        var worldPos = pick.WorldPosition;
        var grid = WorldToCell(worldPos);
        if (grid == null) return;

        var (col, row) = grid.Value;
        int index = FindWaypointAt(col, row);
        if (index >= 0)
        {
            _mapData.PathWaypoints.RemoveAt(index);
            RefreshWaypointList();
            RebuildMapScene();
            GridStatusText.Text = $"Removed waypoint @ ({col}, {row})";
        }
    }

    // ==================== Path Calculation ====================

    private bool IsPathCell(int col, int row)
    {
        var wps = _mapData.PathWaypoints;
        for (int w = 1; w < wps.Count; w++)
        {
            var from = wps[w - 1];
            var to = wps[w];

            int stepX = Math.Sign(to.Col - from.Col);
            int stepZ = Math.Sign(to.Row - from.Row);

            int cx = from.Col, cz = from.Row;

            while (cx != to.Col)
            {
                cx += stepX;
                if (cx == col && cz == row) return true;
            }
            while (cz != to.Row)
            {
                cz += stepZ;
                if (cx == col && cz == row) return true;
            }
        }
        return wps.Count > 0 && wps.Any(w => w.Col == col && w.Row == row);
    }

    private int FindWaypointAt(int col, int row)
    {
        return _mapData.PathWaypoints.FindIndex(w => w.Col == col && w.Row == row);
    }

    private int FindBestInsertionPoint(int col, int row)
    {
        // Simple heuristic: insert at the end (user can reorder by deleting and re-adding)
        // Actually, let's find the closest segment and insert there
        var wps = _mapData.PathWaypoints;
        if (wps.Count < 2) return -1;

        float bestDist = float.MaxValue;
        int bestIdx = -1;

        for (int i = 1; i < wps.Count; i++)
        {
            var from = wps[i - 1];
            var to = wps[i];

            // Check if (col,row) lies near the segment between from and to
            float dist = DistanceToSegment(col, row, from.Col, from.Row, to.Col, to.Row);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx = i;
            }
        }

        return bestDist < 3f ? bestIdx : -1; // insert at end if far from any segment
    }

    private static float DistanceToSegment(float px, float py, float ax, float ay, float bx, float by)
    {
        float abx = bx - ax, aby = by - ay;
        float apx = px - ax, apy = py - ay;
        float t = Math.Clamp((apx * abx + apy * aby) / Math.Max(0.001f, abx * abx + aby * aby), 0, 1);
        float dx = px - (ax + t * abx);
        float dy = py - (ay + t * aby);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    // ==================== Grid Helpers ====================

    private Vector3 CellToWorld(int col, int row)
    {
        return new Vector3(col + 0.5f, 0, row + 0.5f);
    }

    private (int Col, int Row)? WorldToCell(Vector3 pos)
    {
        int col = (int)MathF.Floor(pos.X);
        int row = (int)MathF.Floor(pos.Z);
        if (col < 0 || col >= _mapData.GridCols || row < 0 || row >= _mapData.GridRows)
            return null;
        return (col, row);
    }

    // ==================== Waypoint List Management ====================

    private void RefreshWaypointList()
    {
        WaypointList.Items.Clear();
        for (int i = 0; i < _mapData.PathWaypoints.Count; i++)
        {
            var wp = _mapData.PathWaypoints[i];
            WaypointList.Items.Add($"{i}: ({wp.Col}, {wp.Row})");
        }
        WaypointCount.Text = $"({_mapData.PathWaypoints.Count})";
    }

    private void OnDeleteWaypoint(object? sender, RoutedEventArgs e)
    {
        if (WaypointList.SelectedIndex >= 0)
        {
            _mapData.PathWaypoints.RemoveAt(WaypointList.SelectedIndex);
            RefreshWaypointList();
            RebuildMapScene();
            GridStatusText.Text = "Waypoint deleted.";
        }
    }

    private void OnClearWaypoints(object? sender, RoutedEventArgs e)
    {
        _mapData.PathWaypoints.Clear();
        RefreshWaypointList();
        RebuildMapScene();
        GridStatusText.Text = "All waypoints cleared.";
    }

    // ==================== Wave Management ====================

    private bool _refreshingWaves;

    private void RefreshWaveList()
    {
        _refreshingWaves = true;
        try
        {
            // Deselect before clearing to avoid Avalonia hitting a stale index
            WaveList.SelectedIndex = -1;
            WaveList.Items.Clear();
            for (int i = 0; i < _mapData.Waves.Count; i++)
            {
                var wave = _mapData.Waves[i];
                string summary = $"Wave {i + 1} — {wave.Entries.Count} entries, delay {wave.DelayBeforeWave:F1}s";
                WaveList.Items.Add(summary);
            }
            WaveCountText.Text = $"({_mapData.Waves.Count})";
        }
        finally
        {
            _refreshingWaves = false;
        }
    }

    private void RefreshEntryList()
    {
        if (_refreshingWaves) return;

        EntryList.Items.Clear();
        if (WaveList.SelectedIndex < 0 && _mapData.Waves.Count > 0)
            WaveList.SelectedIndex = 0;

        if (WaveList.SelectedIndex >= 0 && WaveList.SelectedIndex < _mapData.Waves.Count)
        {
            var wave = _mapData.Waves[WaveList.SelectedIndex];
            WaveDelayInput.Value = (decimal)wave.DelayBeforeWave;
            foreach (var entry in wave.Entries)
            {
                EntryList.Items.Add($"{entry.EnemyType} ×{entry.Count} ({entry.SpawnInterval:F1}s)");
            }
        }
        WaveEditPanel.IsVisible = WaveList.SelectedIndex >= 0;
    }

    private void OnWaveListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshEntryList();
    }

    private void OnAddWave(object? sender, RoutedEventArgs e)
    {
        _mapData.Waves.Add(new WaveConfigData
        {
            DelayBeforeWave = 5f,
            Entries = { new WaveEntryData("Basic", 5, 1.0f) }
        });
        RefreshWaveList();
        WaveList.SelectedIndex = _mapData.Waves.Count - 1;
        RefreshEntryList();
    }

    private void OnDeleteWave(object? sender, RoutedEventArgs e)
    {
        if (WaveList.SelectedIndex >= 0 && _mapData.Waves.Count > 1)
        {
            _mapData.Waves.RemoveAt(WaveList.SelectedIndex);
            RefreshWaveList();
            RefreshEntryList();
        }
    }

    private void OnWaveDelayChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (WaveList.SelectedIndex >= 0 && WaveList.SelectedIndex < _mapData.Waves.Count)
        {
            _mapData.Waves[WaveList.SelectedIndex].DelayBeforeWave = (float)e.NewValue;
            RefreshWaveList();
        }
    }

    private void OnAddEntry(object? sender, RoutedEventArgs e)
    {
        if (WaveList.SelectedIndex < 0) return;

        var enemyType = (string)(EnemyTypeCombo.SelectedItem ?? "Basic");
        var wave = _mapData.Waves[WaveList.SelectedIndex];
        wave.Entries.Add(new WaveEntryData(enemyType, 3, 1.0f));
        RefreshEntryList();
        RefreshWaveList();
    }

    private void OnDeleteEntry(object? sender, RoutedEventArgs e)
    {
        if (WaveList.SelectedIndex < 0) return;
        if (EntryList.SelectedIndex < 0) return;

        var wave = _mapData.Waves[WaveList.SelectedIndex];
        if (wave.Entries.Count > 1 || _mapData.Waves.Count > 1)
        {
            if (wave.Entries.Count > 0)
            {
                wave.Entries.RemoveAt(EntryList.SelectedIndex);
                if (EntryList.SelectedIndex >= wave.Entries.Count)
                    EntryList.SelectedIndex = wave.Entries.Count - 1;
                RefreshEntryList();
                RefreshWaveList();
            }
        }
    }

    // ==================== Grid Size ====================

    private void OnGridSizeChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (!_sceneReady) return;

        _mapData.GridCols = (int)GridColsInput.Value;
        _mapData.GridRows = (int)GridRowsInput.Value;

        // Remove waypoints that are now out of bounds
        _mapData.PathWaypoints.RemoveAll(w => w.Col >= _mapData.GridCols || w.Row >= _mapData.GridRows);

        RefreshWaypointList();
        RebuildMapScene();
    }

    // ==================== File Operations ====================

    private void OnMapSelected(object? sender, SelectionChangedEventArgs e)
    {
        // Guard: don't try to load before Initialize() has set up _mapsDir
        if (string.IsNullOrEmpty(_mapsDir))
            return;

        if (MapListCombo.SelectedItem is string name)
        {
            var filePath = Path.Combine(_mapsDir, name + ".json");
            if (File.Exists(filePath))
            {
                LoadMapFromFile(filePath);
            }
        }
    }

    private void OnNewMap(object? sender, RoutedEventArgs e)
    {
        _mapData = new MapData
        {
            Name = "New Map",
            GridCols = 20,
            GridRows = 12,
            CellSize = 1f,
        };
        _currentFilePath = null;
        SyncUIFromMap();
        RebuildMapScene();
        GridStatusText.Text = "New map created. Add waypoints to define the path.";
    }

    private void OnSaveMap(object? sender, RoutedEventArgs e)
    {
        if (_currentFilePath == null)
        {
            // Prompt for name
            var name = PromptMapName();
            if (string.IsNullOrWhiteSpace(name)) return;

            _mapData.Name = name;
            _currentFilePath = Path.Combine(_mapsDir, name + ".json");
        }
        else
        {
            _mapData.Name = Path.GetFileNameWithoutExtension(_currentFilePath);
        }

        _mapData.SaveToFile(_currentFilePath);
        RefreshMapList();
        MapListCombo.SelectedItem = _mapData.Name;
        GridStatusText.Text = $"Saved: {_currentFilePath}";
    }

    private void OnSaveAsMap(object? sender, RoutedEventArgs e)
    {
        var name = PromptMapName();
        if (string.IsNullOrWhiteSpace(name)) return;

        _mapData.Name = name;
        _currentFilePath = Path.Combine(_mapsDir, name + ".json");
        _mapData.SaveToFile(_currentFilePath);
        RefreshMapList();
        MapListCombo.SelectedItem = name;
        GridStatusText.Text = $"Saved as: {_currentFilePath}";
    }

    private string? PromptMapName()
    {
        // Simple approach: generate a name based on timestamp
        // In production you'd use a dialog, but for this simple editor we auto-name
        // Actually, let's just ask for a name via a simple approach
        return $"map_{DateTime.Now:yyyyMMdd_HHmmss}";
    }

    // ==================== Test Map ====================

    private void OnTestMap(object? sender, RoutedEventArgs e)
    {
        OnPlayMap?.Invoke(_mapData);
    }

    // ==================== Helpers ====================

    private static DrawingColor GetWaypointColor(int index, int total)
    {
        if (total <= 1) return DrawingColor.LimeGreen;
        if (index == 0) return DrawingColor.LimeGreen;
        if (index == total - 1) return DrawingColor.Red;

        float t = (float)index / (total - 1);
        // Gradient from green → yellow → orange → red
        return DrawingColor.FromArgb(255,
            (byte)(255 * t),
            (byte)(255 * (1 - Math.Abs(t - 0.5f) * 2)),
            (byte)(50 * (1 - t)));
    }

    private Material CreateMaterial(DrawingColor color)
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

    // ==================== Public API for MainWindow ====================

    /// <summary>
    /// Gets the current map data being edited.
    /// </summary>
    public MapData GetCurrentMap() => _mapData;
}
