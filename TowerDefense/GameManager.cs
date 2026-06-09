using System.Numerics;

namespace TowerDefense;

/// <summary>
/// Core game logic for the tower defense game.
/// Manages state, waves, enemies, towers, projectiles — no rendering dependency.
/// </summary>
public class GameManager
{
    // ==================== Grid & Path ====================

    public int GridCols { get; private set; } = 20;
    public int GridRows { get; private set; } = 12;
    public float CellSize { get; private set; } = 1f;

    // Path waypoints — enemies follow these (world positions), loaded from map data
    private List<WaypointCell> _pathWaypointCells = new();

    // Precomputed path: list of world positions for each step
    public List<Vector3> PathPositions { get; } = new();
    public float TotalPathLength { get; private set; }

    // Which cells are on the path (non-buildable)
    public bool[,] PathCells { get; private set; } = new bool[20, 12];

    // Which cells have towers
    public bool[,] OccupiedCells { get; private set; } = new bool[20, 12];

    /// <summary>
    /// Returns the cell index (col, row) for a world position on the XZ plane, or null if out of bounds.
    /// </summary>
    public (int Col, int Row)? WorldToGrid(Vector3 worldPos)
    {
        int col = (int)MathF.Floor(worldPos.X / CellSize);
        int row = (int)MathF.Floor(worldPos.Z / CellSize);
        if (col < 0 || col >= GridCols || row < 0 || row >= GridRows)
            return null;
        return (col, row);
    }

    public Vector3 GridToWorld(int col, int row)
    {
        return new Vector3(col * CellSize + CellSize / 2, 0, row * CellSize + CellSize / 2);
    }

    public bool CanPlaceTower(int col, int row)
    {
        return !PathCells[col, row] && !OccupiedCells[col, row];
    }

    // ==================== Game State ====================

    public int Gold { get; private set; } = 200;
    public int Lives { get; private set; } = 20;
    public int CurrentWave { get; private set; } = 0; // 0 = not started, 1-5 = active
    public int TotalWaves => _waves.Count;
    public bool IsGameOver { get; private set; }
    public bool IsVictory { get; private set; }
    public bool HasStarted { get; private set; }

    // ==================== Collections ====================

    public List<TowerInstance> Towers { get; } = new();
    public List<EnemyInstance> Enemies { get; } = new();
    public List<ProjectileData> Projectiles { get; } = new();

    // Events for rendering layer
    public event Action<TowerInstance>? TowerAdded;
    public event Action<TowerInstance>? TowerRemoved;
    public event Action<EnemyInstance>? EnemyAdded;
    public event Action<EnemyInstance>? EnemyRemoved;
    public event Action<ProjectileData>? ProjectileAdded;
    public event Action<ProjectileData>? ProjectileRemoved;
    public event Action? GameStateChanged;
    public event Action? GameReset;

    // ==================== Internal State ====================

    private int _nextId;
    private readonly Random _rng = new(42);

    // Wave spawning state
    private List<WaveConfig> _waves = new();
    private float _waveTimer;
    private bool _waveInProgress;
    private int _currentEntryIndex;
    private int _spawnedInEntry;
    private float _spawnTimer;

    // Current loaded map
    public MapData CurrentMap { get; private set; } = null!;

    // ==================== Initialization ====================

    public GameManager()
    {
        LoadMap(MapData.CreateDefault());
    }

    /// <summary>
    /// Load a new map, rebuilding the path and resetting all game state.
    /// </summary>
    public void LoadMap(MapData map)
    {
        CurrentMap = map;

        // Apply map dimensions
        GridCols = map.GridCols;
        GridRows = map.GridRows;
        CellSize = map.CellSize;

        // Copy waypoints
        _pathWaypointCells = new List<WaypointCell>(map.PathWaypoints);

        // Reallocate grid arrays
        PathCells = new bool[GridCols, GridRows];
        OccupiedCells = new bool[GridCols, GridRows];

        // Build path from waypoints
        PathPositions.Clear();
        BuildPath();

        // Load wave config
        _waves = map.Waves.Select(w => new WaveConfig
        {
            WaveNumber = map.Waves.IndexOf(w) + 1,
            DelayBeforeWave = w.DelayBeforeWave,
            Entries = w.Entries.Select(e => new WaveEntry(
                Enum.TryParse<EnemyType>(e.EnemyType, out var type) ? type : EnemyType.Basic,
                e.Count,
                e.SpawnInterval
            )).ToList(),
        }).ToList();

        // Reset runtime state
        ResetState();
    }

    /// <summary>
    /// Reload the current map (resets game without changing map data).
    /// </summary>
    public void Reset()
    {
        // Broadcast removal for all existing objects so the view can clean up nodes
        foreach (var t in Towers.ToList()) TowerRemoved?.Invoke(t);
        foreach (var e in Enemies.ToList()) EnemyRemoved?.Invoke(e);
        foreach (var p in Projectiles.ToList()) ProjectileRemoved?.Invoke(p);

        Towers.Clear();
        Enemies.Clear();
        Projectiles.Clear();
        Array.Clear(OccupiedCells, 0, OccupiedCells.Length);

        ResetState();

        GameReset?.Invoke();
        GameStateChanged?.Invoke();
    }

    private void ResetState()
    {
        Gold = 200;
        Lives = 20;
        CurrentWave = 0;
        IsGameOver = false;
        IsVictory = false;
        HasStarted = false;
        _nextId = 0;
        _waveTimer = 0;
        _waveInProgress = false;
        _currentEntryIndex = 0;
        _spawnedInEntry = 0;
        _spawnTimer = 0;
    }

    private void BuildPath()
    {
        // Convert waypoint cells to a connected path through grid cells
        for (int w = 1; w < _pathWaypointCells.Count; w++)
        {
            var from = _pathWaypointCells[w - 1];
            var to = _pathWaypointCells[w];

            // Walk horizontally then vertically
            int stepX = Math.Sign(to.Col - from.Col);
            int stepZ = Math.Sign(to.Row - from.Row);

            int cx = from.Col, cz = from.Row;

            // Horizontal
            while (cx != to.Col)
            {
                cx += stepX;
                MarkPathCell(cx, cz);
            }

            // Vertical
            while (cz != to.Row)
            {
                cz += stepZ;
                MarkPathCell(cx, cz);
            }
        }

        // Build world-space path positions (center of each path cell, in order)
        for (int w = 1; w < _pathWaypointCells.Count; w++)
        {
            var from = _pathWaypointCells[w - 1];
            var to = _pathWaypointCells[w];

            int stepX = Math.Sign(to.Col - from.Col);
            int stepZ = Math.Sign(to.Row - from.Row);

            int cx = from.Col, cz = from.Row;

            while (cx != to.Col)
            {
                cx += stepX;
                if (cx >= 0 && cx < GridCols && cz >= 0 && cz < GridRows)
                    PathPositions.Add(new Vector3(cx + 0.5f, 0.05f, cz + 0.5f));
            }
            while (cz != to.Row)
            {
                cz += stepZ;
                if (cx >= 0 && cx < GridCols && cz >= 0 && cz < GridRows)
                    PathPositions.Add(new Vector3(cx + 0.5f, 0.05f, cz + 0.5f));
            }
        }

        // Add exit position
        var lastWp = _pathWaypointCells[^1];
        PathPositions.Add(new Vector3(lastWp.Col + 0.5f, 0.05f, lastWp.Row + 0.5f));

        // Compute total path length
        TotalPathLength = 0;
        for (int i = 1; i < PathPositions.Count; i++)
        {
            TotalPathLength += Vector3.Distance(PathPositions[i - 1], PathPositions[i]);
        }
    }

    private void MarkPathCell(int col, int row)
    {
        if (col >= 0 && col < GridCols && row >= 0 && row < GridRows)
            PathCells[col, row] = true;
    }

    // ==================== Player Actions ====================

    public bool TryPlaceTower(int col, int row, TowerType type)
    {
        if (!CanPlaceTower(col, row))
            return false;

        var def = TowerDefinition.Get(type);
        if (Gold < def.Cost)
            return false;

        Gold -= def.Cost;
        OccupiedCells[col, row] = true;

        var tower = new TowerInstance
        {
            Id = _nextId++,
            Def = def,
            GridCol = col,
            GridRow = row,
            Position = GridToWorld(col, row),
        };

        Towers.Add(tower);
        TowerAdded?.Invoke(tower);
        GameStateChanged?.Invoke();
        return true;
    }

    public void StartGame()
    {
        if (HasStarted) return;
        HasStarted = true;
        StartNextWave();
    }

    // ==================== Update Loop ====================

    public void Update(double deltaTime)
    {
        if (IsGameOver || !HasStarted) return;

        float dt = (float)deltaTime;

        UpdateWaveSpawning(dt);
        UpdateEnemies(dt);
        UpdateTowers(dt);
        UpdateProjectiles(dt);
        UpdateDotEffects(dt);
        CheckGameOver();
    }

    private void UpdateWaveSpawning(float dt)
    {
        if (CurrentWave > TotalWaves) return;

        if (!_waveInProgress)
        {
            _waveTimer += dt;
            if (_waveTimer >= _waves[CurrentWave - 1].DelayBeforeWave)
            {
                _waveInProgress = true;
                _currentEntryIndex = 0;
                _spawnedInEntry = 0;
                _spawnTimer = 0;
            }
            return;
        }

        var wave = _waves[CurrentWave - 1];

        // Check if all entries are done
        if (_currentEntryIndex >= wave.Entries.Count)
        {
            // Wave complete — wait for all enemies to be cleared
            if (Enemies.Count == 0)
            {
                _waveInProgress = false;
                StartNextWave();
            }
            return;
        }

        var entry = wave.Entries[_currentEntryIndex];
        _spawnTimer += dt;

        while (_spawnedInEntry < entry.Count && _spawnTimer >= entry.SpawnInterval)
        {
            _spawnTimer -= entry.SpawnInterval;
            SpawnEnemy(entry.Type);
            _spawnedInEntry++;
        }

        if (_spawnedInEntry >= entry.Count)
        {
            _currentEntryIndex++;
            _spawnedInEntry = 0;
            _spawnTimer = 0;
        }
    }

    private void StartNextWave()
    {
        CurrentWave++;
        _waveTimer = 0;
        _waveInProgress = false;
        GameStateChanged?.Invoke();

        if (CurrentWave > TotalWaves)
        {
            // All waves completed — victory when all enemies are dead
            if (Enemies.Count == 0)
            {
                IsGameOver = true;
                IsVictory = true;
                GameStateChanged?.Invoke();
            }
        }
    }

    private void SpawnEnemy(EnemyType type)
    {
        var def = EnemyDefinition.Get(type);
        var enemy = new EnemyInstance
        {
            Id = _nextId++,
            Def = def,
            HP = def.MaxHP,
            MaxHP = def.MaxHP,
            Speed = def.Speed,
            PathDistance = 0,
            Position = PathPositions.Count > 0 ? PathPositions[0] : Vector3.Zero,
        };

        Enemies.Add(enemy);
        EnemyAdded?.Invoke(enemy);
    }

    private void UpdateEnemies(float dt)
    {
        for (int i = Enemies.Count - 1; i >= 0; i--)
        {
            var enemy = Enemies[i];
            if (enemy.IsDead || enemy.ReachedEnd) continue;

            // Apply slow
            if (enemy.SlowTimer > 0)
            {
                enemy.SlowTimer -= dt;
                if (enemy.SlowTimer <= 0)
                    enemy.SlowFactor = 1.0f;
            }

            float speed = enemy.Speed * enemy.SlowFactor;
            enemy.PathDistance += speed * dt;

            // Convert path distance to world position
            enemy.Position = GetPositionOnPath(enemy.PathDistance, out bool reachedEnd);

            if (reachedEnd)
            {
                enemy.ReachedEnd = true;
                Lives = Math.Max(0, Lives - 1);
                EnemyRemoved?.Invoke(enemy);
                Enemies.RemoveAt(i);
                GameStateChanged?.Invoke();
            }
        }
    }

    private Vector3 GetPositionOnPath(float distance, out bool reachedEnd)
    {
        reachedEnd = false;
        if (PathPositions.Count < 2)
            return PathPositions.Count > 0 ? PathPositions[0] : Vector3.Zero;

        float traveled = 0;
        for (int i = 1; i < PathPositions.Count; i++)
        {
            float segmentLength = Vector3.Distance(PathPositions[i - 1], PathPositions[i]);
            if (distance <= traveled + segmentLength)
            {
                float t = (distance - traveled) / segmentLength;
                return Vector3.Lerp(PathPositions[i - 1], PathPositions[i], t);
            }
            traveled += segmentLength;
        }

        reachedEnd = true;
        return PathPositions[^1];
    }

    private void UpdateTowers(float dt)
    {
        foreach (var tower in Towers)
        {
            // Sun tower — continuous AOE, no projectile
            if (tower.Def.AoeRadius > 0)
            {
                for (int i = Enemies.Count - 1; i >= 0; i--)
                {
                    var enemy = Enemies[i];
                    if (enemy.IsDead || enemy.ReachedEnd) continue;
                    float dist = Vector3.Distance(tower.Position, enemy.Position);
                    if (dist <= tower.Def.AoeRadius)
                    {
                        DamageEnemy(enemy, tower.Def.Damage * dt);
                    }
                }
                continue;
            }

            // Collect all enemies in range, sorted by path distance (descending)
            var targetsInRange = new List<EnemyInstance>();
            foreach (var enemy in Enemies)
            {
                if (enemy.IsDead || enemy.ReachedEnd) continue;
                float dist = Vector3.Distance(tower.Position, enemy.Position);
                if (dist <= tower.Def.Range)
                    targetsInRange.Add(enemy);
            }
            targetsInRange.Sort((a, b) => b.PathDistance.CompareTo(a.PathDistance));

            if (targetsInRange.Count == 0)
            {
                tower.Target = null;
                continue;
            }

            tower.Target = targetsInRange[0];

            // Fire logic
            tower.LastFireTime += dt;
            float fireInterval = 1.0f / tower.Def.FireRate;

            if (tower.LastFireTime >= fireInterval)
            {
                tower.LastFireTime = 0;

                int shotCount = tower.Def.MultiShotCount;
                if (shotCount > targetsInRange.Count)
                    shotCount = targetsInRange.Count;

                if (shotCount <= 1)
                {
                    FireProjectile(tower, targetsInRange[0]);
                }
                else
                {
                    // Multi-shot: fire at top N targets in a fan pattern
                    float halfArc = tower.Def.ArcAngle * (shotCount - 1) / 2f;
                    for (int i = 0; i < shotCount; i++)
                    {
                        float angle = -halfArc + i * tower.Def.ArcAngle;
                        FireProjectile(tower, targetsInRange[i], angleDeg: angle);
                    }
                }
            }
        }
    }

    private void FireProjectile(TowerInstance tower, EnemyInstance target, float angleDeg = 0)
    {
        var dir = target.Position - tower.Position;
        Vector3 initialDir;

        if (angleDeg != 0)
        {
            // Rotate direction around Y axis by angleDeg
            float rad = angleDeg * MathF.PI / 180f;
            float cos = MathF.Cos(rad);
            float sin = MathF.Sin(rad);
            initialDir = new Vector3(
                dir.X * cos - dir.Z * sin,
                dir.Y,
                dir.X * sin + dir.Z * cos
            );
        }
        else
        {
            initialDir = dir;
        }

        // Spawn projectile with a small offset in the fire direction
        var spawnPos = tower.Position + new Vector3(0, 0.6f, 0) + Vector3.Normalize(initialDir) * 0.3f;

        var projectile = new ProjectileData
        {
            Id = _nextId++,
            Position = spawnPos,
            TargetEnemyId = target.Id,
            TargetPosition = target.Position,
            Damage = tower.Def.Damage,
            Speed = tower.Def.ProjectileSpeed,
            SplashRadius = tower.Def.SplashRadius,
            SlowAmount = tower.Def.SlowAmount,
            Color = tower.Def.Color,
            CritChance = tower.Def.CritChance,
            CritMultiplier = tower.Def.CritMultiplier,
            DotDamage = tower.Def.DotDamage,
            DotDuration = tower.Def.DotDuration,
        };

        Projectiles.Add(projectile);
        ProjectileAdded?.Invoke(projectile);
    }

    private void UpdateProjectiles(float dt)
    {
        for (int i = Projectiles.Count - 1; i >= 0; i--)
        {
            var proj = Projectiles[i];
            if (proj.IsDead) continue;

            // Try to track the target enemy
            var targetEnemy = Enemies.FirstOrDefault(e => e.Id == proj.TargetEnemyId);
            if (targetEnemy != null && !targetEnemy.IsDead && !targetEnemy.ReachedEnd)
            {
                proj.TargetPosition = targetEnemy.Position;
            }

            Vector3 dir = proj.TargetPosition - proj.Position;
            float dist = dir.Length();

            if (dist < 0.3f)
            {
                // Hit!
                ApplyProjectileDamage(proj);
                proj.IsDead = true;
                ProjectileRemoved?.Invoke(proj);
                Projectiles.RemoveAt(i);
            }
            else
            {
                proj.Position += Vector3.Normalize(dir) * proj.Speed * dt;
            }
        }
    }

    private void ApplyProjectileDamage(ProjectileData proj)
    {
        var target = Enemies.FirstOrDefault(e => e.Id == proj.TargetEnemyId);

        // Calculate damage with crit
        float finalDamage = proj.Damage;
        bool crit = proj.CritChance > 0 && _rng.NextDouble() < proj.CritChance;
        if (crit)
            finalDamage *= proj.CritMultiplier;

        if (proj.SplashRadius > 0)
        {
            // Splash damage
            foreach (var enemy in Enemies.ToList())
            {
                if (enemy.IsDead || enemy.ReachedEnd) continue;
                if (Vector3.Distance(enemy.Position, proj.Position) <= proj.SplashRadius)
                {
                    DamageEnemy(enemy, finalDamage);
                    ApplyDot(enemy, proj.DotDamage, proj.DotDuration);
                }
            }
        }
        else if (target != null)
        {
            DamageEnemy(target, finalDamage);
            ApplyDot(target, proj.DotDamage, proj.DotDuration);
        }

        // Apply slow to all enemies in range
        if (proj.SlowAmount > 0)
        {
            float slowRadius = Math.Max(proj.SplashRadius, 0.8f);
            foreach (var enemy in Enemies)
            {
                if (enemy.IsDead || enemy.ReachedEnd) continue;
                if (Vector3.Distance(enemy.Position, proj.Position) <= slowRadius)
                {
                    enemy.SlowFactor = proj.SlowAmount;
                    enemy.SlowTimer = 2.0f;
                }
            }
        }
    }

    private void ApplyDot(EnemyInstance enemy, float dotDamage, float dotDuration)
    {
        if (dotDamage <= 0 || dotDuration <= 0) return;
        // Only refresh if the new DOT is stronger
        if (dotDamage >= enemy.DotDamage)
        {
            enemy.DotDamage = dotDamage;
            enemy.DotTimer = dotDuration;
        }
    }

    private void UpdateDotEffects(float dt)
    {
        for (int i = Enemies.Count - 1; i >= 0; i--)
        {
            var enemy = Enemies[i];
            if (enemy.IsDead || enemy.ReachedEnd) continue;
            if (enemy.DotTimer > 0)
            {
                DamageEnemy(enemy, enemy.DotDamage * dt);
                enemy.DotTimer -= dt;
                if (enemy.DotTimer <= 0)
                {
                    enemy.DotDamage = 0;
                    enemy.DotTimer = 0;
                }
            }
        }
    }

    private void DamageEnemy(EnemyInstance enemy, float damage)
    {
        enemy.HP -= damage;
        if (enemy.HP <= 0)
        {
            enemy.HP = 0;
            enemy.IsDead = true;
            Gold += enemy.Def.GoldReward;
            EnemyRemoved?.Invoke(enemy);
            Enemies.Remove(enemy);
            GameStateChanged?.Invoke();
        }
    }

    private void CheckGameOver()
    {
        if (Lives <= 0)
        {
            IsGameOver = true;
            IsVictory = false;
            GameStateChanged?.Invoke();
        }

        // Check victory: all waves done and no enemies left
        if (CurrentWave > TotalWaves && Enemies.Count == 0 && !IsGameOver)
        {
            IsGameOver = true;
            IsVictory = true;
            GameStateChanged?.Invoke();
        }
    }
}
