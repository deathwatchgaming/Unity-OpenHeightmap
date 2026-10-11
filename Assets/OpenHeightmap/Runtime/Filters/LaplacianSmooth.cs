/*
 * Asset: OpenHeightmap
 * Filename: LaplacianSmooth.cs
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

using System;

namespace OpenHeightmap
{
    /// <summary>
    /// Laplacian smoothing: each vertex moves toward the average of its 4 neighbors
    /// by a Strength factor. Reduces high-frequency noise while preserving large-scale
    /// terrain shape. Less aggressive than Gaussian blur.
    /// </summary>
    [Serializable]
    public class LaplacianSmooth
    {
        /// <summary>Number of smoothing passes. Range: 1-10.</summary>
        public int Passes = 3;
        /// <summary>How strongly each vertex moves toward its neighbors' average. Range: 0.01-1.</summary>
        public float Strength = 0.5f;

        /// <summary>Applies Laplacian smoothing to the heightmap in-place.</summary>
        public void Apply(HeightmapData heightmap)
        {
            int w = heightmap.Width;
            int h = heightmap.Height;

            for (int p = 0; p < Passes; p++)
            {
                float[,] temp = new float[w, h];
                Array.Copy(heightmap.Heights, temp, w * h);

                for (int x = 1; x < w - 1; x++)
                {
                    for (int y = 1; y < h - 1; y++)
                    {
                        float center = temp[x, y];
                        float neighbors = temp[x - 1, y] + temp[x + 1, y] +
                                         temp[x, y - 1] + temp[x, y + 1];
                        float avg = neighbors / 4f;
                        heightmap.Heights[x, y] = center + (avg - center) * Strength;
                    }
                }
            }

            heightmap.RecalculateBounds();
        }
    }
}
