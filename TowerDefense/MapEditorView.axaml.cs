using Aura3D.Avalonia;
using Aura3D.Core;
using Aura3D.Core.Geometries;
using Aura3D.Core.Nodes;
using Aura3D.Core.Resources;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Numerics;
using DrawingColor = System.Drawing.Color;

namespace TowerDefense;

public enum PlacementMode { Start, Waypoint, End }

public partial class MapEditorView : UserControl
{
    private enum SelectedElement { None, Start, End, Waypoint }

    // ==================== State ====================
    private MapData _mapData = MapData.CreateDefault();
    private string? _currentFilePath;
    private PlacementMode _placementMode = PlacementMode.Waypoint;
    private int _selectedWaypointIndex = -1;
    private SelectedElement _selectedElement;

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

    // ==================== Callbacks ====================
    public Action? OnBack { get; set; }
    public Action<MapData>? OnPlayMap { get; set; }

    // ==================== Initialization ====================

    public MapEditorView()
    {
        InitializeComponent();
        RefreshEnemyTypeCombo();
        UpdatePlacementModeButtons();
    }

    /// <summary>
    /// Load a map for editing. Called when user selects a map from the list or creates a new one.
    /// </summary>
    public void LoadForEdit(MapData map, string filePath)
    {
        _mapData = map;
        _currentFilePath = filePath;
        _selectedWaypointIndex = -1;
        RefreshEnemyTypeCombo();
        SyncUIFromMap();
        if (_sceneReady)
            RebuildMapScene();
    }

    public MapData GetCurrentMap() => _mapData;

    // ==================== Map Loading ====================

    private void SyncUIFromMap()
    {
        GridColsInput.Text = _mapData.GridCols.ToString();
        GridRowsInput.Text = _mapData.GridRows.ToString();
        RefreshWaveList();
        RefreshEntryList();
        HideWaypointInfo();
    }

    // ==================== Scene Initialization ====================

    private void OnEditorSceneInit(object? sender, InitializedRoutedEventArgs e)
    {
        var view = (sender as Aura3DView)!;

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
            var isSelected = i == _selectedWaypointIndex;
            if (isSelected)
                color = DrawingColor.FromArgb(255,
                    (byte)Math.Min(255, color.R + 80),
                    (byte)Math.Min(255, color.G + 80),
                    (byte)Math.Min(255, color.B + 80));

            var node = new Node { Name = $"Waypoint_{i}" };
            var disc = new Mesh { Geometry = _boxGeo!, Material = CreateMaterial(color) };
            disc.Scale = isSelected
                ? new Vector3(0.65f, 0.04f, 0.65f)
                : new Vector3(0.5f, 0.03f, 0.5f);
            node.AddChild(disc, AttachToParentRule.KeepLocal);
            var sphere = new Mesh { Geometry = _sphereGeo!, Material = CreateMaterial(color) };
            sphere.Scale = isSelected
                ? new Vector3(0.26f)
                : new Vector3(0.20f);
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
        if (_mapData.StartCell != null)
        {
            var s = _mapData.StartCell;
            _entryMarkerNode = new Mesh
            {
                Geometry = _boxGeo!,
                Material = CreateMaterial(DrawingColor.LimeGreen),
                Name = "EntryMarker",
            };
            _entryMarkerNode.Scale = new Vector3(0.2f, 0.08f, 0.8f);
            _entryMarkerNode.Position = CellToWorld(s.Col, s.Row) + new Vector3(-0.5f, 0.06f, 0);
            view.AddNode(_entryMarkerNode);
        }
        if (_mapData.EndCell != null)
        {
            var e = _mapData.EndCell;
            _exitMarkerNode = new Mesh
            {
                Geometry = _boxGeo!,
                Material = CreateMaterial(DrawingColor.Red),
                Name = "ExitMarker",
            };
            _exitMarkerNode.Scale = new Vector3(0.2f, 0.08f, 0.8f);
            _exitMarkerNode.Position = CellToWorld(e.Col, e.Row) + new Vector3(0.5f, 0.06f, 0);
            view.AddNode(_exitMarkerNode);
        }
    }

    // ==================== Placement Mode ====================

    private void OnSetStartMode(object? sender, RoutedEventArgs e)
    {
        _placementMode = PlacementMode.Start;
        UpdatePlacementModeButtons();
        GridStatusText.Text = Loc.Get("MapEditor.ModeStart");
    }
    private void OnSetWaypointMode(object? sender, RoutedEventArgs e)
    {
        _placementMode = PlacementMode.Waypoint;
        UpdatePlacementModeButtons();
        GridStatusText.Text = Loc.Get("MapEditor.ModeWaypoint");
    }
    private void OnSetEndMode(object? sender, RoutedEventArgs e)
    {
        _placementMode = PlacementMode.End;
        UpdatePlacementModeButtons();
        GridStatusText.Text = Loc.Get("MapEditor.ModeEnd");
    }

    private void UpdatePlacementModeButtons()
    {
        var dimBg = "#CC3a3a4a";
        var activeBorder = new Avalonia.Thickness(2);
        SetStartBtn.BorderThickness = _placementMode == PlacementMode.Start ? activeBorder : default;
        SetStartBtn.BorderBrush = _placementMode == PlacementMode.Start ? Brushes.LimeGreen : null;
        AddWaypointBtn.BorderThickness = _placementMode == PlacementMode.Waypoint ? activeBorder : default;
        AddWaypointBtn.BorderBrush = _placementMode == PlacementMode.Waypoint ? Brushes.CornflowerBlue : null;
        SetEndBtn.BorderThickness = _placementMode == PlacementMode.End ? activeBorder : default;
        SetEndBtn.BorderBrush = _placementMode == PlacementMode.End ? Brushes.Red : null;
        SetStartBtn.Background = Brush.Parse(_placementMode == PlacementMode.Start ? "#CC2d6a2d" : dimBg);
        AddWaypointBtn.Background = Brush.Parse(_placementMode == PlacementMode.Waypoint ? "#CC4a6a8a" : dimBg);
        SetEndBtn.Background = Brush.Parse(_placementMode == PlacementMode.End ? "#CC6a2a2a" : dimBg);
    }

    // ==================== Waypoint Info Panel ====================

    private void ShowWaypointInfo(int index, int col, int row)
    {
        _selectedElement = SelectedElement.Waypoint;
        _selectedWaypointIndex = index;
        WaypointInfoPanel.IsVisible = true;

        WaypointTypeBadge.Background = Brush.Parse("#CC4a6a8a");
        WaypointTypeText.Text = Loc.Get("MapEditor.Waypoint");
        WaypointCoordText.Text = $"({col}, {row})";

        DeleteWaypointBtn.Content = Loc.Get("MapEditor.DeleteWaypoint");

        GridStatusText.Text = Loc.Get("MapEditor.SelectedWaypoint", index, col, row);
        if (_sceneReady) RebuildMapScene();
    }

    private void ShowStartInfo(int col, int row)
    {
        _selectedElement = SelectedElement.Start;
        _selectedWaypointIndex = -1;
        WaypointInfoPanel.IsVisible = true;

        WaypointTypeBadge.Background = Brush.Parse("#CC2d6a2d");
        WaypointTypeText.Text = Loc.Get("MapEditor.Start");
        WaypointCoordText.Text = $"({col}, {row})";

        DeleteWaypointBtn.Content = Loc.Get("MapEditor.DeleteStart");

        GridStatusText.Text = Loc.Get("MapEditor.StartAt", col, row);
    }

    private void ShowEndInfo(int col, int row)
    {
        _selectedElement = SelectedElement.End;
        _selectedWaypointIndex = -1;
        WaypointInfoPanel.IsVisible = true;

        WaypointTypeBadge.Background = Brush.Parse("#CC4a2a2a");
        WaypointTypeText.Text = Loc.Get("MapEditor.End");
        WaypointCoordText.Text = $"({col}, {row})";

        DeleteWaypointBtn.Content = Loc.Get("MapEditor.DeleteEnd");

        GridStatusText.Text = Loc.Get("MapEditor.EndAt", col, row);
    }

    private void HideWaypointInfo()
    {
        _selectedElement = SelectedElement.None;
        _selectedWaypointIndex = -1;
        WaypointInfoPanel.IsVisible = false;
        if (_sceneReady) RebuildMapScene();
    }

    private void OnDeleteSelectedWaypoint(object? sender, RoutedEventArgs e)
    {
        switch (_selectedElement)
        {
            case SelectedElement.Waypoint:
                if (_selectedWaypointIndex >= 0 && _selectedWaypointIndex < _mapData.PathWaypoints.Count)
                {
                    var wp = _mapData.PathWaypoints[_selectedWaypointIndex];
                    _mapData.PathWaypoints.RemoveAt(_selectedWaypointIndex);
                    GridStatusText.Text = Loc.Get("MapEditor.RemovedWaypoint", wp.Col, wp.Row);
                }
                break;
            case SelectedElement.Start:
                if (_mapData.StartCell != null)
                {
                    var s = _mapData.StartCell;
                    _mapData.StartCell = null;
                    GridStatusText.Text = Loc.Get("MapEditor.RemovedStart", s.Col, s.Row);
                }
                break;
            case SelectedElement.End:
                if (_mapData.EndCell != null)
                {
                    var end = _mapData.EndCell;
                    _mapData.EndCell = null;
                    GridStatusText.Text = Loc.Get("MapEditor.RemovedEnd", end.Col, end.Row);
                }
                break;
        }
        HideWaypointInfo();
        if (_sceneReady) RebuildMapScene();
    }

    // ==================== Pointer Input ====================

    private void OnEditorObjectPicked(object? sender, ObjectPickedEventArgs args)
    {
        if (!_sceneReady) return;
        var nodeName = args.Node.Name ?? "";

        // Clicked on a waypoint marker → select it
        if (nodeName.StartsWith("Waypoint_"))
        {
            if (int.TryParse(nodeName.Replace("Waypoint_", ""), out int index)
                && index < _mapData.PathWaypoints.Count)
            {
                var wp = _mapData.PathWaypoints[index];
                ShowWaypointInfo(index, wp.Col, wp.Row);
            }
            return;
        }

        // Clicked on entry/exit markers → show info
        if (nodeName == "EntryMarker")
        {
            var s = _mapData.StartCell;
            if (s != null) ShowStartInfo(s.Col, s.Row);
            else GridStatusText.Text = Loc.Get("MapEditor.NoStartSet");
            return;
        }
        if (nodeName == "ExitMarker")
        {
            var e = _mapData.EndCell;
            if (e != null) ShowEndInfo(e.Col, e.Row);
            else GridStatusText.Text = Loc.Get("MapEditor.NoEndSet");
            return;
        }

        var worldPos = args.WorldPosition;
        var grid = WorldToCell(worldPos);
        if (grid == null) return;
        var (col, row) = grid.Value;

        // Clicked on a grid cell with existing waypoint → select it
        int existingIdx = FindWaypointAt(col, row);
        if (existingIdx >= 0)
        {
            ShowWaypointInfo(existingIdx, col, row);
            return;
        }
        // Clicked on start/end cell → show info
        if (IsStartCell(col, row))
        {
            ShowStartInfo(col, row);
            return;
        }
        if (IsEndCell(col, row))
        {
            ShowEndInfo(col, row);
            return;
        }

        // Placement mode: add new element
        switch (_placementMode)
        {
            case PlacementMode.Start:
                _mapData.StartCell = new WaypointCell(col, row);
                RebuildMapScene();
                ShowStartInfo(col, row);
                break;
            case PlacementMode.End:
                _mapData.EndCell = new WaypointCell(col, row);
                RebuildMapScene();
                ShowEndInfo(col, row);
                break;
            default:
                _mapData.PathWaypoints.Add(new WaypointCell(col, row));
                int newIndex = _mapData.PathWaypoints.Count - 1;
                RebuildMapScene();
                ShowWaypointInfo(newIndex, col, row);
                break;
        }
    }

    // ==================== Path Calculation ====================

    private bool IsStartCell(int col, int row) => _mapData.StartCell?.Col == col && _mapData.StartCell?.Row == row;
    private bool IsEndCell(int col, int row) => _mapData.EndCell?.Col == col && _mapData.EndCell?.Row == row;

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
            var from = wps[w - 1]; var to = wps[w];
            int stepX = Math.Sign(to.Col - from.Col), stepZ = Math.Sign(to.Row - from.Row);
            int cx = from.Col, cz = from.Row;
            while (cx != to.Col) { cx += stepX; if (cx == col && cz == row) return true; }
            while (cz != to.Row) { cz += stepZ; if (cx == col && cz == row) return true; }
        }
        return wps.Count > 0 && wps.Any(w => w.Col == col && w.Row == row);
    }

    private int FindWaypointAt(int col, int row) => _mapData.PathWaypoints.FindIndex(w => w.Col == col && w.Row == row);

    // ==================== Grid Helpers ====================

    private Vector3 CellToWorld(int col, int row) => new(col + 0.5f, 0, row + 0.5f);

    private (int Col, int Row)? WorldToCell(Vector3 pos)
    {
        int col = (int)MathF.Floor(pos.X), row = (int)MathF.Floor(pos.Z);
        if (col < 0 || col >= _mapData.GridCols || row < 0 || row >= _mapData.GridRows) return null;
        return (col, row);
    }

    // ==================== Wave Management ====================

    private bool _refreshingWaves;
    private bool _refreshingEntry;

    private void RefreshWaveList()
    {
        _refreshingWaves = true;
        try
        {
            var savedIndex = WaveList.SelectedIndex;
            WaveList.Items.Clear();
            for (int i = 0; i < _mapData.Waves.Count; i++)
            {
                var wave = _mapData.Waves[i];
                WaveList.Items.Add(Loc.Get("MapEditor.WaveEntryFormat", i + 1, wave.Entries.Count, wave.DelayBeforeWave));
            }
            WaveCountText.Text = $"({_mapData.Waves.Count})";
            if (savedIndex >= 0 && savedIndex < _mapData.Waves.Count) WaveList.SelectedIndex = savedIndex;
            else if (_mapData.Waves.Count > 0) WaveList.SelectedIndex = 0;
        }
        finally { _refreshingWaves = false; }
    }

    private void RefreshEntryList()
    {
        if (_refreshingWaves) return;
        _refreshingEntry = true;
        try
        {
            var savedIndex = EntryList.SelectedIndex;
            EntryList.Items.Clear();
            if (WaveList.SelectedIndex < 0 && _mapData.Waves.Count > 0) WaveList.SelectedIndex = 0;
            if (WaveList.SelectedIndex >= 0 && WaveList.SelectedIndex < _mapData.Waves.Count)
            {
                var wave = _mapData.Waves[WaveList.SelectedIndex];
                WaveDelayInput.Text = wave.DelayBeforeWave.ToString("F1");
                foreach (var entry in wave.Entries)
                    EntryList.Items.Add(Loc.Get("MapEditor.EntryFormat", entry.EnemyType, entry.Count, entry.SpawnInterval));
                if (savedIndex >= 0 && savedIndex < wave.Entries.Count) EntryList.SelectedIndex = savedIndex;
                else if (wave.Entries.Count > 0) EntryList.SelectedIndex = 0;
                SyncEntryControls();
            }
            WaveEditPanel.IsVisible = WaveList.SelectedIndex >= 0;
        }
        finally { _refreshingEntry = false; }
    }

    private void SyncEntryControls()
    {
        if (WaveList.SelectedIndex < 0) return;
        var wave = _mapData.Waves[WaveList.SelectedIndex];
        if (EntryList.SelectedIndex >= 0 && EntryList.SelectedIndex < wave.Entries.Count)
        {
            _refreshingEntry = true;
            try
            {
                var entry = wave.Entries[EntryList.SelectedIndex];
                EntryCountInput.Text = entry.Count.ToString();
                EntryIntervalInput.Text = entry.SpawnInterval.ToString("F1");
                EnemyTypeCombo.SelectedItem = entry.EnemyType;
            }
            finally { _refreshingEntry = false; }
        }
    }

    private void OnWaveListSelectionChanged(object? sender, SelectionChangedEventArgs e) => RefreshEntryList();

    private void OnAddWave(object? sender, RoutedEventArgs e)
    {
        _mapData.Waves.Add(new WaveConfigData { DelayBeforeWave = 5f, Entries = { new WaveEntryData("Basic", 5, 1.0f) } });
        RefreshWaveList(); WaveList.SelectedIndex = _mapData.Waves.Count - 1; RefreshEntryList();
    }

    private void OnDeleteWave(object? sender, RoutedEventArgs e)
    {
        if (WaveList.SelectedIndex >= 0 && _mapData.Waves.Count > 1)
        { _mapData.Waves.RemoveAt(WaveList.SelectedIndex); RefreshWaveList(); RefreshEntryList(); }
    }

    private void OnWaveDelayKeyDown(object? sender, KeyEventArgs e) { if (e.Key == Key.Enter) ApplyWaveDelay(); }
    private void OnWaveDelayUp(object? sender, RoutedEventArgs e) => AdjustWaveDelay(+0.5f);
    private void OnWaveDelayDown(object? sender, RoutedEventArgs e) => AdjustWaveDelay(-0.5f);
    private void AdjustWaveDelay(float delta) { if (float.TryParse(WaveDelayInput.Text, out float v)) { v = Math.Clamp(v + delta, 0, 60); WaveDelayInput.Text = v.ToString("F1"); } ApplyWaveDelay(); }
    private void ApplyWaveDelay()
    {
        if (WaveList.SelectedIndex < 0 || WaveList.SelectedIndex >= _mapData.Waves.Count) return;
        if (float.TryParse(WaveDelayInput.Text, out float v))
        { v = Math.Clamp(v, 0, 60); WaveDelayInput.Text = v.ToString("F1"); _mapData.Waves[WaveList.SelectedIndex].DelayBeforeWave = v; RefreshWaveList(); }
    }

    private void OnAddEntry(object? sender, RoutedEventArgs e)
    {
        if (WaveList.SelectedIndex < 0) return;
        var enemyType = (string)(EnemyTypeCombo.SelectedItem ?? "Basic");
        _mapData.Waves[WaveList.SelectedIndex].Entries.Add(new WaveEntryData(enemyType, 3, 1.0f));
        RefreshEntryList(); RefreshWaveList();
    }
    private void OnDeleteEntry(object? sender, RoutedEventArgs e)
    {
        if (WaveList.SelectedIndex < 0) return;
        var wave = _mapData.Waves[WaveList.SelectedIndex];
        if (EntryList.SelectedIndex >= 0 && EntryList.SelectedIndex < wave.Entries.Count)
        { wave.Entries.RemoveAt(EntryList.SelectedIndex); RefreshEntryList(); RefreshWaveList(); }
    }

    private void OnEntryListSelectionChanged(object? sender, SelectionChangedEventArgs e) { if (!_refreshingEntry) SyncEntryControls(); }

    private void OnEntryCountKeyDown(object? sender, KeyEventArgs e) { if (e.Key == Key.Enter) ApplyEntryCount(); }
    private void OnEntryCountUp(object? sender, RoutedEventArgs e) => AdjustEntryCount(+1);
    private void OnEntryCountDown(object? sender, RoutedEventArgs e) => AdjustEntryCount(-1);
    private void AdjustEntryCount(int d) { if (_refreshingEntry) return; if (int.TryParse(EntryCountInput.Text, out int v)) { v = Math.Clamp(v + d, 1, 50); EntryCountInput.Text = v.ToString(); } ApplyEntryCount(); }
    private void ApplyEntryCount()
    {
        if (_refreshingEntry) return;
        if (WaveList.SelectedIndex < 0) return;
        var wave = _mapData.Waves[WaveList.SelectedIndex];
        if (EntryList.SelectedIndex >= 0 && EntryList.SelectedIndex < wave.Entries.Count)
        { if (int.TryParse(EntryCountInput.Text, out int v)) { v = Math.Clamp(v, 1, 50); EntryCountInput.Text = v.ToString(); wave.Entries[EntryList.SelectedIndex] = wave.Entries[EntryList.SelectedIndex] with { Count = v }; RefreshEntryList(); RefreshWaveList(); } }
    }

    private void OnEntryIntervalKeyDown(object? sender, KeyEventArgs e) { if (e.Key == Key.Enter) ApplyEntryInterval(); }
    private void OnEntryIntervalUp(object? sender, RoutedEventArgs e) => AdjustEntryInterval(+0.1f);
    private void OnEntryIntervalDown(object? sender, RoutedEventArgs e) => AdjustEntryInterval(-0.1f);
    private void AdjustEntryInterval(float d) { if (_refreshingEntry) return; if (float.TryParse(EntryIntervalInput.Text, out float v)) { v = Math.Clamp(v + d, 0.2f, 10f); EntryIntervalInput.Text = v.ToString("F1"); } ApplyEntryInterval(); }
    private void ApplyEntryInterval()
    {
        if (_refreshingEntry) return;
        if (WaveList.SelectedIndex < 0) return;
        var wave = _mapData.Waves[WaveList.SelectedIndex];
        if (EntryList.SelectedIndex >= 0 && EntryList.SelectedIndex < wave.Entries.Count)
        { if (float.TryParse(EntryIntervalInput.Text, out float v)) { v = Math.Clamp(v, 0.2f, 10f); EntryIntervalInput.Text = v.ToString("F1"); wave.Entries[EntryList.SelectedIndex] = wave.Entries[EntryList.SelectedIndex] with { SpawnInterval = v }; RefreshEntryList(); RefreshWaveList(); } }
    }

    private void OnEntryEnemyTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingEntry) return;
        if (WaveList.SelectedIndex < 0) return;
        var enemyType = (string)(EnemyTypeCombo.SelectedItem ?? "Basic");
        var wave = _mapData.Waves[WaveList.SelectedIndex];
        if (EntryList.SelectedIndex >= 0 && EntryList.SelectedIndex < wave.Entries.Count)
        { wave.Entries[EntryList.SelectedIndex] = wave.Entries[EntryList.SelectedIndex] with { EnemyType = enemyType }; RefreshEntryList(); RefreshWaveList(); }
    }

    // ==================== Grid Size ====================

    private void OnGridKeyDown(object? sender, KeyEventArgs e) { if (e.Key == Key.Enter) ApplyGridSize(); }
    private void OnGridColsUp(object? sender, RoutedEventArgs e) => AdjustGridSize(GridColsInput, +1);
    private void OnGridColsDown(object? sender, RoutedEventArgs e) => AdjustGridSize(GridColsInput, -1);
    private void OnGridRowsUp(object? sender, RoutedEventArgs e) => AdjustGridSize(GridRowsInput, +1);
    private void OnGridRowsDown(object? sender, RoutedEventArgs e) => AdjustGridSize(GridRowsInput, -1);

    private void AdjustGridSize(TextBox input, int delta)
    {
        if (!_sceneReady) return;
        if (int.TryParse(input.Text, out int v)) { v = Math.Clamp(v + delta, 5, 50); input.Text = v.ToString(); }
        ApplyGridSize();
    }

    private void ApplyGridSize()
    {
        if (!int.TryParse(GridColsInput.Text, out int cols) || cols < 5) cols = 5;
        if (cols > 50) cols = 50;
        if (!int.TryParse(GridRowsInput.Text, out int rows) || rows < 5) rows = 5;
        if (rows > 50) rows = 50;
        GridColsInput.Text = cols.ToString(); GridRowsInput.Text = rows.ToString();
        if (cols == _mapData.GridCols && rows == _mapData.GridRows) return;
        _mapData.GridCols = cols; _mapData.GridRows = rows;
        _mapData.PathWaypoints.RemoveAll(w => w.Col >= cols || w.Row >= rows);
        if (_selectedWaypointIndex >= _mapData.PathWaypoints.Count)
            _selectedWaypointIndex = -1;
        HideWaypointInfo();
        RebuildMapScene();
    }

    // ==================== File Operations ====================

    private string? ValidateMap()
    {
        if (_mapData.StartCell == null) return Loc.Get("MapEditor.MissingStart");
        if (_mapData.EndCell == null) return Loc.Get("MapEditor.MissingEnd");
        if (_mapData.Waves.Count == 0) return Loc.Get("MapEditor.NeedWave");
        return null;
    }

    private void OnSaveMap(object? sender, RoutedEventArgs e)
    {
        var error = ValidateMap();
        if (error != null) { GridStatusText.Text = Loc.Get("MapEditor.CannotSave", error); return; }
        if (_currentFilePath == null)
        {
            GridStatusText.Text = Loc.Get("MapEditor.NoFilePath");
            return;
        }
        _mapData.SaveToFile(_currentFilePath);
        GridStatusText.Text = Loc.Get("MapEditor.Saved", _currentFilePath);
    }

    // ==================== Test Map ====================

    private void OnTestMap(object? sender, RoutedEventArgs e)
    {
        var error = ValidateMap();
        if (error != null) { GridStatusText.Text = Loc.Get("MapEditor.CannotTest", error); return; }
        OnPlayMap?.Invoke(_mapData);
    }

    // ==================== Navigation ====================

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        OnBack?.Invoke();
    }

    // ==================== Helpers ====================

    private static DrawingColor GetWaypointColor(int index, int total)
    {
        if (total <= 1) return DrawingColor.LimeGreen;
        if (index == 0) return DrawingColor.LimeGreen;
        if (index == total - 1) return DrawingColor.Red;
        float t = (float)index / (total - 1);
        return DrawingColor.FromArgb(255, (byte)(255 * t), (byte)(255 * (1 - Math.Abs(t - 0.5f) * 2)), (byte)(50 * (1 - t)));
    }

    private void RefreshEnemyTypeCombo()
    {
        var names = EnemyDefinition.All.Keys.OrderBy(n => n).ToList();
        EnemyTypeCombo.ItemsSource = names;
        if (names.Count > 0 && EnemyTypeCombo.SelectedIndex < 0)
            EnemyTypeCombo.SelectedIndex = 0;
    }

    private static Material CreateMaterial(DrawingColor color) => new()
    {
        BlendMode = BlendMode.Opaque,
        DoubleSided = true,
        Channels = { new() { Name = "BaseColor", Texture = Texture.CreateFromColor(color) } }
    };
}
