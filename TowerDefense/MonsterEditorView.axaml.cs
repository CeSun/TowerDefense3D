using Aura3D.Avalonia;
using Aura3D.Core;
using Aura3D.Core.Geometries;
using Aura3D.Core.Nodes;
using Aura3D.Core.Resources;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Numerics;
using DrawingColor = System.Drawing.Color;

namespace TowerDefense;

public partial class MonsterEditorView : UserControl
{
    // ==================== State ====================
    private string _enemiesFilePath = string.Empty;
    private string _currentFileName = string.Empty;
    private bool _sceneReady;
    private float _rotationTimer;

    // ==================== Scene Cache ====================
    private Node? _groundNode;
    private Node? _monsterNode;
    private Node? _shadowLightNode;
    private Node? _ambientLightNode;
    private PlaneGeometry? _planeGeo;
    private SphereGeometry? _sphereGeo;
    private Material? _groundMat;
    private Material? _monsterMat;

    // ==================== Callbacks ====================
    public Action? OnBack { get; set; }

    public MonsterEditorView()
    {
        InitializeComponent();
    }

    // ==================== Initialization ====================

    public void Initialize(string enemiesFilePath)
    {
        _enemiesFilePath = enemiesFilePath;
        RefreshMonsterList();
    }

    // ==================== Monster List ====================

    private List<EnemyData> LoadAllEnemies() => EnemyData.LoadListFromFile(_enemiesFilePath);

    private void RefreshMonsterList()
    {
        var enemies = LoadAllEnemies();
        MonsterListCombo.Items.Clear();
        foreach (var e in enemies)
            MonsterListCombo.Items.Add(e.Name);
        MonsterListCombo.SelectedIndex = -1;
        DeleteBtn.IsVisible = enemies.Count > 0;
    }

    private void OnMonsterSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (MonsterListCombo.SelectedItem is not string name) return;

        var enemies = LoadAllEnemies();
        var data = enemies.FirstOrDefault(en => en.Name == name);
        if (data != null)
        {
            LoadMonsterData(data);
            _currentFileName = name;
            StatusLabel.Text = $"Loaded: {name}";
        }
    }

    private void OnNewMonster(object? sender, RoutedEventArgs e)
    {
        var data = new EnemyData();
        LoadMonsterData(data);
        _currentFileName = string.Empty;
        StatusLabel.Text = "New monster (unsaved)";
    }

    private void OnDeleteMonster(object? sender, RoutedEventArgs e)
    {
        if (MonsterListCombo.SelectedItem is not string name)
        {
            StatusLabel.Text = "Select a monster to delete.";
            return;
        }

        var enemies = LoadAllEnemies();
        enemies.RemoveAll(en => en.Name == name);
        EnemyData.SaveListToFile(_enemiesFilePath, enemies);
        EnemyDefinition.All.Remove(name);

        _currentFileName = string.Empty;
        RefreshMonsterList();
        StatusLabel.Text = $"Deleted: {name}";
    }

    // ==================== Data Load / Gather ====================

    private void LoadMonsterData(EnemyData data)
    {
        // Suppress events while setting values programmatically
        NameInput.Text = data.Name;

        HPSlider.Value = Math.Clamp(data.MaxHP, HPSlider.Minimum, HPSlider.Maximum);
        HPInput.Text = data.MaxHP.ToString("F0");

        SpeedSlider.Value = Math.Clamp(data.Speed, SpeedSlider.Minimum, SpeedSlider.Maximum);
        SpeedInput.Text = data.Speed.ToString("F1");

        GoldSlider.Value = Math.Clamp(data.GoldReward, GoldSlider.Minimum, GoldSlider.Maximum);
        GoldInput.Text = data.GoldReward.ToString();

        RadiusSlider.Value = Math.Clamp(data.Radius, RadiusSlider.Minimum, RadiusSlider.Maximum);
        RadiusInput.Text = data.Radius.ToString("F2");

        ColorRSlider.Value = Math.Clamp(data.ColorR, 0, 255);
        ColorRInput.Text = data.ColorR.ToString();

        ColorGSlider.Value = Math.Clamp(data.ColorG, 0, 255);
        ColorGInput.Text = data.ColorG.ToString();

        ColorBSlider.Value = Math.Clamp(data.ColorB, 0, 255);
        ColorBInput.Text = data.ColorB.ToString();

        UpdateColorPreview();
        UpdateSummary();
        RebuildPreviewScene();
    }

    private EnemyData GatherMonsterData()
    {
        return new EnemyData
        {
            Name = NameInput.Text?.Trim() ?? "Unnamed",
            MaxHP = ParseFloat(HPInput.Text, 100f),
            Speed = ParseFloat(SpeedInput.Text, 1.8f),
            GoldReward = (int)ParseFloat(GoldInput.Text, 10f),
            Radius = ParseFloat(RadiusInput.Text, 0.25f),
            ColorR = (int)ParseFloat(ColorRInput.Text, 220f),
            ColorG = (int)ParseFloat(ColorGInput.Text, 20f),
            ColorB = (int)ParseFloat(ColorBInput.Text, 60f),
        };
    }

    private static float ParseFloat(string? text, float fallback)
    {
        if (float.TryParse(text, out var val)) return val;
        return fallback;
    }

    // ==================== Property Change Handlers ====================

    private void OnSliderChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Slider.ValueProperty) return;

        SyncSliderToInput();
        UpdateColorPreview();
        UpdateSummary();
        RebuildPreviewScene();
    }

    private void OnColorSliderChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Slider.ValueProperty) return;

        SyncColorSliderToInput();
        UpdateColorPreview();
        UpdateSummary();
        RebuildPreviewScene();
    }

    private void OnPropertyKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        SyncInputToSlider();
        UpdateColorPreview();
        UpdateSummary();
        RebuildPreviewScene();
    }

    private void SyncSliderToInput()
    {
        HPInput.Text = HPSlider.Value.ToString("F0");
        SpeedInput.Text = SpeedSlider.Value.ToString("F1");
        GoldInput.Text = ((int)GoldSlider.Value).ToString();
        RadiusInput.Text = RadiusSlider.Value.ToString("F2");
    }

    private void SyncInputToSlider()
    {
        HPSlider.Value = Math.Clamp(ParseFloat(HPInput.Text, 100f), HPSlider.Minimum, HPSlider.Maximum);
        HPInput.Text = HPSlider.Value.ToString("F0");

        SpeedSlider.Value = Math.Clamp(ParseFloat(SpeedInput.Text, 1.8f), SpeedSlider.Minimum, SpeedSlider.Maximum);
        SpeedInput.Text = SpeedSlider.Value.ToString("F1");

        GoldSlider.Value = Math.Clamp(ParseFloat(GoldInput.Text, 10f), GoldSlider.Minimum, GoldSlider.Maximum);
        GoldInput.Text = ((int)GoldSlider.Value).ToString();

        RadiusSlider.Value = Math.Clamp(ParseFloat(RadiusInput.Text, 0.25f), RadiusSlider.Minimum, RadiusSlider.Maximum);
        RadiusInput.Text = RadiusSlider.Value.ToString("F2");

        ColorRSlider.Value = Math.Clamp(ParseFloat(ColorRInput.Text, 220f), 0, 255);
        ColorRInput.Text = ((int)ColorRSlider.Value).ToString();

        ColorGSlider.Value = Math.Clamp(ParseFloat(ColorGInput.Text, 20f), 0, 255);
        ColorGInput.Text = ((int)ColorGSlider.Value).ToString();

        ColorBSlider.Value = Math.Clamp(ParseFloat(ColorBInput.Text, 60f), 0, 255);
        ColorBInput.Text = ((int)ColorBSlider.Value).ToString();
    }

    private void SyncColorSliderToInput()
    {
        ColorRInput.Text = ((int)ColorRSlider.Value).ToString();
        ColorGInput.Text = ((int)ColorGSlider.Value).ToString();
        ColorBInput.Text = ((int)ColorBSlider.Value).ToString();
    }

    // ==================== Color Preview ====================

    private void UpdateColorPreview()
    {
        int r = (int)Math.Clamp(ParseFloat(ColorRInput.Text, 220f), 0, 255);
        int g = (int)Math.Clamp(ParseFloat(ColorGInput.Text, 20f), 0, 255);
        int b = (int)Math.Clamp(ParseFloat(ColorBInput.Text, 60f), 0, 255);

        var color = Avalonia.Media.Color.FromRgb((byte)r, (byte)g, (byte)b);
        ColorPreview.Background = new SolidColorBrush(color);
    }

    private void OnPresetColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex && hex.Length >= 7)
        {
            int r = Convert.ToInt32(hex.Substring(1, 2), 16);
            int g = Convert.ToInt32(hex.Substring(3, 2), 16);
            int b = Convert.ToInt32(hex.Substring(5, 2), 16);

            ColorRSlider.Value = r;
            ColorGSlider.Value = g;
            ColorBSlider.Value = b;
            ColorRInput.Text = r.ToString();
            ColorGInput.Text = g.ToString();
            ColorBInput.Text = b.ToString();

            UpdateColorPreview();
            UpdateSummary();
            RebuildPreviewScene();
        }
    }

    // ==================== Summary ====================

    private void UpdateSummary()
    {
        var data = GatherMonsterData();
        SummaryName.Text = data.Name;
        SummaryHP.Text = Loc.Get("MonsterEditor.HP", $"{data.MaxHP:F0}");
        SummarySpeed.Text = Loc.Get("MonsterEditor.SpeedSummary", $"{data.Speed:F1}");
        SummaryGold.Text = Loc.Get("MonsterEditor.GoldSummary", data.GoldReward);
        SummaryRadius.Text = Loc.Get("MonsterEditor.SizeSummary", $"{data.Radius:F2}");
    }

    // ==================== Save ====================

    private void OnSaveMonster(object? sender, RoutedEventArgs e)
    {
        var data = GatherMonsterData();
        if (string.IsNullOrWhiteSpace(data.Name))
        {
            StatusLabel.Text = "Please enter a name.";
            return;
        }

        var dir = Path.GetDirectoryName(_enemiesFilePath);
        if (dir != null) Directory.CreateDirectory(dir);

        // Load all enemies, update/add the current one, save all
        var enemies = LoadAllEnemies();
        var existing = enemies.FindIndex(en => en.Name == data.Name);
        if (existing >= 0)
            enemies[existing] = data;
        else
            enemies.Add(data);
        EnemyData.SaveListToFile(_enemiesFilePath, enemies);

        _currentFileName = data.Name;
        RefreshMonsterList();
        MonsterListCombo.SelectedItem = data.Name;

        // Update the runtime registry
        var def = data.ToDefinition();
        EnemyDefinition.All[data.Name] = def;

        StatusLabel.Text = $"Saved: {data.Name}";
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(result) ? "enemy" : result.Trim();
    }

    // ==================== Back ====================

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

        // CSM shadow quality
        view.PipelineSettings.CsmShadowMapResolution = 2048;
        view.PipelineSettings.CsmCascadeCount = 4;
        view.PipelineSettings.CsmSplitLambda = 0.6f;

        _planeGeo = new PlaneGeometry();
        _sphereGeo = new SphereGeometry();

        _groundMat = CreateMaterial(DrawingColor.FromArgb(255, 40, 40, 60));

        // Main directional light with shadows
        var dl = new DirectionalLight
        {
            RotationDegrees = new Vector3(-40, -20, 0),
            LightColor = DrawingColor.White,
            CastShadow = true,
            ShadowConfig = new DirectionalLightShadowMapConfig
            {
                Width = 20,
                Height = 20,
                NearPlane = 0.5f,
                FarPlane = 30,
            }
        };
        view.AddNode(dl);
        view.Scene.MainDirectionalLight = dl;
        _shadowLightNode = dl;

        // Ambient fill
        var ambient = new DirectionalLight
        {
            RotationDegrees = new Vector3(20, 150, 0),
            LightColor = DrawingColor.FromArgb(255, 80, 80, 100),
        };
        view.AddNode(ambient);
        _ambientLightNode = ambient;

        _sceneReady = true;
        RebuildPreviewScene();
    }

    private void OnPreviewSceneUpdated(object? sender, UpdateRoutedEventArgs args)
    {
        // Slowly rotate the monster for a dynamic preview
        _rotationTimer += (float)(args.DeltaTime * 20.0); // ~20 degrees/sec

        if (_monsterNode != null)
        {
            _monsterNode.RotationDegrees = new Vector3(0, _rotationTimer % 360, 0);
        }

        PreviewView.RequestNextFrameRendering();
    }

    private void RebuildPreviewScene()
    {
        if (!_sceneReady) return;
        var view = PreviewView;

        // Remove old monster
        if (_monsterNode != null)
        {
            view.Remove(_monsterNode);
            _monsterNode = null;
        }

        // Ground (always present)
        if (_groundNode == null)
        {
            var ground = new Mesh
            {
                Geometry = _planeGeo!,
                Material = _groundMat!,
            };
            ground.Scale = new Vector3(8, 1, 8);
            ground.Position = new Vector3(0, -0.05f, 0);
            _groundNode = ground;
            view.AddNode(ground);
        }

        // Build monster sphere
        var data = GatherMonsterData();
        var color = data.GetColor();
        _monsterMat = CreateMaterial(color);

        var monster = new Mesh
        {
            Geometry = _sphereGeo!,
            Material = _monsterMat,
        };
        monster.Scale = new Vector3(data.Radius * 2, data.Radius * 2, data.Radius * 2);
        monster.Position = new Vector3(0, data.Radius, 0);
        monster.Name = "MonsterPreview";
        _monsterNode = monster;
        view.AddNode(monster);
    }

    // ==================== Helpers ====================

    private static Material CreateMaterial(DrawingColor color) => new()
    {
        BlendMode = BlendMode.Opaque,
        DoubleSided = true,
        Channels = { new() { Name = "BaseColor", Texture = Texture.CreateFromColor(color) } }
    };
}
