using System;
using OpenTK.Mathematics;

namespace GPR202_Assessment3
{
    public enum EnemyPersonality
    {
        Wanderer,
        Aggressor,
        Bomber
    }

    [Flags]
    public enum EnemyWeapon
    {
        None = 0,
        MachineGuns = 1 << 0,
        Missiles = 1 << 1,
        Bombs = 1 << 2,
        All = MachineGuns | Missiles | Bombs
    }

    public readonly struct EnemyFireCommand
    {
        public bool FireGuns { get; init; }
        public int GunPairToggle { get; init; }
        public Vector3 GunAimPoint { get; init; }

        public bool FireMissile { get; init; }
        public int MissileSlot { get; init; }
        public Vector3 MissileAimPoint { get; init; }

        public bool DropBomb { get; init; }
        public int BombSlot { get; init; }
    }

    // AI-controlled enemy aircraft driven by the exact same FlightPhysics model as the player.
    public sealed class EnemyAI
    {
        private static readonly int[] MissileLaunchOrder = { 0, 5, 1, 4, 2, 3 };
        private static readonly int[] BombLaunchOrder = { 0, 1 };

        private readonly EnemyFlightSettings flightSettings;
        private readonly Random rng;

        public EnemyFlightSettings FlightSettings => flightSettings;
        public FlightPhysics Flight { get; }
        public Plane Plane { get; }
        public EnemyPersonality Personality { get; }
        public EnemyWeapon EquippedWeapon { get; }

        public int InitialMachineGunAmmo { get; }
        public int InitialMissileAmmo { get; }
        public int InitialBombAmmo { get; }

        public int MachineGunAmmo { get; private set; }
        public int MissileAmmo { get; private set; }
        public int BombAmmo { get; private set; }

        public float MaxHealth => Plane.MaxHealth;
        public float Health => Plane.Health;
        public float HealthFraction => Plane.HealthFraction;
        public bool IsAlive => !Plane.IsDestroyed;
        public bool IsSpawned { get; private set; }

        public float DetectionProgress { get; private set; }
        public bool HasDetectedPlayer => DetectionProgress >= 1f;
        public Vector3 DesiredPosition { get; private set; }
        public float PropellerAngle { get; private set; }

        public bool[] MountedMissiles { get; } = new bool[Plane.MissileCount];
        public bool[] MountedBombs { get; } = new bool[Plane.BombCount];

        public Vector3 Position => Flight.Position;
        public Vector3 Velocity => Flight.Velocity;
        public Matrix4 Orientation => Flight.Orientation;
        public Matrix4 ModelMatrix => Flight.Orientation * Matrix4.CreateTranslation(Flight.Position);

        public Vector3 LiveryTint => Personality switch
        {
            EnemyPersonality.Aggressor => new Vector3(0.96f, 0.36f, 0.32f), // Crimson aggressor
            EnemyPersonality.Bomber => new Vector3(0.48f, 0.56f, 0.78f),    // Steel-blue bomber
            _ => new Vector3(0.58f, 0.74f, 0.48f)                           // Olive wanderer
        };

        private Vector3 wanderWaypoint;
        private bool hasWaypoint;
        private float gunCooldown;
        private float missileCooldown;
        private float bombCooldown;
        private int gunPairToggle;
        private int nextMissileOrderIdx;
        private int nextBombOrderIdx;

        public EnemyAI(
            EnemyPersonality personality,
            EnemyWeapon weapon,
            int machineGunAmmo,
            int missileAmmo,
            int bombAmmo,
            EnemyFlightSettings? flightSettings = null,
            int? seed = null)
            : this(personality, weapon, machineGunAmmo, missileAmmo, bombAmmo, Plane.DefaultMaxHealth, flightSettings, seed)
        {
        }

        public EnemyAI(
            EnemyPersonality personality,
            EnemyWeapon weapon,
            int machineGunAmmo,
            int missileAmmo,
            int bombAmmo,
            float health,
            EnemyFlightSettings? flightSettings = null,
            int? seed = null)
        {
            Personality = personality;
            EquippedWeapon = weapon;
            InitialMachineGunAmmo = Math.Max(0, machineGunAmmo);
            InitialMissileAmmo = Math.Max(0, missileAmmo);
            InitialBombAmmo = Math.Max(0, bombAmmo);

            Plane = Plane.CreateEntity(health > 0f ? health : Plane.DefaultMaxHealth);

            // Give each enemy its own EnemyFlightSettings copy so it uses the same FlightPhysics
            // aerodynamic model while remaining tunable independently of the player.
            this.flightSettings = (flightSettings ?? new EnemyFlightSettings()).Clone();
            this.flightSettings.AssistedControls = true;
            Flight = new FlightPhysics(this.flightSettings);
            rng = seed.HasValue ? new Random(seed.Value) : new Random(Random.Shared.Next());

            ResetLoadout();
        }

        private void ResetLoadout()
        {
            MachineGunAmmo = InitialMachineGunAmmo;
            MissileAmmo = InitialMissileAmmo;
            BombAmmo = InitialBombAmmo;
            gunCooldown = 0f;
            missileCooldown = 0f;
            bombCooldown = 0f;
            gunPairToggle = 0;
            nextMissileOrderIdx = 0;
            nextBombOrderIdx = 0;
            DetectionProgress = 0f;
            Plane.ResetHealth();

            Array.Fill(MountedMissiles, false);
            int visualMissiles = Math.Min(Plane.MissileCount, HasWeapon(EnemyWeapon.Missiles) ? MissileAmmo : 0);
            for (int i = 0; i < visualMissiles; i++)
            {
                MountedMissiles[MissileLaunchOrder[i]] = true;
            }

            Array.Fill(MountedBombs, false);
            int visualBombs = Math.Min(Plane.BombCount, HasWeapon(EnemyWeapon.Bombs) ? BombAmmo : 0);
            for (int i = 0; i < visualBombs; i++)
            {
                MountedBombs[BombLaunchOrder[i]] = true;
            }
        }

        public bool HasWeapon(EnemyWeapon weapon) => (EquippedWeapon & weapon) != 0;

        public void Spawn(Vector3 position, Vector3 initialHeadingDirection)
        {
            ResetLoadout();

            Vector3 flatFwd = new Vector3(initialHeadingDirection.X, 0f, initialHeadingDirection.Z);
            if (flatFwd.LengthSquared > 1e-4f)
            {
                flatFwd.Normalize();
                Matrix4 initialOrientation = CombatSimulation.BuildOrientationFromForward(flatFwd, Vector3.UnitY);
                Flight.Reset(position, initialOrientation);
                wanderWaypoint = position + flatFwd * 360f;
                DesiredPosition = wanderWaypoint;
                hasWaypoint = true;
            }
            else
            {
                Flight.Reset(position);
                wanderWaypoint = position + Flight.Forward * 360f;
                DesiredPosition = wanderWaypoint;
                hasWaypoint = true;
            }

            IsSpawned = true;
        }

        public bool ApplyDamage(float damage)
        {
            if (!IsAlive || damage <= 0f) return false;
            bool destroyed = Plane.TakeDamage(damage);
            if (!destroyed)
            {
                // Getting hit alerts the enemy immediately.
                DetectionProgress = 1f;
            }
            return destroyed;
        }

        public void Kill()
        {
            Plane.Destroy();
        }

        public EnemyFireCommand Update(
            float dt,
            Vector3 playerPos,
            Vector3 playerVel,
            Vector3 playerForward,
            bool playerAlive,
            Func<float, float, float> getTerrainHeight,
            float propellerRpm = 400f)
        {
            if (!IsAlive || !IsSpawned) return default;

            if (gunCooldown > 0f) gunCooldown = Math.Max(0f, gunCooldown - dt);
            if (missileCooldown > 0f) missileCooldown = Math.Max(0f, missileCooldown - dt);
            if (bombCooldown > 0f) bombCooldown = Math.Max(0f, bombCooldown - dt);

            bool canSeePlayer = playerAlive && CheckCanSeePlayer(playerPos, getTerrainHeight);
            UpdateDetection(dt, canSeePlayer);

            // 1. Choose desired world position & throttle according to personality.
            float desiredThrottle = ChooseDesiredTarget(playerPos, playerVel, playerForward, playerAlive, canSeePlayer, getTerrainHeight);

            // 2. Apply terrain lookahead avoidance and world bounds.
            ApplySafetyConstraints(getTerrainHeight);

            // 3. Steer aircraft via the exact same FlightPhysics inputs (pitch, roll, yaw, throttle).
            GuideAircraft(dt, desiredThrottle);

            // Spin propeller visually.
            PropellerAngle = (float)((PropellerAngle + dt * propellerRpm * Flight.PropellerSpeedFraction * Math.PI / 30.0) % (Math.PI * 2.0));

            // 4. Decide whether to fire equipped weapons.
            return EvaluateWeapons(playerPos, playerVel, playerAlive, canSeePlayer);
        }

        private bool CheckCanSeePlayer(Vector3 playerPos, Func<float, float, float> getTerrainHeight)
        {
            Vector3 toPlayer = playerPos - Flight.Position;
            float distSq = toPlayer.LengthSquared;
            float maxRange = Personality switch
            {
                EnemyPersonality.Aggressor => 950f,
                EnemyPersonality.Bomber => 850f,
                _ => 500f
            };
            if (distSq > maxRange * maxRange || distSq < 1f) return false;

            float dist = MathF.Sqrt(distSq);
            Vector3 dir = toPlayer / dist;
            float cosAngle = Vector3.Dot(Flight.Forward, dir);

            bool inCone = Personality switch
            {
                EnemyPersonality.Aggressor => cosAngle > -0.25f || dist < 280f, // wide awareness + 360 close
                EnemyPersonality.Bomber => cosAngle > -0.35f || dist < 320f,
                _ => cosAngle > 0.50f                                          // Wanderer: narrower ~60 deg forward cone
            };
            if (!inCone) return false;

            // Terrain line-of-sight check (6 samples along the ray).
            const int samples = 6;
            for (int i = 1; i < samples; i++)
            {
                float t = i / (float)samples;
                Vector3 p = Flight.Position + toPlayer * t;
                if (p.Y < getTerrainHeight(p.X, p.Z) + 2f) return false;
            }
            return true;
        }

        private void UpdateDetection(float dt, bool canSeePlayer)
        {
            if (canSeePlayer)
            {
                float gainRate = Personality switch
                {
                    EnemyPersonality.Aggressor => 2.5f, // ~0.4s to detect
                    EnemyPersonality.Bomber => 1.4f,    // ~0.7s to detect
                    _ => 0.30f                          // Wanderer: ~3.3s to detect
                };
                DetectionProgress = Math.Min(1f, DetectionProgress + gainRate * dt);
            }
            else
            {
                float decayRate = Personality switch
                {
                    EnemyPersonality.Aggressor => 0.08f, // persistent tracker
                    EnemyPersonality.Bomber => 0.10f,
                    _ => 0.25f                           // Wanderer loses interest faster
                };
                DetectionProgress = Math.Max(0f, DetectionProgress - decayRate * dt);
            }
        }

        private float ChooseDesiredTarget(
            Vector3 playerPos,
            Vector3 playerVel,
            Vector3 playerForward,
            bool playerAlive,
            bool canSeePlayer,
            Func<float, float, float> getTerrainHeight)
        {
            if (!playerAlive || !HasDetectedPlayer)
            {
                // Patrol interior waypoints until the player is detected.
                if (!hasWaypoint || Vector3.DistanceSquared(Flight.Position, wanderWaypoint) < 95f * 95f)
                {
                    PickNewWanderWaypoint(getTerrainHeight, Personality == EnemyPersonality.Bomber ? 175f : 110f);
                }
                DesiredPosition = wanderWaypoint;
                return Personality == EnemyPersonality.Bomber ? 0.72f : 0.62f;
            }

            switch (Personality)
            {
                case EnemyPersonality.Aggressor:
                {
                    // Aim to get behind the player's tail (their six o'clock) and track them closely.
                    Vector3 pFwd = playerForward.LengthSquared > 1e-4f ? Vector3.Normalize(playerForward) : Vector3.UnitZ;
                    Vector3 tailSlot = playerPos - pFwd * 75f + new Vector3(0f, 8f, 0f);
                    float distToPlayer = Vector3.Distance(Flight.Position, playerPos);

                    // Once close behind the tail, lead the player directly for accurate bursts.
                    float behindAlignment = Vector3.Dot(Vector3.Normalize(playerPos - Flight.Position), pFwd);
                    if (distToPlayer < 160f && behindAlignment > 0.55f)
                    {
                        DesiredPosition = playerPos + playerVel * 0.35f;
                    }
                    else
                    {
                        DesiredPosition = tailSlot;
                    }

                    // Dynamic throttle: boost to catch up, throttle back slightly to avoid overshooting tail.
                    if (distToPlayer > 220f) return 1.0f;
                    if (distToPlayer < 65f) return 0.45f;
                    return 0.78f;
                }

                case EnemyPersonality.Bomber:
                {
                    // Fly high directly above the player's predicted bomb-impact position.
                    float dropHeight = 135f;
                    float fallTime = MathF.Sqrt(2f * dropHeight / CombatSimulation.BombGravity);
                    Vector3 predictedPos = playerPos + playerVel * (fallTime * 0.85f);
                    DesiredPosition = new Vector3(predictedPos.X, playerPos.Y + dropHeight, predictedPos.Z);

                    // If we are directly over or slightly past without bombs ready, push slightly ahead to swing around.
                    Vector2 horizDelta = new Vector2(DesiredPosition.X - Flight.Position.X, DesiredPosition.Z - Flight.Position.Z);
                    if (horizDelta.Length < 45f && bombCooldown > 0.2f)
                    {
                        Vector3 flatFwd = new Vector3(Flight.Forward.X, 0f, Flight.Forward.Z);
                        if (flatFwd.LengthSquared < 1e-4f) flatFwd = Vector3.UnitZ;
                        flatFwd.Normalize();
                        DesiredPosition = Flight.Position + flatFwd * 220f + new Vector3(0f, 15f, 0f);
                    }

                    return Flight.Position.Y < DesiredPosition.Y - 20f ? 0.96f : 0.76f;
                }

                default: // Wanderer
                {
                    // Continues wandering, loosely nosing toward the player to shoot when spotted in view.
                    if (!hasWaypoint || Vector3.DistanceSquared(Flight.Position, wanderWaypoint) < 95f * 95f)
                    {
                        PickNewWanderWaypoint(getTerrainHeight, 105f);
                    }
                    DesiredPosition = canSeePlayer
                        ? Vector3.Lerp(wanderWaypoint, playerPos + new Vector3(0f, 10f, 0f), 0.78f)
                        : wanderWaypoint;
                    return 0.65f;
                }
            }
        }

        private void PickNewWanderWaypoint(Func<float, float, float> getTerrainHeight, float minClearance)
        {
            float wx = (rng.NextSingle() * 2f - 1f) * 780f;
            float wz = (rng.NextSingle() * 2f - 1f) * 780f;
            float ground = getTerrainHeight(wx, wz);
            float wy = ground + minClearance + rng.NextSingle() * 110f;
            wanderWaypoint = new Vector3(wx, wy, wz);
            hasWaypoint = true;
        }

        private void ApplySafetyConstraints(Func<float, float, float> getTerrainHeight)
        {
            // Keep desired point inside the interior of the 2048x2048 terrain map.
            Vector3 desired = DesiredPosition;
            desired.X = Math.Clamp(desired.X, -860f, 860f);
            desired.Z = Math.Clamp(desired.Z, -860f, 860f);

            // GPWS lookahead: sample terrain now, 1.5s ahead, and 3.0s ahead along velocity.
            float clearance = Personality == EnemyPersonality.Bomber
                ? flightSettings.BomberTerrainClearance
                : flightSettings.MinTerrainClearance;
            Vector3 p1 = Flight.Position + Flight.Velocity * 1.5f;
            Vector3 p2 = Flight.Position + Flight.Velocity * 3.0f;
            float safeY = Math.Max(
                getTerrainHeight(Flight.Position.X, Flight.Position.Z),
                Math.Max(getTerrainHeight(p1.X, p1.Z), getTerrainHeight(p2.X, p2.Z))) + clearance;
            float destGroundY = getTerrainHeight(desired.X, desired.Z) + clearance;
            desired.Y = Math.Max(desired.Y, Math.Max(safeY, destGroundY));

            // Keep enemies below extreme sky ceiling so they stay in the playable airspace.
            desired.Y = Math.Min(desired.Y, flightSettings.MaxAltitude);
            DesiredPosition = desired;
        }

        private void GuideAircraft(float dt, float desiredThrottle)
        {
            Vector3 toTarget = DesiredPosition - Flight.Position;
            float horizDist = MathF.Sqrt(toTarget.X * toTarget.X + toTarget.Z * toTarget.Z);

            // 1. Horizontal heading error (signed: + = target is to aircraft starboard/right).
            Vector3 fwdFlat = new Vector3(Flight.Forward.X, 0f, Flight.Forward.Z);
            if (fwdFlat.LengthSquared < 1e-4f) fwdFlat = Vector3.UnitZ;
            fwdFlat.Normalize();

            // True starboard right vector in FlightPhysics coordinates: Cross(Forward, Up) = -Flight.Right.
            Vector3 starboard = Vector3.Cross(fwdFlat, Vector3.UnitY);
            if (starboard.LengthSquared < 1e-4f) starboard = Vector3.UnitX;
            starboard.Normalize();

            float headingError = 0f;
            if (horizDist > 1f)
            {
                Vector3 targetFlat = new Vector3(toTarget.X / horizDist, 0f, toTarget.Z / horizDist);
                float lateral = Vector3.Dot(targetFlat, starboard);
                float forwardDot = Vector3.Dot(targetFlat, fwdFlat);
                headingError = MathF.Atan2(lateral, forwardDot);
            }

            // 2. Desired pitch angle (clamped conservatively to avoid stalling in climbs).
            float desiredPitch = Math.Clamp(
                MathF.Atan2(toTarget.Y, Math.Max(25f, horizDist)),
                -flightSettings.MaxPitchDownRadians,
                flightSettings.MaxPitchUpRadians);
            float currentPitch = MathF.Asin(Math.Clamp(Flight.Forward.Y, -1f, 1f));

            // Stall recovery & low-speed energy protection.
            float throttle = desiredThrottle;
            if (Flight.StallWarning > 0.25f || Flight.Airspeed < flightSettings.StallRecoveryAirspeed)
            {
                desiredPitch = Math.Min(desiredPitch, -0.06f);
                throttle = flightSettings.MaxThrottle;
            }
            else if (toTarget.Y > 30f)
            {
                throttle = Math.Max(throttle, 0.88f);
            }

            // Convert errors into FlightPhysics stick commands:
            // +roll = right bank -> turns right (+headingError); -yaw = yaws right; +pitch = nose up.
            float rollCmd = Math.Clamp(headingError * 1.45f, -0.85f, 0.85f);
            float yawCmd = Math.Clamp(-headingError * 0.40f, -0.45f, 0.45f);
            float bankLiftComp = (1f - MathF.Abs(MathF.Cos(Flight.BankAngle))) * 0.22f;
            float pitchCmd = Math.Clamp((desiredPitch - currentPitch) * 2.5f + bankLiftComp, -0.85f, 0.85f);

            Flight.Throttle = Math.Clamp(throttle, flightSettings.MinThrottle, flightSettings.MaxThrottle);
            Flight.Step(dt, pitchCmd, rollCmd, yawCmd);
        }

        private EnemyFireCommand EvaluateWeapons(Vector3 playerPos, Vector3 playerVel, bool playerAlive, bool canSeePlayer)
        {
            if (!playerAlive || !HasDetectedPlayer) return default;

            Vector3 toPlayer = playerPos - Flight.Position;
            float dist = toPlayer.Length;
            if (dist < 1f) return default;

            bool fireGuns = false;
            int pairToggle = gunPairToggle;
            Vector3 gunAim = playerPos;

            bool fireMissile = false;
            int missileSlot = -1;
            Vector3 missileAim = playerPos;

            bool dropBomb = false;
            int bombSlot = -1;

            // --- Machine Guns ---
            if (canSeePlayer && HasWeapon(EnemyWeapon.MachineGuns) && MachineGunAmmo > 0 && gunCooldown <= 0f)
            {
                float maxGunRange = Personality == EnemyPersonality.Aggressor ? 340f : 260f;
                float minCosCone = Personality == EnemyPersonality.Aggressor ? 0.94f : 0.90f; // ~20 deg vs ~26 deg
                if (dist <= maxGunRange)
                {
                    float flightTime = dist / (CombatSimulation.BulletSpeed + Math.Max(0f, Flight.Airspeed));
                    Vector3 leadPos = playerPos + playerVel * (flightTime * (Personality == EnemyPersonality.Aggressor ? 0.9f : 0.5f));
                    Vector3 aimDir = Vector3.Normalize(leadPos - Flight.Position);
                    if (Vector3.Dot(Flight.Forward, aimDir) >= minCosCone)
                    {
                        fireGuns = true;
                        pairToggle = gunPairToggle;
                        gunPairToggle = 1 - gunPairToggle;
                        gunAim = leadPos;
                        MachineGunAmmo = Math.Max(0, MachineGunAmmo - 2);
                        gunCooldown = Personality == EnemyPersonality.Aggressor ? 0.14f : 0.24f;
                    }
                }
            }

            // --- Missiles ---
            if (canSeePlayer && HasWeapon(EnemyWeapon.Missiles) && MissileAmmo > 0 && missileCooldown <= 0f)
            {
                float minRange = 75f;
                float maxRange = Personality == EnemyPersonality.Aggressor ? 520f : 420f;
                if (dist >= minRange && dist <= maxRange)
                {
                    float flightTime = dist / 210f;
                    Vector3 leadPos = playerPos + playerVel * (flightTime * 0.7f);
                    Vector3 aimDir = Vector3.Normalize(leadPos - Flight.Position);
                    if (Vector3.Dot(Flight.Forward, aimDir) >= 0.88f) // ~28 deg cone
                    {
                        fireMissile = true;
                        missileSlot = MissileLaunchOrder[nextMissileOrderIdx % MissileLaunchOrder.Length];
                        nextMissileOrderIdx++;
                        MissileAmmo--;
                        if (MissileAmmo < Plane.MissileCount)
                        {
                            MountedMissiles[missileSlot] = false;
                        }
                        missileAim = leadPos;
                        missileCooldown = Personality == EnemyPersonality.Aggressor ? 2.4f : 3.4f;
                    }
                }
            }

            // --- Bombs ---
            if (HasWeapon(EnemyWeapon.Bombs) && BombAmmo > 0 && bombCooldown <= 0f)
            {
                float heightAbove = Flight.Position.Y - playerPos.Y;
                if (heightAbove >= 25f && heightAbove <= 260f)
                {
                    // Predict where a dropped bomb will reach the player's altitude under gravity.
                    float tFall = MathF.Sqrt(2f * heightAbove / CombatSimulation.BombGravity);
                    Vector3 bombImpactAtPlayerY = Flight.Position + new Vector3(Flight.Velocity.X * tFall, 0f, Flight.Velocity.Z * tFall);
                    Vector3 playerFuture = playerPos + new Vector3(playerVel.X * tFall, 0f, playerVel.Z * tFall);
                    float missDistXZ = new Vector2(bombImpactAtPlayerY.X - playerFuture.X, bombImpactAtPlayerY.Z - playerFuture.Z).Length;

                    if (missDistXZ <= CombatSimulation.BombExplosionRadius * 0.72f)
                    {
                        dropBomb = true;
                        bombSlot = BombLaunchOrder[nextBombOrderIdx % BombLaunchOrder.Length];
                        nextBombOrderIdx++;
                        BombAmmo--;
                        if (BombAmmo < Plane.BombCount)
                        {
                            MountedBombs[bombSlot] = false;
                        }
                        bombCooldown = 2.0f;
                    }
                }
            }

            return new EnemyFireCommand
            {
                FireGuns = fireGuns,
                GunPairToggle = pairToggle,
                GunAimPoint = gunAim,
                FireMissile = fireMissile,
                MissileSlot = missileSlot,
                MissileAimPoint = missileAim,
                DropBomb = dropBomb,
                BombSlot = bombSlot
            };
        }
    }
}
