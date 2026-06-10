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

public enum PlacementMode { Start, Waypoint, End }

public partial class MapEditorView : UserControl
{
    // ==================== State ====================
    private MapData _mapData = MapData.CreateDefault();
    private string? _currentFilePath;
    private string _mapsDir = string.Empty;
    private PlacementMode _placementMode = PlacementMode.Waypoint;

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

        // Discard all stale node references from any previous scene
        _groundNode = null;
        _entryMarkerNode = null;
        _exitMarkerNode = null;
        _pathNodes.Clear();
        _gridDotNodes.Clear();
        _waypointMarkerNodes.Clear();
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
        var wps = _mapData.PathWaypoints; // intermediate waypoints only
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
        // Entry marker at StartCell
        if (_mapData.StartCell != null)
        {
            var s = _mapData.StartCell;
            var entryPos = CellToWorld(s.Col, s.Row) + new Vector3(-0.5f, 0.06f, 0);
            _entryMarkerNode = new Mesh
            {
                Geometry = _boxGeo!,
                Material = CreateMaterial(DrawingColor.LimeGreen),
                Name = "EntryMarker",
            };
            _entryMarkerNode.Scale = new Vector3(0.2f, 0.08f, 0.8f);
            _entryMarkerNode.Position = entryPos;
            view.AddNode(_entryMarkerNode);
        }

        // Exit marker at EndCell
        if (_mapData.EndCell != null)
        {
            var e = _mapData.EndCell;
            var exitPos = CellToWorld(e.Col, e.Row) + new Vector3(0.5f, 0.06f, 0);
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
    }

    // ==================== Placement Mode ====================

    private void OnSetStartMode(object? sender, RoutedEventArgs e)
    {
        _placementMode = PlacementMode.Start;
        UpdatePlacementModeButtons();
        GridStatusText.Text = "Mode: START — Click a cell to set it as the path entry point";
    }

    private void OnSetWaypointMode(object? sender, RoutedEventArgs e)
    {
        _placementMode = PlacementMode.Waypoint;
        UpdatePlacementModeButtons();
        GridStatusText.Text = "Mode: WAYPOINT — Click a cell to add a waypoint along the path";
    }

    private void OnSetEndMode(object? sender, RoutedEventArgs e)
    {
        _placementMode = PlacementMode.End;
        UpdatePlacementModeButtons();
        GridStatusText.Text = "Mode: END — Click a cell to set it as the path exit point";
    }

    private void UpdatePlacementModeButtons()
    {
        var dimBg = "#CC3a3a4a";
        var activeBorder = new Avalonia.Thickness(2);

        SetStartBtn.BorderThickness = _placementMode == PlacementMode.Start ? activeBorder : default;
        SetStartBtn.BorderBrush = _placementMode == PlacementMode.Start ? Avalonia.Media.Brushes.LimeGreen : null;

        AddWaypointBtn.BorderThickness = _placementMode == PlacementMode.Waypoint ? activeBorder : default;
        AddWaypointBtn.BorderBrush = _placementMode == PlacementMode.Waypoint ? Avalonia.Media.Brushes.CornflowerBlue : null;

        SetEndBtn.BorderThickness = _placementMode == PlacementMode.End ? activeBorder : default;
        SetEndBtn.BorderBrush = _placementMode == PlacementMode.End ? Avalonia.Media.Brushes.Red : null;

        // Dim background of inactive buttons
        SetStartBtn.Background = Avalonia.Media.Brush.Parse(
            _placementMode == PlacementMode.Start ? "#CC2d6a2d" : dimBg);
        AddWaypointBtn.Background = Avalonia.Media.Brush.Parse(
            _placementMode == PlacementMode.Waypoint ? "#CC4a6a8a" : dimBg);
        SetEndBtn.Background = Avalonia.Media.Brush.Parse(
            _placementMode == PlacementMode.End ? "#CC6a2a2a" : dimBg);
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

        // Clicked a waypoint marker — select it in the list
        if (nodeName.StartsWith("Waypoint_"))
        {
            if (int.TryParse(nodeName.Replace("Waypoint_", ""), out int index)
                && index < _mapData.PathWaypoints.Count)
            {
                WaypointList.SelectedIndex = index;
                GridStatusText.Text = $"Selected waypoint {index} @ ({_mapData.PathWaypoints[index].Col}, {_mapData.PathWaypoints[index].Row})";
            }
            return;
        }

        // Clicked entry/exit markers — show status
        if (nodeName == "EntryMarker")
        {
            var s = _mapData.StartCell;
            GridStatusText.Text = s != null ? $"START @ ({s.Col}, {s.Row})" : "No start set.";
            return;
        }
        if (nodeName == "ExitMarker")
        {
            var e = _mapData.EndCell;
            GridStatusText.Text = e != null ? $"END @ ({e.Col}, {e.Row})" : "No end set.";
            return;
        }

        var worldPos = args.WorldPosition;
        var grid = WorldToCell(worldPos);
        if (grid == null) return;
        var (col, row) = grid.Value;

        switch (_placementMode)
        {
            case PlacementMode.Start:
                _mapData.StartCell = new WaypointCell(col, row);
                RefreshWaypointList();
                RebuildMapScene();
                GridStatusText.Text = $"Set START @ ({col}, {row})";
                break;

            case PlacementMode.End:
                _mapData.EndCell = new WaypointCell(col, row);
                RefreshWaypointList();
                RebuildMapScene();
                GridStatusText.Text = $"Set END @ ({col}, {row})";
                break;

            default: // Waypoint
                // Don't allow placing waypoints on start/end cells
                if (IsStartCell(col, row) || IsEndCell(col, row))
                {
                    GridStatusText.Text = $"Cell ({col}, {row}) is start/end. Switch mode to change it.";
                    return;
                }
                int existing = FindWaypointAt(col, row);
                if (existing >= 0)
                {
                    WaypointList.SelectedIndex = existing;
                    GridStatusText.Text = $"Selected waypoint {existing} @ ({col}, {row})";
                    return;
                }
                // Always append — use ↑↓ buttons to reorder
                _mapData.PathWaypoints.Add(new WaypointCell(col, row));
                RefreshWaypointList();
                WaypointList.SelectedIndex = _mapData.PathWaypoints.Count + 1; // +2 offset, last waypoint
                RebuildMapScene();
                GridStatusText.Text = $"Added waypoint @ ({col}, {row}). Use ↑↓ to reorder.";
                break;
        }
    }

    private void HandleRightClick(Avalonia.Point pos)
    {
        var pick = EditorView.PickClosestAt(pos.X, pos.Y);
        if (pick == null) return;

        var worldPos = pick.WorldPosition;
        var grid = WorldToCell(worldPos);
        if (grid == null) return;

        var (col, row) = grid.Value;

        if (IsStartCell(col, row))
        {
            _mapData.StartCell = null;
            RefreshWaypointList();
            RebuildMapScene();
            GridStatusText.Text = $"Removed START @ ({col}, {row})";
            return;
        }
        if (IsEndCell(col, row))
        {
            _mapData.EndCell = null;
            RefreshWaypointList();
            RebuildMapScene();
            GridStatusText.Text = $"Removed END @ ({col}, {row})";
            return;
        }
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

    private bool IsStartCell(int col, int row)
    {
        return _mapData.StartCell?.Col == col && _mapData.StartCell?.Row == row;
    }

    private bool IsEndCell(int col, int row)
    {
        return _mapData.EndCell?.Col == col && _mapData.EndCell?.Row == row;
    }

    /// <summary>Build the full ordered path: StartCell → waypoints → EndCell.</summary>
    private List<WaypointCell> BuildFullPathList()
    {
        var list = new List<WaypointCell>();
        if (_mapData.StartCell != null) list.Add(_mapData.StartCell);
        list.AddRange(_mapData.PathWaypoints);
        if (_mapData.EndCell != null) list.Add(_mapData.EndCell);
        return list;
    }

    private bool IsPathCell(int col, int row)
    {
        var wps = BuildFullPathList();
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

    /// <summary>Finds an intermediate waypoint at the given cell (excludes start/end).</summary>
    private int FindWaypointAt(int col, int row)
    {
        return _mapData.PathWaypoints.FindIndex(w => w.Col == col && w.Row == row);
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
        WaypointList.SelectedIndex = -1;
        WaypointList.Items.Clear();

        // Show start/end status at top
        var startStr = _mapData.StartCell != null
            ? $"START: ({_mapData.StartCell.Col}, {_mapData.StartCell.Row})"
            : "START: (not set)";
        var endStr = _mapData.EndCell != null
            ? $"END: ({_mapData.EndCell.Col}, {_mapData.EndCell.Row})"
            : "END: (not set)";
        WaypointList.Items.Add(startStr);
        WaypointList.Items.Add(endStr);

        // Show intermediate waypoints
        for (int i = 0; i < _mapData.PathWaypoints.Count; i++)
        {
            var wp = _mapData.PathWaypoints[i];
            WaypointList.Items.Add($"  {i}: ({wp.Col}, {wp.Row})");
        }
        WaypointCount.Text = $"({_mapData.PathWaypoints.Count})";
    }

    private void OnDeleteWaypoint(object? sender, RoutedEventArgs e)
    {
        var idx = WaypointList.SelectedIndex - 2; // skip start/end header rows
        if (idx >= 0 && idx < _mapData.PathWaypoints.Count)
        {
            _mapData.PathWaypoints.RemoveAt(idx);
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

    private void OnMoveWaypointUp(object? sender, RoutedEventArgs e)
    {
        var idx = WaypointList.SelectedIndex - 2; // skip start/end header rows
        if (idx <= 0 || idx >= _mapData.PathWaypoints.Count) return;

        var wps = _mapData.PathWaypoints;
        (wps[idx - 1], wps[idx]) = (wps[idx], wps[idx - 1]);

        RefreshWaypointList();
        RebuildMapScene();
        WaypointList.SelectedIndex = idx + 1; // +2 offset, -1 for up = +1
        GridStatusText.Text = $"Moved waypoint {idx} up → position {idx - 1}";
    }

    private void OnMoveWaypointDown(object? sender, RoutedEventArgs e)
    {
        var idx = WaypointList.SelectedIndex - 2; // skip start/end header rows
        if (idx < 0 || idx >= _mapData.PathWaypoints.Count - 1) return;

        var wps = _mapData.PathWaypoints;
        (wps[idx], wps[idx + 1]) = (wps[idx + 1], wps[idx]);

        RefreshWaypointList();
        RebuildMapScene();
        WaypointList.SelectedIndex = idx + 3; // +2 offset, +1 for down = +3
        GridStatusText.Text = $"Moved waypoint {idx} down → position {idx + 1}";
    }

    // ==================== Wave Management ====================

    private bool _refreshingWaves;
    private bool _refreshingEntry;

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

        _refreshingEntry = true;
        try
        {
            EntryList.SelectedIndex = -1;
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

                // Sync entry edit controls to selected entry
                SyncEntryControls();
            }
            WaveEditPanel.IsVisible = WaveList.SelectedIndex >= 0;
        }
        finally
        {
            _refreshingEntry = false;
        }
    }

    private void SyncEntryControls()
    {
        if (WaveList.SelectedIndex < 0) return;
        var wave = _mapData.Waves[WaveList.SelectedIndex];
        if (EntryList.SelectedIndex >= 0 && EntryList.SelectedIndex < wave.Entries.Count)
        {
            // Prevent cascading ValueChanged/SelectionChanged events during sync
            _refreshingEntry = true;
            try
            {
                var entry = wave.Entries[EntryList.SelectedIndex];
                EntryCountInput.Value = entry.Count;
                EntryIntervalInput.Value = (decimal)entry.SpawnInterval;
                EnemyTypeCombo.SelectedItem = entry.EnemyType;
            }
            finally
            {
                _refreshingEntry = false;
            }
        }
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

    private void OnEntryListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingEntry) return;
        SyncEntryControls();
    }

    private void OnEntryCountChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_refreshingEntry) return;
        if (WaveList.SelectedIndex < 0) return;
        var wave = _mapData.Waves[WaveList.SelectedIndex];
        if (EntryList.SelectedIndex >= 0 && EntryList.SelectedIndex < wave.Entries.Count)
        {
            wave.Entries[EntryList.SelectedIndex] = wave.Entries[EntryList.SelectedIndex] with { Count = (int)e.NewValue };
            RefreshEntryList();
            RefreshWaveList();
        }
    }

    private void OnEntryIntervalChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_refreshingEntry) return;
        if (WaveList.SelectedIndex < 0) return;
        var wave = _mapData.Waves[WaveList.SelectedIndex];
        if (EntryList.SelectedIndex >= 0 && EntryList.SelectedIndex < wave.Entries.Count)
        {
            wave.Entries[EntryList.SelectedIndex] = wave.Entries[EntryList.SelectedIndex] with { SpawnInterval = (float)e.NewValue };
            RefreshEntryList();
            RefreshWaveList();
        }
    }

    private void OnEntryEnemyTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingEntry) return;
        if (WaveList.SelectedIndex < 0) return;
        var enemyType = (string)(EnemyTypeCombo.SelectedItem ?? "Basic");
        var wave = _mapData.Waves[WaveList.SelectedIndex];
        if (EntryList.SelectedIndex >= 0 && EntryList.SelectedIndex < wave.Entries.Count)
        {
            wave.Entries[EntryList.SelectedIndex] = wave.Entries[EntryList.SelectedIndex] with { EnemyType = enemyType };
            RefreshEntryList();
            RefreshWaveList();
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

    /// <summary>Validates the map and returns an error message, or null if complete.</summary>
    private string? ValidateMap()
    {
        if (_mapData.StartCell == null) return "Missing START point. Select Start mode and click a cell.";
        if (_mapData.EndCell == null) return "Missing END point. Select End mode and click a cell.";
        if (_mapData.Waves.Count == 0) return "Add at least one wave.";
        return null;
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
        GridStatusText.Text = "New map created. Set START, END, and waypoints to define the path.";
    }

    private void OnSaveMap(object? sender, RoutedEventArgs e)
    {
        var error = ValidateMap();
        if (error != null)
        {
            GridStatusText.Text = $"Cannot save: {error}";
            return;
        }

        if (_currentFilePath == null)
        {
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
        var error = ValidateMap();
        if (error != null)
        {
            GridStatusText.Text = $"Cannot save: {error}";
            return;
        }

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
        return $"map_{DateTime.Now:yyyyMMdd_HHmmss}";
    }

    // ==================== Test Map ====================

    private void OnTestMap(object? sender, RoutedEventArgs e)
    {
        var error = ValidateMap();
        if (error != null)
        {
            GridStatusText.Text = $"Cannot test: {error}";
            return;
        }
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
