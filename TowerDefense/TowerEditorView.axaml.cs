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
    private string _towersFilePath = string.Empty;
    private string _currentFileName = string.Empty;
    private readonly List<TowerShapeData> _shapes = new();
    private int _selectedShapeIndex = -1;
    private bool _sceneReady;
    private float _rotationTimer;
    private bool _suppressShapeUpdates;

    // ==================== Scene Cache ====================
    private Node? _groundNode;
    private Node? _towerNode;
    private PlaneGeometry? _planeGeo;
    private BoxGeometry? _boxGeo;
    private CylinderGeometry? _cylinderGeo;
    private SphereGeometry? _sphereGeo;
    private Material? _groundMat;

    // ==================== Callbacks ====================
    public Action? OnBack { get; set; }

    public TowerEditorView()
    {
        InitializeComponent();
    }

    // ==================== Initialization ====================

    public void Initialize(string towersFilePath)
    {
        _towersFilePath = towersFilePath;
        RefreshTowerList();
    }

    // ==================== Tower List ====================

    private List<TowerData> LoadAllTowers() => TowerData.LoadListFromFile(_towersFilePath);

    private void RefreshTowerList()
    {
        var towers = LoadAllTowers();
        TowerListCombo.Items.Clear();
        foreach (var t in towers)
            TowerListCombo.Items.Add(t.Name);
        TowerListCombo.SelectedIndex = -1;
        DeleteBtn.IsVisible = towers.Count > 0;
    }

    private void OnTowerSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (TowerListCombo.SelectedItem is not string name) return;

        var towers = LoadAllTowers();
        var data = towers.FirstOrDefault(t => t.Name == name);
        if (data != null)
        {
            LoadTowerData(data);
            _currentFileName = name;
            StatusLabel.Text = Loc.Get("TowerEditor.Loaded", name);
        }
    }

    private void OnNewTower(object? sender, RoutedEventArgs e)
    {
        var data = new TowerData();
        LoadTowerData(data);
        _currentFileName = string.Empty;
        StatusLabel.Text = Loc.Get("TowerEditor.NewUnsaved");
    }

    private void OnDeleteTower(object? sender, RoutedEventArgs e)
    {
        if (TowerListCombo.SelectedItem is not string name)
        {
            StatusLabel.Text = Loc.Get("TowerEditor.SelectToDelete");
            return;
        }
        var towers = LoadAllTowers();
        towers.RemoveAll(t => t.Name == name);
        TowerData.SaveListToFile(_towersFilePath, towers);
        TowerDefinition.All.Remove(name);
        _currentFileName = string.Empty;
        RefreshTowerList();
        StatusLabel.Text = Loc.Get("TowerEditor.DeletedMsg", name);
    }

    // ==================== Shape Management ====================

    private void AddShape(string type)
    {
        var shape = new TowerShapeData
        {
            Type = type,
            ScaleX = type switch { "Box" => 0.5f, "Cylinder" => 0.3f, "Sphere" => 0.25f, "Cone" => 0.25f, _ => 0.3f },
            ScaleY = type switch { "Box" => 0.1f, "Cylinder" => 0.4f, "Sphere" => 0.25f, "Cone" => 0.35f, _ => 0.3f },
            ScaleZ = type switch { "Box" => 0.5f, "Cylinder" => 0.3f, "Sphere" => 0.25f, "Cone" => 0.25f, _ => 0.3f },
            ColorR = 180, ColorG = 180, ColorB = 180,
        };
        // Add to end = top of tower = top of panel (panel is reversed)
        _shapes.Add(shape);
        _selectedShapeIndex = _shapes.Count - 1;
        SelectShape(_selectedShapeIndex);
        RebuildPreviewScene();
        UpdateSummary();
    }

    private void RemoveShape(int index)
    {
        if (index < 0 || index >= _shapes.Count) return;
        _shapes.RemoveAt(index);
        _selectedShapeIndex = _shapes.Count > 0 ? Math.Min(index, _shapes.Count - 1) : -1;
        if (_selectedShapeIndex >= 0)
            SelectShape(_selectedShapeIndex);
        else
            ClearShapeEditor();
        RebuildPreviewScene();
        UpdateSummary();
    }

    // Panel is reversed: top of panel = last index (top of tower).
    // "Up" moves toward higher index (top of tower).
    // Panel is reversed: top of panel = last index (top of tower).
    // "Up" moves toward higher index (top of tower).
    private void MoveShapeUp(int index)
    {
        if (index >= _shapes.Count - 1) return;
        (_shapes[index], _shapes[index + 1]) = (_shapes[index + 1], _shapes[index]);
        _selectedShapeIndex = index + 1;
        SelectShape(_selectedShapeIndex);
        RebuildPreviewScene();
    }

    // "Down" moves toward lower index (bottom of tower).
    private void MoveShapeDown(int index)
    {
        if (index <= 0) return;
        (_shapes[index], _shapes[index - 1]) = (_shapes[index - 1], _shapes[index]);
        _selectedShapeIndex = index - 1;
        SelectShape(_selectedShapeIndex);
        RebuildPreviewScene();
    }

    private void SelectShape(int index)
    {
        if (index < 0 || index >= _shapes.Count)
        {
            ClearShapeEditor();
            return;
        }

        _selectedShapeIndex = index;
        var s = _shapes[index];

        _suppressShapeUpdates = true;

        SelectedShapeLabel.Text = Loc.Get("TowerEditor.ShapeLabel", s.Type, index + 1);
        ShapeColorPreview.IsVisible = true;
        ShapeColorPanel.IsVisible = true;
        ShapeScalePanel.IsVisible = true;
        RemoveShapeBtn.IsVisible = true;

        ShapeColorRSlider.Value = Math.Clamp(s.ColorR, 0, 255);
        ShapeColorRInput.Text = s.ColorR.ToString();
        ShapeColorGSlider.Value = Math.Clamp(s.ColorG, 0, 255);
        ShapeColorGInput.Text = s.ColorG.ToString();
        ShapeColorBSlider.Value = Math.Clamp(s.ColorB, 0, 255);
        ShapeColorBInput.Text = s.ColorB.ToString();
        UpdateShapeColorPreview();

        ShapeScaleXSlider.Value = Math.Clamp(s.ScaleX, (float)ShapeScaleXSlider.Minimum, (float)ShapeScaleXSlider.Maximum);
        ShapeScaleXInput.Text = s.ScaleX.ToString("F2");
        ShapeScaleYSlider.Value = Math.Clamp(s.ScaleY, (float)ShapeScaleYSlider.Minimum, (float)ShapeScaleYSlider.Maximum);
        ShapeScaleYInput.Text = s.ScaleY.ToString("F2");
        ShapeScaleZSlider.Value = Math.Clamp(s.ScaleZ, (float)ShapeScaleZSlider.Minimum, (float)ShapeScaleZSlider.Maximum);
        ShapeScaleZInput.Text = s.ScaleZ.ToString("F2");
        ShapeOffsetYSlider.Value = Math.Clamp(s.OffsetY, (float)ShapeOffsetYSlider.Minimum, (float)ShapeOffsetYSlider.Maximum);
        ShapeOffsetYInput.Text = s.OffsetY.ToString("F2");
        ShapeOffsetXSlider.Value = Math.Clamp(s.OffsetX, (float)ShapeOffsetXSlider.Minimum, (float)ShapeOffsetXSlider.Maximum);
        ShapeOffsetXInput.Text = s.OffsetX.ToString("F2");
        ShapeRotationYSlider.Value = s.RotationY;
        ShapeRotationYInput.Text = s.RotationY.ToString("F0");

        _suppressShapeUpdates = false;

        // Refresh cards to show selection highlight
        RefreshShapeList();
    }

    private void ClearShapeEditor()
    {
        _selectedShapeIndex = -1;
        SelectedShapeLabel.Text = Loc.Get("TowerEditor.None");
        ShapeColorPreview.IsVisible = false;
        ShapeColorPanel.IsVisible = false;
        ShapeScalePanel.IsVisible = false;
        RemoveShapeBtn.IsVisible = false;
        RefreshShapeList();
    }

    private void RefreshShapeList()
    {
        ShapeListPanel.Children.Clear();
        var icons = new Dictionary<string, string> { ["Box"] = "📦", ["Cylinder"] = "🥫", ["Sphere"] = "🔵", ["Cone"] = "🔺" };

        // Display in reverse: top-of-tower shapes (last in list) shown at top of panel
        for (int ri = _shapes.Count - 1; ri >= 0; ri--)
        {
            var s = _shapes[ri];
            var idx = ri;
            var icon = icons.GetValueOrDefault(s.Type, "⬜");
            var col = Avalonia.Media.Color.FromRgb((byte)s.ColorR, (byte)s.ColorG, (byte)s.ColorB);

            var card = new Border
            {
                Background = idx == _selectedShapeIndex ? Avalonia.Media.Brush.Parse("#444") : Avalonia.Media.Brush.Parse("#2a2a3a"),
                CornerRadius = new Avalonia.CornerRadius(4),
                Padding = new Avalonia.Thickness(6),
                Tag = idx,
            };

            var sp = new StackPanel { Spacing = 4 };

            // Color preview + name
            var header = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
            var colorDot = new Border
            {
                Width = 14, Height = 14, CornerRadius = new Avalonia.CornerRadius(3),
                Background = new SolidColorBrush(col),
            };
            var label = new TextBlock { Text = $"{icon} {s.Type}", Foreground = Avalonia.Media.Brushes.White, FontSize = 11, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            header.Children.Add(colorDot);
            header.Children.Add(label);
            sp.Children.Add(header);

            // Buttons row
            var btns = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 2 };
            var upBtn = new Button { Content = "▲", FontSize = 9, Padding = new Avalonia.Thickness(4, 1), CornerRadius = new Avalonia.CornerRadius(3), Background = Avalonia.Media.Brush.Parse("#CC3a3a5a"), Foreground = Avalonia.Media.Brushes.White };
            upBtn.Click += (_, _) => MoveShapeUp(idx);
            var dnBtn = new Button { Content = "▼", FontSize = 9, Padding = new Avalonia.Thickness(4, 1), CornerRadius = new Avalonia.CornerRadius(3), Background = Avalonia.Media.Brush.Parse("#CC3a3a5a"), Foreground = Avalonia.Media.Brushes.White };
            dnBtn.Click += (_, _) => MoveShapeDown(idx);
            var delBtn = new Button { Content = "✕", FontSize = 9, Padding = new Avalonia.Thickness(4, 1), CornerRadius = new Avalonia.CornerRadius(3), Background = Avalonia.Media.Brush.Parse("#CC5a3a3a"), Foreground = Avalonia.Media.Brushes.White };
            delBtn.Click += (_, _) => RemoveShape(idx);
            btns.Children.Add(upBtn);
            btns.Children.Add(dnBtn);
            btns.Children.Add(delBtn);
            sp.Children.Add(btns);

            card.Child = sp;

            // Click card to select
            card.PointerPressed += (_, _) => SelectShape(idx);
            ShapeListPanel.Children.Add(card);
        }
    }

    // ==================== Shape Add Handlers ====================

    private void OnAddBox(object? s, RoutedEventArgs e) => AddShape("Box");
    private void OnAddCylinder(object? s, RoutedEventArgs e) => AddShape("Cylinder");
    private void OnAddSphere(object? s, RoutedEventArgs e) => AddShape("Sphere");
    private void OnRemoveShape(object? s, RoutedEventArgs e) => RemoveShape(_selectedShapeIndex);

    // ==================== Shape Property Handlers ====================

    private void OnShapeColorSliderChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Slider.ValueProperty || _suppressShapeUpdates) return;
        SyncShapeColorToInput();
        ApplyShapeProperties();
    }

    private void OnShapeScaleSliderChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Slider.ValueProperty || _suppressShapeUpdates) return;
        SyncShapeScaleToInput();
        ApplyShapeProperties();
    }

    private void OnShapePropertyKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _suppressShapeUpdates) return;
        SyncShapeInputToSliders();
        ApplyShapeProperties();
    }

    private void SyncShapeColorToInput()
    {
        ShapeColorRInput.Text = ((int)ShapeColorRSlider.Value).ToString();
        ShapeColorGInput.Text = ((int)ShapeColorGSlider.Value).ToString();
        ShapeColorBInput.Text = ((int)ShapeColorBSlider.Value).ToString();
    }

    private void SyncShapeScaleToInput()
    {
        ShapeScaleXInput.Text = ShapeScaleXSlider.Value.ToString("F2");
        ShapeScaleYInput.Text = ShapeScaleYSlider.Value.ToString("F2");
        ShapeScaleZInput.Text = ShapeScaleZSlider.Value.ToString("F2");
        ShapeOffsetYInput.Text = ShapeOffsetYSlider.Value.ToString("F2");
        ShapeOffsetXInput.Text = ShapeOffsetXSlider.Value.ToString("F2");
        ShapeRotationYInput.Text = ((int)ShapeRotationYSlider.Value).ToString();
    }

    private void SyncShapeInputToSliders()
    {
        ShapeColorRSlider.Value = Math.Clamp(ParseFloat(ShapeColorRInput.Text, 180), 0, 255);
        ShapeColorGSlider.Value = Math.Clamp(ParseFloat(ShapeColorGInput.Text, 180), 0, 255);
        ShapeColorBSlider.Value = Math.Clamp(ParseFloat(ShapeColorBInput.Text, 180), 0, 255);
        ShapeColorRInput.Text = ((int)ShapeColorRSlider.Value).ToString();
        ShapeColorGInput.Text = ((int)ShapeColorGSlider.Value).ToString();
        ShapeColorBInput.Text = ((int)ShapeColorBSlider.Value).ToString();

        ShapeScaleXSlider.Value = Math.Clamp(ParseFloat(ShapeScaleXInput.Text, 1), (float)ShapeScaleXSlider.Minimum, (float)ShapeScaleXSlider.Maximum);
        ShapeScaleXInput.Text = ShapeScaleXSlider.Value.ToString("F2");
        ShapeScaleYSlider.Value = Math.Clamp(ParseFloat(ShapeScaleYInput.Text, 1), (float)ShapeScaleYSlider.Minimum, (float)ShapeScaleYSlider.Maximum);
        ShapeScaleYInput.Text = ShapeScaleYSlider.Value.ToString("F2");
        ShapeScaleZSlider.Value = Math.Clamp(ParseFloat(ShapeScaleZInput.Text, 1), (float)ShapeScaleZSlider.Minimum, (float)ShapeScaleZSlider.Maximum);
        ShapeScaleZInput.Text = ShapeScaleZSlider.Value.ToString("F2");
        ShapeOffsetYSlider.Value = Math.Clamp(ParseFloat(ShapeOffsetYInput.Text, 0), (float)ShapeOffsetYSlider.Minimum, (float)ShapeOffsetYSlider.Maximum);
        ShapeOffsetYInput.Text = ShapeOffsetYSlider.Value.ToString("F2");
        ShapeOffsetXSlider.Value = Math.Clamp(ParseFloat(ShapeOffsetXInput.Text, 0), (float)ShapeOffsetXSlider.Minimum, (float)ShapeOffsetXSlider.Maximum);
        ShapeOffsetXInput.Text = ShapeOffsetXSlider.Value.ToString("F2");
        ShapeRotationYSlider.Value = (int)ParseFloat(ShapeRotationYInput.Text, 0);
        ShapeRotationYInput.Text = ((int)ShapeRotationYSlider.Value).ToString();
    }

    private void ApplyShapeProperties()
    {
        if (_selectedShapeIndex < 0 || _selectedShapeIndex >= _shapes.Count) return;

        var s = _shapes[_selectedShapeIndex];
        s.ColorR = (int)Math.Clamp(ParseFloat(ShapeColorRInput.Text, 180), 0, 255);
        s.ColorG = (int)Math.Clamp(ParseFloat(ShapeColorGInput.Text, 180), 0, 255);
        s.ColorB = (int)Math.Clamp(ParseFloat(ShapeColorBInput.Text, 180), 0, 255);
        s.ScaleX = ParseFloat(ShapeScaleXInput.Text, 1);
        s.ScaleY = ParseFloat(ShapeScaleYInput.Text, 1);
        s.ScaleZ = ParseFloat(ShapeScaleZInput.Text, 1);
        s.OffsetY = ParseFloat(ShapeOffsetYInput.Text, 0);
        s.OffsetX = ParseFloat(ShapeOffsetXInput.Text, 0);
        s.RotationY = ParseFloat(ShapeRotationYInput.Text, 0);

        UpdateShapeColorPreview();
        SelectShape(_selectedShapeIndex);
        RebuildPreviewScene();
    }

    private void UpdateShapeColorPreview()
    {
        int r = (int)Math.Clamp(ParseFloat(ShapeColorRInput.Text, 180), 0, 255);
        int g = (int)Math.Clamp(ParseFloat(ShapeColorGInput.Text, 180), 0, 255);
        int b = (int)Math.Clamp(ParseFloat(ShapeColorBInput.Text, 180), 0, 255);
        ShapeColorPreview.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb((byte)r, (byte)g, (byte)b));
    }

    private void OnShapePresetColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex && hex.Length >= 7)
        {
            int r = Convert.ToInt32(hex.Substring(1, 2), 16);
            int g = Convert.ToInt32(hex.Substring(3, 2), 16);
            int b = Convert.ToInt32(hex.Substring(5, 2), 16);
            ShapeColorRSlider.Value = r; ShapeColorRInput.Text = r.ToString();
            ShapeColorGSlider.Value = g; ShapeColorGInput.Text = g.ToString();
            ShapeColorBSlider.Value = b; ShapeColorBInput.Text = b.ToString();
            UpdateShapeColorPreview();
            ApplyShapeProperties();
        }
    }

    // ==================== Tower Property Handlers ====================

    private void OnSliderChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Slider.ValueProperty) return;
        SyncTowerSliderToInput();
        UpdateSummary();
    }

    private void OnPropertyKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        SyncTowerInputToSlider();
        UpdateSummary();
    }

    private void SyncTowerSliderToInput()
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

    private void SyncTowerInputToSlider()
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

        // Load shapes
        _shapes.Clear();
        _shapes.AddRange(data.Shapes.Select(s => s.Clone()));
        _selectedShapeIndex = _shapes.Count > 0 ? 0 : -1;
        if (_selectedShapeIndex >= 0)
            SelectShape(_selectedShapeIndex);
        else
            ClearShapeEditor();

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
            SplashRadius = ParseFloat(SplashRadiusInput.Text, 0f),
            SlowAmount = ParseFloat(SlowAmountInput.Text, 0f),
            MultiShotCount = (int)ParseFloat(MultiShotCountInput.Text, 1f),
            ArcAngle = ParseFloat(ArcAngleInput.Text, 0f),
            CritChance = ParseFloat(CritChanceInput.Text, 0f),
            CritMultiplier = ParseFloat(CritMultiplierInput.Text, 2f),
            DotDamage = ParseFloat(DotDamageInput.Text, 0f),
            DotDuration = ParseFloat(DotDurationInput.Text, 0f),
            AoeRadius = ParseFloat(AoeRadiusInput.Text, 0f),
            Shapes = _shapes.Select(s => s.Clone()).ToList(),
        };
    }

    // ==================== Summary ====================

    private void UpdateSummary()
    {
        var data = GatherTowerData();
        SummaryName.Text = data.Name;
        SummaryCost.Text = Loc.Get("TowerEditor.CostSummary", data.Cost);
        SummaryDamage.Text = Loc.Get("TowerEditor.DamageSummary", data.Damage.ToString("F0"));
        SummaryRange.Text = Loc.Get("TowerEditor.RangeSummary", data.Range.ToString("F1"));
        SummaryFireRate.Text = data.FireRate > 0 ? Loc.Get("TowerEditor.FireRateSummary", data.FireRate.ToString("F1")) : Loc.Get("TowerEditor.FireRateContinuous");
        SummaryShapes.Text = Loc.Get("TowerEditor.ShapesCount", _shapes.Count);
    }

    // ==================== Save ====================

    private void OnSaveTower(object? sender, RoutedEventArgs e)
    {
        var data = GatherTowerData();
        if (string.IsNullOrWhiteSpace(data.Name))
        {
            StatusLabel.Text = Loc.Get("TowerEditor.EnterName");
            return;
        }

        var dir = Path.GetDirectoryName(_towersFilePath);
        if (dir != null) Directory.CreateDirectory(dir);

        // Load all towers, update/add the current one, save all
        var towers = LoadAllTowers();
        var existing = towers.FindIndex(t => t.Name == data.Name);
        if (existing >= 0)
            towers[existing] = data;
        else
            towers.Add(data);
        TowerData.SaveListToFile(_towersFilePath, towers);

        var fileName = SanitizeFileName(data.Name);
        _currentFileName = fileName;
        RefreshTowerList();
        TowerListCombo.SelectedItem = data.Name;

        var def = data.ToDefinition();
        TowerDefinition.All[data.Name] = def;

        StatusLabel.Text = Loc.Get("TowerEditor.Saved", data.Name);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(result) ? "tower" : result.Trim();
    }

    // ==================== Back ====================

    private void OnBackClick(object? sender, RoutedEventArgs e) => OnBack?.Invoke();

    // ==================== 3D Preview Scene ====================

    private void OnPreviewSceneInit(object? sender, InitializedRoutedEventArgs e)
    {
        var view = (sender as Aura3DView)!;
        _groundNode = null; _towerNode = null;

        view.MainCamera.Position = new Vector3(0, 3.5f, 5);
        view.MainCamera.RotationDegrees = new Vector3(-35, 0, 0);
        view.Scene.Background = Texture.CreateFromColor(DrawingColor.FromArgb(255, 30, 30, 50));

        // CSM shadow quality
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

    private void OnPreviewObjectPicked(object? sender, ObjectPickedEventArgs args)
    {
        var nodeName = args.Node.Name ?? "";
        if (nodeName.StartsWith("Shape_") && int.TryParse(nodeName.Replace("Shape_", ""), out int index)
            && index >= 0 && index < _shapes.Count)
        {
            SelectShape(index);
        }
    }

    private void OnPreviewSceneUpdated(object? sender, UpdateRoutedEventArgs args)
    {
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

        // Auto-stack shapes along Y axis
        float currentY = 0;
        for (int i = 0; i < _shapes.Count; i++)
        {
            var s = _shapes[i];
            var color = s.GetColor();
            var mat = CreateMaterial(color);

            Mesh mesh;
            if (s.Type == "Box") mesh = new Mesh { Geometry = _boxGeo!, Material = mat, Name = $"Shape_{i}" };
            else if (s.Type == "Sphere") mesh = new Mesh { Geometry = _sphereGeo!, Material = mat, Name = $"Shape_{i}" };
            else mesh = new Mesh { Geometry = _cylinderGeo!, Material = mat, Name = $"Shape_{i}" };

            mesh.Scale = new Vector3(s.ScaleX, s.ScaleY, s.ScaleZ);

            // Position: auto-stacked Y + manual offsets
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

    private static float ParseFloat(string? text, float fallback)
    {
        if (float.TryParse(text, out var val)) return val;
        return fallback;
    }
}
