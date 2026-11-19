using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

public enum WaveEnemyKind : byte { LungeMan, Giant, RedNinja, TrueHealer, GiantKing, BlackNinja, ArcaneEye, Wizard }

[DefaultExecutionOrder(-200)]
public sealed class WaveDirector : MonoBehaviour
{
    public const int MaximumConcurrentEnemies = 32;

    [Serializable]
    public sealed class EnemyGroup
    {
        public WaveEnemyKind enemy;
        [Min(0)] public int count = 1;
    }

    [Serializable]
    internal sealed class LegacySubWave
    {
        public EnemyGroup[] enemies;
    }

    [Serializable]
    public sealed class Wave
    {
        public string name = "New wave";
        [Min(0f)] public float delayBeforeStart = 4f;
        [Range(1, MaximumConcurrentEnemies)] public int maxConcurrentEnemies;
        public EnemyGroup[] enemies = Array.Empty<EnemyGroup>();
        [SerializeField, HideInInspector, FormerlySerializedAs("subWaves")]
        internal LegacySubWave[] legacySubWaves;
    }

    [Serializable]
    public sealed class EnemyPrefab
    {
        public WaveEnemyKind enemy;
        public CombatEnemy prefab;
    }

    public enum WaveStage : byte { Stopped, Countdown, Spawning, Fighting, Complete }

    [Header("Enemy prefabs")]
    [SerializeField] private EnemyPrefab[] enemyPrefabs = Array.Empty<EnemyPrefab>();
    [Header("Waves - each wave waits until every enemy is defeated")]
    [SerializeField] private Wave[] waves = Array.Empty<Wave>();
    [Header("Spawning")]
    [SerializeField] private bool startAutomatically = true;
    [SerializeField, HideInInspector, FormerlySerializedAs("maxConcurrentEnemies")]
    private int legacyConcurrentLimit = 8;
    [SerializeField] private Transform[] spawnPoints = Array.Empty<Transform>();
    [SerializeField, Min(1f)] private float spawnRadius = 13f;
    [SerializeField, Min(0f)] private float minimumPlayerDistance = 6f;
    [SerializeField, Min(0f)] private float minimumEnemyDistance = 1.8f;
    [SerializeField, Min(0f)] private float corpseLifetime = 1.2f;
    [SerializeField] private GameObject[] sceneEnemiesToReplace = Array.Empty<GameObject>();
    [Header("HUD")]
    [SerializeField] private Text statusLabel;

    internal sealed class SpawnedEnemy
    {
        internal ushort Id;
        internal WaveEnemyKind Kind;
        internal CombatEnemy Enemy;
        internal int WaveIndex;
        internal float DiedAt = -1f;
    }

    private sealed class PendingGroup
    {
        internal WaveEnemyKind Kind;
        internal CombatEnemy Prefab;
        internal int Remaining;
    }

    private readonly List<SpawnedEnemy> spawned = new List<SpawnedEnemy>();
    private readonly List<PendingGroup> pending = new List<PendingGroup>();
    private readonly HashSet<ushort> remoteManifest = new HashSet<ushort>();
    private WasdBlockMovement player;
    private GameplayHud hud;
    private CoopSession session;
    private WaveStage stage;
    private int waveIndex;
    private float nextWaveAt;
    private ushort nextEnemyId;
    private int remoteAlive, remotePending, remoteTotal, remoteEnemyTotal, remoteLimit;
    private float remoteCountdown;
    private string displayedStatus;

    public WaveStage Stage => stage;
    public int TotalWaves => IsReplica ? remoteTotal : waves?.Length ?? 0;
    public int CurrentWave => TotalWaves == 0 ? 0 : Mathf.Clamp(waveIndex + 1, 1, TotalWaves);
    public int CurrentEnemyLimit => IsReplica ? remoteLimit :
        Mathf.Clamp(CurrentDefinition?.maxConcurrentEnemies > 0
            ? CurrentDefinition.maxConcurrentEnemies : legacyConcurrentLimit, 1, MaximumConcurrentEnemies);
    private Wave CurrentDefinition => waves != null && waveIndex >= 0 && waveIndex < waves.Length ? waves[waveIndex] : null;
    public int TotalEnemiesInCurrentWave => IsReplica ? remoteEnemyTotal : CountWaveEnemies();
    public int RemainingEnemiesInCurrentWave => stage == WaveStage.Countdown
        ? TotalEnemiesInCurrentWave : AliveEnemies + EnemiesWaitingToSpawn;

    private int CountWaveEnemies()
    {
        var definition = CurrentDefinition;
        long count = 0;
        if (definition?.enemies != null)
            foreach (var group in definition.enemies)
                if (group != null) count += Mathf.Max(0, group.count);
        return (int)System.Math.Min(count, int.MaxValue);
    }

    public int AliveEnemies
    {
        get
        {
            if (IsReplica) return remoteAlive;
            int count = 0;
            // Disabled or stunned enemies are still alive and must not clear the wave.
            foreach (var entry in spawned)
                if (entry.Enemy != null && entry.Enemy.Health > 0) count++;
            return count;
        }
    }
    public int EnemiesWaitingToSpawn
    {
        get
        {
            if (IsReplica) return remotePending;
            long count = 0;
            foreach (var group in pending) count += group.Remaining;
            return (int)System.Math.Min(count, int.MaxValue);
        }
    }
    internal bool IsReplica => session != null && session.InMatch && !session.IsHost;
    internal IReadOnlyList<SpawnedEnemy> Spawned => spawned;
    internal float Countdown => stage == WaveStage.Countdown
        ? IsReplica ? Mathf.Max(0f, remoteCountdown - Time.time) : Mathf.Max(0f, nextWaveAt - Time.time) : 0f;

    private void Awake()
    {
        MigrateLegacyWaves();
        player = FindAnyObjectByType<WasdBlockMovement>();
        hud = FindAnyObjectByType<GameplayHud>();
        session = FindAnyObjectByType<CoopSession>();
        foreach (var enemy in sceneEnemiesToReplace)
            if (enemy != null) enemy.SetActive(false);
    }

    private void Start()
    {
        if (startAutomatically && !IsReplica) StartWaves();
        RefreshHud();
    }

    private void Update()
    {
        if (!IsReplica) Tick(Time.time);
        RefreshHud();
    }

    [ContextMenu("Start waves (Play Mode)")]
    public void StartWaves()
    {
        if (!Application.isPlaying || IsReplica || stage == WaveStage.Spawning || stage == WaveStage.Fighting)
            return;
        ClearSpawned();
        pending.Clear();
        waveIndex = 0;
        MigrateLegacyWaves();
        if (waves == null || waves.Length == 0)
        {
            stage = WaveStage.Complete;
            return;
        }
        stage = WaveStage.Countdown;
        nextWaveAt = Time.time + Mathf.Max(0f, waves[0]?.delayBeforeStart ?? 0f);
        RefreshHud();
    }

    internal void Tick(float now)
    {
        CleanupDefeated(now);
        if (stage == WaveStage.Countdown && now >= nextWaveAt) BeginWave();
        if (stage != WaveStage.Spawning && stage != WaveStage.Fighting) return;

        int alive = AliveEnemies;
        int waiting = EnemiesWaitingToSpawn;
        while (alive < CurrentEnemyLimit && waiting > 0 && spawned.Count < 128)
        {
            if (!TryChooseSpawn(out var position)) break;
            var group = ChoosePendingGroup();
            if (group == null) break;
            if (++nextEnemyId == 0) nextEnemyId = 1;
            Spawn(group.Kind, group.Prefab, position, nextEnemyId);
            group.Remaining--;
            alive++;
            waiting--;
        }
        stage = waiting > 0 ? WaveStage.Spawning : WaveStage.Fighting;
        if (alive == 0 && waiting == 0) FinishWave(now);
    }

    private PendingGroup ChoosePendingGroup()
    {
        long total = 0;
        foreach (var group in pending) total += group.Remaining;
        if (total == 0) return null;
        // Draw from the remaining individual enemies, preserving each configured quantity.
        long choice = System.Math.Min(total - 1, (long)(UnityEngine.Random.value * (double)total));
        foreach (var group in pending)
        {
            if (choice < group.Remaining) return group;
            choice -= group.Remaining;
        }
        return null;
    }

    private void BeginWave()
    {
        pending.Clear();
        var definition = CurrentDefinition;
        if (definition?.enemies != null)
        {
            foreach (var group in definition.enemies)
            {
                if (group == null || group.count <= 0) continue;
                var prefab = GetPrefab(group.enemy);
                if (prefab == null)
                {
                    Debug.LogError($"Wave {CurrentWave}: missing prefab for {group.enemy}. Waves stopped.", this);
                    pending.Clear();
                    stage = WaveStage.Stopped;
                    return;
                }
                pending.Add(new PendingGroup { Kind = group.enemy, Prefab = prefab, Remaining = group.count });
            }
        }
        stage = WaveStage.Spawning;
    }

    private void FinishWave(float now)
    {
        if (waveIndex + 1 >= waves.Length) stage = WaveStage.Complete;
        else
        {
            waveIndex++;
            stage = WaveStage.Countdown;
            nextWaveAt = now + Mathf.Max(0f, CurrentDefinition?.delayBeforeStart ?? 0f);
        }
    }

    private void OnValidate() => MigrateLegacyWaves();

    private void MigrateLegacyWaves()
    {
        if (waves == null) return;
        foreach (var wave in waves)
        {
            if (wave == null) continue;
            if (wave.maxConcurrentEnemies <= 0)
                wave.maxConcurrentEnemies = Mathf.Clamp(legacyConcurrentLimit, 1, MaximumConcurrentEnemies);
            if (wave.legacySubWaves == null || wave.legacySubWaves.Length == 0) continue;

            var groups = new List<EnemyGroup>();
            foreach (var sub in wave.legacySubWaves)
            {
                if (sub?.enemies == null) continue;
                foreach (var group in sub.enemies)
                {
                    if (group == null || group.count <= 0) continue;
                    var existing = groups.Find(entry => entry.enemy == group.enemy &&
                        (long)entry.count + group.count <= int.MaxValue);
                    if (existing == null)
                        groups.Add(new EnemyGroup { enemy = group.enemy, count = group.count });
                    else
                        existing.count += group.count;
                }
            }
            wave.enemies = groups.ToArray();
            wave.legacySubWaves = null;
        }
    }

    internal CombatEnemy GetPrefab(WaveEnemyKind kind)
    {
        foreach (var entry in enemyPrefabs)
            if (entry != null && entry.enemy == kind) return entry.prefab;
        return null;
    }

    private CombatEnemy Spawn(WaveEnemyKind kind, CombatEnemy prefab, Vector3 position, ushort id)
    {
        var heading = player != null ? player.transform.position - position : Vector3.forward;
        heading.y = 0f;
        var enemy = Instantiate(prefab, position, heading.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(heading) : Quaternion.identity);
        enemy.name = $"{kind} - Wave {CurrentWave} - {id}";
        enemy.WaveSpawnId = id;
        enemy.ConfigureForWave(player);
        enemy.gameObject.SetActive(true);
        spawned.Add(new SpawnedEnemy { Id = id, Kind = kind, Enemy = enemy,
            WaveIndex = waveIndex });
        return enemy;
    }

    private bool TryChooseSpawn(out Vector3 position)
    {
        Vector3 center = player != null ? player.transform.position : transform.position;
        for (int attempt = 0; attempt < 48; attempt++)
        {
            if (attempt < spawnPoints.Length && spawnPoints.Length > 0)
            {
                var point = spawnPoints[(attempt + nextEnemyId) % spawnPoints.Length];
                if (point == null) continue;
                position = point.position;
            }
            else
            {
                float angle = UnityEngine.Random.value * Mathf.PI * 2f;
                float radius = Mathf.Max(minimumPlayerDistance, spawnRadius) * UnityEngine.Random.Range(0.8f, 1f);
                position = center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
            }
            if (player != null)
            {
                position = player.ClampToMovementBoundary(position, 2f);
                if (!player.TryGetGroundHeight(position, out float height)) continue;
                position.y = height;
            }
            Vector3 nearestPlayer = center;
            float playerDistance = hud == null || hud.Health > 0 ? PlanarDistance(position, center) : float.PositiveInfinity;
            session?.FindNearestLivingPeer(position, ref nearestPlayer, ref playerDistance);
            if (playerDistance < minimumPlayerDistance * minimumPlayerDistance) continue;
            bool crowded = false;
            foreach (var entry in spawned)
                if (entry.Enemy != null && entry.Enemy.Health > 0 &&
                    PlanarDistance(position, entry.Enemy.transform.position) < minimumEnemyDistance * minimumEnemyDistance)
                { crowded = true; break; }
            if (!crowded) return true;
        }
        position = default;
        return false;
    }

    private void CleanupDefeated(float now)
    {
        for (int i = spawned.Count - 1; i >= 0; i--)
        {
            var entry = spawned[i];
            if (entry.Enemy == null)
            {
                spawned.RemoveAt(i);
                continue;
            }
            if (entry.Enemy.Health > 0) continue;
            if (entry.DiedAt < 0f)
            {
                entry.DiedAt = now;
            }
            if (now < entry.DiedAt + corpseLifetime) continue;
            Destroy(entry.Enemy.gameObject);
            spawned.RemoveAt(i);
        }
    }

    internal CombatEnemy FindEnemy(ushort id)
    {
        foreach (var entry in spawned) if (entry.Id == id) return entry.Enemy;
        return null;
    }

    internal CombatEnemy GetOrSpawnReplica(ushort id, WaveEnemyKind kind, Vector3 position)
    {
        var enemy = FindEnemy(id);
        if (enemy != null) return enemy;
        var prefab = GetPrefab(kind);
        return prefab != null ? Spawn(kind, prefab, position, id) : null;
    }

    internal void ApplyRemoteWave(WaveStage value, int current, int total, int limit,
        int alive, int waiting, int totalEnemies, float countdown, ushort[] manifest)
    {
        stage = value;
        waveIndex = Mathf.Max(0, current - 1);
        remoteTotal = total;
        remoteLimit = limit;
        remoteAlive = alive;
        remotePending = waiting;
        remoteEnemyTotal = Mathf.Max(0, totalEnemies);
        remoteCountdown = Time.time + Mathf.Max(0f, countdown);
        remoteManifest.Clear();
        foreach (ushort id in manifest) remoteManifest.Add(id);
        for (int i = spawned.Count - 1; i >= 0; i--)
            if (!remoteManifest.Contains(spawned[i].Id))
            {
                if (spawned[i].Enemy != null) Destroy(spawned[i].Enemy.gameObject);
                spawned.RemoveAt(i);
            }
        RefreshHud();
    }

    internal bool AcceptsReplica(ushort id) => remoteManifest.Contains(id);

    private void RefreshHud()
    {
        hud?.SetWave(CurrentWave, TotalWaves);
        hud?.SetEnemyCount(RemainingEnemiesInCurrentWave, TotalEnemiesInCurrentWave);
        if (statusLabel == null) return;
        string status = stage == WaveStage.Complete ? "ALL WAVES CLEARED"
            : stage == WaveStage.Countdown ? $"WAVE {CurrentWave} IN {Mathf.CeilToInt(Countdown)}"
            : stage == WaveStage.Spawning || stage == WaveStage.Fighting
                ? $"WAVE {CurrentWave}  |  ENEMIES LEFT: {RemainingEnemiesInCurrentWave}"
            : IsReplica ? "WAITING FOR HOST" : "";
        if (status != displayedStatus) { displayedStatus = status; statusLabel.text = status; }
    }

    private void ClearSpawned()
    {
        foreach (var entry in spawned) if (entry.Enemy != null) Destroy(entry.Enemy.gameObject);
        spawned.Clear();
    }
    private void OnDestroy() => ClearSpawned();
    private static float PlanarDistance(Vector3 a, Vector3 b)
    { var offset = a - b; offset.y = 0f; return offset.sqrMagnitude; }
}
