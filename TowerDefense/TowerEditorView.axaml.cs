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

public partial class TowerEditorView : UserControl
{
    // ==================== State ====================
    private string _towersDir = string.Empty;       // single dir for all towers (built-in + custom)
    private string _currentFileName = string.Empty;
    private bool _sceneReady;
    private float _rotationTimer;

    // ==================== Scene Cache ====================
    private Node? _groundNode;
    private Node? _towerNode;
    private Node? _shadowLightNode;
    private Node? _ambientLightNode;
    private PlaneGeometry? _planeGeo;
    private BoxGeometry? _boxGeo;
    private CylinderGeometry? _cylinderGeo;
    private SphereGeometry? _sphereGeo;
    private Material? _groundMat;
    private Material? _towerMat;
    private Material? _towerDarkMat;
    private Material? _towerLightMat;

    // ==================== Callbacks ====================
    public Action? OnBack { get; set; }

    public TowerEditorView()
    {
        InitializeComponent();
    }

    // ==================== Initialization ====================

    public void Initialize(string towersDir)
    {
        _towersDir = towersDir;
        RefreshTowerList();
    }

    // ==================== Tower List ====================

    private void RefreshTowerList()
    {
        var names = TowerData.ListNames(_towersDir);
        var sorted = names.OrderBy(n => n).ToList();
        TowerListCombo.Items.Clear();
        foreach (var name in sorted)
            TowerListCombo.Items.Add(name);
        TowerListCombo.SelectedIndex = -1;
        DeleteBtn.IsVisible = sorted.Count > 0;
    }

    private void OnTowerSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (TowerListCombo.SelectedItem is not string name) return;

        var data = TowerData.LoadFromFile(Path.Combine(_towersDir, name + ".json"));
        if (data != null)
        {
            LoadTowerData(data);
            _currentFileName = name;
            StatusLabel.Text = $"Loaded: {name}";
        }
    }

    private void OnNewTower(object? sender, RoutedEventArgs e)
    {
        var data = new TowerData();
        LoadTowerData(data);
        _currentFileName = string.Empty;
        StatusLabel.Text = "New tower (unsaved)";
    }

    private void OnDeleteTower(object? sender, RoutedEventArgs e)
    {
        if (TowerListCombo.SelectedItem is not string name)
        {
            StatusLabel.Text = "Select a tower to delete.";
            return;
        }

        var filePath = Path.Combine(_towersDir, name + ".json");
        if (File.Exists(filePath))
            File.Delete(filePath);

        _currentFileName = string.Empty;
        RefreshTowerList();
        StatusLabel.Text = $"Deleted: {name}";
    }

    // ==================== Data Load / Gather ====================

    private void LoadTowerData(TowerData data)
    {
        NameInput.Text = data.Name;

        CostSlider.Value = Math.Clamp(data.Cost, CostSlider.Minimum, CostSlider.Maximum);
        CostInput.Text = data.Cost.ToString();

        DamageSlider.Value = Math.Clamp(data.Damage, DamageSlider.Minimum, DamageSlider.Maximum);
        DamageInput.Text = data.Damage.ToString("F0");

        RangeSlider.Value = Math.Clamp(data.Range, RangeSlider.Minimum, RangeSlider.Maximum);
        RangeInput.Text = data.Range.ToString("F1");

        FireRateSlider.Value = Math.Clamp(data.FireRate, FireRateSlider.Minimum, FireRateSlider.Maximum);
        FireRateInput.Text = data.FireRate.ToString("F1");

        ProjSpeedSlider.Value = Math.Clamp(data.ProjectileSpeed, ProjSpeedSlider.Minimum, ProjSpeedSlider.Maximum);
        ProjSpeedInput.Text = data.ProjectileSpeed.ToString("F0");

        ColorRSlider.Value = Math.Clamp(data.ColorR, 0, 255);
        ColorRInput.Text = data.ColorR.ToString();

        ColorGSlider.Value = Math.Clamp(data.ColorG, 0, 255);
        ColorGInput.Text = data.ColorG.ToString();

        ColorBSlider.Value = Math.Clamp(data.ColorB, 0, 255);
        ColorBInput.Text = data.ColorB.ToString();

        SplashRadiusSlider.Value = Math.Clamp(data.SplashRadius, SplashRadiusSlider.Minimum, SplashRadiusSlider.Maximum);
        SplashRadiusInput.Text = data.SplashRadius.ToString("F1");

        SlowAmountSlider.Value = Math.Clamp(data.SlowAmount, SlowAmountSlider.Minimum, SlowAmountSlider.Maximum);
        SlowAmountInput.Text = data.SlowAmount.ToString("F2");

        MultiShotCountSlider.Value = Math.Clamp(data.MultiShotCount, MultiShotCountSlider.Minimum, MultiShotCountSlider.Maximum);
        MultiShotCountInput.Text = data.MultiShotCount.ToString();

        ArcAngleSlider.Value = Math.Clamp(data.ArcAngle, ArcAngleSlider.Minimum, ArcAngleSlider.Maximum);
        ArcAngleInput.Text = data.ArcAngle.ToString("F0");

        CritChanceSlider.Value = Math.Clamp(data.CritChance, CritChanceSlider.Minimum, CritChanceSlider.Maximum);
        CritChanceInput.Text = data.CritChance.ToString("F2");

        CritMultiplierSlider.Value = Math.Clamp(data.CritMultiplier, CritMultiplierSlider.Minimum, CritMultiplierSlider.Maximum);
        CritMultiplierInput.Text = data.CritMultiplier.ToString("F1");

        DotDamageSlider.Value = Math.Clamp(data.DotDamage, DotDamageSlider.Minimum, DotDamageSlider.Maximum);
        DotDamageInput.Text = data.DotDamage.ToString("F0");

        DotDurationSlider.Value = Math.Clamp(data.DotDuration, DotDurationSlider.Minimum, DotDurationSlider.Maximum);
        DotDurationInput.Text = data.DotDuration.ToString("F1");

        AoeRadiusSlider.Value = Math.Clamp(data.AoeRadius, AoeRadiusSlider.Minimum, AoeRadiusSlider.Maximum);
        AoeRadiusInput.Text = data.AoeRadius.ToString("F1");

        // Visual Style
        SetVisualStyleCombo(data.VisualStyle);

        UpdateColorPreview();
        UpdateSummary();
        RebuildPreviewScene();
    }

    private TowerData GatherTowerData()
    {
        return new TowerData
        {
            Name = NameInput.Text?.Trim() ?? "Unnamed",
            Cost = (int)ParseFloat(CostInput.Text, 50f),
            Damage = ParseFloat(DamageInput.Text, 15f),
            Range = ParseFloat(RangeInput.Text, 3.5f),
            FireRate = ParseFloat(FireRateInput.Text, 1.8f),
            ProjectileSpeed = ParseFloat(ProjSpeedInput.Text, 5f),
            ColorR = (int)ParseFloat(ColorRInput.Text, 50f),
            ColorG = (int)ParseFloat(ColorGInput.Text, 205f),
            ColorB = (int)ParseFloat(ColorBInput.Text, 50f),
            SplashRadius = ParseFloat(SplashRadiusInput.Text, 0f),
            SlowAmount = ParseFloat(SlowAmountInput.Text, 0f),
            MultiShotCount = (int)ParseFloat(MultiShotCountInput.Text, 1f),
            ArcAngle = ParseFloat(ArcAngleInput.Text, 0f),
            CritChance = ParseFloat(CritChanceInput.Text, 0f),
            CritMultiplier = ParseFloat(CritMultiplierInput.Text, 2f),
            DotDamage = ParseFloat(DotDamageInput.Text, 0f),
            DotDuration = ParseFloat(DotDurationInput.Text, 0f),
            AoeRadius = ParseFloat(AoeRadiusInput.Text, 0f),
            VisualStyle = GetVisualStyleCombo(),
        };
    }

    private string GetVisualStyleCombo()
    {
        if (VisualStyleCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            return tag;
        return "Auto";
    }

    private void SetVisualStyleCombo(string style)
    {
        foreach (var item in VisualStyleCombo.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Tag is string tag && tag == style)
            {
                VisualStyleCombo.SelectedItem = cbi;
                return;
            }
        }
        VisualStyleCombo.SelectedIndex = 0; // default Auto
    }

    private void OnVisualStyleChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateSummary();
        RebuildPreviewScene();
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
        CostInput.Text = ((int)CostSlider.Value).ToString();
        DamageInput.Text = DamageSlider.Value.ToString("F0");
        RangeInput.Text = RangeSlider.Value.ToString("F1");
        FireRateInput.Text = FireRateSlider.Value.ToString("F1");
        ProjSpeedInput.Text = ProjSpeedSlider.Value.ToString("F0");
        SplashRadiusInput.Text = SplashRadiusSlider.Value.ToString("F1");
        SlowAmountInput.Text = SlowAmountSlider.Value.ToString("F2");
        MultiShotCountInput.Text = ((int)MultiShotCountSlider.Value).ToString();
        ArcAngleInput.Text = ArcAngleSlider.Value.ToString("F0");
        CritChanceInput.Text = CritChanceSlider.Value.ToString("F2");
        CritMultiplierInput.Text = CritMultiplierSlider.Value.ToString("F1");
        DotDamageInput.Text = DotDamageSlider.Value.ToString("F0");
        DotDurationInput.Text = DotDurationSlider.Value.ToString("F1");
        AoeRadiusInput.Text = AoeRadiusSlider.Value.ToString("F1");
    }

    private void SyncInputToSlider()
    {
        CostSlider.Value = Math.Clamp(ParseFloat(CostInput.Text, 50f), CostSlider.Minimum, CostSlider.Maximum);
        CostInput.Text = ((int)CostSlider.Value).ToString();

        DamageSlider.Value = Math.Clamp(ParseFloat(DamageInput.Text, 15f), DamageSlider.Minimum, DamageSlider.Maximum);
        DamageInput.Text = DamageSlider.Value.ToString("F0");

        RangeSlider.Value = Math.Clamp(ParseFloat(RangeInput.Text, 3.5f), RangeSlider.Minimum, RangeSlider.Maximum);
        RangeInput.Text = RangeSlider.Value.ToString("F1");

        FireRateSlider.Value = Math.Clamp(ParseFloat(FireRateInput.Text, 1.8f), FireRateSlider.Minimum, FireRateSlider.Maximum);
        FireRateInput.Text = FireRateSlider.Value.ToString("F1");

        ProjSpeedSlider.Value = Math.Clamp(ParseFloat(ProjSpeedInput.Text, 5f), ProjSpeedSlider.Minimum, ProjSpeedSlider.Maximum);
        ProjSpeedInput.Text = ProjSpeedSlider.Value.ToString("F0");

        SplashRadiusSlider.Value = Math.Clamp(ParseFloat(SplashRadiusInput.Text, 0f), SplashRadiusSlider.Minimum, SplashRadiusSlider.Maximum);
        SplashRadiusInput.Text = SplashRadiusSlider.Value.ToString("F1");

        SlowAmountSlider.Value = Math.Clamp(ParseFloat(SlowAmountInput.Text, 0f), SlowAmountSlider.Minimum, SlowAmountSlider.Maximum);
        SlowAmountInput.Text = SlowAmountSlider.Value.ToString("F2");

        MultiShotCountSlider.Value = Math.Clamp(ParseFloat(MultiShotCountInput.Text, 1f), MultiShotCountSlider.Minimum, MultiShotCountSlider.Maximum);
        MultiShotCountInput.Text = ((int)MultiShotCountSlider.Value).ToString();

        ArcAngleSlider.Value = Math.Clamp(ParseFloat(ArcAngleInput.Text, 0f), ArcAngleSlider.Minimum, ArcAngleSlider.Maximum);
        ArcAngleInput.Text = ArcAngleSlider.Value.ToString("F0");

        CritChanceSlider.Value = Math.Clamp(ParseFloat(CritChanceInput.Text, 0f), CritChanceSlider.Minimum, CritChanceSlider.Maximum);
        CritChanceInput.Text = CritChanceSlider.Value.ToString("F2");

        CritMultiplierSlider.Value = Math.Clamp(ParseFloat(CritMultiplierInput.Text, 2f), CritMultiplierSlider.Minimum, CritMultiplierSlider.Maximum);
        CritMultiplierInput.Text = CritMultiplierSlider.Value.ToString("F1");

        DotDamageSlider.Value = Math.Clamp(ParseFloat(DotDamageInput.Text, 0f), DotDamageSlider.Minimum, DotDamageSlider.Maximum);
        DotDamageInput.Text = DotDamageSlider.Value.ToString("F0");

        DotDurationSlider.Value = Math.Clamp(ParseFloat(DotDurationInput.Text, 0f), DotDurationSlider.Minimum, DotDurationSlider.Maximum);
        DotDurationInput.Text = DotDurationSlider.Value.ToString("F1");

        AoeRadiusSlider.Value = Math.Clamp(ParseFloat(AoeRadiusInput.Text, 0f), AoeRadiusSlider.Minimum, AoeRadiusSlider.Maximum);
        AoeRadiusInput.Text = AoeRadiusSlider.Value.ToString("F1");

        // Color sliders
        ColorRSlider.Value = Math.Clamp(ParseFloat(ColorRInput.Text, 50f), 0, 255);
        ColorRInput.Text = ((int)ColorRSlider.Value).ToString();

        ColorGSlider.Value = Math.Clamp(ParseFloat(ColorGInput.Text, 205f), 0, 255);
        ColorGInput.Text = ((int)ColorGSlider.Value).ToString();

        ColorBSlider.Value = Math.Clamp(ParseFloat(ColorBInput.Text, 50f), 0, 255);
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
        int r = (int)Math.Clamp(ParseFloat(ColorRInput.Text, 50f), 0, 255);
        int g = (int)Math.Clamp(ParseFloat(ColorGInput.Text, 205f), 0, 255);
        int b = (int)Math.Clamp(ParseFloat(ColorBInput.Text, 50f), 0, 255);

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
        var data = GatherTowerData();
        SummaryName.Text = data.Name;
        SummaryCost.Text = $"Cost: {data.Cost} gold";
        SummaryDamage.Text = $"Damage: {data.Damage:F0}";
        SummaryRange.Text = $"Range: {data.Range:F1}";
        SummaryFireRate.Text = data.FireRate > 0
            ? $"Fire Rate: {data.FireRate:F1}/s"
            : "Fire Rate: Continuous AOE";

        // Build specials string
        var specials = new List<string>();
        if (data.SplashRadius > 0) specials.Add($"Splash({data.SplashRadius:F1})");
        if (data.SlowAmount > 0) specials.Add($"Slow({data.SlowAmount:F2}x)");
        if (data.MultiShotCount > 1) specials.Add($"Multi({data.MultiShotCount}x)");
        if (data.CritChance > 0) specials.Add($"Crit({data.CritChance*100:F0}%×{data.CritMultiplier:F1})");
        if (data.DotDamage > 0) specials.Add($"DOT({data.DotDamage:F0}/{data.DotDuration:F1}s)");
        if (data.AoeRadius > 0) specials.Add($"AOE({data.AoeRadius:F1})");
        SummarySpecials.Text = specials.Count > 0
            ? $"Specials: {string.Join(", ", specials)}"
            : "Specials: None";
    }

    // ==================== Save ====================

    private void OnSaveTower(object? sender, RoutedEventArgs e)
    {
        var data = GatherTowerData();
        if (string.IsNullOrWhiteSpace(data.Name))
        {
            StatusLabel.Text = "Please enter a name.";
            return;
        }

        var dir = _towersDir;
        Directory.CreateDirectory(dir);

        var fileName = SanitizeFileName(data.Name);
        var filePath = Path.Combine(dir, fileName + ".json");
        data.SaveToFile(filePath);

        _currentFileName = fileName;
        RefreshTowerList();
        TowerListCombo.SelectedItem = fileName;

        // Update the runtime registry
        var def = data.ToDefinition();
        TowerDefinition.All[data.Name] = def;

        StatusLabel.Text = $"Saved: {fileName}.json";
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(result) ? "tower" : result.Trim();
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
        _towerNode = null;

        view.MainCamera.Position = new Vector3(0, 3.5f, 5);
        view.MainCamera.RotationDegrees = new Vector3(-35, 0, 0);
        view.Scene.Background = Texture.CreateFromColor(DrawingColor.FromArgb(255, 30, 30, 50));

        _planeGeo = new PlaneGeometry();
        _boxGeo = new BoxGeometry();
        _cylinderGeo = new CylinderGeometry();
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
        _rotationTimer += (float)(args.DeltaTime * 20.0);

        if (_towerNode != null)
        {
            _towerNode.RotationDegrees = new Vector3(0, _rotationTimer % 360, 0);
        }

        PreviewView.RequestNextFrameRendering();
    }

    private void RebuildPreviewScene()
    {
        if (!_sceneReady) return;
        var view = PreviewView;

        // Remove old tower
        if (_towerNode != null)
        {
            view.Remove(_towerNode);
            _towerNode = null;
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

        // Build tower model
        var data = GatherTowerData();
        var color = data.GetColor();
        _towerMat = CreateMaterial(color);
        _towerDarkMat = CreateMaterial(Darken(color, 0.6f));
        _towerLightMat = CreateMaterial(Lighten(color, 0.4f));

        var tower = new Node();

        // Base platform
        var baseMesh = new Mesh { Geometry = _boxGeo!, Material = _towerDarkMat };
        baseMesh.Scale = new Vector3(0.55f, 0.1f, 0.55f);
        baseMesh.Position = new Vector3(0, 0.1f, 0);
        tower.AddChild(baseMesh, AttachToParentRule.KeepLocal);

        // Body cylinder
        var bodyMesh = new Mesh { Geometry = _cylinderGeo!, Material = _towerMat };
        bodyMesh.Scale = new Vector3(0.3f, 0.4f, 0.3f);
        bodyMesh.Position = new Vector3(0, 0.5f, 0);
        tower.AddChild(bodyMesh, AttachToParentRule.KeepLocal);

        // Resolve visual style: explicit choice or auto-derived from stats
        var style = data.VisualStyle;
        if (style == "Auto")
        {
            if (data.AoeRadius > 0) style = "Sun";
            else if (data.MultiShotCount > 1) style = "MultiShot";
            else if (data.CritChance > 0) style = "Sniper";
            else if (data.DotDamage > 0) style = "Poison";
            else if (data.SplashRadius > 0 || data.SlowAmount > 0) style = "Cannon";
            else style = "Arrow";
        }

        switch (style)
        {
            case "Sun":
                {
                    var sunCore = new Mesh { Geometry = _sphereGeo!, Material = _towerLightMat };
                    sunCore.Scale = new Vector3(0.35f, 0.35f, 0.35f);
                    sunCore.Position = new Vector3(0, 0.95f, 0);
                    tower.AddChild(sunCore, AttachToParentRule.KeepLocal);

                    var glowRing = new Mesh { Geometry = _cylinderGeo!, Material = _towerMat };
                    glowRing.Scale = new Vector3(0.42f, 0.04f, 0.42f);
                    glowRing.Position = new Vector3(0, 0.8f, 0);
                    tower.AddChild(glowRing, AttachToParentRule.KeepLocal);
                    break;
                }
            case "MultiShot":
                {
                    float[] angles = { 0, -30, 30 };
                    float radius = 0.1f;
                    foreach (var ang in angles)
                    {
                        var rad = ang * MathF.PI / 180f;
                        var barrel = new Mesh { Geometry = _cylinderGeo!, Material = _towerDarkMat };
                        barrel.Scale = new Vector3(0.07f, 0.22f, 0.07f);
                        barrel.Position = new Vector3(
                            MathF.Sin(rad) * radius,
                            1.05f,
                            MathF.Cos(rad) * radius
                        );
                        barrel.RotationDegrees = new Vector3(ang * 0.4f, 0, 0);
                        tower.AddChild(barrel, AttachToParentRule.KeepLocal);
                    }
                    var centerBall = new Mesh { Geometry = _sphereGeo!, Material = _towerLightMat };
                    centerBall.Scale = new Vector3(0.14f, 0.14f, 0.14f);
                    centerBall.Position = new Vector3(0, 0.95f, 0);
                    tower.AddChild(centerBall, AttachToParentRule.KeepLocal);
                    break;
                }
            case "Sniper":
                {
                    var barrel = new Mesh { Geometry = _cylinderGeo!, Material = _towerDarkMat };
                    barrel.Scale = new Vector3(0.1f, 0.5f, 0.1f);
                    barrel.Position = new Vector3(0, 1.1f, 0);
                    tower.AddChild(barrel, AttachToParentRule.KeepLocal);

                    var ring = new Mesh { Geometry = _cylinderGeo!, Material = _towerLightMat };
                    ring.Scale = new Vector3(0.18f, 0.06f, 0.18f);
                    ring.Position = new Vector3(0, 1.35f, 0);
                    tower.AddChild(ring, AttachToParentRule.KeepLocal);
                    break;
                }
            case "Poison":
                {
                    var poisonBall = new Mesh { Geometry = _sphereGeo!, Material = _towerLightMat };
                    poisonBall.Scale = new Vector3(0.28f, 0.28f, 0.28f);
                    poisonBall.Position = new Vector3(0, 0.95f, 0);
                    tower.AddChild(poisonBall, AttachToParentRule.KeepLocal);

                    var spike = new Mesh { Geometry = _cylinderGeo!, Material = _towerDarkMat };
                    spike.Scale = new Vector3(0.05f, 0.18f, 0.05f);
                    spike.Position = new Vector3(0, 1.2f, 0);
                    tower.AddChild(spike, AttachToParentRule.KeepLocal);
                    break;
                }
            case "Cannon":
                {
                    var topMesh = new Mesh { Geometry = _sphereGeo!, Material = _towerMat };
                    topMesh.Scale = new Vector3(0.22f, 0.22f, 0.22f);
                    topMesh.Position = new Vector3(0, 0.95f, 0);
                    tower.AddChild(topMesh, AttachToParentRule.KeepLocal);
                    break;
                }
            default: // Arrow
                {
                    var topMesh = new Mesh { Geometry = _cylinderGeo!, Material = _towerDarkMat };
                    topMesh.Scale = new Vector3(0.08f, 0.25f, 0.08f);
                    topMesh.Position = new Vector3(0, 1.0f, 0);
                    tower.AddChild(topMesh, AttachToParentRule.KeepLocal);
                    break;
                }
        }

        tower.Name = "TowerPreview";
        _towerNode = tower;
        view.AddNode(tower);
    }

    // ==================== Helpers ====================

    private static Material CreateMaterial(DrawingColor color) => new()
    {
        BlendMode = BlendMode.Opaque,
        DoubleSided = true,
        Channels = { new() { Name = "BaseColor", Texture = Texture.CreateFromColor(color) } }
    };

    private static DrawingColor Darken(DrawingColor c, float factor)
    {
        return DrawingColor.FromArgb(c.A, (byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));
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
