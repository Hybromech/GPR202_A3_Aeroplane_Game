using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GPR202_Assessment3
{
    public enum OrdnanceKind
    {
        Missile,
        Bomb
    }

    public readonly struct CombatExplosionEvent
    {
        public Vector3 Position { get; init; }
        public float Radius { get; init; }
        public float Damage { get; init; }
        public float ScorchRadius { get; init; }
        public bool HitGround { get; init; }
        public OrdnanceKind Source { get; init; }
        public int OwnerId { get; init; } // -1 = player, >= 0 = enemy index
    }

    public readonly struct CombatDamageEvent
    {
        public int TargetId { get; init; } // -1 = player, >= 0 = enemy index
        public int OwnerId { get; init; }
        public float Damage { get; init; }
    }

    public struct BulletProjectile
    {
        public int OwnerId; // -1 = player, >= 0 = enemy
        public Vector3 Position;
        public Vector3 PrevPosition;
        public Vector3 Velocity;
        public float Age;
        public float MaxAge;
    }

    public struct OrdnanceProjectile
    {
        public int OwnerId; // -1 = player, >= 0 = enemy
        public OrdnanceKind Kind;
        public int SlotIndex;
        public Vector3 Position;
        public Vector3 PrevPosition;
        public Vector3 Velocity;
        public Matrix4 Orientation;
        public float Age;
        public float MaxAge;
        public float ExplosionRadius;
        public float Damage;
        public float ScorchRadius;
        public float ExhaustTimer;
    }

    public struct MuzzleFlash
    {
        public Vector3 Position;
        public float Age;
        public float Lifetime;
    }

    public readonly struct TargetSphere
    {
        public int Index { get; init; }
        public Vector3 Center { get; init; }
        public float Radius { get; init; }
    }

    public readonly struct ObstacleBox
    {
        public Vector3 Center { get; init; }
        public Vector3 HalfExtents { get; init; }
    }

    // Pure CPU simulation for wing machine guns, 6 underwing missiles, and 2 large bombs.
    public sealed class CombatSimulation
    {
        public const int MaxGunAmmo = 400;
        public const int MaxMissiles = 6;
        public const int MaxBombs = 2;

        public const float BulletSpeed = 390f;
        public const float BulletDamage = 2f;
        public const float BulletLifetime = 3.0f;
        public const float GunFireInterval = 0.075f; // Fires a symmetric pair (1 left + 1 right) each cycle

        public const float MissileLaunchBoost = 55f;
        public const float MissileAccel = 220f;
        public const float MissileMaxSpeed = 300f;
        public const float MissileExplosionRadius = 45f;
        public const float MissileDamage = 100f;
        public const float MissileScorchRadius = 20f;
        public const float MissileCooldown = 0.30f;

        public const float BombGravity = 26f;
        public const float BombExplosionRadius = 95f;
        public const float BombDamage = 250f;
        public const float BombScorchRadius = 44f;
        public const float BombCooldown = 0.40f;

        // Balanced alternating wing launch order: L-Outer, R-Outer, L-Mid, R-Mid, L-Inner, R-Inner.
        private static readonly int[] MissileLaunchOrder = { 0, 5, 1, 4, 2, 3 };
        private static readonly int[] BombLaunchOrder = { 0, 1 };

        public int GunAmmo { get; private set; } = MaxGunAmmo;
        public int MissilesRemaining { get; private set; } = MaxMissiles;
        public int BombsRemaining { get; private set; } = MaxBombs;
        public float HitMarkerTimer { get; private set; }

        public List<BulletProjectile> Bullets { get; } = new(128);
        public List<OrdnanceProjectile> ActiveOrdnance { get; } = new(16);
        public List<MuzzleFlash> MuzzleFlashes { get; } = new(16);
        public ParticleSystem ExhaustTrail { get; } = new();

        private readonly ParticleSystem[] explosionBursts = new ParticleSystem[8];
        private int nextExplosionBurst;

        private float gunTimer;
        private float missileTimer;
        private float bombTimer;
        private int gunPairToggle;
        private int nextMissileOrderIdx;
        private int nextBombOrderIdx;

        public IReadOnlyList<ParticleSystem> ExplosionBursts => explosionBursts;

        public CombatSimulation()
        {
            for (int i = 0; i < explosionBursts.Length; i++)
            {
                explosionBursts[i] = new ParticleSystem();
            }
        }

        public void RestockWeapons()
        {
            GunAmmo = MaxGunAmmo;
            MissilesRemaining = MaxMissiles;
            BombsRemaining = MaxBombs;
            gunTimer = 0f;
            missileTimer = 0f;
            bombTimer = 0f;
            nextMissileOrderIdx = 0;
            nextBombOrderIdx = 0;
        }

        public void Reset()
        {
            RestockWeapons();
            HitMarkerTimer = 0f;
            gunPairToggle = 0;
            Bullets.Clear();
            ActiveOrdnance.Clear();
            MuzzleFlashes.Clear();
            ExhaustTrail.Clear();
            foreach (var burst in explosionBursts) burst.Clear();
        }

        public void TriggerHitMarker() => HitMarkerTimer = 0.22f;

        // Fires 2 wing machine guns simultaneously (1 per wing), alternating outer & inner wing barrels.
        public bool TryFireGuns(Matrix4 planeModel, Vector3 planeVelocity, Vector3 aimPoint)
        {
            if (gunTimer > 0f || GunAmmo <= 0) return false;

            int leftIdx = gunPairToggle == 0 ? 0 : 1;
            int rightIdx = gunPairToggle == 0 ? 3 : 2;
            gunPairToggle = 1 - gunPairToggle;

            int fired = 0;
            int[] pair = { leftIdx, rightIdx };
            foreach (int muzzleIdx in pair)
            {
                if (GunAmmo <= 0) break;
                Vector3 muzzleWorld = Vector3.TransformPosition(Plane.WingGunMuzzles[muzzleIdx], planeModel);
                Vector3 toAim = aimPoint - muzzleWorld;
                Vector3 dir = toAim.LengthSquared > 0.01f ? Vector3.Normalize(toAim) : Vector3.Normalize(Vector3.TransformVector(Vector3.UnitZ, planeModel));
                float forwardCarry = Math.Max(0f, Vector3.Dot(planeVelocity, dir));
                Bullets.Add(new BulletProjectile
                {
                    OwnerId = -1,
                    Position = muzzleWorld,
                    PrevPosition = muzzleWorld,
                    Velocity = dir * (forwardCarry + BulletSpeed),
                    Age = 0f,
                    MaxAge = BulletLifetime
                });
                MuzzleFlashes.Add(new MuzzleFlash
                {
                    Position = muzzleWorld,
                    Age = 0f,
                    Lifetime = 0.055f
                });
                GunAmmo--;
                fired++;
            }

            if (fired > 0)
            {
                gunTimer = GunFireInterval;
                return true;
            }
            return false;
        }

        // Launches the next mounted underwing missile submodel; returns slot index or -1 if unavailable.
        public int TryFireMissile(Matrix4 planeModel, Matrix4 planeOrientation, Vector3 planeVelocity, Vector3 aimPoint, Func<int, Vector3> getMissileLocalCenter)
        {
            if (missileTimer > 0f || MissilesRemaining <= 0 || nextMissileOrderIdx >= MissileLaunchOrder.Length)
                return -1;

            int slot = MissileLaunchOrder[nextMissileOrderIdx++];
            MissilesRemaining--;
            missileTimer = MissileCooldown;

            Vector3 spawnPos = Vector3.TransformPosition(getMissileLocalCenter(slot), planeModel);
            Vector3 toAim = aimPoint - spawnPos;
            Vector3 dir = toAim.LengthSquared > 0.01f ? Vector3.Normalize(toAim) : Vector3.Normalize(Vector3.TransformVector(Vector3.UnitZ, planeOrientation));
            float forwardCarry = Math.Max(0f, Vector3.Dot(planeVelocity, dir));
            Vector3 vel = dir * (forwardCarry + MissileLaunchBoost);

            ActiveOrdnance.Add(new OrdnanceProjectile
            {
                OwnerId = -1,
                Kind = OrdnanceKind.Missile,
                SlotIndex = slot,
                Position = spawnPos,
                PrevPosition = spawnPos,
                Velocity = vel,
                Orientation = BuildOrientationFromForward(dir, Vector3.TransformVector(Vector3.UnitY, planeOrientation)),
                Age = 0f,
                MaxAge = 8.0f,
                ExplosionRadius = MissileExplosionRadius,
                Damage = MissileDamage,
                ScorchRadius = MissileScorchRadius,
                ExhaustTimer = 0f
            });

            return slot;
        }

        // Drops the next mounted underwing bomb submodel; returns slot index or -1 if unavailable.
        public int TryDropBomb(Matrix4 planeModel, Matrix4 planeOrientation, Vector3 planeVelocity, Func<int, Vector3> getBombLocalCenter)
        {
            if (bombTimer > 0f || BombsRemaining <= 0 || nextBombOrderIdx >= BombLaunchOrder.Length)
                return -1;

            int slot = BombLaunchOrder[nextBombOrderIdx++];
            BombsRemaining--;
            bombTimer = BombCooldown;

            Vector3 spawnPos = Vector3.TransformPosition(getBombLocalCenter(slot), planeModel);
            Vector3 vel = planeVelocity + new Vector3(0f, -3.5f, 0f);

            ActiveOrdnance.Add(new OrdnanceProjectile
            {
                OwnerId = -1,
                Kind = OrdnanceKind.Bomb,
                SlotIndex = slot,
                Position = spawnPos,
                PrevPosition = spawnPos,
                Velocity = vel,
                Orientation = planeOrientation,
                Age = 0f,
                MaxAge = 16.0f,
                ExplosionRadius = BombExplosionRadius,
                Damage = BombDamage,
                ScorchRadius = BombScorchRadius,
                ExhaustTimer = 0f
            });

            return slot;
        }

        // Enemy weapon spawn helpers (share the same physics, tracers, submodels, and explosions).
        public void SpawnEnemyBulletPair(int ownerId, Matrix4 enemyModel, Vector3 enemyVelocity, Vector3 aimPoint, int pairToggle)
        {
            int leftIdx = pairToggle == 0 ? 0 : 1;
            int rightIdx = pairToggle == 0 ? 3 : 2;
            int[] pair = { leftIdx, rightIdx };
            foreach (int muzzleIdx in pair)
            {
                Vector3 muzzleWorld = Vector3.TransformPosition(Plane.WingGunMuzzles[muzzleIdx], enemyModel);
                Vector3 toAim = aimPoint - muzzleWorld;
                Vector3 dir = toAim.LengthSquared > 0.01f ? Vector3.Normalize(toAim) : Vector3.Normalize(Vector3.TransformVector(Vector3.UnitZ, enemyModel));
                float forwardCarry = Math.Max(0f, Vector3.Dot(enemyVelocity, dir));
                Bullets.Add(new BulletProjectile
                {
                    OwnerId = ownerId,
                    Position = muzzleWorld,
                    PrevPosition = muzzleWorld,
                    Velocity = dir * (forwardCarry + BulletSpeed * 0.9f),
                    Age = 0f,
                    MaxAge = BulletLifetime
                });
                MuzzleFlashes.Add(new MuzzleFlash
                {
                    Position = muzzleWorld,
                    Age = 0f,
                    Lifetime = 0.055f
                });
            }
        }

        public void SpawnEnemyMissile(int ownerId, int slotIndex, Matrix4 enemyModel, Matrix4 enemyOrientation, Vector3 enemyVelocity, Vector3 aimPoint, Vector3 localCenter)
        {
            Vector3 spawnPos = Vector3.TransformPosition(localCenter, enemyModel);
            Vector3 toAim = aimPoint - spawnPos;
            Vector3 dir = toAim.LengthSquared > 0.01f ? Vector3.Normalize(toAim) : Vector3.Normalize(Vector3.TransformVector(Vector3.UnitZ, enemyOrientation));
            float forwardCarry = Math.Max(0f, Vector3.Dot(enemyVelocity, dir));
            ActiveOrdnance.Add(new OrdnanceProjectile
            {
                OwnerId = ownerId,
                Kind = OrdnanceKind.Missile,
                SlotIndex = Math.Clamp(slotIndex, 0, Plane.MissileCount - 1),
                Position = spawnPos,
                PrevPosition = spawnPos,
                Velocity = dir * (forwardCarry + MissileLaunchBoost),
                Orientation = BuildOrientationFromForward(dir, Vector3.TransformVector(Vector3.UnitY, enemyOrientation)),
                Age = 0f,
                MaxAge = 8.0f,
                ExplosionRadius = MissileExplosionRadius,
                Damage = MissileDamage,
                ScorchRadius = MissileScorchRadius,
                ExhaustTimer = 0f
            });
        }

        public void SpawnEnemyBomb(int ownerId, int slotIndex, Matrix4 enemyModel, Matrix4 enemyOrientation, Vector3 enemyVelocity, Vector3 localCenter)
        {
            Vector3 spawnPos = Vector3.TransformPosition(localCenter, enemyModel);
            ActiveOrdnance.Add(new OrdnanceProjectile
            {
                OwnerId = ownerId,
                Kind = OrdnanceKind.Bomb,
                SlotIndex = Math.Clamp(slotIndex, 0, Plane.BombCount - 1),
                Position = spawnPos,
                PrevPosition = spawnPos,
                Velocity = enemyVelocity + new Vector3(0f, -3.5f, 0f),
                Orientation = enemyOrientation,
                Age = 0f,
                MaxAge = 16.0f,
                ExplosionRadius = BombExplosionRadius,
                Damage = BombDamage,
                ScorchRadius = BombScorchRadius,
                ExhaustTimer = 0f
            });
        }

        public void SpawnAircraftExplosion(Vector3 position)
        {
            SpawnBlastParticles(position, velocityScale: 1.8f, lifetimeScale: 1.35f);
        }

        public void Update(
            float dt,
            Func<float, float, float> getTerrainHeight,
            IReadOnlyList<TargetSphere> targets,
            IReadOnlyList<ObstacleBox> obstacles,
            List<int> hitTargetIndices,
            List<CombatExplosionEvent> explosions,
            IReadOnlyList<TargetSphere>? aircraftTargets = null,
            List<CombatDamageEvent>? aircraftDamage = null)
        {
            if (gunTimer > 0f) gunTimer = Math.Max(0f, gunTimer - dt);
            if (missileTimer > 0f) missileTimer = Math.Max(0f, missileTimer - dt);
            if (bombTimer > 0f) bombTimer = Math.Max(0f, bombTimer - dt);
            if (HitMarkerTimer > 0f) HitMarkerTimer = Math.Max(0f, HitMarkerTimer - dt);

            for (int i = MuzzleFlashes.Count - 1; i >= 0; i--)
            {
                var flash = MuzzleFlashes[i];
                flash.Age += dt;
                if (flash.Age >= flash.Lifetime) MuzzleFlashes.RemoveAt(i);
                else MuzzleFlashes[i] = flash;
            }

            // Update machine-gun bullets.
            for (int i = Bullets.Count - 1; i >= 0; i--)
            {
                var b = Bullets[i];
                b.Age += dt;
                if (b.Age >= b.MaxAge)
                {
                    Bullets.RemoveAt(i);
                    continue;
                }

                b.PrevPosition = b.Position;
                b.Velocity.Y -= 9.81f * dt;
                b.Position += b.Velocity * dt;

                bool hit = false;
                // Player bullets pop balloons.
                if (b.OwnerId < 0)
                {
                    for (int t = 0; t < targets.Count; t++)
                    {
                        if (SegmentIntersectsSphere(b.PrevPosition, b.Position, targets[t].Center, targets[t].Radius))
                        {
                            hitTargetIndices.Add(targets[t].Index);
                            TriggerHitMarker();
                            hit = true;
                            break;
                        }
                    }
                }

                // Aircraft hits (player hits enemies; enemies hit player).
                if (!hit && aircraftTargets != null && aircraftDamage != null)
                {
                    for (int a = 0; a < aircraftTargets.Count; a++)
                    {
                        var ac = aircraftTargets[a];
                        if (ac.Index == b.OwnerId) continue;
                        if (b.OwnerId >= 0 && ac.Index >= 0) continue; // enemies don't friendly-fire each other
                        if (SegmentIntersectsSphere(b.PrevPosition, b.Position, ac.Center, ac.Radius))
                        {
                            aircraftDamage.Add(new CombatDamageEvent { TargetId = ac.Index, OwnerId = b.OwnerId, Damage = BulletDamage });
                            if (b.OwnerId < 0) TriggerHitMarker();
                            hit = true;
                            break;
                        }
                    }
                }

                // Check world obstacles (e.g., mountain billboard).
                if (!hit)
                {
                    for (int o = 0; o < obstacles.Count; o++)
                    {
                        if (SegmentIntersectsBox(b.PrevPosition, b.Position, obstacles[o].Center, obstacles[o].HalfExtents))
                        {
                            hit = true;
                            break;
                        }
                    }
                }

                // Check terrain impact.
                if (!hit && b.Position.Y <= getTerrainHeight(b.Position.X, b.Position.Z))
                {
                    hit = true;
                }

                if (hit) Bullets.RemoveAt(i);
                else Bullets[i] = b;
            }

            // Update active missiles and bombs.
            for (int i = ActiveOrdnance.Count - 1; i >= 0; i--)
            {
                var ord = ActiveOrdnance[i];
                ord.Age += dt;
                if (ord.Age >= ord.MaxAge)
                {
                    ActiveOrdnance.RemoveAt(i);
                    continue;
                }

                ord.PrevPosition = ord.Position;
                if (ord.Kind == OrdnanceKind.Missile)
                {
                    float speed = ord.Velocity.Length;
                    Vector3 fwd = speed > 0.1f ? ord.Velocity / speed : Vector3.UnitZ;
                    if (speed < MissileMaxSpeed)
                    {
                        ord.Velocity += fwd * (MissileAccel * dt);
                    }
                    ord.Velocity.Y -= 4.0f * dt;
                    ord.Position += ord.Velocity * dt;
                    if (ord.Velocity.LengthSquared > 0.01f)
                    {
                        ord.Orientation = BuildOrientationFromForward(Vector3.Normalize(ord.Velocity), Vector3.UnitY);
                    }

                    // Spawn rocket smoke/flame trail particles behind the missile.
                    ord.ExhaustTimer += dt;
                    if (ord.ExhaustTimer >= 0.016f)
                    {
                        ord.ExhaustTimer = 0f;
                        Vector3 tail = ord.Position - Vector3.Normalize(ord.Velocity) * 1.0f;
                        SpawnExhaustParticle(tail, -ord.Velocity * 0.05f);
                    }
                }
                else
                {
                    ord.Velocity.Y -= BombGravity * dt;
                    ord.Velocity *= MathF.Exp(-0.08f * dt);
                    ord.Position += ord.Velocity * dt;
                    if (ord.Velocity.LengthSquared > 0.01f)
                    {
                        ord.Orientation = BuildOrientationFromForward(Vector3.Normalize(ord.Velocity), Vector3.UnitY);
                    }
                }

                bool impacted = false;
                bool hitGround = false;
                Vector3 impactPos = ord.Position;

                // Impact against balloons (player ordnance).
                if (ord.OwnerId < 0)
                {
                    for (int t = 0; t < targets.Count; t++)
                    {
                        if (SegmentIntersectsSphere(ord.PrevPosition, ord.Position, targets[t].Center, targets[t].Radius + 2.0f))
                        {
                            impacted = true;
                            impactPos = targets[t].Center;
                            break;
                        }
                    }
                }

                // Direct/proximity impact against enemy or player aircraft.
                if (!impacted && aircraftTargets != null)
                {
                    for (int a = 0; a < aircraftTargets.Count; a++)
                    {
                        var ac = aircraftTargets[a];
                        if (ac.Index == ord.OwnerId) continue;
                        if (ord.OwnerId >= 0 && ac.Index >= 0) continue;
                        float prox = ord.Kind == OrdnanceKind.Bomb ? 6.0f : 3.5f;
                        if (SegmentIntersectsSphere(ord.PrevPosition, ord.Position, ac.Center, ac.Radius + prox))
                        {
                            impacted = true;
                            impactPos = ac.Center;
                            break;
                        }
                    }
                }

                // Impact against obstacles (e.g., billboard).
                if (!impacted)
                {
                    for (int o = 0; o < obstacles.Count; o++)
                    {
                        if (SegmentIntersectsBox(ord.PrevPosition, ord.Position, obstacles[o].Center, obstacles[o].HalfExtents))
                        {
                            impacted = true;
                            impactPos = ord.Position;
                            break;
                        }
                    }
                }

                // Impact against terrain.
                if (!impacted)
                {
                    float groundY = getTerrainHeight(ord.Position.X, ord.Position.Z);
                    if (ord.Position.Y <= groundY + 0.5f)
                    {
                        impacted = true;
                        hitGround = true;
                        impactPos = new Vector3(ord.Position.X, groundY + 1.0f, ord.Position.Z);
                    }
                }

                if (impacted)
                {
                    Detonate(ord, impactPos, hitGround, targets, hitTargetIndices, explosions, aircraftTargets, aircraftDamage);
                    ActiveOrdnance.RemoveAt(i);
                }
                else
                {
                    ActiveOrdnance[i] = ord;
                }
            }

            ExhaustTrail.Update(dt);
            foreach (var burst in explosionBursts) burst.Update(dt);
        }

        private void Detonate(
            in OrdnanceProjectile ord,
            Vector3 impactPos,
            bool hitGround,
            IReadOnlyList<TargetSphere> targets,
            List<int> hitTargetIndices,
            List<CombatExplosionEvent> explosions,
            IReadOnlyList<TargetSphere>? aircraftTargets,
            List<CombatDamageEvent>? aircraftDamage)
        {
            float radiusSq = ord.ExplosionRadius * ord.ExplosionRadius;
            if (ord.OwnerId < 0)
            {
                for (int t = 0; t < targets.Count; t++)
                {
                    if (Vector3.DistanceSquared(impactPos, targets[t].Center) <= radiusSq)
                    {
                        hitTargetIndices.Add(targets[t].Index);
                        TriggerHitMarker();
                    }
                }
            }

            if (aircraftTargets != null && aircraftDamage != null)
            {
                for (int a = 0; a < aircraftTargets.Count; a++)
                {
                    var ac = aircraftTargets[a];
                    if (ac.Index == ord.OwnerId) continue;
                    if (ord.OwnerId >= 0 && ac.Index >= 0) continue;
                    float distSq = Vector3.DistanceSquared(impactPos, ac.Center);
                    if (distSq <= radiusSq)
                    {
                        // Smooth splash falloff from 100% at center to 40% at edge of blast radius.
                        float dist = MathF.Sqrt(distSq);
                        float falloff = Math.Clamp(1f - 0.6f * (dist / Math.Max(1f, ord.ExplosionRadius)), 0.4f, 1f);
                        aircraftDamage.Add(new CombatDamageEvent
                        {
                            TargetId = ac.Index,
                            OwnerId = ord.OwnerId,
                            Damage = ord.Damage * falloff
                        });
                        if (ord.OwnerId < 0) TriggerHitMarker();
                    }
                }
            }

            // Spawn particle fireball(s): bombs create a dramatically larger multi-burst blast.
            if (ord.Kind == OrdnanceKind.Bomb)
            {
                SpawnBlastParticles(impactPos, velocityScale: 2.3f, lifetimeScale: 1.5f);
                SpawnBlastParticles(impactPos + Vector3.UnitY * 4f, velocityScale: 3.1f, lifetimeScale: 1.75f);
            }
            else
            {
                SpawnBlastParticles(impactPos, velocityScale: 1.15f, lifetimeScale: 1.0f);
            }

            explosions.Add(new CombatExplosionEvent
            {
                Position = impactPos,
                Radius = ord.ExplosionRadius,
                Damage = ord.Damage,
                ScorchRadius = ord.ScorchRadius,
                HitGround = hitGround,
                Source = ord.Kind,
                OwnerId = ord.OwnerId
            });
        }

        private void SpawnBlastParticles(Vector3 position, float velocityScale, float lifetimeScale)
        {
            var ps = explosionBursts[nextExplosionBurst];
            nextExplosionBurst = (nextExplosionBurst + 1) % explosionBursts.Length;
            ps.Explode(position);
            for (int i = 0; i < ps.Count; i++)
            {
                ps.Particles[i].Velocity *= velocityScale;
                ps.Particles[i].Lifetime *= lifetimeScale;
            }
        }

        private void SpawnExhaustParticle(Vector3 position, Vector3 velocity)
        {
            ExhaustTrail.Emit(position, velocity + new Vector3(
                (Random.Shared.NextSingle() - 0.5f) * 2.5f,
                (Random.Shared.NextSingle() - 0.5f) * 2.5f,
                (Random.Shared.NextSingle() - 0.5f) * 2.5f),
                0.35f + Random.Shared.NextSingle() * 0.25f);
        }

        public static Matrix4 BuildOrientationFromForward(Vector3 forward, Vector3 upHint)
        {
            Vector3 f = forward.LengthSquared > 1e-6f ? Vector3.Normalize(forward) : Vector3.UnitZ;
            Vector3 up = MathF.Abs(Vector3.Dot(f, upHint)) > 0.98f ? Vector3.UnitX : upHint;
            // Match FlightPhysics handedness: Right = Up x Forward, Up = Forward x Right.
            Vector3 right = Vector3.Normalize(Vector3.Cross(up, f));
            Vector3 orthoUp = Vector3.Normalize(Vector3.Cross(f, right));
            return new Matrix4(
                new Vector4(right, 0f),
                new Vector4(orthoUp, 0f),
                new Vector4(f, 0f),
                new Vector4(0f, 0f, 0f, 1f));
        }

        public static bool SegmentIntersectsSphere(Vector3 a, Vector3 b, Vector3 center, float radius)
        {
            Vector3 ab = b - a;
            float lenSq = ab.LengthSquared;
            float t = lenSq > 1e-6f ? Math.Clamp(Vector3.Dot(center - a, ab) / lenSq, 0f, 1f) : 0f;
            Vector3 closest = a + ab * t;
            return Vector3.DistanceSquared(closest, center) <= radius * radius;
        }

        public static bool SegmentIntersectsBox(Vector3 a, Vector3 b, Vector3 center, Vector3 halfExtents)
        {
            Vector3 min = center - halfExtents;
            Vector3 max = center + halfExtents;
            Vector3 dir = b - a;
            float tMin = 0f, tMax = 1f;
            for (int axis = 0; axis < 3; axis++)
            {
                float origin = axis == 0 ? a.X : axis == 1 ? a.Y : a.Z;
                float d = axis == 0 ? dir.X : axis == 1 ? dir.Y : dir.Z;
                float bMin = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
                float bMax = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;
                if (MathF.Abs(d) < 1e-6f)
                {
                    if (origin < bMin || origin > bMax) return false;
                }
                else
                {
                    float inv = 1f / d;
                    float t1 = (bMin - origin) * inv;
                    float t2 = (bMax - origin) * inv;
                    if (t1 > t2) (t1, t2) = (t2, t1);
                    tMin = MathF.Max(tMin, t1);
                    tMax = MathF.Min(tMax, t2);
                    if (tMin > tMax) return false;
                }
            }
            return true;
        }
    }

    // Renders glowing HDR tracer bolts, wing muzzle flashes, and rocket exhaust plumes.
    public sealed class CombatRenderer : IDisposable
    {
        private const int MaxQuads = 256;
        private int program, vao, vbo;
        private readonly float[] vertices = new float[MaxQuads * 6 * 7]; // x,y,z, r,g,b,a
        private readonly int viewLocation, projectionLocation;
        private bool disposed;

        public CombatRenderer()
        {
            int vs = 0, fs = 0;
            try
            {
                vs = Compile(ShaderType.VertexShader, @"#version 330 core
                    layout(location = 0) in vec3 aPos;
                    layout(location = 1) in vec4 aColor;
                    uniform mat4 view;
                    uniform mat4 projection;
                    out vec4 vColor;
                    void main()
                    {
                        vColor = aColor;
                        gl_Position = vec4(aPos, 1.0) * view * projection;
                    }");

                fs = Compile(ShaderType.FragmentShader, @"#version 330 core
                    in vec4 vColor;
                    out vec4 fragColor;
                    void main()
                    {
                        fragColor = vColor;
                    }");

                program = GL.CreateProgram();
                GL.AttachShader(program, vs);
                GL.AttachShader(program, fs);
                GL.LinkProgram(program);
                GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int ok);
                if (ok == 0) throw new InvalidOperationException("CombatRenderer link error: " + GL.GetProgramInfoLog(program));
                GL.DetachShader(program, vs);
                GL.DetachShader(program, fs);

                vao = GL.GenVertexArray();
                vbo = GL.GenBuffer();
                GL.BindVertexArray(vao);
                GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
                GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), IntPtr.Zero, BufferUsageHint.StreamDraw);
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), 0);
                GL.EnableVertexAttribArray(1);
                GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 7 * sizeof(float), 3 * sizeof(float));
                GL.BindVertexArray(0);

                viewLocation = GL.GetUniformLocation(program, "view");
                projectionLocation = GL.GetUniformLocation(program, "projection");
            }
            catch
            {
                Dispose();
                throw;
            }
            finally
            {
                if (vs != 0) GL.DeleteShader(vs);
                if (fs != 0) GL.DeleteShader(fs);
            }
        }

        private static int Compile(ShaderType type, string source)
        {
            int s = GL.CreateShader(type);
            GL.ShaderSource(s, source);
            GL.CompileShader(s);
            GL.GetShader(s, ShaderParameter.CompileStatus, out int ok);
            if (ok != 0) return s;
            string log = GL.GetShaderInfoLog(s);
            GL.DeleteShader(s);
            throw new InvalidOperationException($"CombatRenderer {type}: {log}");
        }

        public void Draw(CombatSimulation sim, Matrix4 view, Matrix4 projection, Vector3 cameraPos)
        {
            if (disposed) return;

            int vFloat = 0;
            void PushVert(Vector3 p, Vector4 col)
            {
                vertices[vFloat++] = p.X;
                vertices[vFloat++] = p.Y;
                vertices[vFloat++] = p.Z;
                vertices[vFloat++] = col.X;
                vertices[vFloat++] = col.Y;
                vertices[vFloat++] = col.Z;
                vertices[vFloat++] = col.W;
            }

            void PushRibbon(Vector3 head, Vector3 tail, float halfWidth, Vector4 headCol, Vector4 tailCol)
            {
                if (vFloat + 6 * 7 > vertices.Length) return;
                Vector3 seg = head - tail;
                Vector3 toCam = cameraPos - (head + tail) * 0.5f;
                Vector3 side = Vector3.Cross(seg, toCam);
                if (side.LengthSquared < 1e-6f) side = Vector3.UnitY;
                side = Vector3.Normalize(side) * halfWidth;

                Vector3 hL = head - side, hR = head + side;
                Vector3 tL = tail - side, tR = tail + side;

                PushVert(tL, tailCol);
                PushVert(tR, tailCol);
                PushVert(hR, headCol);
                PushVert(tL, tailCol);
                PushVert(hR, headCol);
                PushVert(hL, headCol);
            }

            // Glowing tracer streaks (gold-yellow for player, crimson-red for enemies).
            for (int i = 0; i < sim.Bullets.Count; i++)
            {
                var b = sim.Bullets[i];
                Vector3 dir = b.Velocity.LengthSquared > 1e-4f ? Vector3.Normalize(b.Velocity) : Vector3.UnitZ;
                Vector3 head = b.Position;
                Vector3 tail = head - dir * 8.5f;
                Vector4 headCol = b.OwnerId < 0 ? new Vector4(4.5f, 3.4f, 1.0f, 1.0f) : new Vector4(4.8f, 0.9f, 0.4f, 1.0f);
                Vector4 tailCol = b.OwnerId < 0 ? new Vector4(2.5f, 0.7f, 0.1f, 0.0f) : new Vector4(2.8f, 0.15f, 0.05f, 0.0f);
                PushRibbon(head, tail, 0.16f, headCol, tailCol);
            }

            // Bright orange-white rocket flames behind active missiles.
            for (int i = 0; i < sim.ActiveOrdnance.Count; i++)
            {
                var ord = sim.ActiveOrdnance[i];
                if (ord.Kind != OrdnanceKind.Missile) continue;
                Vector3 dir = ord.Velocity.LengthSquared > 1e-4f ? Vector3.Normalize(ord.Velocity) : Vector3.UnitZ;
                Vector3 nozzle = ord.Position - dir * 0.9f;
                Vector3 plumeEnd = nozzle - dir * 4.5f;
                PushRibbon(nozzle, plumeEnd, 0.34f, new Vector4(5.0f, 3.2f, 1.1f, 0.95f), new Vector4(2.2f, 0.45f, 0.05f, 0.0f));
            }

            // Wing muzzle flashes (crossed quads facing camera).
            for (int i = 0; i < sim.MuzzleFlashes.Count; i++)
            {
                var f = sim.MuzzleFlashes[i];
                float life = 1f - f.Age / f.Lifetime;
                float size = 0.55f + 0.35f * life;
                Vector3 toCam = cameraPos - f.Position;
                Vector3 right = Vector3.Cross(Vector3.UnitY, toCam);
                if (right.LengthSquared < 1e-5f) right = Vector3.UnitX;
                right = Vector3.Normalize(right) * size;
                Vector3 up = Vector3.Normalize(Vector3.Cross(toCam, right)) * size;
                Vector4 c = new Vector4(4.5f, 2.8f, 0.8f, life * 0.9f);
                PushRibbon(f.Position + up, f.Position - up, size, c, c);
            }

            int vertexCount = vFloat / 7;
            if (vertexCount == 0) return;

            bool blend = GL.IsEnabled(EnableCap.Blend);
            bool cull = GL.IsEnabled(EnableCap.CullFace);
            GL.GetBoolean(GetPName.DepthWritemask, out bool depthWrite);
            GL.GetInteger(GetPName.BlendSrcRgb, out int srcRgb);
            GL.GetInteger(GetPName.BlendDstRgb, out int dstRgb);
            GL.GetInteger(GetPName.BlendSrcAlpha, out int srcAlpha);
            GL.GetInteger(GetPName.BlendDstAlpha, out int dstAlpha);

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            GL.Disable(EnableCap.CullFace);
            GL.DepthMask(false);

            GL.UseProgram(program);
            GL.UniformMatrix4(viewLocation, true, ref view);
            GL.UniformMatrix4(projectionLocation, true, ref projection);

            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vFloat * sizeof(float), vertices);
            GL.DrawArrays(PrimitiveType.Triangles, 0, vertexCount);
            GL.BindVertexArray(0);

            GL.DepthMask(depthWrite);
            GL.BlendFuncSeparate((BlendingFactorSrc)srcRgb, (BlendingFactorDest)dstRgb, (BlendingFactorSrc)srcAlpha, (BlendingFactorDest)dstAlpha);
            if (!blend) GL.Disable(EnableCap.Blend);
            if (cull) GL.Enable(EnableCap.CullFace);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (vbo != 0) GL.DeleteBuffer(vbo);
            if (vao != 0) GL.DeleteVertexArray(vao);
            if (program != 0) GL.DeleteProgram(program);
            vbo = vao = program = 0;
        }
    }
}
