/*
 * Asset: OpenHeightmap
 * Filename: HydraulicErosion.cs
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

using System;

namespace OpenHeightmap
{
    /// <summary>
    /// Particle-based hydraulic erosion simulation. Spawns particles at random positions,
    /// each flows downhill under gravity, picks up sediment (erosion) and deposits it
    /// where velocity decreases. The single most impactful pass for terrain realism.
    /// 
    /// Algorithm: Each particle has position, velocity, water amount, and sediment load.
    /// At each step: compute height gradient → update velocity → move particle →
    /// compare sediment capacity to actual load → erode or deposit accordingly.
    /// </summary>
    [Serializable]
    public class HydraulicErosion
    {
        /// <summary>
        /// Erosion amount, in hundreds of droplets on a 513 map (200 = 20,000 droplets).
        /// The droplet count scales with map area, so 1025 gets four times as many and looks the same.
        /// Range: 10-1000.
        /// </summary>
        public int Iterations = 200;
        /// <summary>Droplets simulated per Iterations unit on a 513 map.</summary>
        public int DropletsPerIteration = 100;
        /// <summary>Max simulation steps per particle before forced death. Range: 5-80.</summary>
        public int MaxLifetime = 30;
        /// <summary>
        /// Momentum retention vs. gravity direction. 0 = always follows steepest descent,
        /// high values = particles carry momentum (smoother, longer rivers). Range: 0-0.2.
        /// </summary>
        public float Inertia = 0.05f;
        /// <summary>Downhill acceleration. Higher = particles gain speed faster = deeper carving. Range: 0-20.</summary>
        public float Gravity = 4f;
        /// <summary>Fraction of water lost per step. Higher = shorter-lived rivers. Range: 0-0.1.</summary>
        public float EvaporationRate = 0.02f;
        /// <summary>Speed of sediment drop-off when particle velocity decreases. Range: 0-1.</summary>
        public float depositionRate = 0.3f;
        /// <summary>How aggressively terrain is carved under particles. Range: 0-1.</summary>
        public float ErosionRate = 0.3f;
        /// <summary>Minimum sediment capacity prevents zero-capacity edge cases.</summary>
        public float MinimumSedimentCapacity = 0.01f;
        /// <summary>Scaling factor for capacity calculation. Higher = more erosion before depositing.</summary>
        public float SedimentCapacityFactor = 4f;

        private Random rng;

        // Erosion brush: each erosion step removes material from a small disc instead of a single
        // point, which carves channels instead of pits. Radius scales with resolution.
        /// <summary>X offsets of the cells in the erosion brush.</summary>
        private int[] brushOffsetX;
        /// <summary>Y offsets of the cells in the erosion brush.</summary>
        private int[] brushOffsetY;
        /// <summary>Normalized weight per brush cell (sums to 1).</summary>
        private float[] brushWeights;

        /// <summary>Runs the hydraulic erosion simulation on the given heightmap.</summary>
        public void Apply(HeightmapData heightmap, int seed = 0)
        {
            rng = new Random(seed);
            int w = heightmap.Width;
            int h = heightmap.Height;

            float resScale = (w - 1) / HeightmapGenerator.ReferenceSize;
            // Capped at 3 so very large maps (2049/4097) stay fast.
            BuildBrush(Math.Clamp((int)Math.Round(2f * resScale), 1, 3));

            long droplets = (long)Math.Max(0, Iterations) * DropletsPerIteration;
            droplets = (long)(droplets * Math.Max(0.0625f, resScale * resScale));

            for (long i = 0; i < droplets; i++)
            {
                ErodeParticle(heightmap, w, h);
            }

            heightmap.RecalculateBounds();
        }

        /// <summary>Builds a disc-shaped erosion brush of the given radius in pixels.</summary>
        private void BuildBrush(int radius)
        {
            var xs = new System.Collections.Generic.List<int>();
            var ys = new System.Collections.Generic.List<int>();
            var ws = new System.Collections.Generic.List<float>();
            float sum = 0f;

            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                    if (dist > radius)
                    {
                        continue;
                    }

                    float weight = 1f - dist / (radius + 1f);
                    xs.Add(dx);
                    ys.Add(dy);
                    ws.Add(weight);
                    sum += weight;
                }
            }

            brushOffsetX = xs.ToArray();
            brushOffsetY = ys.ToArray();
            brushWeights = ws.ToArray();

            for (int i = 0; i < brushWeights.Length; i++)
            {
                brushWeights[i] /= sum;
            }
        }

        private void ErodeParticle(HeightmapData heightmap, int w, int h)
        {
            float x = (float)rng.NextDouble() * (w - 1);
            float y = (float)rng.NextDouble() * (h - 1);

            float dirX = 0f;
            float dirY = 0f;
            float speed = 1f;
            float water = 1f;
            float sediment = 0f;

            for (int lifetime = 0; lifetime < MaxLifetime; lifetime++)
            {
                int ix = (int)x;
                int iy = (int)y;
                float fx = x - ix;
                float fy = y - iy;

                if (ix < 1 || ix >= w - 2 || iy < 1 || iy >= h - 2)
                {
                    break;
                }

                float h00 = heightmap.Heights[ix, iy];
                float h10 = heightmap.Heights[ix + 1, iy];
                float h01 = heightmap.Heights[ix, iy + 1];
                float h11 = heightmap.Heights[ix + 1, iy + 1];

                float hX = (h10 - h00) * (1f - fy) + (h11 - h01) * fy;
                float hY = (h01 - h00) * (1f - fx) + (h11 - h10) * fx;

                float newDirX = dirX * Inertia - hX * (1f - Inertia);
                float newDirY = dirY * Inertia - hY * (1f - Inertia);

                float len = (float)Math.Sqrt(newDirX * newDirX + newDirY * newDirY);

                if (len < 0.0001f)
                {
                    newDirX = (float)rng.NextDouble() * 2f - 1f;
                    newDirY = (float)rng.NextDouble() * 2f - 1f;
                    len = (float)Math.Sqrt(newDirX * newDirX + newDirY * newDirY);
                }

                dirX = newDirX / len;
                dirY = newDirY / len;

                x += dirX;
                y += dirY;

                if (x < 1 || x >= w - 2 || y < 1 || y >= h - 2)
                {
                    break;
                }

                int nix = (int)x;
                int niy = (int)y;
                float nfx = x - nix;
                float nfy = y - niy;

                float newHeight = BilinearSample(heightmap, x, y);
                float oldHeight = BilinearSample(heightmap, x - dirX, y - dirY);
                float heightDiff = newHeight - oldHeight;

                float sedimentCapacity = Math.Max(-heightDiff * speed * water * SedimentCapacityFactor, MinimumSedimentCapacity);

                if (sediment > sedimentCapacity || heightDiff > 0f)
                {
                    float amountToDeposit = (heightDiff > 0f)
                        ? Math.Min(heightDiff, sediment)
                        : (sediment - sedimentCapacity) * depositionRate;
                    sediment -= amountToDeposit;

                    DepositSediment(heightmap, x - dirX, y - dirY, amountToDeposit);
                }
                
                else
                {
                    float amountToErode = Math.Min((sedimentCapacity - sediment) * ErosionRate, -heightDiff);
                    amountToErode = Math.Min(amountToErode, 0.1f);

                    if (amountToErode > 0f)
                    {
                        sediment += amountToErode;
                        ErodeAt(heightmap, x - dirX, y - dirY, amountToErode);
                    }
                }

                speed = (float)Math.Sqrt(Math.Max(0f, speed * speed + heightDiff * Gravity));
                water *= (1f - EvaporationRate);
                sediment *= 0.9f;
            }
        }

        private float BilinearSample(HeightmapData hm, float x, float y)
        {
            int ix = (int)x;
            int iy = (int)y;
            float fx = x - ix;
            float fy = y - iy;

            ix = Math.Clamp(ix, 0, hm.Width - 2);
            iy = Math.Clamp(iy, 0, hm.Height - 2);

            float h00 = hm.Heights[ix, iy];
            float h10 = hm.Heights[ix + 1, iy];
            float h01 = hm.Heights[ix, iy + 1];
            float h11 = hm.Heights[ix + 1, iy + 1];

            return (h00 * (1 - fx) + h10 * fx) * (1 - fy) + (h01 * (1 - fx) + h11 * fx) * fy;
        }

        private void DepositSediment(HeightmapData hm, float x, float y, float amount)
        {
            int ix = (int)x;
            int iy = (int)y;
            float fx = x - ix;
            float fy = y - iy;

            ix = Math.Clamp(ix, 0, hm.Width - 2);
            iy = Math.Clamp(iy, 0, hm.Height - 2);

            hm.Heights[ix, iy] += amount * (1 - fx) * (1 - fy);
            hm.Heights[ix + 1, iy] += amount * fx * (1 - fy);
            hm.Heights[ix, iy + 1] += amount * (1 - fx) * fy;
            hm.Heights[ix + 1, iy + 1] += amount * fx * fy;
        }

        private void ErodeAt(HeightmapData hm, float x, float y, float amount)
        {
            int cx = (int)Math.Round(x);
            int cy = (int)Math.Round(y);
            int w = hm.Width;
            int h = hm.Height;

            for (int i = 0; i < brushWeights.Length; i++)
            {
                int px = cx + brushOffsetX[i];
                int py = cy + brushOffsetY[i];

                if (px < 0 || px >= w || py < 0 || py >= h)
                {
                    continue;
                }

                float delta = amount * brushWeights[i];
                // Never dig a cell below zero.
                hm.Heights[px, py] -= Math.Min(delta, Math.Max(0f, hm.Heights[px, py]));
            }
        }
    }
}
