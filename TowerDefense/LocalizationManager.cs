using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Resources;
using TowerDefense.Properties;

namespace TowerDefense;

/// <summary>
/// Singleton localization manager that wraps the .resx ResourceManager
/// with INotifyPropertyChanged support for live UI binding updates.
///
/// All public members must be preserved for AOT because the indexer
/// is accessed via Avalonia dynamic bindings and the events are
/// subscribed via INotifyPropertyChanged/INotifyCollectionChanged.
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties |
                           DynamicallyAccessedMemberTypes.PublicEvents)]
public sealed class LocalizationManager : INotifyPropertyChanged, INotifyCollectionChanged
{
    public static LocalizationManager Instance { get; } = new();

    private readonly ResourceManager _resourceManager;
    private CultureInfo _currentCulture;

    private LocalizationManager()
    {
        _resourceManager = Resources.ResourceManager;
        _currentCulture = CultureInfo.CurrentUICulture;
    }

    /// <summary>
    /// Gets or sets the current UI culture. Setting this triggers
    /// PropertyChanged so all bindings auto-refresh.
    /// </summary>
    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (_currentCulture.Name == value.Name) return;
            _currentCulture = value;
            CultureInfo.CurrentUICulture = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    /// <summary>
    /// Indexer for binding. Access localization keys as <c>LocalizationManager.Instance["key"]</c>.
    /// </summary>
    public string this[string key]
    {
        get
        {
            var value = _resourceManager.GetString(key, _currentCulture);
            return value ?? $"#{key}#";
        }
    }

    /// <summary>
    /// Get a localized formatted string.
    /// </summary>
    public string Get(string key, params object[] args)
    {
        var format = this[key];
        return args.Length > 0 ? string.Format(format, args) : format;
    }

    /// <summary>
    /// Switch language by culture code (e.g. "en", "zh-CN").
    /// </summary>
    public void Switch(string cultureCode)
    {
        CurrentCulture = new CultureInfo(cultureCode);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event NotifyCollectionChangedEventHandler? CollectionChanged;
}
