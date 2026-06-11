using System.Drawing;
using System.Numerics;

namespace TowerDefense;

// ==================== Tower Definition ====================

/// <summary>
/// Runtime tower definition. All towers are loaded from JSON config files
/// (built-in or custom) — no hardcoded types.
/// </summary>
public record TowerDefinition(
    string Name,
    int Cost,
    float Damage,
    float Range,
    float FireRate,
    float ProjectileSpeed,
    float SplashRadius = 0,
    float SlowAmount = 0,
    int MultiShotCount = 1,
    float ArcAngle = 0,
    float CritChance = 0,
    float CritMultiplier = 2.0f,
    float DotDamage = 0,
    float DotDuration = 0,
    float AoeRadius = 0,
    List<TowerShapeData>? Shapes = null
)
{
    /// <summary>Fallback definition used when a tower name cannot be resolved.</summary>
    public static readonly TowerDefinition Default = new(
        Name: "Arrow Tower",
        Cost: 50,
        Damage: 15,
        Range: 3.5f,
        FireRate: 1.8f,
        ProjectileSpeed: 5f,
        Shapes: new List<TowerShapeData>
        {
            new() { Type = "Box", ScaleX = 0.55f, ScaleY = 0.1f, ScaleZ = 0.55f, ColorR = 30, ColorG = 120, ColorB = 30 },
            new() { Type = "Cylinder", ScaleX = 0.3f, ScaleY = 0.4f, ScaleZ = 0.3f, ColorR = 50, ColorG = 205, ColorB = 50 },
            new() { Type = "Cylinder", ScaleX = 0.08f, ScaleY = 0.25f, ScaleZ = 0.08f, ColorR = 30, ColorG = 120, ColorB = 30 },
        }
    );

    /// <summary>Derived projectile color: first shape's color, or white.</summary>
    public Color Color => Shapes is { Count: > 0 } ? Shapes[0].GetColor() : Color.White;

    /// <summary>All loaded tower definitions, keyed by name.</summary>
    public static readonly Dictionary<string, TowerDefinition> All = new();

    /// <summary>
    /// Resolve a tower definition by name (case-sensitive).
    /// Returns <see cref="Default"/> if not found.
    /// </summary>
    public static TowerDefinition Resolve(string name)
    {
        if (All.TryGetValue(name, out var def))
            return def;
        return Default;
    }

    /// <summary>Create a TowerDefinition from serializable TowerData.</summary>
    public static TowerDefinition FromTowerData(TowerData data) => new(
        Name: data.Name,
        Cost: data.Cost,
        Damage: data.Damage,
        Range: data.Range,
        FireRate: data.FireRate,
        ProjectileSpeed: data.ProjectileSpeed,
        SplashRadius: data.SplashRadius,
        SlowAmount: data.SlowAmount,
        MultiShotCount: data.MultiShotCount,
        ArcAngle: data.ArcAngle,
        CritChance: data.CritChance,
        CritMultiplier: data.CritMultiplier,
        DotDamage: data.DotDamage,
        DotDuration: data.DotDuration,
        AoeRadius: data.AoeRadius,
        Shapes: data.Shapes.Select(s => s.Clone()).ToList()
    );
}

// ==================== Enemy Definition ====================

/// <summary>
/// Runtime enemy definition. All enemies are loaded from JSON config files
/// (built-in or custom) — no hardcoded types.
/// </summary>
public record EnemyDefinition(
    string Name,
    float MaxHP,
    float Speed,
    int GoldReward,
    Color Color,
    float Radius = 0.25f
)
{
    /// <summary>Fallback definition used when an enemy name cannot be resolved.</summary>
    public static readonly EnemyDefinition Default = new(
        Name: "Unknown",
        MaxHP: 100,
        Speed: 1.8f,
        GoldReward: 10,
        Color: Color.Gray,
        Radius: 0.25f
    );

    /// <summary>All loaded enemy definitions, keyed by name.</summary>
    public static readonly Dictionary<string, EnemyDefinition> All = new();

    /// <summary>
    /// Resolve an enemy definition by name (case-sensitive).
    /// Returns <see cref="Default"/> if not found.
    /// </summary>
    public static EnemyDefinition Resolve(string name)
    {
        if (All.TryGetValue(name, out var def))
            return def;
        return Default;
    }

    /// <summary>Create an EnemyDefinition from serializable EnemyData.</summary>
    public static EnemyDefinition FromEnemyData(EnemyData data) => new(
        Name: data.Name,
        MaxHP: data.MaxHP,
        Speed: data.Speed,
        GoldReward: data.GoldReward,
        Color: data.GetColor(),
        Radius: data.Radius
    );
}

// ==================== Runtime Game Objects ====================

public class TowerInstance
{
    public int Id { get; set; }
    public string TowerName { get; set; } = "Arrow Tower";
    public TowerDefinition Def { get; set; } = TowerDefinition.Default;
    public int GridCol { get; set; }
    public int GridRow { get; set; }
    public Vector3 Position { get; set; }
    public float LastFireTime { get; set; } = 0;
    public EnemyInstance? Target { get; set; }
}

public class EnemyInstance
{
    public int Id { get; set; }
    public EnemyDefinition Def { get; set; } = EnemyDefinition.Default;
    public float HP { get; set; }
    public float MaxHP { get; set; }
    public float Speed { get; set; }
    public float PathDistance { get; set; } // how far along the path
    public Vector3 Position { get; set; }
    public bool IsDead { get; set; }
    public bool ReachedEnd { get; set; }
    public float SlowTimer { get; set; }
    public float SlowFactor { get; set; } = 1.0f; // 1 = normal, 0.5 = half speed
    public float DotDamage { get; set; } // damage per second from poison
    public float DotTimer { get; set; } // remaining DOT duration
}

public class ProjectileData
{
    public int Id { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 TargetPosition { get; set; }
    public int TargetEnemyId { get; set; }
    public float Damage { get; set; }
    public float Speed { get; set; }
    public bool IsDead { get; set; }
    public float SplashRadius { get; set; }
    public float SlowAmount { get; set; }
    public Color Color { get; set; }
    public float CritChance { get; set; }
    public float CritMultiplier { get; set; } = 2.0f;
    public float DotDamage { get; set; }
    public float DotDuration { get; set; }
}

// ==================== Wave Configuration ====================

public record WaveEntry(string EnemyName, int Count, float SpawnInterval);

public class WaveConfig
{
    public int WaveNumber { get; init; }
    public List<WaveEntry> Entries { get; init; } = new();
    public float DelayBeforeWave { get; init; } = 5f; // seconds before wave starts

}
