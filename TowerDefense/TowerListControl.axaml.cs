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

public partial class TowerListControl : UserControl
{
    // ==================== State ====================
    private string _towersFilePath = string.Empty;
    private TowerData _previewTower = new();
    private int _selectedIndex = -1;
    private readonly List<Button> _towerCards = new();
    private bool _sceneReady;

    // ==================== Scene Cache ====================
    private Node? _groundNode;
    private Node? _towerNode;
    private PlaneGeometry? _planeGeo;
    private BoxGeometry? _boxGeo;
    private CylinderGeometry? _cylinderGeo;
    private SphereGeometry? _sphereGeo;
    private Material? _groundMat;

    private float _rotationTimer;

    // ==================== Callbacks ====================
    public Action? OnBack { get; set; }
    /// <summary>Fired when user clicks Edit. Args: (towerData, filePath)</summary>
    public Action<TowerData, string>? OnEditTower { get; set; }

    public TowerListControl()
    {
        InitializeComponent();
    }

    // ==================== Initialization ====================

    public void Initialize(string towersFilePath)
    {
        _towersFilePath = towersFilePath;
        EnsureTowersFile();
        BuildTowerGrid();
    }

    /// <summary>Refresh the tower grid (called when returning from editor).</summary>
    public void Refresh()
    {
        BuildTowerGrid();
    }

    private List<TowerData> LoadAllTowers() => TowerData.LoadListFromFile(_towersFilePath);

    private void EnsureTowersFile()
    {
        var dir = Path.GetDirectoryName(_towersFilePath);
        if (dir != null) Directory.CreateDirectory(dir);
    }

    // ==================== Tower Card Grid ====================

    private void BuildTowerGrid()
    {
        TowerCardGrid.Children.Clear();
        _towerCards.Clear();

        var towers = LoadAllTowers();
        for (int i = 0; i < towers.Count; i++)
        {
            var t = towers[i];
            var color = t.GetColor();
            var card = new Button
            {
                Width = 120,
                Height = 90,
                Margin = new Avalonia.Thickness(6),
                CornerRadius = new Avalonia.CornerRadius(8),
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                Background = Brush.Parse("#CC2d3a5a"),
                BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(color.R, color.G, color.B)),
                BorderThickness = new Avalonia.Thickness(0),
                Content = t.Name,
                Tag = i,
            };
            var idx = i; // capture for lambda
            card.Click += (_, _) => SelectTowerCard(idx);
            _towerCards.Add(card);
            TowerCardGrid.Children.Add(card);
        }

        _selectedIndex = -1;
        UpdateTowerCardStyles();
        ClearDetails();

        ListStatus.Text = towers.Count == 0
            ? "No towers yet — create a new one!"
            : $"{towers.Count} towers available";
    }

    private void SelectTowerCard(int index)
    {
        var towers = LoadAllTowers();
        if (index < 0 || index >= towers.Count) return;

        _selectedIndex = index;
        _previewTower = towers[index];
        UpdateTowerCardStyles();

        // Update 3D preview
        RebuildPreviewScene();

        // Update detail panel
        var t = _previewTower;
        DetailName.Text = t.Name;
        DetailCost.Text = Loc.Get("TowerList.Cost", t.Cost);
        DetailDamage.Text = Loc.Get("TowerList.Damage", t.Damage.ToString("F0"));
        DetailRange.Text = Loc.Get("TowerList.Range", t.Range.ToString("F1"));
        DetailFireRate.Text = t.FireRate > 0
            ? Loc.Get("TowerList.FireRate", t.FireRate.ToString("F1"))
            : Loc.Get("TowerList.FireRateContinuous");
        DetailProjSpeed.Text = Loc.Get("TowerList.ProjectileSpeed", t.ProjectileSpeed.ToString("F0"));
        DetailShapes.Text = Loc.Get("TowerList.Shapes", t.Shapes.Count);

        // Special effects summary
        var specials = new List<string>();
        if (t.SplashRadius > 0) specials.Add($"Splash {t.SplashRadius:F1}");
        if (t.SlowAmount > 0) specials.Add($"Slow {t.SlowAmount * 100:F0}%");
        if (t.MultiShotCount > 1) specials.Add($"Multi {t.MultiShotCount}");
        if (t.CritChance > 0) specials.Add($"Crit {t.CritChance * 100:F0}%");
        if (t.DotDamage > 0) specials.Add($"DoT {t.DotDamage:F0}/{t.DotDuration:F1}s");
        if (t.AoeRadius > 0) specials.Add($"AoE {t.AoeRadius:F1}");
        DetailSpecial.Text = specials.Count > 0
            ? $"Special: {string.Join(", ", specials)}"
            : "";

        EditBtn.IsVisible = true;
        DeleteBtn.IsVisible = true;
    }

    private void UpdateTowerCardStyles()
    {
        for (int i = 0; i < _towerCards.Count; i++)
        {
            if (i == _selectedIndex)
            {
                _towerCards[i].BorderThickness = new Avalonia.Thickness(3);
                _towerCards[i].Background = Brush.Parse("#CC4a5a8a");
            }
            else
            {
                _towerCards[i].BorderThickness = new Avalonia.Thickness(0);
                _towerCards[i].Background = Brush.Parse("#CC2d3a5a");
            }
        }
    }

    private void ClearDetails()
    {
        DetailName.Text = Loc.Get("TowerList.SelectATower");
        DetailCost.Text = Loc.Get("TowerList.CostDash");
        DetailDamage.Text = Loc.Get("TowerList.DamageDash");
        DetailRange.Text = Loc.Get("TowerList.RangeDash");
        DetailFireRate.Text = Loc.Get("TowerList.FireRateDash");
        DetailProjSpeed.Text = Loc.Get("TowerList.ProjSpeedDash");
        DetailShapes.Text = Loc.Get("TowerList.ShapesDash");
        DetailSpecial.Text = "";
        EditBtn.IsVisible = false;
        DeleteBtn.IsVisible = false;
    }

    // ==================== Tower Actions ====================

    private void OnEditTowerClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0) return;
        OnEditTower?.Invoke(_previewTower, _towersFilePath);
    }

    private void OnDeleteTower(object? sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0) return;

        var towers = LoadAllTowers();
        if (towers.Count <= 1)
        {
            ListStatus.Text = "Cannot delete the last tower.";
            return;
        }

        var name = towers[_selectedIndex].Name;
        towers.RemoveAt(_selectedIndex);
        TowerData.SaveListToFile(_towersFilePath, towers);
        TowerDefinition.All.Remove(name);

        BuildTowerGrid();
        ListStatus.Text = $"Deleted: {name}";
    }

    private void OnNewTower(object? sender, RoutedEventArgs e)
    {
        var newTower = new TowerData
        {
            Name = "New Tower",
            Cost = 50,
            Damage = 15,
            Range = 3.5f,
            FireRate = 1.8f,
            ProjectileSpeed = 5f,
            Shapes =
            {
                new TowerShapeData { Type = "Box", ScaleX = 0.55f, ScaleY = 0.1f, ScaleZ = 0.55f, ColorR = 30, ColorG = 120, ColorB = 30 },
                new TowerShapeData { Type = "Cylinder", ScaleX = 0.3f, ScaleY = 0.4f, ScaleZ = 0.3f, ColorR = 50, ColorG = 205, ColorB = 50 },
            },
        };

        OnEditTower?.Invoke(newTower, _towersFilePath);
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        OnBack?.Invoke();
    }

    // ==================== 3D Preview Scene ====================

    private void OnPreviewSceneInit(object? sender, InitializedRoutedEventArgs e)
    {
        var view = (sender as Aura3DView)!;
        _groundNode = null;
        _towerNode = null;

        view.MainCamera.Position = new Vector3(0, 3.5f, 5);
        view.MainCamera.RotationDegrees = new Vector3(-35, 0, 0);
        view.Scene.Background = Texture.CreateFromColor(DrawingColor.FromArgb(255, 30, 30, 50));

        view.PipelineSettings.CsmShadowMapResolution = 2048;
        view.PipelineSettings.CsmCascadeCount = 4;
        view.PipelineSettings.CsmSplitLambda = 0.6f;

        _planeGeo = new PlaneGeometry();
        _boxGeo = new BoxGeometry();
        _cylinderGeo = new CylinderGeometry();
        _sphereGeo = new SphereGeometry();
        _groundMat = CreateMaterial(DrawingColor.FromArgb(255, 40, 40, 60));

        var dl = new DirectionalLight
        {
            RotationDegrees = new Vector3(-40, -20, 0),
            LightColor = DrawingColor.White,
            CastShadow = true,
            ShadowConfig = new DirectionalLightShadowMapConfig { Width = 20, Height = 20, NearPlane = 0.5f, FarPlane = 30, }
        };
        view.AddNode(dl);
        view.Scene.MainDirectionalLight = dl;

        var ambient = new DirectionalLight { RotationDegrees = new Vector3(20, 150, 0), LightColor = DrawingColor.FromArgb(255, 80, 80, 100) };
        view.AddNode(ambient);

        _sceneReady = true;
        RebuildPreviewScene();
    }

    private void OnPreviewSceneUpdated(object? sender, UpdateRoutedEventArgs args)
    {
        // Rotate tower slowly for a dynamic preview
        _rotationTimer += (float)(args.DeltaTime * 20.0);
        if (_towerNode != null)
            _towerNode.RotationDegrees = new Vector3(0, _rotationTimer % 360, 0);
        PreviewView.RequestNextFrameRendering();
    }

    private void RebuildPreviewScene()
    {
        if (!_sceneReady) return;
        var view = PreviewView;

        if (_towerNode != null)
        {
            view.Remove(_towerNode);
            _towerNode = null;
        }

        if (_groundNode == null)
        {
            var ground = new Mesh { Geometry = _planeGeo!, Material = _groundMat! };
            ground.Scale = new Vector3(8, 1, 8);
            ground.Position = new Vector3(0, -0.05f, 0);
            _groundNode = ground;
            view.AddNode(ground);
        }

        var tower = new Node { Name = "TowerPreview" };

        // Auto-stack shapes along Y axis (same logic as TowerEditorView)
        float currentY = 0;
        foreach (var s in _previewTower.Shapes)
        {
            var color = s.GetColor();
            var mat = CreateMaterial(color);

            Mesh mesh;
            if (s.Type == "Box") mesh = new Mesh { Geometry = _boxGeo!, Material = mat };
            else if (s.Type == "Sphere") mesh = new Mesh { Geometry = _sphereGeo!, Material = mat };
            else mesh = new Mesh { Geometry = _cylinderGeo!, Material = mat };

            mesh.Scale = new Vector3(s.ScaleX, s.ScaleY, s.ScaleZ);

            float y = currentY + s.ScaleY / 2 + s.OffsetY;
            mesh.Position = new Vector3(s.OffsetX, y, s.OffsetZ);
            mesh.RotationDegrees = new Vector3(0, s.RotationY, 0);

            currentY += s.ScaleY + s.OffsetY;

            tower.AddChild(mesh, AttachToParentRule.KeepLocal);
        }

        _towerNode = tower;
        view.AddNode(tower);
    }

    // ==================== Helpers ====================

    private static Material CreateMaterial(DrawingColor color) => new()
    {
        BlendMode = BlendMode.Opaque, DoubleSided = true,
        Channels = { new() { Name = "BaseColor", Texture = Texture.CreateFromColor(color) } }
    };
}
