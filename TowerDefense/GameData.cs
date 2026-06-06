using System.Drawing;
using System.Numerics;

namespace TowerDefense;

// ==================== Tower Types ====================

public enum TowerType { Arrow, Cannon, Ice }

public record TowerDefinition(
    TowerType Type,
    string Name,
    int Cost,
    float Damage,
    float Range,
    float FireRate,
    float ProjectileSpeed,
    Color Color,
    float SplashRadius = 0,
    float SlowAmount = 0
)
{
    public static readonly TowerDefinition Arrow = new(
        Type: TowerType.Arrow,
        Name: "Arrow Tower",
        Cost: 50,
        Damage: 15,
        Range: 3.5f,
        FireRate: 1.8f,
        ProjectileSpeed: 5f,
        Color: Color.LimeGreen
    );

    public static readonly TowerDefinition Cannon = new(
        Type: TowerType.Cannon,
        Name: "Cannon Tower",
        Cost: 100,
        Damage: 40,
        Range: 3.0f,
        FireRate: 0.6f,
        ProjectileSpeed: 3f,
        Color: Color.OrangeRed,
        SplashRadius: 1.5f
    );

    public static readonly TowerDefinition Ice = new(
        Type: TowerType.Ice,
        Name: "Ice Tower",
        Cost: 75,
        Damage: 8,
        Range: 2.8f,
        FireRate: 1.0f,
        ProjectileSpeed: 4f,
        Color: Color.Cyan,
        SlowAmount: 0.5f
    );

    public static TowerDefinition Get(TowerType type) => type switch
    {
        TowerType.Arrow => Arrow,
        TowerType.Cannon => Cannon,
        TowerType.Ice => Ice,
        _ => Arrow
    };
}

// ==================== Enemy Types ====================

public enum EnemyType { Basic, Fast, Tank }

public record EnemyDefinition(
    EnemyType Type,
    string Name,
    float MaxHP,
    float Speed,
    int GoldReward,
    Color Color,
    float Radius = 0.25f
)
{
    public static readonly EnemyDefinition Basic = new(
        Type: EnemyType.Basic,
        Name: "Basic",
        MaxHP: 100,
        Speed: 1.8f,
        GoldReward: 10,
        Color: Color.Crimson,
        Radius: 0.22f
    );

    public static readonly EnemyDefinition Fast = new(
        Type: EnemyType.Fast,
        Name: "Fast",
        MaxHP: 60,
        Speed: 3.5f,
        GoldReward: 15,
        Color: Color.Gold,
        Radius: 0.18f
    );

    public static readonly EnemyDefinition Tank = new(
        Type: EnemyType.Tank,
        Name: "Tank",
        MaxHP: 350,
        Speed: 1.0f,
        GoldReward: 30,
        Color: Color.DarkViolet,
        Radius: 0.30f
    );

    public static EnemyDefinition Get(EnemyType type) => type switch
    {
        EnemyType.Basic => Basic,
        EnemyType.Fast => Fast,
        EnemyType.Tank => Tank,
        _ => Basic
    };
}

// ==================== Runtime Game Objects ====================

public class TowerInstance
{
    public int Id { get; set; }
    public TowerDefinition Def { get; set; } = TowerDefinition.Arrow;
    public int GridCol { get; set; }
    public int GridRow { get; set; }
    public Vector3 Position { get; set; }
    public float LastFireTime { get; set; } = 0;
    public EnemyInstance? Target { get; set; }
}

public class EnemyInstance
{
    public int Id { get; set; }
    public EnemyDefinition Def { get; set; } = EnemyDefinition.Basic;
    public float HP { get; set; }
    public float MaxHP { get; set; }
    public float Speed { get; set; }
    public float PathDistance { get; set; } // how far along the path
    public Vector3 Position { get; set; }
    public bool IsDead { get; set; }
    public bool ReachedEnd { get; set; }
    public float SlowTimer { get; set; }
    public float SlowFactor { get; set; } = 1.0f; // 1 = normal, 0.5 = half speed
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
}

// ==================== Wave Configuration ====================

public record WaveEntry(EnemyType Type, int Count, float SpawnInterval);

public class WaveConfig
{
    public int WaveNumber { get; init; }
    public List<WaveEntry> Entries { get; init; } = new();
    public float DelayBeforeWave { get; init; } = 5f; // seconds before wave starts

    public static List<WaveConfig> DefaultWaves = new()
    {
        new WaveConfig
        {
            WaveNumber = 1,
            DelayBeforeWave = 3f,
            Entries = { new(EnemyType.Basic, 8, 1.2f) }
        },
        new WaveConfig
        {
            WaveNumber = 2,
            DelayBeforeWave = 5f,
            Entries = { new(EnemyType.Basic, 6, 0.9f), new(EnemyType.Fast, 4, 1.5f) }
        },
        new WaveConfig
        {
            WaveNumber = 3,
            DelayBeforeWave = 5f,
            Entries = { new(EnemyType.Basic, 8, 0.7f), new(EnemyType.Fast, 6, 1.0f), new(EnemyType.Tank, 2, 2.0f) }
        },
        new WaveConfig
        {
            WaveNumber = 4,
            DelayBeforeWave = 5f,
            Entries = { new(EnemyType.Fast, 10, 0.8f), new(EnemyType.Tank, 4, 1.5f) }
        },
        new WaveConfig
        {
            WaveNumber = 5,
            DelayBeforeWave = 5f,
            Entries = { new(EnemyType.Basic, 10, 0.5f), new(EnemyType.Fast, 8, 0.7f), new(EnemyType.Tank, 6, 1.2f) }
        },
    };
}
