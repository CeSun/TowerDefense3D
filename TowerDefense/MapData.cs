using System.Text.Json;
using System.Text.Json.Serialization;

namespace TowerDefense;

// ==================== Map Data Model ====================

/// <summary>
/// Serializable map definition loaded from JSON files.
/// </summary>
public class MapData
{
    public string Name { get; set; } = "Untitled";
    public int GridCols { get; set; } = 20;
    public int GridRows { get; set; } = 12;
    public float CellSize { get; set; } = 1f;
    public WaypointCell? StartCell { get; set; }
    public WaypointCell? EndCell { get; set; }
    public List<WaypointCell> PathWaypoints { get; set; } = new();
    public List<WaveConfigData> Waves { get; set; } = new();

    /// <summary>True when StartCell, EndCell, and at least one wave are configured.</summary>
    [JsonIgnore]
    public bool IsComplete => StartCell != null && EndCell != null && Waves.Count > 0;

    // ==================== Serialization ====================

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = MapDataJsonContext.Default,
    };

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public static MapData? FromJson(string json)
    {
        return JsonSerializer.Deserialize(json, MapDataJsonContext.Default.MapData);
    }

    public void SaveToFile(string filePath)
    {
        var json = ToJson();
        File.WriteAllText(filePath, json);
    }

    public static MapData? LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;
        var json = File.ReadAllText(filePath);
        return FromJson(json);
    }

    // ==================== Default Map ====================

    /// <summary>
    /// Returns the built-in default map (matching the original hardcoded data).
    /// </summary>
    public static MapData CreateDefault()
    {
        return new MapData
        {
            Name = "Default Map",
            GridCols = 20,
            GridRows = 12,
            CellSize = 1f,
            StartCell = new WaypointCell(0, 6),
            EndCell = new WaypointCell(19, 6),
            PathWaypoints = new List<WaypointCell>
            {
                new(3, 6), new(3, 2), new(9, 2),
                new(9, 10), new(15, 10), new(15, 6),
            },
            Waves = new List<WaveConfigData>
            {
                new()
                {
                    DelayBeforeWave = 3f,
                    Entries = { new("Basic", 8, 1.2f) },
                },
                new()
                {
                    DelayBeforeWave = 5f,
                    Entries = { new("Basic", 6, 0.9f), new("Fast", 4, 1.5f) },
                },
                new()
                {
                    DelayBeforeWave = 5f,
                    Entries = { new("Basic", 8, 0.7f), new("Fast", 6, 1.0f), new("Tank", 2, 2.0f) },
                },
                new()
                {
                    DelayBeforeWave = 5f,
                    Entries = { new("Fast", 10, 0.8f), new("Tank", 4, 1.5f) },
                },
                new()
                {
                    DelayBeforeWave = 5f,
                    Entries = { new("Basic", 10, 0.5f), new("Fast", 8, 0.7f), new("Tank", 6, 1.2f) },
                },
            },
        };
    }
}

// ==================== Sub-Models ====================

public record WaypointCell(int Col, int Row);

public class WaveConfigData
{
    public float DelayBeforeWave { get; set; } = 5f;
    public List<WaveEntryData> Entries { get; set; } = new();
}

public record WaveEntryData(string EnemyType, int Count, float SpawnInterval);

// ==================== Custom Enemy Data ====================

/// <summary>
/// Serializable enemy definition. Stored as JSON in the Enemies directory.
/// Can be used alongside built-in enemy types in wave configurations.
/// </summary>
public class EnemyData
{
    public string Name { get; set; } = "New Enemy";
    public float MaxHP { get; set; } = 100f;
    public float Speed { get; set; } = 1.8f;
    public int GoldReward { get; set; } = 10;
    public int ColorR { get; set; } = 220;
    public int ColorG { get; set; } = 20;
    public int ColorB { get; set; } = 60;
    public float Radius { get; set; } = 0.25f;

    public System.Drawing.Color GetColor() =>
        System.Drawing.Color.FromArgb(ColorR, ColorG, ColorB);

    public EnemyDefinition ToDefinition() => EnemyDefinition.FromEnemyData(this);

    // ==================== Serialization ====================

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = MapDataJsonContext.Default,
    };

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public static EnemyData? FromJson(string json)
    {
        return JsonSerializer.Deserialize(json, MapDataJsonContext.Default.EnemyData);
    }

    // ==================== File I/O ====================

    public void SaveToFile(string filePath)
    {
        var json = ToJson();
        var dir = Path.GetDirectoryName(filePath);
        if (dir != null) Directory.CreateDirectory(dir);
        File.WriteAllText(filePath, json);
    }

    public static EnemyData? LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;
        var json = File.ReadAllText(filePath);
        return FromJson(json);
    }

    /// <summary>List all custom enemy names found in the given directory.</summary>
    public static List<string> ListNames(string customEnemiesDir)
    {
        var names = new List<string>();
        if (!Directory.Exists(customEnemiesDir))
            return names;

        foreach (var file in Directory.GetFiles(customEnemiesDir, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name != "_save")
                names.Add(name);
        }
        names.Sort();
        return names;
    }
}

// ==================== Custom Tower Data ====================

/// <summary>
/// Serializable tower definition. Stored as JSON in the Towers directory.
/// Can be used to create custom tower types alongside built-in ones.
/// </summary>
public class TowerData
{
    public string Name { get; set; } = "New Tower";
    public int Cost { get; set; } = 50;
    public float Damage { get; set; } = 15f;
    public float Range { get; set; } = 3.5f;
    public float FireRate { get; set; } = 1.8f;
    public float ProjectileSpeed { get; set; } = 5f;
    public int ColorR { get; set; } = 50;
    public int ColorG { get; set; } = 205;
    public int ColorB { get; set; } = 50;
    public float SplashRadius { get; set; }
    public float SlowAmount { get; set; }
    public int MultiShotCount { get; set; } = 1;
    public float ArcAngle { get; set; }
    public float CritChance { get; set; }
    public float CritMultiplier { get; set; } = 2.0f;
    public float DotDamage { get; set; }
    public float DotDuration { get; set; }
    public float AoeRadius { get; set; }

    /// <summary>
    /// Explicit visual style for the 3D model. One of: Auto, Arrow, Cannon, Sniper, MultiShot, Poison, Sun.
    /// "Auto" (default) derives the visual from stats.
    /// </summary>
    public string VisualStyle { get; set; } = "Auto";

    public System.Drawing.Color GetColor() =>
        System.Drawing.Color.FromArgb(ColorR, ColorG, ColorB);

    public TowerDefinition ToDefinition() => TowerDefinition.FromTowerData(this);

    // ==================== Serialization ====================

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = MapDataJsonContext.Default,
    };

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public static TowerData? FromJson(string json)
    {
        return JsonSerializer.Deserialize(json, MapDataJsonContext.Default.TowerData);
    }

    // ==================== File I/O ====================

    public void SaveToFile(string filePath)
    {
        var json = ToJson();
        var dir = Path.GetDirectoryName(filePath);
        if (dir != null) Directory.CreateDirectory(dir);
        File.WriteAllText(filePath, json);
    }

    public static TowerData? LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;
        var json = File.ReadAllText(filePath);
        return FromJson(json);
    }

    /// <summary>List all tower names found in the given directory.</summary>
    public static List<string> ListNames(string dir)
    {
        var names = new List<string>();
        if (!Directory.Exists(dir))
            return names;

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name != "_save")
                names.Add(name);
        }
        names.Sort();
        return names;
    }
}

// ==================== Save Data ====================

/// <summary>
/// Minimal player-progress save file stored alongside map files.
/// </summary>
public record SaveData(int HighestUnlockedLevel = 1)
{
    private const string FileName = "_save.json";

    public static string GetSavePath(string mapsDir) => Path.Combine(mapsDir, FileName);

    public static SaveData Load(string mapsDir)
    {
        var path = GetSavePath(mapsDir);
        if (!File.Exists(path))
            return new SaveData();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, MapDataJsonContext.Default.SaveData) ?? new SaveData();
        }
        catch
        {
            return new SaveData();
        }
    }

    public void Save(string mapsDir)
    {
        var path = GetSavePath(mapsDir);
        var json = JsonSerializer.Serialize(this, MapDataJsonContext.Default.SaveData);
        Directory.CreateDirectory(mapsDir);
        File.WriteAllText(path, json);
    }
}

// ==================== AOT-Compatible JSON Source Generation ====================

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(MapData))]
[JsonSerializable(typeof(WaypointCell))]
[JsonSerializable(typeof(WaveConfigData))]
[JsonSerializable(typeof(WaveEntryData))]
[JsonSerializable(typeof(List<WaypointCell>))]
[JsonSerializable(typeof(List<WaveConfigData>))]
[JsonSerializable(typeof(List<WaveEntryData>))]
[JsonSerializable(typeof(EnemyData))]
[JsonSerializable(typeof(TowerData))]
[JsonSerializable(typeof(SaveData))]
public partial class MapDataJsonContext : JsonSerializerContext
{
}
