/*
 * Asset: OpenHeightmap
 * Filename: MidpointDisplacement.cs
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

using System;

namespace OpenHeightmap
{
    [Serializable]
    public class MidpointDisplacement : INoiseProvider
    {
        public string Name => "Midpoint Displacement";
        public float Roughness = 0.5f;

        private float[,] grid;
        private int resolution;
        private Random rng;

        public float Sample(float x, float y, int seed = 0)
        {
            if (grid == null)
            {
                Generate(seed);
            }

            int ix = (int)(x * (resolution - 1));
            int iy = (int)(y * (resolution - 1));
            ix = Math.Clamp(ix, 0, resolution - 1);
            iy = Math.Clamp(iy, 0, resolution - 1);

            return grid[ix, iy];
        }

        public float[,] Generate(int seed = 0, int size = 0)
        {
            resolution = size > 0 ? size : 513;
            if ((resolution - 1 & (resolution - 2)) != 0)
            {
                resolution = 513;
            }

            grid = new float[resolution, resolution];
            rng = new Random(seed);

            grid[0, 0] = (float)rng.NextDouble();
            grid[0, resolution - 1] = (float)rng.NextDouble();
            grid[resolution - 1, 0] = (float)rng.NextDouble();
            grid[resolution - 1, resolution - 1] = (float)rng.NextDouble();

            float range = 1f;
            int step = resolution - 1;

            while (step > 1)
            {
                int half = step / 2;

                for (int y = 0; y < resolution - 1; y += step)
                {
                    for (int x = 0; x < resolution - 1; x += step)
                    {
                        float avg = (grid[x, y] + grid[x + step, y] +
                                     grid[x, y + step] + grid[x + step, y + step]) / 4f;
                        grid[x + half, y + half] = avg + ((float)rng.NextDouble() * 2f - 1f) * range;
                    }
                }

                for (int y = 0; y < resolution; y += half)
                {
                    for (int x = (y / half % 2 == 0) ? half : 0; x < resolution; x += step)
                    {
                        if (grid[x, y] == 0f)
                        {
                            int count = 0;
                            float sum = 0f;

                            if (x - half >= 0) 
                            { 
                                sum += grid[x - half, y]; count++; 
                            }

                            if (x + half < resolution) 
                            { 
                                sum += grid[x + half, y]; count++; 
                            }

                            if (y - half >= 0) 
                            { 
                                sum += grid[x, y - half]; count++; 
                            }

                            if (y + half < resolution) 
                            { 
                                sum += grid[x, y + half]; count++; 
                            }

                            grid[x, y] = sum / count + ((float)rng.NextDouble() * 2f - 1f) * range;
                        }
                    }
                }

                step = half;
                range *= Roughness;
            }

            NormalizeGrid();
            return grid;
        }

        private void NormalizeGrid()
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int x = 0; x < resolution; x++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    if (grid[x, y] < min) 
                    {
                        min = grid[x, y];
                    }

                    if (grid[x, y] > max) 
                    {
                        max = grid[x, y];
                    }
                }
            }

            float range = max - min;
            if (range < 0.0001f) 
            {
                return;
            }

            for (int x = 0; x < resolution; x++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    grid[x, y] = (grid[x, y] - min) / range;
                }
            }
        }
    }
}
