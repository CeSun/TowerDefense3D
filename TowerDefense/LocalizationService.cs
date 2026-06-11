namespace TowerDefense;

/// <summary>
/// Static helper for accessing localized strings from code-behind.
/// Delegates to <see cref="LocalizationManager.Instance"/>.
/// </summary>
public static class Loc
{
    /// <summary>
    /// Get a localized string by key.
    /// </summary>
    public static string Get(string key) => LocalizationManager.Instance[key];

    /// <summary>
    /// Get a formatted localized string by key.
    /// </summary>
    public static string Get(string key, params object[] args) => LocalizationManager.Instance.Get(key, args);

    /// <summary>
    /// Switch the current language.
    /// </summary>
    /// <param name="culture">Culture code, e.g. "en", "zh-CN"</param>
    public static void Switch(string culture) => LocalizationManager.Instance.Switch(culture);
}
