using Avalonia.Data;
using Avalonia.Markup.Xaml;
using System.Diagnostics.CodeAnalysis;

namespace TowerDefense;

/// <summary>
/// Avalonia XAML markup extension that creates a OneWay binding to
/// <see cref="LocalizationManager.Instance"/>'s string indexer.
///
/// Usage: <c>Text="{loc:Loc Menu.Title}"</c>
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
public sealed class LocExtension : MarkupExtension
{
    /// <summary>Resource key to look up.</summary>
    public string Key { get; set; } = string.Empty;

    public LocExtension() { }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding
        {
            Source = LocalizationManager.Instance,
            Path = $"[{Key}]",
            Mode = BindingMode.OneWay,
        };
    }
}
