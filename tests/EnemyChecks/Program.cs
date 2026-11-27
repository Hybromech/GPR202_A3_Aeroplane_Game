using GPR202_Assessment3;
using OpenTK.Mathematics;

static void Assert(bool cond, string msg)
{
    if (!cond) throw new Exception("FAIL: " + msg);
}

static float FlatGround(float x, float z) => 0f;

// 1. Verify EnemyFlightSettings inheritance + GameManager + EnemySpawner setup API.
var customEnemySettings = new EnemyFlightSettings { StartSpeed = 90f, MinTerrainClearance = 60f };
Assert(customEnemySettings is FlightSettings, "EnemyFlightSettings must inherit from FlightSettings");
var gm = new GameManager(customEnemySettings);
Assert(gm.EnemyFlightSettings == customEnemySettings, "GameManager must use supplied EnemyFlightSettings");
gm.EnemySpawner.ConfigureSpawnContext(new Vector3(0f, 200f, 0f), FlatGround);
var e1 = new EnemyAI(EnemyPersonality.Aggressor, EnemyWeapon.MachineGuns, 100, 0, 0, customEnemySettings, seed: 1);
Assert(e1.FlightSettings is FlightSettings && e1.FlightSettings.StartSpeed == 90f && e1.FlightSettings.MinTerrainClearance == 60f, "EnemyAI must use cloned EnemyFlightSettings");
var e2 = new EnemyAI(EnemyPersonality.Wanderer, EnemyWeapon.Missiles, 0, 5, 0, seed: 2);
var e3 = new EnemyAI(EnemyPersonality.Bomber, EnemyWeapon.Bombs, 0, 0, 2, seed: 3);
gm.EnemySpawner.Add(e1);
gm.EnemySpawner.Add(e2);
gm.EnemySpawner.Add(e3);
gm.EnemySpawner.SpawnAll();

Assert(gm.EnemySpawner.AliveCount == 3, "All 3 queued enemies must spawn alive");
Assert(e1.MachineGunAmmo == 100 && e1.MissileAmmo == 0 && e1.BombAmmo == 0, "Enemy1 loadout mismatch");
Assert(e2.MachineGunAmmo == 0 && e2.MissileAmmo == 5 && e2.BombAmmo == 0, "Enemy2 loadout mismatch");
Assert(e3.MachineGunAmmo == 0 && e3.MissileAmmo == 0 && e3.BombAmmo == 2, "Enemy3 loadout mismatch");

// 2. Verify Aggressor detects faster than Wanderer under identical line-of-sight conditions.
var agg = new EnemyAI(EnemyPersonality.Aggressor, EnemyWeapon.MachineGuns, 100, 0, 0, seed: 10);
var wan = new EnemyAI(EnemyPersonality.Wanderer, EnemyWeapon.MachineGuns, 100, 0, 0, seed: 11);
agg.Spawn(new Vector3(0f, 200f, 0f), new Vector3(0f, 0f, -1f));
wan.Spawn(new Vector3(0f, 200f, 0f), new Vector3(0f, 0f, -1f));
Vector3 playerPos = new Vector3(0f, 200f, -260f);
Vector3 playerVel = new Vector3(0f, 0f, -80f);
Vector3 playerFwd = new Vector3(0f, 0f, -1f);

float aggDetectTime = -1f, wanDetectTime = -1f;
for (int step = 0; step < 360; step++)
{
    float dt = 1f / 60f;
    playerPos += playerVel * dt;
    agg.Update(dt, playerPos, playerVel, playerFwd, true, FlatGround);
    wan.Update(dt, playerPos, playerVel, playerFwd, true, FlatGround);
    if (aggDetectTime < 0f && agg.HasDetectedPlayer) aggDetectTime = step * dt;
    if (wanDetectTime < 0f && wan.HasDetectedPlayer) wanDetectTime = step * dt;
}
Assert(aggDetectTime >= 0f && aggDetectTime < 1.0f, $"Aggressor must detect quickly (took {aggDetectTime:F2}s)");
Assert(wanDetectTime < 0f || wanDetectTime > aggDetectTime * 3f, $"Wanderer must detect much slower than Aggressor (agg={aggDetectTime:F2}s, wan={wanDetectTime:F2}s)");

// 3. Verify Aggressor turns toward a player off to its starboard side, closes behind tail, and fires guns.
var pursuer = new EnemyAI(EnemyPersonality.Aggressor, EnemyWeapon.MachineGuns, 100, 0, 0, seed: 20);
pursuer.Spawn(new Vector3(0f, 220f, 0f), new Vector3(0f, 0f, -1f));
Vector3 pPos = new Vector3(140f, 220f, -240f);
Vector3 pFwd = new Vector3(0f, 0f, -1f);
Vector3 pVel = pFwd * 65f;
bool aggressorFired = false;
float initialDist = Vector3.Distance(pursuer.Position, pPos);
float minDist = initialDist;
for (int step = 0; step < 900; step++)
{
    float dt = 1f / 60f;
    pPos += pVel * dt;
    var cmd = pursuer.Update(dt, pPos, pVel, pFwd, true, FlatGround);
    if (cmd.FireGuns) aggressorFired = true;
    minDist = MathF.Min(minDist, Vector3.Distance(pursuer.Position, pPos));
    Assert(pursuer.Position.Y > 10f, "Aggressor must stay safely above terrain");
}
Assert(minDist < initialDist * 0.7f, $"Aggressor must close distance toward player tail (init={initialDist:F1}m, min={minDist:F1}m)");
Assert(aggressorFired, "Aggressor equipped with MachineGuns must fire when tracking player");
Assert(pursuer.MachineGunAmmo < 100, "Aggressor gun ammo must decrement when firing");

// 4. Verify Bomber climbs above player and drops bombs when overhead.
var bomber = new EnemyAI(EnemyPersonality.Bomber, EnemyWeapon.Bombs, 0, 0, 2, seed: 30);
bomber.Spawn(new Vector3(0f, 240f, 160f), new Vector3(0f, 0f, -1f));
Vector3 bPlayerPos = new Vector3(0f, 140f, -60f);
Vector3 bPlayerVel = new Vector3(0f, 0f, -40f);
bool bomberDropped = false;
float maxBomberY = bomber.Position.Y;
for (int step = 0; step < 600; step++)
{
    float dt = 1f / 60f;
    bPlayerPos += bPlayerVel * dt;
    var cmd = bomber.Update(dt, bPlayerPos, bPlayerVel, new Vector3(0f, 0f, -1f), true, FlatGround);
    maxBomberY = MathF.Max(maxBomberY, bomber.Position.Y);
    if (cmd.DropBomb) bomberDropped = true;
}
Assert(maxBomberY > bPlayerPos.Y + 80f, "Bomber must fly high above the player");
Assert(bomberDropped, "Bomber must drop a bomb when flying over the player");
Assert(bomber.BombAmmo < 2, "Bomber bomb ammo must decrement on drop");

// 5. Verify Wanderer equipped with Missiles fires missiles once it detects the player.
var missileWanderer = new EnemyAI(EnemyPersonality.Wanderer, EnemyWeapon.Missiles, 0, 5, 0, seed: 40);
missileWanderer.Spawn(new Vector3(0f, 220f, 0f), new Vector3(0f, 0f, -1f));
Vector3 mPlayerPos = new Vector3(0f, 220f, -220f);
Vector3 mPlayerVel = new Vector3(0f, 0f, -65f);
bool missileFired = false;
for (int step = 0; step < 480; step++)
{
    float dt = 1f / 60f;
    mPlayerPos += mPlayerVel * dt;
    var cmd = missileWanderer.Update(dt, mPlayerPos, mPlayerVel, new Vector3(0f, 0f, -1f), true, FlatGround);
    if (cmd.FireMissile) { missileFired = true; break; }
}
Assert(missileFired, "Wanderer equipped with Missiles must launch a missile after detecting player");
Assert(missileWanderer.MissileAmmo == 4, "Wanderer missile ammo must decrement from 5 to 4");

// 6. Verify EnemyAI constructor sets plane health and GameManager damage destroys planes when health reaches 0.
var customHpEnemy = new EnemyAI(EnemyPersonality.Bomber, EnemyWeapon.Bombs, 0, 0, 2, health: 150f);
Assert(customHpEnemy.Plane.Health == 150f && customHpEnemy.Plane.MaxHealth == 150f, "EnemyAI constructor must set its Plane health");

var combat = new CombatSimulation();
var playerPlane = Plane.CreateEntity();
var gm2 = new GameManager();
gm2.ResetForNewGame(new Vector3(0f, 200f, 0f), FlatGround, playerPlane);
Assert(gm2.EnemySpawner.AliveCount == 4, "Default GameManager.SetupEnemies() must spawn 4 enemies");
Assert(gm2.EnemySpawner.Enemies[0].Health == 100f && playerPlane.Health == 100f, "Default planes must start at 100 health");

var dmgEvents = new List<CombatDamageEvent>
{
    new() { TargetId = 0, OwnerId = -1, Damage = 100f },
    new() { TargetId = -1, OwnerId = 1, Damage = CombatSimulation.BulletDamage }
};
bool playerDead = gm2.ApplyDamageEvents(dmgEvents, combat, playerPlane);
Assert(!playerDead && playerPlane.Health == 98f && gm2.PlayerHealth == 98f, "One enemy bullet must reduce player plane health from 100 to 98");
Assert(gm2.EnemiesDestroyed == 1 && gm2.EnemySpawner.AliveCount == 3 && gm2.EnemySpawner.Enemies[0].Plane.IsDestroyed, "Enemy 0 plane must be destroyed when health is depleted");

bool playerKilledNow = gm2.ApplyDamageEvents(new List<CombatDamageEvent> { new() { TargetId = -1, OwnerId = 1, Damage = 98f } }, combat, playerPlane);
Assert(playerKilledNow && playerPlane.IsDestroyed && playerPlane.Health == 0f, "Player plane must be destroyed when health is depleted");

Console.WriteLine("PASS EnemyAI personalities (Wanderer/Aggressor/Bomber), weapons (MachineGuns/Missiles/Bombs), FlightPhysics guidance, EnemySpawner, and GameManager checks.");

