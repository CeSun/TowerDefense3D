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
    public bool IsComplete => StartCell != null && EndCell != null && Waves.Count > 0;

    // ==================== Serialization ====================

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public static MapData? FromJson(string json)
    {
        return JsonSerializer.Deserialize<MapData>(json, JsonOptions);
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
