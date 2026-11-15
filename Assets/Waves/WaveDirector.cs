using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum WaveEnemyKind : byte { LungeMan, Giant, RedNinja, TrueHealer }

[DefaultExecutionOrder(-200)]
public sealed class WaveDirector : MonoBehaviour
{
    public const int MaximumConcurrentEnemies = 32;

    [Serializable]
    public sealed class EnemyGroup
    {
        public WaveEnemyKind enemy;
        [Min(0)] public int count = 1;
        [Min(0f)] public float delay;
        [Min(0f)] public float spawnInterval = 0.6f;
    }

    [Serializable]
    public sealed class SubWave
    {
        public string name = "New sub-wave";
        [Min(0f), Tooltip("Delay for the remaining enemies after the previous sub-wave clears. Same-type replacements spawn immediately on a kill.")]
        public float delayBeforeStart;
        public EnemyGroup[] enemies = { new EnemyGroup() };
    }

    [Serializable]
    public sealed class Wave
    {
        public string name = "New wave";
        [Min(0f)] public float delayBeforeStart = 4f;
        public SubWave[] subWaves = Array.Empty<SubWave>();
        // Retain the old serialized field so previously configured scenes keep their enemies.
        [HideInInspector] public EnemyGroup[] enemies = Array.Empty<EnemyGroup>();
    }

    [Serializable]
    public sealed class EnemyPrefab
    {
        public WaveEnemyKind enemy;
        public CombatEnemy prefab;
    }

    public enum WaveStage : byte { Stopped, Countdown, Spawning, Fighting, Complete, SubWaveCountdown }

    [Header("Enemy prefabs")]
    [SerializeField] private EnemyPrefab[] enemyPrefabs = Array.Empty<EnemyPrefab>();
    [Header("Waves - each wave waits until every enemy is defeated")]
    [SerializeField] private Wave[] waves = Array.Empty<Wave>();
    [Header("Spawning")]
    [SerializeField] private bool startAutomatically = true;
    [SerializeField, Range(1, MaximumConcurrentEnemies)] private int maxConcurrentEnemies = 8;
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
        internal int SubWaveIndex;
        internal float DiedAt = -1f;
    }

    private sealed class PendingGroup
    {
        internal EnemyGroup Group;
        internal CombatEnemy Prefab;
        internal int SubWaveIndex;
        internal int Remaining;
        internal int Replacements;
        internal bool Started;
        internal float NextSpawn;
    }

    private readonly List<SpawnedEnemy> spawned = new List<SpawnedEnemy>();
    private readonly List<PendingGroup> pending = new List<PendingGroup>();
    private readonly HashSet<ushort> remoteManifest = new HashSet<ushort>();
    private WasdBlockMovement player;
    private GameplayHud hud;
    private CoopSession session;
    private WaveStage stage;
    private int waveIndex, subWaveIndex;
    private float nextWaveAt;
    private ushort nextEnemyId;
    private int remoteAlive, remotePending, remoteTotal, remoteSubWaveTotal, remoteEnemyTotal;
    private float remoteCountdown;
    private string displayedStatus;

    public WaveStage Stage => stage;
    public int TotalWaves => IsReplica ? remoteTotal : waves?.Length ?? 0;
    public int CurrentWave => TotalWaves == 0 ? 0 : Mathf.Clamp(waveIndex + 1, 1, TotalWaves);
    public int TotalSubWaves => IsReplica ? remoteSubWaveTotal : GetSubWaveCount();
    public int CurrentSubWave => TotalSubWaves == 0 ? 0 : Mathf.Clamp(subWaveIndex + 1, 1, TotalSubWaves);
    public int TotalEnemiesInCurrentWave => IsReplica ? remoteEnemyTotal : CountWaveEnemies();
    public int RemainingEnemiesInCurrentWave => stage == WaveStage.Countdown
        ? TotalEnemiesInCurrentWave : AliveEnemies + EnemiesWaitingToSpawn;

    private int CountWaveEnemies()
    {
        if (waves == null || waveIndex < 0 || waveIndex >= waves.Length || waves[waveIndex] == null)
            return 0;
        var definition = waves[waveIndex];
        long count = 0;
        if (definition.subWaves != null && definition.subWaves.Length > 0)
        {
            foreach (var sub in definition.subWaves)
                if (sub?.enemies != null)
                    foreach (var group in sub.enemies)
                        if (group != null) count += Mathf.Max(0, group.count);
        }
        else if (definition.enemies != null)
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
            int count = 0;
            foreach (var group in pending) count += group.Remaining;
            return count;
        }
    }
    internal bool IsReplica => session != null && session.InMatch && !session.IsHost;
    internal IReadOnlyList<SpawnedEnemy> Spawned => spawned;
    internal float Countdown => stage == WaveStage.Countdown || stage == WaveStage.SubWaveCountdown
        ? IsReplica ? Mathf.Max(0f, remoteCountdown - Time.time) : Mathf.Max(0f, nextWaveAt - Time.time) : 0f;

    private void Awake()
    {
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
        subWaveIndex = 0;
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
        if (stage == WaveStage.Countdown && now >= nextWaveAt) BeginWave(now);
        if (stage == WaveStage.SubWaveCountdown && now >= nextWaveAt) BeginSubWave(now);
        if (stage != WaveStage.Spawning && stage != WaveStage.Fighting &&
            stage != WaveStage.SubWaveCountdown) return;

        int budget = 8;
        foreach (var group in pending)
        {
            // A kill releases one matching enemy immediately, even when its sub-wave
            // has not begun or another enemy from an earlier sub-wave is still alive.
            while (group.Remaining > 0 &&
                (group.Replacements > 0 || group.Started && now >= group.NextSpawn) && budget > 0 &&
                spawned.Count < 128 &&
                AliveEnemies < Mathf.Clamp(maxConcurrentEnemies, 1, MaximumConcurrentEnemies))
            {
                if (!TryChooseSpawn(out var position))
                {
                    group.NextSpawn = now + 0.5f;
                    break;
                }
                if (++nextEnemyId == 0) nextEnemyId = 1;
                Spawn(group.Group.enemy, group.Prefab, position, nextEnemyId, group.SubWaveIndex);
                group.Remaining--;
                if (group.Replacements > 0) group.Replacements--;
                group.NextSpawn = now + Mathf.Max(0f, group.Group.spawnInterval);
                budget--;
            }
        }

        if (stage == WaveStage.SubWaveCountdown) return;
        stage = WaveStage.Fighting;
        foreach (var group in pending)
            if (group.Remaining > 0 && (group.Started || group.Replacements > 0))
            { stage = WaveStage.Spawning; break; }

        // Track the oldest unfinished sub-wave. Enemies released early keep their
        // own source index, so they cannot block or duplicate an earlier sub-wave.
        while (!HasUnfinishedSubWave(subWaveIndex))
        {
            if (subWaveIndex + 1 >= GetSubWaveCount())
            {
                if (AliveEnemies == 0 && EnemiesWaitingToSpawn == 0) FinishWave(now);
                return;
            }
            subWaveIndex++;
            if (HasUnspawnedEnemies(subWaveIndex))
            {
                stage = WaveStage.SubWaveCountdown;
                nextWaveAt = now + GetSubWaveDelay();
                return;
            }
        }
    }

    private bool HasUnspawnedEnemies(int index)
    {
        foreach (var group in pending)
            if (group.SubWaveIndex == index && group.Remaining > 0) return true;
        return false;
    }

    private bool HasUnfinishedSubWave(int index)
    {
        if (HasUnspawnedEnemies(index)) return true;
        foreach (var entry in spawned)
            if (entry.WaveIndex == waveIndex && entry.SubWaveIndex == index &&
                entry.Enemy != null && entry.Enemy.Health > 0) return true;
        return false;
    }

    private void QueueReplacement(SpawnedEnemy defeated)
    {
        if (defeated.WaveIndex != waveIndex ||
            (stage != WaveStage.Spawning && stage != WaveStage.Fighting &&
             stage != WaveStage.SubWaveCountdown)) return;
        int nextSubWave = defeated.SubWaveIndex + 1;
        foreach (var group in pending)
        {
            if (group.SubWaveIndex != nextSubWave || group.Group.enemy != defeated.Kind ||
                group.Remaining <= group.Replacements) continue;
            group.Replacements++;
            return;
        }
    }

    private int GetSubWaveCount()
    {
        if (waves == null || waveIndex < 0 || waveIndex >= waves.Length) return 0;
        var wave = waves[waveIndex];
        if (wave?.subWaves != null && wave.subWaves.Length > 0) return wave.subWaves.Length;
        return wave?.enemies != null && wave.enemies.Length > 0 ? 1 : 0;
    }

    private SubWave GetSubWave()
    {
        var wave = waves[waveIndex];
        return wave?.subWaves != null && subWaveIndex < wave.subWaves.Length ? wave.subWaves[subWaveIndex] : null;
    }

    private float GetSubWaveDelay() => Mathf.Max(0f, GetSubWave()?.delayBeforeStart ?? 0f);

    private void BeginWave(float now)
    {
        subWaveIndex = 0;
        if (GetSubWaveCount() == 0) { FinishWave(now); return; }
        if (!PrepareWave()) return;
        stage = WaveStage.SubWaveCountdown;
        nextWaveAt = now + GetSubWaveDelay();
    }

    private void FinishWave(float now)
    {
        if (waveIndex + 1 >= waves.Length) stage = WaveStage.Complete;
        else
        {
            waveIndex++;
            subWaveIndex = 0;
            stage = WaveStage.Countdown;
            nextWaveAt = now + Mathf.Max(0f, waves[waveIndex]?.delayBeforeStart ?? 0f);
        }
    }

    private bool PrepareWave()
    {
        pending.Clear();
        var wave = waves[waveIndex];
        for (int index = 0; index < GetSubWaveCount(); index++)
        {
            var groups = wave.subWaves != null && wave.subWaves.Length > 0
                ? wave.subWaves[index]?.enemies : wave.enemies;
            if (groups == null) continue;
            foreach (var group in groups)
            {
                if (group == null || group.count <= 0) continue;
                var prefab = GetPrefab(group.enemy);
                if (prefab == null)
                {
                    Debug.LogError($"Wave {waveIndex + 1}, sub-wave {index + 1}: missing prefab for {group.enemy}. Waves stopped.", this);
                    pending.Clear();
                    stage = WaveStage.Stopped;
                    return false;
                }
                pending.Add(new PendingGroup { Group = group, Prefab = prefab,
                    SubWaveIndex = index, Remaining = group.count, NextSpawn = float.PositiveInfinity });
            }
        }
        return true;
    }

    private void BeginSubWave(float now)
    {
        foreach (var group in pending)
            if (group.SubWaveIndex == subWaveIndex)
            {
                group.Started = true;
                group.NextSpawn = now + Mathf.Max(0f, group.Group.delay);
            }
        stage = WaveStage.Spawning;
    }

    internal CombatEnemy GetPrefab(WaveEnemyKind kind)
    {
        foreach (var entry in enemyPrefabs)
            if (entry != null && entry.enemy == kind) return entry.prefab;
        return null;
    }

    private CombatEnemy Spawn(WaveEnemyKind kind, CombatEnemy prefab, Vector3 position, ushort id, int sourceSubWave = -1)
    {
        var heading = player != null ? player.transform.position - position : Vector3.forward;
        heading.y = 0f;
        var enemy = Instantiate(prefab, position, heading.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(heading) : Quaternion.identity);
        int source = sourceSubWave >= 0 ? sourceSubWave : subWaveIndex;
        enemy.name = $"{kind} - Wave {CurrentWave}.{source + 1} - {id}";
        enemy.WaveSpawnId = id;
        enemy.ConfigureForWave(player);
        enemy.gameObject.SetActive(true);
        spawned.Add(new SpawnedEnemy { Id = id, Kind = kind, Enemy = enemy,
            WaveIndex = waveIndex, SubWaveIndex = source });
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
                if (entry.DiedAt < 0f) QueueReplacement(entry);
                spawned.RemoveAt(i);
                continue;
            }
            if (entry.Enemy.Health > 0) continue;
            if (entry.DiedAt < 0f)
            {
                entry.DiedAt = now;
                QueueReplacement(entry);
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

    internal void ApplyRemoteWave(WaveStage value, int current, int total, int currentSubWave, int totalSubWaves,
        int alive, int waiting, int totalEnemies, float countdown, ushort[] manifest)
    {
        stage = value;
        waveIndex = Mathf.Max(0, current - 1);
        remoteTotal = total;
        subWaveIndex = Mathf.Max(0, currentSubWave - 1);
        remoteSubWaveTotal = totalSubWaves;
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
            : stage == WaveStage.SubWaveCountdown
                ? $"WAVE {CurrentWave}  |  SUB-WAVE {CurrentSubWave}/{TotalSubWaves} IN {Mathf.CeilToInt(Countdown)}"
            : stage == WaveStage.Spawning || stage == WaveStage.Fighting
                ? $"WAVE {CurrentWave}  |  SUB-WAVE {CurrentSubWave}/{TotalSubWaves}  |  ENEMIES LEFT: {AliveEnemies + EnemiesWaitingToSpawn}"
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
