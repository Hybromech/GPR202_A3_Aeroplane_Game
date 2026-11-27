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
            enemies.Add(enemy);
        }

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
                // Distribute enemies evenly around the player at comfortable combat range.
                float angle = (MathF.Tau * i / Math.Max(1, count)) + 0.45f;
                float radius = enemy.Personality switch
                {
                    EnemyPersonality.Aggressor => 320f,
                    EnemyPersonality.Bomber => 420f,
                    _ => 380f
                };

                float sx = Math.Clamp(playerPosition.X + MathF.Sin(angle) * radius, -820f, 820f);
                float sz = Math.Clamp(playerPosition.Z + MathF.Cos(angle) * radius, -820f, 820f);
                float groundY = terrainHeightProvider(sx, sz);
                float extraAlt = enemy.Personality == EnemyPersonality.Bomber ? 220f : 140f;
                float sy = Math.Max(groundY + extraAlt, playerPosition.Y + (enemy.Personality == EnemyPersonality.Bomber ? 110f : 20f));

                Vector3 spawnPos = new Vector3(sx, sy, sz);
                Vector3 toPlayer = playerPosition - spawnPos;
                enemy.Spawn(spawnPos, toPlayer);
            }
        }
    }

    // Coordinates enemy squadrons, AI updates, combat damage exchange, and player health.
    public sealed class GameManager
    {
        public const float MaxPlayerHealth = Plane.DefaultMaxHealth;

        public EnemySpawner EnemySpawner { get; } = new();
        public EnemyFlightSettings EnemyFlightSettings { get; }
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

            var enemy1 = new EnemyAI(EnemyPersonality.Aggressor, EnemyWeapon.MachineGuns, 500, 0, 0, Plane.DefaultMaxHealth, EnemyFlightSettings);
            var enemy2 = new EnemyAI(EnemyPersonality.Wanderer, EnemyWeapon.Missiles, 0, 15, 0, Plane.DefaultMaxHealth, EnemyFlightSettings);
            var enemy3 = new EnemyAI(EnemyPersonality.Bomber, EnemyWeapon.Bombs, 0, 0, 14, Plane.DefaultMaxHealth, EnemyFlightSettings);
            var enemy4 = new EnemyAI(EnemyPersonality.Aggressor, EnemyWeapon.MachineGuns | EnemyWeapon.Missiles, 550, 2, 0, Plane.DefaultMaxHealth, EnemyFlightSettings);

            EnemySpawner.Add(enemy1);
            EnemySpawner.Add(enemy2);
            EnemySpawner.Add(enemy3);
            EnemySpawner.Add(enemy4);

            EnemySpawner.SpawnAll();
        }

        public void ResetForNewGame(Vector3 playerSpawnPos, Func<float, float, float> getTerrainHeight, Plane? playerPlane = null)
        {
            playerPlane?.ResetHealth();
            PlayerHealth = playerPlane?.Health ?? MaxPlayerHealth;
            EnemiesDestroyed = 0;
            EnemySpawner.ConfigureSpawnContext(playerSpawnPos, getTerrainHeight);
            SetupEnemies();
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

