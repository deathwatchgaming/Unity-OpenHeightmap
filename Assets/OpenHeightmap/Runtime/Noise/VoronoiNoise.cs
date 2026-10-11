/*
 * Asset: OpenHeightmap
 * Filename: VoronoiNoise.cs
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

using System;

namespace OpenHeightmap
{
    public enum VoronoiDistanceMetric { Euclidean, Manhattan, Chebyshev }
    public enum VoronoiReturnType { CellValue, Distance, Distance2 }

    [Serializable]
    public class VoronoiNoise : INoiseProvider
    {
        public string Name => "Voronoi";
        public VoronoiDistanceMetric DistanceMetric = VoronoiDistanceMetric.Euclidean;
        public VoronoiReturnType ReturnType = VoronoiReturnType.CellValue;


        private float Distance(float x1, float y1, float x2, float y2)
        {
            return DistanceMetric switch
            {
                VoronoiDistanceMetric.Manhattan => Math.Abs(x1 - x2) + Math.Abs(y1 - y2),
                VoronoiDistanceMetric.Chebyshev => Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2)),
                _ => (float)Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2))
            };
        }

        /// <summary>
        /// Integer hash of a cell (plus a salt) to [0, 1]. Integer maths avoids the overflow the old
        /// float-based version hit, which gave most cells the same value.
        /// </summary>
        private static float Hash(int x, int y, int seed, int salt)
        {
            unchecked
            {
                int h = seed * 1274126177 + x * 374761393 + y * 668265263 + salt * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        public float Sample(float x, float y, int seed = 0)
        {
            int xi = (int)Math.Floor(x);
            int yi = (int)Math.Floor(y);

            float minDist = float.MaxValue;
            float secondMinDist = float.MaxValue;
            float cellValue = 0f;

            for (int cx = -1; cx <= 1; cx++)
            {
                for (int cy = -1; cy <= 1; cy++)
                {
                    int px = xi + cx;
                    int py = yi + cy;

                    float fpX = Hash(px, py, seed, 0) + px;
                    float fpY = Hash(px, py, seed, 1) + py;

                    float dist = Distance(x, y, fpX, fpY);

                    if (dist < minDist)
                    {
                        secondMinDist = minDist;
                        minDist = dist;
                        cellValue = Hash(px, py, seed, 2);
                    }
                    
                    else if (dist < secondMinDist)
                    {
                        secondMinDist = dist;
                    }
                }
            }

            return ReturnType switch
            {
                VoronoiReturnType.Distance => minDist,
                VoronoiReturnType.Distance2 => secondMinDist - minDist,
                _ => cellValue
            };
        }
    }
}
