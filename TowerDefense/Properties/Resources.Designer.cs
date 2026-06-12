#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Resources;

namespace TowerDefense.Properties;

/// <summary>
/// Provides access to the embedded .resx resources via a <see cref="ResourceManager"/>.
/// This is a manually maintained counterpart to the auto-generated designer file
/// that Visual Studio would normally create from Resources.resx.
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
public static class Resources
{
    private static ResourceManager? _resourceManager;

    /// <summary>
    /// Returns the cached ResourceManager instance used by the localizer.
    /// The base name must match the root namespace + folder path of the .resx files.
    /// </summary>
    public static ResourceManager ResourceManager
    {
        get
        {
            if (_resourceManager is null)
            {
                _resourceManager = new ResourceManager(
                    "TowerDefense.Properties.Resources",
                    typeof(Resources).Assembly);
            }
            return _resourceManager;
        }
    }
}
