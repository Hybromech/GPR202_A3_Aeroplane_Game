using GPR202_Assessment3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTK.Mathematics;
using System.Diagnostics;

namespace GPR_Command
{
    public class BalloonState
    {
        private const float DEFAULT_SCALE = 2.0f;
        private const float ROTATE_SPEED = 1.0f;

        public static Random random = new Random();

        public Vector3 position;
        public Vector3 rotation;
        public bool collected;
        public float animate;
        public float scale;
        public Vector3 SpawnPosition { get; private set; }
        private double wanderTime;
        private float wanderPhase;


        

        public BalloonState()
        {
            position = Vector3.Zero;
            collected = false;
            animate = 0f;
            scale = DEFAULT_SCALE;
        }

        // Reset Balloon State and assign a random position in the game world.
        public void Spawn()
        {
            int position_x = random.Next(0, (int)Terrain.width);
            int position_z = random.Next(0, (int)Terrain.length);
            float balloon_h = random.Next(15, 65);

            position = Terrain.WorldPositionFromHeightMap(position_x, position_z) + new Vector3(0f, balloon_h, 0f);
            SpawnPosition = position;
            wanderTime = 0;
            wanderPhase = random.NextSingle() * MathF.Tau;
            rotation = new Vector3(0f, random.Next(0, 359), 0f);
            collected = false;
            animate = 0f;
            scale = DEFAULT_SCALE;
        }

        private void UpdateWander(float dt)
        {
            float radius = Math.Max(0f, Balloon.WanderRadius);
            float speed = Math.Max(0f, Balloon.WanderSpeed);
            wanderTime += dt;
            double t = wanderTime * speed / Math.Max(1f, radius);
            // Independent phases avoid synchronized motion; intersecting slow waves resemble drifting wind.
            Vector3 offset = new Vector3(
                (float)Math.Sin(t + wanderPhase)*radius*0.65f,
                (float)Math.Sin(wanderTime*Math.Tau/Math.Max(0.1f,Balloon.BobPeriod)+wanderPhase)*Math.Max(0f,Balloon.BobHeight),
                (float)Math.Sin(t*0.73 + wanderPhase*1.7f)*radius*0.65f);
            if(offset.LengthSquared>radius*radius && offset.LengthSquared>0f) offset=offset.Normalized()*radius;
            Vector3 target=SpawnPosition+offset;
            target.X=Math.Clamp(target.X,-Terrain.width/2f,Terrain.width/2f-1f);
            target.Z=Math.Clamp(target.Z,-Terrain.length/2f,Terrain.length/2f-1f);
            Vector3 movement=target-position;
            float maxMove=speed*dt;
            if(movement.LengthSquared>maxMove*maxMove && movement.LengthSquared>0f) movement=movement.Normalized()*maxMove;
            Vector3 candidate=position+movement;
            // Stay above mountains rather than jumping upward or leaving the spawn radius.
            if(candidate.Y >= Terrain.GetInterpolatedHeightAtWorld(candidate.X,candidate.Z)+Math.Max(0f,Balloon.GroundClearance))
                position=candidate;
        }

        public Vector3 Center => position + new Vector3(0f, 5.0f, 0f);

        public bool Pop()
        {
            if (collected) return false;
            collected = true;
            animate = 0.0001f;
            return true;
        }

        public bool Update(Vector3 playerpos, float deltaT)
        {
            if (collected == false)
            {
                UpdateWander(Math.Max(0f, deltaT));
                rotation += new Vector3(0f, deltaT * ROTATE_SPEED, 0f);

                // PERFORMANCE OPTIMIZATION: Use DistanceSquared to avoid expensive square root 
                // calculations (sqrt) during per-frame collision checks.
                float collectThresholdSq = 15.0f * 15.0f;
                if (Vector3.DistanceSquared(Center, playerpos) < collectThresholdSq)
                {
                    Pop();
                }
            }
            else
            {
                if (animate > 0f && animate < 1f)
                {
                    animate += (deltaT * 4);
                    animate = MathHelper.Clamp(animate, 0f, 1f);
                    scale = MathHelper.Clamp(2f - (animate * 2), 0f, 2f);
                    position += new Vector3(0f, (deltaT * 64), 0f);
                    rotation += new Vector3((deltaT * 64), 0, (deltaT * 64));
                    if (animate == 1f)
                    {
                        // Return true to destroy balloon.
                        return true;
                    }
                    
                }
            }
            return false;
        }
    }
}
