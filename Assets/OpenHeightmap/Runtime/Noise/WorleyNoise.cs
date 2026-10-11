/*
 * Asset: OpenHeightmap
 * Filename: WorleyNoise.cs
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

using System;

namespace OpenHeightmap
{
    public enum WorleyReturnType { F1, F2, F2MinusF1, F1Normalized }

    [Serializable]
    public class WorleyNoise : INoiseProvider
    {
        public string Name => "Worley";
        public WorleyReturnType ReturnType = WorleyReturnType.F1;

        private float Hash2D(int x, int y, int seed, int offset)
        {
            int h = seed + x * 374761393 + y * 668265263 + offset * 1274126177;
            h = (h ^ (h >> 13)) * 1274126177;
            h = h ^ (h >> 16);
            return (h & 0x7fffffff) / (float)0x7fffffff;
        }

        private float Distance(float x1, float y1, float x2, float y2)
        {
            float dx = x1 - x2;
            float dy = y1 - y2;
            return dx * dx + dy * dy;
        }

        public float Sample(float x, float y, int seed = 0)
        {
            int xi = (int)Math.Floor(x);
            int yi = (int)Math.Floor(y);

            float minDist = float.MaxValue;
            float secondMinDist = float.MaxValue;

            for (int cx = -1; cx <= 1; cx++)
            {
                for (int cy = -1; cy <= 1; cy++)
                {
                    int px = xi + cx;
                    int py = yi + cy;

                    float fpX = Hash2D(px, py, seed, 0) + px;
                    float fpY = Hash2D(px, py, seed, 1) + py;

                    float dist = Distance(x, y, fpX, fpY);

                    if (dist < minDist)
                    {
                        secondMinDist = minDist;
                        minDist = dist;
                    }
                    
                    else if (dist < secondMinDist)
                    {
                        secondMinDist = dist;
                    }
                }
            }

            minDist = (float)Math.Sqrt(minDist);
            secondMinDist = (float)Math.Sqrt(secondMinDist);

            return ReturnType switch
            {
                WorleyReturnType.F2 => secondMinDist,
                WorleyReturnType.F2MinusF1 => secondMinDist - minDist,
                WorleyReturnType.F1Normalized => 1f - minDist,
                _ => minDist
            };
        }
    }
}
