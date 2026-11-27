using GPR202_Assessment3;
using OpenTK.Mathematics;

static void Assert(bool cond, string msg)
{
    if (!cond) throw new Exception("FAIL: " + msg);
}

var sim = new CombatSimulation();
var hits = new List<int>();
var explosions = new List<CombatExplosionEvent>();
var emptyTargets = Array.Empty<TargetSphere>();
var emptyObstacles = Array.Empty<ObstacleBox>();

// 1. Verify initial ammo and bomb > missile blast radius and damage.
Assert(sim.GunAmmo == 400, "Initial gun ammo must be 400");
Assert(sim.MissilesRemaining == 6, "Initial missiles must be 6");
Assert(sim.BombsRemaining == 2, "Initial bombs must be 2");
Assert(CombatSimulation.BombExplosionRadius > CombatSimulation.MissileExplosionRadius, "Bomb blast radius must exceed missile");
Assert(CombatSimulation.BombDamage > CombatSimulation.MissileDamage, "Bomb damage must exceed missile");
Assert(Plane.WingGunMuzzles.Length == 4, "Plane must have 2 machine guns per wing (4 total)");

// 2. Verify wing machine guns fire pairs and pop a balloon on hit.
Matrix4 planeModel = Matrix4.CreateTranslation(new Vector3(0f, 100f, 0f));
Vector3 aim = new Vector3(0f, 100f, 120f);
Assert(sim.TryFireGuns(planeModel, new Vector3(0f, 0f, 80f), aim), "First gun shot must fire");
Assert(sim.GunAmmo == 398, "Pair of wing guns must consume 2 rounds");
Assert(sim.Bullets.Count == 2, "Pair of wing guns must spawn 2 bullets");

var balloonTarget = new[] { new TargetSphere { Index = 7, Center = new Vector3(0f, 100f, 120f), Radius = 12f } };
bool bulletPopped = false;
for (int step = 0; step < 60; step++)
{
    hits.Clear();
    explosions.Clear();
    sim.Update(1f / 60f, (_, _) => 0f, balloonTarget, emptyObstacles, hits, explosions);
    if (hits.Contains(7)) { bulletPopped = true; break; }
}
Assert(bulletPopped, "Machine-gun bullets must hit and pop targeted balloon");
Assert(sim.HitMarkerTimer > 0f, "Balloon hit must trigger crosshair hit marker");

// 3. Verify all 6 missiles fire in alternating wing order and cannot exceed 6.
sim.Reset();
var firedSlots = new List<int>();
for (int k = 0; k < 8; k++)
{
    int slot = sim.TryFireMissile(planeModel, Matrix4.Identity, new Vector3(0f, 0f, 80f), new Vector3(0f, 100f, 300f), i => new Vector3(i < 3 ? -4f : 4f, 0.2f, 0f));
    if (slot >= 0) firedSlots.Add(slot);
    for (int s = 0; s < 30; s++) sim.Update(1f / 60f, (_, _) => -1000f, emptyTargets, emptyObstacles, hits, explosions);
}
Assert(firedSlots.Count == 6, "Exactly 6 missiles must be launchable");
Assert(sim.MissilesRemaining == 0, "Missiles remaining must reach 0");
Assert(firedSlots.Distinct().Count() == 6, "All 6 distinct missile submodel slots must be used");

// 4. Verify missile explodes on obstacle impact and pops balloons inside missile radius (not outside).
sim.Reset();
int mSlot = sim.TryFireMissile(planeModel, Matrix4.Identity, new Vector3(0f, 0f, 80f), new Vector3(0f, 100f, 200f), _ => Vector3.Zero);
Assert(mSlot >= 0, "Missile must launch");
var wall = new[] { new ObstacleBox { Center = new Vector3(0f, 100f, 120f), HalfExtents = new Vector3(20f, 20f, 5f) } };
var blastTargets = new[]
{
    new TargetSphere { Index = 1, Center = new Vector3(30f, 100f, 120f), Radius = 12f }, // within 45m missile radius
    new TargetSphere { Index = 2, Center = new Vector3(70f, 100f, 120f), Radius = 12f }  // outside 45m, inside 95m bomb radius
};
bool missileExploded = false;
var missileHits = new HashSet<int>();
for (int step = 0; step < 120 && !missileExploded; step++)
{
    hits.Clear();
    explosions.Clear();
    sim.Update(1f / 60f, (_, _) => -500f, blastTargets, wall, hits, explosions);
    foreach (int h in hits) missileHits.Add(h);
    if (explosions.Count > 0) missileExploded = true;
}
Assert(missileExploded, "Missile must explode on obstacle impact");
Assert(missileHits.Contains(1), "Missile blast must pop balloon within 45m");
Assert(!missileHits.Contains(2), "Missile blast must not reach balloon 70m away");

// 5. Verify 2 large bombs drop, explode on ground, and pop balloon 70m away with larger damage/radius.
sim.Reset();
int b0 = sim.TryDropBomb(planeModel, Matrix4.Identity, new Vector3(0f, 0f, 0f), _ => new Vector3(-3.3f, -0.1f, 0f));
Assert(b0 == 0 && sim.BombsRemaining == 1, "First bomb must drop from slot 0");
var bombTargets = new[] { new TargetSphere { Index = 2, Center = new Vector3(70f, 10f, 0f), Radius = 12f } };
bool bombExploded = false;
bool bombPoppedFarBalloon = false;
CombatExplosionEvent bombExp = default;
for (int step = 0; step < 300 && !bombExploded; step++)
{
    hits.Clear();
    explosions.Clear();
    sim.Update(1f / 60f, (_, _) => 0f, bombTargets, emptyObstacles, hits, explosions);
    if (hits.Contains(2)) bombPoppedFarBalloon = true;
    if (explosions.Count > 0) { bombExploded = true; bombExp = explosions[0]; }
}
Assert(bombExploded && bombExp.HitGround, "Bomb must fall and explode on terrain impact");
Assert(bombExp.Damage == CombatSimulation.BombDamage && bombExp.Radius == CombatSimulation.BombExplosionRadius, "Bomb explosion must use larger radius and damage");
Assert(bombPoppedFarBalloon, "Bomb larger blast radius (95m) must pop balloon 70m away");

int b1 = sim.TryDropBomb(planeModel, Matrix4.Identity, Vector3.Zero, _ => new Vector3(3.2f, -0.1f, 0f));
Assert(b1 == 1 && sim.BombsRemaining == 0, "Second bomb must drop from slot 1");
for (int s = 0; s < 35; s++) sim.Update(1f / 60f, (_, _) => -1000f, emptyTargets, emptyObstacles, hits, explosions);
Assert(sim.TryDropBomb(planeModel, Matrix4.Identity, Vector3.Zero, _ => Vector3.Zero) == -1, "No third bomb may be dropped");

// Verify RestockWeapons refills ammo & ordnance slots without clearing in-flight projectiles.
int inFlightBefore = sim.ActiveOrdnance.Count;
sim.RestockWeapons();
Assert(sim.GunAmmo == 400 && sim.MissilesRemaining == 6 && sim.BombsRemaining == 2, "RestockWeapons must restore all ammo");
Assert(sim.ActiveOrdnance.Count == inFlightBefore, "RestockWeapons must preserve active in-flight ordnance");

sim.Reset();
Assert(sim.GunAmmo == 400 && sim.MissilesRemaining == 6 && sim.BombsRemaining == 2, "Reset must restore full combat loadout");
Console.WriteLine("PASS combat system: 4 wing guns, 6 missile submodels, 2 large bombs, obstacle/ground explosions, and balloon pop checks.");

