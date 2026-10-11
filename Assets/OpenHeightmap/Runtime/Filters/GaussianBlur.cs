/*
 * Asset: OpenHeightmap
 * Filename: GaussianBlur.cs
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

using System;

namespace OpenHeightmap
{
    /// <summary>
    /// Convolution-based Gaussian blur filter. Smooths heightmap detail by averaging
    /// neighboring pixels weighted by a Gaussian distribution. Multiple passes compound
    /// the effect (equivalent to a wider kernel).
    /// </summary>
    [Serializable]
    public class GaussianBlur
    {
        /// <summary>Kernel width/height in pixels. Must be odd (auto-corrected in UI). Range: 3-15.</summary>
        public int KernelSize = 3;
        /// <summary>Gaussian standard deviation. Controls blur spread. Higher = more blur. Range: 0.1-10.</summary>
        public float Sigma = 1.0f;
        /// <summary>Number of blur passes. Each pass applies the full kernel again. Range: 1-5.</summary>
        public int Passes = 1;

        /// <summary>Applies Gaussian blur to the entire heightmap.</summary>
        public void Apply(HeightmapData heightmap)
        {
            for (int p = 0; p < Passes; p++)
            {
                float[,] kernel = GenerateKernel();
                float[,] result = new float[heightmap.Width, heightmap.Height];

                int half = KernelSize / 2;

                for (int x = 0; x < heightmap.Width; x++)
                {
                    for (int y = 0; y < heightmap.Height; y++)
                    {
                        float sum = 0f;
                        float weightSum = 0f;

                        for (int kx = -half; kx <= half; kx++)
                        {
                            for (int ky = -half; ky <= half; ky++)
                            {
                                int sx = Math.Clamp(x + kx, 0, heightmap.Width - 1);
                                int sy = Math.Clamp(y + ky, 0, heightmap.Height - 1);
                                float w = kernel[kx + half, ky + half];
                                sum += heightmap.Heights[sx, sy] * w;
                                weightSum += w;
                            }
                        }

                        result[x, y] = sum / weightSum;
                    }
                }

                Array.Copy(result, heightmap.Heights, heightmap.Width * heightmap.Height);
            }

            heightmap.RecalculateBounds();
        }

        public void ApplySelective(HeightmapData heightmap, float heightThreshold, bool belowThreshold = true)
        {
            for (int p = 0; p < Passes; p++)
            {
                float[,] kernel = GenerateKernel();
                float[,] result = new float[heightmap.Width, heightmap.Height];

                int half = KernelSize / 2;

                for (int x = 0; x < heightmap.Width; x++)
                {
                    for (int y = 0; y < heightmap.Height; y++)
                    {
                        result[x, y] = heightmap.Heights[x, y];

                        bool shouldBlur = belowThreshold
                            ? heightmap.Heights[x, y] < heightThreshold
                            : heightmap.Heights[x, y] > heightThreshold;

                        if (!shouldBlur) 
                        {
                            continue;
                        }

                        float sum = 0f;
                        float weightSum = 0f;

                        for (int kx = -half; kx <= half; kx++)
                        {
                            for (int ky = -half; ky <= half; ky++)
                            {
                                int sx = Math.Clamp(x + kx, 0, heightmap.Width - 1);
                                int sy = Math.Clamp(y + ky, 0, heightmap.Height - 1);
                                float w = kernel[kx + half, ky + half];
                                sum += heightmap.Heights[sx, sy] * w;
                                weightSum += w;
                            }
                        }

                        result[x, y] = sum / weightSum;
                    }
                }

                Array.Copy(result, heightmap.Heights, heightmap.Width * heightmap.Height);
            }

            heightmap.RecalculateBounds();
        }

        private float[,] GenerateKernel()
        {
            float[,] kernel = new float[KernelSize, KernelSize];
            int half = KernelSize / 2;
            float twoSigmaSq = 2f * Sigma * Sigma;
            float sum = 0f;

            for (int x = -half; x <= half; x++)
            {
                for (int y = -half; y <= half; y++)
                {
                    float val = (float)Math.Exp(-(x * x + y * y) / twoSigmaSq);
                    kernel[x + half, y + half] = val;
                    sum += val;
                }
            }

            for (int x = 0; x < KernelSize; x++)
            {
                for (int y = 0; y < KernelSize; y++)
                {
                    kernel[x, y] /= sum;
                }
            }

            return kernel;
        }
    }
}
