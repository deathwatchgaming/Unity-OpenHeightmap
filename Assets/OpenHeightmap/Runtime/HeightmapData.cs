/*
 * Asset: OpenHeightmap
 * Filename: HeightmapData.cs
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

using System;

namespace OpenHeightmap
{
    /// <summary>
    /// Core heightmap data container. Stores a 2D float grid with accessors for
    /// integer and bilinear sampling, normalization, and utility operations.
    /// Resolution should typically be power-of-2 + 1 (e.g. 513, 1025, 2049)
    /// to match Unity Terrain requirements.
    /// </summary>
    [Serializable]
    public class HeightmapData
    {
        /// <summary>Width of the heightmap in pixels.</summary>
        public int Width { get; private set; }
        /// <summary>Height of the heightmap in pixels.</summary>
        public int Height { get; private set; }
        /// <summary>Raw height values. Indexed as [x, y]. Not serialized by Unity (2D array).</summary>
        [NonSerialized] public float[,] Heights;

        /// <summary>Minimum height value across the entire map (updated by RecalculateBounds).</summary>
        public float MinHeight { get; private set; }
        /// <summary>Maximum height value across the entire map (updated by RecalculateBounds).</summary>
        public float MaxHeight { get; private set; }

        /// <summary>
        /// Creates a square heightmap with the given resolution (width = height).
        /// Use power-of-2 + 1 values: 65, 129, 257, 513, 1025, 2049, 4097.
        /// </summary>
        public HeightmapData(int resolution)
        {
            Width = resolution;
            Height = resolution;
            Heights = new float[resolution, resolution];
            MinHeight = 0f;
            MaxHeight = 1f;
        }

        /// <summary>Creates a rectangular heightmap with separate width and height.</summary>
        public HeightmapData(int width, int height)
        {
            Width = width;
            Height = height;
            Heights = new float[width, height];
            MinHeight = 0f;
            MaxHeight = 1f;
        }

        /// <summary>
        /// Gets the height at integer coordinates with edge clamping.
        /// Coordinates outside bounds are clamped to the nearest edge.
        /// </summary>
        public float GetHeight(int x, int y)
        {
            x = Math.Clamp(x, 0, Width - 1);
            y = Math.Clamp(y, 0, Height - 1);
            return Heights[x, y];
        }

        /// <summary>Sets the height value at the given integer coordinates. Does nothing if out of bounds.</summary>
        public void SetHeight(int x, int y, float value)
        {
            if (x >= 0 && x < Width && y >= 0 && y < Height)
            {
                Heights[x, y] = value;
            }
        }

        /// <summary>
        /// Bilinear interpolation sample using normalized UV coordinates [0, 1].
        /// Returns smooth height values between grid points. Best for terrain
        /// detail placement, splatmap queries, and preview rendering.
        /// </summary>
        public float GetHeightBilinear(float x, float y)
        {
            float fx = x * (Width - 1);
            float fy = y * (Height - 1);

            int x0 = (int)fx;
            int y0 = (int)fy;
            int x1 = Math.Min(x0 + 1, Width - 1);
            int y1 = Math.Min(y0 + 1, Height - 1);

            float tx = fx - x0;
            float ty = fy - y0;

            float h00 = Heights[x0, y0];
            float h10 = Heights[x1, y0];
            float h01 = Heights[x0, y1];
            float h11 = Heights[x1, y1];

            float h0 = h00 + (h10 - h00) * tx;
            float h1 = h01 + (h11 - h01) * tx;

            return h0 + (h1 - h0) * ty;
        }

        /// <summary>Scans all pixels and updates MinHeight/MaxHeight.</summary>
        public void RecalculateBounds()
        {
            MinHeight = float.MaxValue;
            MaxHeight = float.MinValue;

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    float h = Heights[x, y];

                    if (h < MinHeight) 
                    {
                        MinHeight = h;
                    }

                    if (h > MaxHeight) 
                    {
                        MaxHeight = h;
                    }
                }
            }
        }

        /// <summary>
        /// Normalizes all height values to [0, 1] range based on current Min/Max.
        /// Always called at the end of the generation pipeline.
        /// </summary>
        public void Normalize()
        {
            RecalculateBounds();
            float range = MaxHeight - MinHeight;

            if (range < 0.0001f) 
            {
                return;
            }

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    Heights[x, y] = (Heights[x, y] - MinHeight) / range;
                }
            }

            MinHeight = 0f;
            MaxHeight = 1f;
        }

        /// <summary>Clamps all height values to the given [min, max] range, then recalculates bounds.</summary>
        public void Clamp(float min, float max)
        {
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    Heights[x, y] = Math.Clamp(Heights[x, y], min, max);
                }
            }

            RecalculateBounds();
        }

        /// <summary>Creates a deep copy of this heightmap data.</summary>
        public HeightmapData Clone()
        {
            var clone = new HeightmapData(Width, Height);
            Array.Copy(Heights, clone.Heights, Width * Height);
            clone.MinHeight = MinHeight;
            clone.MaxHeight = MaxHeight;
            return clone;
        }

        /// <summary>Copies all height values from source into this map. Dimensions must match.</summary>
        public void CopyFrom(HeightmapData source)
        {
            if (source.Width != Width || source.Height != Height)
            {
                throw new ArgumentException("Source dimensions do not match.");
            }

            Array.Copy(source.Heights, Heights, Width * Height);
            MinHeight = source.MinHeight;
            MaxHeight = source.MaxHeight;
        }

        /// <summary>Alias for GetHeightBilinear. Samples with normalized [0, 1] UV coordinates.</summary>
        public float SampleNormalized(float u, float v)
        {
            return GetHeightBilinear(u, v);
        }

        /// <summary>
        /// Applies a function to every height value. Use for custom transformations.
        /// Recalculates bounds after all pixels are processed.
        /// </summary>
        public void ApplyOperation(Func<float, float> operation)
        {
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    Heights[x, y] = operation(Heights[x, y]);
                }
            }

            RecalculateBounds();
        }

        /// <summary>
        /// Applies a function that receives (x, y, currentHeight) to every pixel.
        /// Use for position-dependent transformations.
        /// </summary>
        public void ApplyOperation(Func<int, int, float, float> operation)
        {
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    Heights[x, y] = operation(x, y, Heights[x, y]);
                }
            }

            RecalculateBounds();
        }

        /// <summary>
        /// Applies a Gaussian blur with the given kernel radius (in pixels).
        /// Higher values reduce high-frequency detail. A radius of 1 is a light smooth,
        /// radius of 3-5 is moderate, radius of 8+ is heavy. Uses a separable 2-pass
        /// approach (horizontal then vertical) for O(n*r) performance instead of O(n*r²).
        /// </summary>
        public void GaussianSmooth(int radius)
        {
            if (radius < 1) 
            {
                return;
            }

            int w = Width, h = Height;

            float[] kernel = BuildGaussianKernel(radius);
            float[] temp = new float[w * h];
            float[] result = new float[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float sum = 0f;
                    float weightSum = 0f;

                    for (int k = -radius; k <= radius; k++)
                    {
                        int sx = Math.Clamp(x + k, 0, w - 1);
                        float weight = kernel[k + radius];
                        sum += Heights[sx, y] * weight;
                        weightSum += weight;
                    }

                    temp[y * w + x] = sum / weightSum;
                }
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float sum = 0f;
                    float weightSum = 0f;

                    for (int k = -radius; k <= radius; k++)
                    {
                        int sy = Math.Clamp(y + k, 0, h - 1);
                        float weight = kernel[k + radius];
                        sum += temp[sy * w + x] * weight;
                        weightSum += weight;
                    }

                    result[y * w + x] = sum / weightSum;
                }
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Heights[x, y] = result[y * w + x];
                }
            }

            RecalculateBounds();
        }

        private static float[] BuildGaussianKernel(int radius)
        {
            float[] kernel = new float[radius * 2 + 1];
            float sigma = radius / 3f;
            float twoSigmaSq = 2f * sigma * sigma;
            float sum = 0f;

            for (int i = -radius; i <= radius; i++)
            {
                float val = (float)Math.Exp(-(i * i) / twoSigmaSq);
                kernel[i + radius] = val;
                sum += val;
            }

            for (int i = 0; i < kernel.Length; i++)
            {
                kernel[i] /= sum;
            }

            return kernel;
        }

        /// <summary>Flattens the 2D array to a 1D array in row-major order (y * Width + x).</summary>
        public float[] ToFlatArray()
        {
            float[] flat = new float[Width * Height];

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    flat[y * Width + x] = Heights[x, y];
                }
            }

            return flat;
        }

        /// <summary>Creates a HeightmapData from a flat array. Data must be in row-major order.</summary>
        public static HeightmapData FromFlatArray(float[] data, int width, int height)
        {
            var hm = new HeightmapData(width, height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    hm.Heights[x, y] = data[y * width + x];
                }
            }
            
            hm.RecalculateBounds();
            return hm;
        }
    }
}
