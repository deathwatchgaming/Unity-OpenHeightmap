/*
 * Asset: OpenHeightmap
 * Filename: ThermalErosion.cs
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

using System;

namespace OpenHeightmap
{
    /// <summary>
    /// Thermal erosion: slope-based material displacement. Material slides downhill when
    /// the slope exceeds the TalusAngle threshold. Produces natural rounding of peaks and
    /// filling of valleys. Less dramatic than hydraulic but gives a weathered look.
    /// </summary>
    [Serializable]
    public class ThermalErosion
    {
        /// <summary>Number of thermal erosion passes. Range: 1-50.</summary>
        public int Iterations = 20;
        /// <summary>
        /// Slope threshold before material moves. Lower = more aggressive rounding.
        /// Range: 0.001-0.1.
        /// </summary>
        public float TalusAngle = 0.01f;
        /// <summary>Material transfer rate constant. 1.0 = standard speed. Range: 0.1-5.</summary>
        public float C = 1f;

        /// <summary>Applies thermal erosion to the heightmap in-place.</summary>
        public void Apply(HeightmapData heightmap)
        {
            int w = heightmap.Width;
            // Talus is defined per reference pixel (a 513 map). Neighbouring cells on a finer map
            // are closer together, so the same slope gives a smaller height difference.
            float talus = TalusAngle * HeightmapGenerator.GetReferenceScale(w);
            int h = heightmap.Height;
            float[,] temp = new float[w, h];

            for (int iter = 0; iter < Iterations; iter++)
            {
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        temp[x, y] = heightmap.Heights[x, y];
                    }
                }

                for (int x = 1; x < w - 1; x++)
                {
                    for (int y = 1; y < h - 1; y++)
                    {
                        float maxDelta = 0f;
                        int maxX = 0, maxY = 0;

                        for (int nx = -1; nx <= 1; nx++)
                        {
                            for (int ny = -1; ny <= 1; ny++)
                            {
                                if (nx == 0 && ny == 0) 
                                {
                                    continue;
                                }

                                float delta = temp[x, y] - temp[x + nx, y + ny];
                                
                                if (delta > maxDelta)
                                {
                                    maxDelta = delta;
                                    maxX = nx;
                                    maxY = ny;
                                }
                            }
                        }

                        if (maxDelta > talus)
                        {
                            float transfer = C * (maxDelta - talus) / 8f;
                            transfer = Math.Min(transfer, maxDelta * 0.5f);

                            heightmap.Heights[x, y] -= transfer;
                            heightmap.Heights[x + maxX, y + maxY] += transfer;
                        }
                    }
                }
            }

            heightmap.RecalculateBounds();
        }
    }
}
