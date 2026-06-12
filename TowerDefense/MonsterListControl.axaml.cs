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

public partial class MonsterListControl : UserControl
{
    // ==================== State ====================
    private string _enemiesFilePath = string.Empty;
    private EnemyData _previewEnemy = new();
    private int _selectedIndex = -1;
    private readonly List<Button> _monsterCards = new();
    private bool _sceneReady;

    // ==================== Scene Cache ====================
    private Node? _groundNode;
    private Node? _monsterNode;
    private PlaneGeometry? _planeGeo;
    private SphereGeometry? _sphereGeo;
    private Material? _groundMat;
    private Material? _monsterMat;

    private float _rotationTimer;

    // ==================== Callbacks ====================
    public Action? OnBack { get; set; }
    /// <summary>Fired when user clicks Edit. Args: (enemyData, filePath)</summary>
    public Action<EnemyData, string>? OnEditMonster { get; set; }

    public MonsterListControl()
    {
        InitializeComponent();
    }

    // ==================== Initialization ====================

    public void Initialize(string enemiesFilePath)
    {
        _enemiesFilePath = enemiesFilePath;
        EnsureEnemiesFile();
        BuildMonsterGrid();
    }

    /// <summary>Refresh the monster grid (called when returning from editor).</summary>
    public void Refresh()
    {
        BuildMonsterGrid();
    }

    private List<EnemyData> LoadAllEnemies() => EnemyData.LoadListFromFile(_enemiesFilePath);

    private void EnsureEnemiesFile()
    {
        var dir = Path.GetDirectoryName(_enemiesFilePath);
        if (dir != null) Directory.CreateDirectory(dir);
    }

    // ==================== Monster Card Grid ====================

    private void BuildMonsterGrid()
    {
        MonsterCardGrid.Children.Clear();
        _monsterCards.Clear();

        var enemies = LoadAllEnemies();
        for (int i = 0; i < enemies.Count; i++)
        {
            var en = enemies[i];
            var color = en.GetColor();
            var card = new Button
            {
                Width = 120,
                Height = 90,
                Margin = new Avalonia.Thickness(6),
                CornerRadius = new Avalonia.CornerRadius(8),
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                Background = Brush.Parse("#CC5a3a2d"),
                BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(color.R, color.G, color.B)),
                BorderThickness = new Avalonia.Thickness(0),
                Content = en.Name,
                Tag = i,
            };
            var idx = i; // capture for lambda
            card.Click += (_, _) => SelectMonsterCard(idx);
            _monsterCards.Add(card);
            MonsterCardGrid.Children.Add(card);
        }

        _selectedIndex = -1;
        UpdateMonsterCardStyles();
        ClearDetails();

        ListStatus.Text = enemies.Count == 0
            ? "No monsters yet — create a new one!"
            : $"{enemies.Count} monsters available";
    }

    private void SelectMonsterCard(int index)
    {
        var enemies = LoadAllEnemies();
        if (index < 0 || index >= enemies.Count) return;

        _selectedIndex = index;
        _previewEnemy = enemies[index];
        UpdateMonsterCardStyles();

        // Update 3D preview
        RebuildPreviewScene();

        // Update detail panel
        var en = _previewEnemy;
        DetailName.Text = en.Name;
        DetailColorSwatch.IsVisible = true;
        var col = en.GetColor();
        DetailColorSwatch.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(col.R, col.G, col.B));
        DetailHP.Text = Loc.Get("MonsterList.HP", $"{en.MaxHP:F0}");
        DetailSpeed.Text = Loc.Get("MonsterList.Speed", $"{en.Speed:F1}");
        DetailGold.Text = Loc.Get("MonsterList.Gold", en.GoldReward);
        DetailSize.Text = Loc.Get("MonsterList.Size", $"{en.Radius:F2}");

        EditBtn.IsVisible = true;
        DeleteBtn.IsVisible = true;
    }

    private void UpdateMonsterCardStyles()
    {
        for (int i = 0; i < _monsterCards.Count; i++)
        {
            if (i == _selectedIndex)
            {
                _monsterCards[i].BorderThickness = new Avalonia.Thickness(3);
                _monsterCards[i].Background = Brush.Parse("#CC7a5a3a");
            }
            else
            {
                _monsterCards[i].BorderThickness = new Avalonia.Thickness(0);
                _monsterCards[i].Background = Brush.Parse("#CC5a3a2d");
            }
        }
    }

    private void ClearDetails()
    {
        DetailName.Text = Loc.Get("MonsterList.SelectAMonster");
        DetailColorSwatch.IsVisible = false;
        DetailHP.Text = Loc.Get("MonsterList.HPDash");
        DetailSpeed.Text = Loc.Get("MonsterList.SpeedDash");
        DetailGold.Text = Loc.Get("MonsterList.GoldDash");
        DetailSize.Text = Loc.Get("MonsterList.SizeDash");
        EditBtn.IsVisible = false;
        DeleteBtn.IsVisible = false;
    }

    // ==================== Monster Actions ====================

    private void OnEditMonsterClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0) return;
        OnEditMonster?.Invoke(_previewEnemy, _enemiesFilePath);
    }

    private void OnDeleteMonster(object? sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0) return;

        var enemies = LoadAllEnemies();
        if (enemies.Count <= 1)
        {
            ListStatus.Text = "Cannot delete the last monster.";
            return;
        }

        var name = enemies[_selectedIndex].Name;
        enemies.RemoveAt(_selectedIndex);
        EnemyData.SaveListToFile(_enemiesFilePath, enemies);
        EnemyDefinition.All.Remove(name);

        BuildMonsterGrid();
        ListStatus.Text = $"Deleted: {name}";
    }

    private void OnNewMonster(object? sender, RoutedEventArgs e)
    {
        var newEnemy = new EnemyData
        {
            Name = "New Enemy",
            MaxHP = 100,
            Speed = 1.8f,
            GoldReward = 10,
            Radius = 0.25f,
            ColorR = 220, ColorG = 20, ColorB = 60,
        };

        OnEditMonster?.Invoke(newEnemy, _enemiesFilePath);
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
        _monsterNode = null;

        view.MainCamera.Position = new Vector3(0, 3.5f, 5);
        view.MainCamera.RotationDegrees = new Vector3(-35, 0, 0);
        view.Scene.Background = Texture.CreateFromColor(DrawingColor.FromArgb(255, 30, 30, 50));

        view.PipelineSettings.CsmShadowMapResolution = 2048;
        view.PipelineSettings.CsmCascadeCount = 4;
        view.PipelineSettings.CsmSplitLambda = 0.6f;

        _planeGeo = new PlaneGeometry();
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
        // Rotate monster slowly for a dynamic preview
        _rotationTimer += (float)(args.DeltaTime * 20.0);
        if (_monsterNode != null)
            _monsterNode.RotationDegrees = new Vector3(0, _rotationTimer % 360, 0);
        PreviewView.RequestNextFrameRendering();
    }

    private void RebuildPreviewScene()
    {
        if (!_sceneReady) return;
        var view = PreviewView;

        if (_monsterNode != null)
        {
            view.Remove(_monsterNode);
            _monsterNode = null;
        }

        if (_groundNode == null)
        {
            var ground = new Mesh { Geometry = _planeGeo!, Material = _groundMat! };
            ground.Scale = new Vector3(8, 1, 8);
            ground.Position = new Vector3(0, -0.05f, 0);
            _groundNode = ground;
            view.AddNode(ground);
        }

        var color = _previewEnemy.GetColor();
        _monsterMat = CreateMaterial(color);

        var monster = new Mesh
        {
            Geometry = _sphereGeo!,
            Material = _monsterMat,
        };
        monster.Scale = new Vector3(_previewEnemy.Radius * 2, _previewEnemy.Radius * 2, _previewEnemy.Radius * 2);
        monster.Position = new Vector3(0, _previewEnemy.Radius, 0);
        monster.Name = "MonsterPreview";
        _monsterNode = monster;
        view.AddNode(monster);
    }

    // ==================== Helpers ====================

    private static Material CreateMaterial(DrawingColor color) => new()
    {
        BlendMode = BlendMode.Opaque, DoubleSided = true,
        Channels = { new() { Name = "BaseColor", Texture = Texture.CreateFromColor(color) } }
    };
}
