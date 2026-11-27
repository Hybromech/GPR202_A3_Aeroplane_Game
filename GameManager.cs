using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace GPR202_Assessment3
{
    // Holds queued EnemyAI definitions and spawns them safely into the world airspace.
    public sealed class EnemySpawner
    {
        private readonly List<EnemyAI> enemies = new();
        private Vector3 spawnAnchor = new Vector3(0f, 150f, 0f);
        private Func<float, float, float> terrainHeightProvider = (_, _) => 0f;

        public IReadOnlyList<EnemyAI> Enemies => enemies;

        public int AliveCount
        {
            get
            {
                int alive = 0;
                for (int i = 0; i < enemies.Count; i++)
                {
                    if (enemies[i].IsAlive && enemies[i].IsSpawned) alive++;
                }
                return alive;
            }
        }

        public void ConfigureSpawnContext(Vector3 playerPosition, Func<float, float, float> getTerrainHeight)
        {
            spawnAnchor = playerPosition;
            terrainHeightProvider = getTerrainHeight ?? ((_, _) => 0f);
        }

        public void Add(EnemyAI enemy)
        {
            if (enemy == null) throw new ArgumentNullException(nameof(enemy));
            // If the same EnemyAI instance is added more than once as a template,
            // clone it so each spawned plane is independent and isn't updated multiple times per tick.
            enemies.Add(enemies.Contains(enemy) ? enemy.Clone() : enemy);
        }

        public float AggressorSpawnRadius { get; set; } = 3680f;
        public float WandererSpawnRadius { get; set; } = 3760f;
        public float BomberSpawnRadius { get; set; } = 3820f;

        public void Clear() => enemies.Clear();

        public void SpawnAll() => SpawnAll(spawnAnchor, terrainHeightProvider);

        public void SpawnAll(Vector3 playerPosition, Func<float, float, float> getTerrainHeight)
        {
            spawnAnchor = playerPosition;
            terrainHeightProvider = getTerrainHeight ?? ((_, _) => 0f);

            int count = enemies.Count;
            for (int i = 0; i < count; i++)
            {
                var enemy = enemies[i];
                // Distribute enemies evenly around the outer airspace well away from the player's spawn.
                float angle = (MathF.Tau * i / Math.Max(1, count)) + 0.45f;
                float radius = enemy.Personality switch
                {
                    EnemyPersonality.Aggressor => AggressorSpawnRadius,
                    EnemyPersonality.Bomber => BomberSpawnRadius,
                    _ => WandererSpawnRadius
                };

                float sx = Math.Clamp(playerPosition.X + MathF.Sin(angle) * radius, -880f, 880f);
                float sz = Math.Clamp(playerPosition.Z + MathF.Cos(angle) * radius, -880f, 880f);
                float groundY = terrainHeightProvider(sx, sz);
                float extraAlt = enemy.Personality == EnemyPersonality.Bomber ? 220f : 140f;
                float sy = Math.Max(groundY + extraAlt, playerPosition.Y + (enemy.Personality == EnemyPersonality.Bomber ? 110f : 20f));

                Vector3 spawnPos = new Vector3(sx, sy, sz);
                Vector3 toPlayer = playerPosition - spawnPos;
                enemy.Spawn(spawnPos, toPlayer);
            }
        }
    }

    public enum GamePhase
    {
        BalloonHunt,
        EnemyCombat,
        Victory
    }

    // Coordinates game progression, enemy squadrons, AI updates, combat damage exchange, and player health.
    public sealed class GameManager
    {
        public const float MaxPlayerHealth = Plane.DefaultMaxHealth;

        public EnemySpawner EnemySpawner { get; } = new();
        public EnemyFlightSettings EnemyFlightSettings { get; }
        public GamePhase Phase { get; private set; } = GamePhase.BalloonHunt;
        public bool PlayerWeaponsEnabled => Phase != GamePhase.BalloonHunt;
        public bool IsVictory => Phase == GamePhase.Victory;
        public float PlayerHealth { get; private set; } = MaxPlayerHealth;
        public int EnemiesDestroyed { get; private set; }

        public GameManager(EnemyFlightSettings? enemyFlightSettings = null)
        {
            EnemyFlightSettings = enemyFlightSettings ?? new EnemyFlightSettings();
        }

        // Configures and spawns the default enemy squadron via EnemySpawner.
        public void SetupEnemies()
        {
            EnemySpawner.Clear();

            var enemy1 = new EnemyAI(EnemyPersonality.Aggressor, 500, 2, 0, Plane.DefaultMaxHealth, EnemyFlightSettings);
            var enemy2 = new EnemyAI(EnemyPersonality.Wanderer, 500, 2, 0, Plane.DefaultMaxHealth, EnemyFlightSettings);
            var enemy3 = new EnemyAI(EnemyPersonality.Bomber, 500, 2, 14, Plane.DefaultMaxHealth, EnemyFlightSettings);
           
            EnemySpawner.Add(enemy1);
            EnemySpawner.Add(enemy1);
            EnemySpawner.Add(enemy1);
            EnemySpawner.Add(enemy2);
            EnemySpawner.Add(enemy2);
            EnemySpawner.Add(enemy2);
            EnemySpawner.Add(enemy3);
            EnemySpawner.Add(enemy3);
            EnemySpawner.Add(enemy3);

            EnemySpawner.SpawnAll();
        }

        // Resets to Phase 1 (BalloonHunt): player weapons disabled, no enemies spawned yet.
        public void ResetForNewGame(Vector3 playerSpawnPos, Func<float, float, float> getTerrainHeight, Plane? playerPlane = null)
        {
            playerPlane?.ResetHealth();
            PlayerHealth = playerPlane?.Health ?? MaxPlayerHealth;
            EnemiesDestroyed = 0;
            Phase = GamePhase.BalloonHunt;
            EnemySpawner.Clear();
            EnemySpawner.ConfigureSpawnContext(playerSpawnPos, getTerrainHeight);
        }

        // Advances from BalloonHunt -> EnemyCombat: enables player weapons and spawns enemies around the player.
        public bool OnAllBalloonsPopped(Vector3 playerPos, Func<float, float, float> getTerrainHeight)
        {
            if (Phase != GamePhase.BalloonHunt) return false;
            Phase = GamePhase.EnemyCombat;
            EnemySpawner.ConfigureSpawnContext(playerPos, getTerrainHeight);
            SetupEnemies();
            return true;
        }

        // Checks whether all spawned enemy planes have been destroyed and transitions to Victory once.
        public bool CheckAndTriggerVictory()
        {
            if (Phase == GamePhase.EnemyCombat && EnemySpawner.Enemies.Count > 0 && EnemySpawner.AliveCount == 0)
            {
                Phase = GamePhase.Victory;
                return true;
            }
            return false;
        }

        public void Update(
            float dt,
            Vector3 playerPos,
            Vector3 playerVel,
            Vector3 playerForward,
            bool playerAlive,
            Func<float, float, float> getTerrainHeight,
            CombatSimulation combat,
            Func<int, Vector3> getMissileLocalCenter,
            Func<int, Vector3> getBombLocalCenter,
            Action<Vector3>? onEnemyDestroyed = null,
            Action? onEnemyGunShot = null,
            Action? onEnemyMissileLaunch = null,
            float propellerRpm = 400f)
        {
            var enemies = EnemySpawner.Enemies;
            for (int i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i];
                if (!enemy.IsAlive || !enemy.IsSpawned) continue;

                var cmd = enemy.Update(dt, playerPos, playerVel, playerForward, playerAlive, getTerrainHeight, propellerRpm);

                // Check terrain crash for the enemy aircraft.
                float groundY = getTerrainHeight(enemy.Position.X, enemy.Position.Z);
                if (enemy.Position.Y < groundY + 2f)
                {
                    enemy.Kill();
                    EnemiesDestroyed++;
                    combat.SpawnAircraftExplosion(enemy.Position + Vector3.UnitY * 2f);
                    onEnemyDestroyed?.Invoke(enemy.Position);
                    continue;
                }

                Matrix4 enemyModel = enemy.ModelMatrix;
                if (cmd.FireGuns)
                {
                    combat.SpawnEnemyBulletPair(i, enemyModel, enemy.Velocity, cmd.GunAimPoint, cmd.GunPairToggle);
                    onEnemyGunShot?.Invoke();
                }

                if (cmd.FireMissile)
                {
                    combat.SpawnEnemyMissile(i, cmd.MissileSlot, enemyModel, enemy.Orientation, enemy.Velocity, cmd.MissileAimPoint, getMissileLocalCenter(cmd.MissileSlot));
                    onEnemyMissileLaunch?.Invoke();
                }

                if (cmd.DropBomb)
                {
                    combat.SpawnEnemyBomb(i, cmd.BombSlot, enemyModel, enemy.Orientation, enemy.Velocity, getBombLocalCenter(cmd.BombSlot));
                }
            }
        }

        // Appends player (-1) and alive enemy (0..N-1) hit spheres for CombatSimulation.
        public void CollectAircraftTargets(Vector3 playerPos, bool playerAlive, List<TargetSphere> output)
        {
            output.Clear();
            if (playerAlive)
            {
                output.Add(new TargetSphere { Index = -1, Center = playerPos, Radius = 7.5f });
            }

            var enemies = EnemySpawner.Enemies;
            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i].IsAlive && enemies[i].IsSpawned)
                {
                    output.Add(new TargetSphere { Index = i, Center = enemies[i].Position, Radius = 9.0f });
                }
            }
        }

        // Applies combat damage events; returns true if the player plane was destroyed this step.
        public bool ApplyDamageEvents(
            IReadOnlyList<CombatDamageEvent> damageEvents,
            CombatSimulation combat,
            Plane? playerPlane = null,
            Action<Vector3>? onEnemyDestroyed = null)
        {
            bool playerKilled = false;
            var enemies = EnemySpawner.Enemies;

            for (int i = 0; i < damageEvents.Count; i++)
            {
                var ev = damageEvents[i];
                if (ev.TargetId == -1)
                {
                    if (playerPlane != null)
                    {
                        if (playerPlane.TakeDamage(ev.Damage)) playerKilled = true;
                        PlayerHealth = playerPlane.Health;
                    }
                    else if (PlayerHealth > 0f)
                    {
                        PlayerHealth = Math.Max(0f, PlayerHealth - ev.Damage);
                        if (PlayerHealth <= 0f) playerKilled = true;
                    }
                }
                else if (ev.TargetId >= 0 && ev.TargetId < enemies.Count)
                {
                    // Enemy weapons never damage enemy aircraft (no friendly fire).
                    if (ev.OwnerId >= 0) continue;
                    var enemy = enemies[ev.TargetId];
                    if (enemy.ApplyDamage(ev.Damage))
                    {
                        EnemiesDestroyed++;
                        combat.SpawnAircraftExplosion(enemy.Position);
                        onEnemyDestroyed?.Invoke(enemy.Position);
                    }
                }
            }

            return playerKilled;
        }
    }
}

