/*
 * Asset: OpenHeightmap
 * Filename: DomainWarping.cs
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

using System;

namespace OpenHeightmap
{
    /// <summary>
    /// Domain warping distorts noise sample coordinates using a secondary Perlin noise field
    /// before sampling the primary noise. This breaks up regularity and produces organic,
    /// non-repeating geological formations. Applied as the second pass (after noise, before erosion).
    /// </summary>
    [Serializable]
    public class DomainWarping
    {
        /// <summary>
        /// How far heights are displaced, as a fraction of the map width. 0.02 = up to about 2% of the map.
        /// Range: 0.001-0.1.
        /// </summary>
        public float WarpStrength = 0.02f;
        /// <summary>Number of noise octaves used for the warp field. Range: 1-4.</summary>
        public int WarpOctaves = 2;
        /// <summary>
        /// Frequency of the warp field, in the same units as the main noise Scale
        /// (0.01 = about 5 warp "swirls" across the map). Range: 0.001-0.1.
        /// </summary>
        public float WarpFrequency = 0.01f;
        /// <summary>Master toggle. When false, Apply() is a no-op.</summary>
        public bool Enabled = false;

        /// <summary>Noise used for the warp field.</summary>
        private readonly PerlinNoise perlin = new PerlinNoise();

        public void Apply(HeightmapData heightmap, int seed = 0)
        {
            if (!Enabled) 
            {
                return;
            }

            int w = heightmap.Width;
            int h = heightmap.Height;
            int octaves = Math.Max(1, WarpOctaves);
            float refScale = HeightmapGenerator.GetReferenceScale(w);

            // Separate offset sets for the X and Y warp fields so they are independent.
            HeightmapGenerator.GetSeedOffsets(seed ^ 0x5bd1e995, octaves, out float[] offXa, out float[] offYa);
            HeightmapGenerator.GetSeedOffsets(seed ^ 0x1b873593, octaves, out float[] offXb, out float[] offYb);

            float[,] result = new float[w, h];

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    // Warp field sampled in the same reference space as the main noise.
                    float px = x * refScale;
                    float py = y * refScale;

                    float warpX = 0f;
                    float warpY = 0f;
                    float amp = 1f;
                    float ampSum = 0f;
                    float freq = WarpFrequency;

                    for (int o = 0; o < octaves; o++)
                    {
                        warpX += amp * perlin.Sample(px * freq + offXa[o], py * freq + offYa[o], seed);
                        warpY += amp * perlin.Sample(px * freq + offXb[o], py * freq + offYb[o], seed);
                        ampSum += amp;
                        amp *= 0.5f;
                        freq *= 2f;
                    }

                    warpX /= ampSum;
                    warpY /= ampSum;

                    float nx = (float)x / (w - 1);
                    float ny = (float)y / (h - 1);

                    float sampleX = Math.Clamp(nx + warpX * WarpStrength, 0f, 1f);
                    float sampleY = Math.Clamp(ny + warpY * WarpStrength, 0f, 1f);

                    result[x, y] = heightmap.GetHeightBilinear(sampleX, sampleY);
                }
            }

            Array.Copy(result, heightmap.Heights, w * h);
            heightmap.RecalculateBounds();
        }
    }
}
