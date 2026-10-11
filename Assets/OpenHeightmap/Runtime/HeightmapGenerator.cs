/*
 * Asset: OpenHeightmap
 * Filename: HeightmapGenerator.cs
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

using System;
using System.Diagnostics;

namespace OpenHeightmap
{
    /// <summary>Noise algorithm selector. Determines the base shape of generated terrain.</summary>
    public enum NoiseType { Perlin, Simplex, Voronoi, Worley, Value, MidpointDisplacement }

    /// <summary>
    /// Complete parameter set for heightmap generation. Controls noise selection,
    /// octave layering, erosion simulation, post-processing filters, and output shape.
    /// Serializable for JSON clone/preset support.
    /// </summary>
    [Serializable]
    public class HeightmapGeneratorParameters
    {
        // ── Output ──
        /// <summary>Resolution in pixels (power-of-2 + 1). Maps directly to terrain heightmap size.</summary>
        public int Resolution = 513;
        /// <summary>Random seed. Different seeds produce different terrain from the same parameters.</summary>
        public int Seed = 0;

        // ── Noise ──
        /// <summary>Noise algorithm to use for base terrain generation.</summary>
        public NoiseType NoiseType = NoiseType.Perlin;
        /// <summary>
        /// Base frequency of the noise. Controls how "zoomed in" the terrain appears.
        /// Lower values = larger features. Typical range: 0.003 - 0.02.
        /// </summary>
        public float Scale = 0.01f;
        /// <summary>
        /// Number of noise layers stacked. Each octave adds finer detail on top of the previous.
        /// More octaves = more detail but slower generation. Range: 1-12.
        /// </summary>
        public int Octaves = 6;
        /// <summary>
        /// Amplitude decay per octave. Each successive octave has this fraction of the previous
        /// octave's amplitude. 0.5 = each octave is half as strong. Range: 0-1.
        /// </summary>
        public float Persistence = 0.5f;
        /// <summary>
        /// Frequency multiplier per octave. Each successive octave samples at this multiple of
        /// the previous frequency. 2.0 = standard doubling. Range: 1-4.
        /// </summary>
        public float Lacunarity = 2f;
        /// <summary>
        /// Overall height of the final map (applied after normalization, so the result stays in 0-1).
        /// 1 = full range. Below 1 lowers the whole terrain linearly. Above 1 raises it with a smooth
        /// curve that lifts lowlands and slopes without flattening the peaks. Range: 0.1-5.
        /// </summary>
        public float HeightMultiplier = 1f;
        /// <summary>World-space offset for the noise sampling origin (X axis).</summary>
        public float OffsetX = 0f;
        /// <summary>World-space offset for the noise sampling origin (Y axis).</summary>
        public float OffsetY = 0f;

        // ── Domain Warping ──
        /// <summary>When enabled, distorts sample coordinates with a secondary noise field before sampling.</summary>
        public bool UseDomainWarping = false;
        /// <summary>How far coordinates are displaced. Higher = more distortion. Range: 0.001-0.1.</summary>
        public float WarpStrength = 0.02f;
        /// <summary>Number of noise octaves used for the warp field itself. Range: 1-4.</summary>
        public int WarpOctaves = 2;
        /// <summary>Frequency of the warp field noise. Range: 0.001-0.1.</summary>
        public float WarpFrequency = 0.01f;

        // ── Hydraulic Erosion ──
        /// <summary>Enables particle-based water erosion simulation. Most impactful realism pass.</summary>
        public bool ApplyHydraulicErosion = false;
        /// <summary>Number of erosion particles spawned. More = finer detail, slower. Range: 10-1000.</summary>
        public int HydraulicIterations = 200;
        /// <summary>Max steps per particle before it evaporates. Range: 5-80.</summary>
        public int HydraulicLifetime = 30;
        /// <summary>How much particle momentum carries vs. gravity. Higher = smoother channels. Range: 0-0.2.</summary>
        public float HydraulicInertia = 0.05f;
        /// <summary>Downhill acceleration. Higher = deeper channels. Range: 0-20.</summary>
        public float HydraulicGravity = 4f;
        /// <summary>Water loss per step. Higher = shorter rivers. Range: 0-0.1.</summary>
        public float HydraulicEvaporation = 0.02f;
        /// <summary>Speed of sediment drop-off when velocity decreases. Range: 0-1.</summary>
        public float HydraulicDeposition = 0.3f;
        /// <summary>Speed of terrain carving under particles. Range: 0-1.</summary>
        public float HydraulicErosionRate = 0.3f;

        // ── Thermal Erosion ──
        /// <summary>Enables slope-based material displacement. Rounds peaks and fills valleys.</summary>
        public bool ApplyThermalErosion = false;
        /// <summary>Number of thermal erosion passes. Range: 1-50.</summary>
        public int ThermalIterations = 20;
        /// <summary>Slope threshold before material moves. Lower = more erosion. Range: 0.001-0.1.</summary>
        public float ThermalTalus = 0.01f;
        /// <summary>Material transfer rate constant. 1.0 = standard. Range: 0.1-5.</summary>
        public float ThermalC = 1f;

        // ── Gaussian Blur ──
        /// <summary>Enables convolution-based Gaussian blur.</summary>
        public bool ApplyGaussianBlur = false;
        /// <summary>Blur kernel size in pixels. Must be odd (auto-corrected). Range: 3-15.</summary>
        public int BlurKernelSize = 3;
        /// <summary>Gaussian sigma. Higher = wider blur spread. Range: 0.1-10.</summary>
        public float BlurSigma = 1f;
        /// <summary>Number of blur passes. Multiple passes compound the effect. Range: 1-5.</summary>
        public int BlurPasses = 1;

        // ── Laplacian Smoothing ──
        /// <summary>Enables vertex-averaging smooth pass. Reduces high-frequency noise.</summary>
        public bool ApplyLaplacianSmooth = false;
        /// <summary>Number of smoothing passes. Range: 1-10.</summary>
        public int SmoothPasses = 3;
        /// <summary>How strongly each vertex moves toward its neighbors' average. Range: 0.01-1.</summary>
        public float SmoothStrength = 0.5f;

        // ── Falloff ──
        /// <summary>Enables multiplicative edge falloff for island/bounded terrain.</summary>
        public bool ApplyFalloff = false;
        /// <summary>Falloff shape: Circular, Square, Diamond, or Gaussian.</summary>
        public FalloffType FalloffType = FalloffType.Circular;
        /// <summary>Overall falloff intensity. Range: 0-3.</summary>
        public float FalloffStrength = 1f;
        /// <summary>Power curve exponent. Higher = steeper drop at edges. Range: 0.5-5.</summary>
        public float FalloffPower = 2f;
        /// <summary>Shifts where falloff begins. Higher = more flat center. Range: 0-0.9.</summary>
        public float FalloffOffset = 0f;

        // ── Noise-specific ──
        /// <summary>Voronoi distance metric (Euclidean, Manhattan, Chebyshev).</summary>
        public VoronoiDistanceMetric VoronoiMetric = VoronoiDistanceMetric.Euclidean;
        /// <summary>Voronoi return mode (CellValue, Distance, Distance2).</summary>
        public VoronoiReturnType VoronoiReturn = VoronoiReturnType.CellValue;
        /// <summary>Worley return mode (F1, F2, F2MinusF1, F1Normalized).</summary>
        public WorleyReturnType WorleyReturn = WorleyReturnType.F1;
        /// <summary>Midpoint displacement roughness. Lower = smoother. Range: 0.1-1.</summary>
        public float MidpointRoughness = 0.5f;
    }


    /// <summary>
    /// Main orchestrator. Chains the full generation pipeline:
    /// Noise → Domain Warp → Hydraulic Erosion → Thermal Erosion →
    /// Gaussian Blur → Laplacian Smooth → Falloff → Normalize → Height Multiplier.
    /// Only enabled passes execute. All heights end in the [0,1] range.
    /// </summary>
    [Serializable]
    public class HeightmapGenerator
    {
        /// <summary>
        /// Noise is sampled in a fixed "reference" space of this many units across the map, whatever the
        /// resolution. A 1025 map therefore shows the same terrain as a 513 map, just with more detail.
        /// 512 keeps the look that Scale values were tuned for at 513.
        /// </summary>
        public const float ReferenceSize = 512f;

        /// <summary>Optional progress callback: (stage description, 0-1 progress).</summary>
        [NonSerialized] public Action<string, float> Progress;

        /// <summary>
        /// Generates a complete heightmap by executing the configured pipeline.
        /// Returns a HeightmapData with all values in [0, 1] range.
        /// </summary>
        public HeightmapData Generate(HeightmapGeneratorParameters p)
        {
            var sw = Stopwatch.StartNew();
            var heightmap = new HeightmapData(p.Resolution);

            Report("Generating noise", 0.05f);
            GenerateNoise(heightmap, p);

            if (p.UseDomainWarping)
            {
                Report("Domain warping", 0.25f);
                var warp = new DomainWarping
                {
                    Enabled = true,
                    WarpStrength = p.WarpStrength,
                    WarpOctaves = p.WarpOctaves,
                    WarpFrequency = p.WarpFrequency
                };

                warp.Apply(heightmap, p.Seed);
            }

            if (p.ApplyHydraulicErosion)
            {
                Report("Hydraulic erosion", 0.4f);
                var erosion = new HydraulicErosion
                {
                    Iterations = p.HydraulicIterations,
                    MaxLifetime = p.HydraulicLifetime,
                    Inertia = p.HydraulicInertia,
                    Gravity = p.HydraulicGravity,
                    EvaporationRate = p.HydraulicEvaporation,
                    depositionRate = p.HydraulicDeposition,
                    ErosionRate = p.HydraulicErosionRate
                };

                erosion.Apply(heightmap, p.Seed);
            }

            if (p.ApplyThermalErosion)
            {
                Report("Thermal erosion", 0.6f);
                var thermal = new ThermalErosion
                {
                    Iterations = p.ThermalIterations,
                    TalusAngle = p.ThermalTalus,
                    C = p.ThermalC
                };

                thermal.Apply(heightmap);
            }

            if (p.ApplyGaussianBlur)
            {
                Report("Gaussian blur", 0.75f);
                var blur = new GaussianBlur
                {
                    KernelSize = p.BlurKernelSize,
                    Sigma = p.BlurSigma,
                    Passes = p.BlurPasses
                };

                blur.Apply(heightmap);
            }

            if (p.ApplyLaplacianSmooth)
            {
                Report("Laplacian smoothing", 0.85f);
                var smooth = new LaplacianSmooth
                {
                    Passes = p.SmoothPasses,
                    Strength = p.SmoothStrength
                };

                smooth.Apply(heightmap);
            }

            if (p.ApplyFalloff)
            {
                Report("Falloff", 0.9f);
                var falloff = new FalloffGenerator
                {
                    Type = p.FalloffType,
                    Strength = p.FalloffStrength,
                    Power = p.FalloffPower,
                    Offset = p.FalloffOffset
                };
                
                falloff.Apply(heightmap);
            }

            Report("Normalizing", 0.95f);
            heightmap.Normalize();
            ApplyHeightMultiplier(heightmap, p.HeightMultiplier);
            heightmap.RecalculateBounds();

            sw.Stop();
            UnityEngine.Debug.Log($"[OpenHeightmap] Generated {p.Resolution}x{p.Resolution} in {sw.ElapsedMilliseconds}ms");

            return heightmap;
        }

        /// <summary>Sends a progress update to the Progress callback, if set.</summary>
        private void Report(string stage, float progress)
        {
            Progress?.Invoke(stage, progress);
        }

        /// <summary>
        /// Applies the height multiplier to an already-normalized [0,1] map.
        /// Below 1: linear scale (lower terrain). Above 1: 1 - (1 - h)^m, which raises the terrain
        /// while keeping 0 at 0 and 1 at 1, so nothing is clipped flat.
        /// </summary>
        private static void ApplyHeightMultiplier(HeightmapData heightmap, float multiplier)
        {
            if (Math.Abs(multiplier - 1f) < 0.0001f || multiplier <= 0f)
            {
                return;
            }

            int w = heightmap.Width;
            int h = heightmap.Height;

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    float v = heightmap.Heights[x, y];

                    heightmap.Heights[x, y] = multiplier < 1f
                        ? v * multiplier
                        : 1f - (float)Math.Pow(Math.Max(0f, 1f - v), multiplier);
                }
            }
        }

        /// <summary>Scale from pixel coordinates to the fixed reference space (see ReferenceSize).</summary>
        public static float GetReferenceScale(int resolution)
        {
            return resolution > 1 ? ReferenceSize / (resolution - 1) : 1f;
        }

        private void GenerateNoise(HeightmapData heightmap, HeightmapGeneratorParameters p)
        {
            switch (p.NoiseType)
            {
                case NoiseType.Perlin:
                    GenerateOctaveNoise(heightmap, p, new PerlinNoise());
                    break;
                case NoiseType.Simplex:
                    GenerateOctaveNoise(heightmap, p, new SimplexNoise());
                    break;
                case NoiseType.Voronoi:
                    var voronoi = new VoronoiNoise
                    {
                        DistanceMetric = p.VoronoiMetric,
                        ReturnType = p.VoronoiReturn
                    };
                    GenerateSingleNoise(heightmap, p, voronoi);
                    break;
                case NoiseType.Worley:
                    var worley = new WorleyNoise { ReturnType = p.WorleyReturn };
                    GenerateSingleNoise(heightmap, p, worley);
                    break;
                case NoiseType.Value:
                    GenerateOctaveNoise(heightmap, p, new ValueNoise());
                    break;
                case NoiseType.MidpointDisplacement:
                    var midpoint = new MidpointDisplacement { Roughness = p.MidpointRoughness };
                    midpoint.Generate(p.Seed, p.Resolution);
                    GenerateFromProvider(heightmap, p, midpoint);
                    break;
            }
        }

        /// <summary>
        /// Builds per-octave sampling offsets from the seed. Perlin, Simplex and Value noise use a fixed
        /// permutation table and ignore their seed argument, so the seed has to move where each octave
        /// samples instead. The tables repeat every 256 units, so offsets cover that whole period.
        /// </summary>
        public static void GetSeedOffsets(int seed, int count, out float[] offsetsX, out float[] offsetsY)
        {
            var rng = new Random(seed);
            offsetsX = new float[count];
            offsetsY = new float[count];

            for (int i = 0; i < count; i++)
            {
                offsetsX[i] = (float)(rng.NextDouble() * 256.0);
                offsetsY[i] = (float)(rng.NextDouble() * 256.0);
            }
        }

        private void GenerateOctaveNoise(HeightmapData heightmap, HeightmapGeneratorParameters p, INoiseProvider noise)
        {
            int w = heightmap.Width;
            int h = heightmap.Height;
            int octaves = Math.Max(1, p.Octaves);
            float refScale = GetReferenceScale(w);

            GetSeedOffsets(p.Seed, octaves, out float[] seedX, out float[] seedY);

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    float amplitude = 1f;
                    float frequency = p.Scale;
                    float noiseHeight = 0f;
                    float maxAmplitude = 0f;

                    for (int o = 0; o < octaves; o++)
                    {
                        float sampleX = (x * refScale + p.OffsetX) * frequency + seedX[o];
                        float sampleY = (y * refScale + p.OffsetY) * frequency + seedY[o];

                        float val = noise.Sample(sampleX, sampleY, p.Seed + o * 1000);
                        noiseHeight += val * amplitude;
                        maxAmplitude += amplitude;

                        amplitude *= p.Persistence;
                        frequency *= p.Lacunarity;
                    }

                    heightmap.Heights[x, y] = maxAmplitude > 0f ? (noiseHeight / maxAmplitude + 1f) * 0.5f : 0f;
                }
            }
        }

        private void GenerateSingleNoise(HeightmapData heightmap, HeightmapGeneratorParameters p, INoiseProvider noise)
        {
            int w = heightmap.Width;
            int h = heightmap.Height;
            float refScale = GetReferenceScale(w);

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    float sampleX = (x * refScale + p.OffsetX) * p.Scale;
                    float sampleY = (y * refScale + p.OffsetY) * p.Scale;

                    float val = noise.Sample(sampleX, sampleY, p.Seed);
                    heightmap.Heights[x, y] = (val + 1f) * 0.5f;
                }
            }
        }

        private void GenerateFromProvider(HeightmapData heightmap, HeightmapGeneratorParameters p, INoiseProvider noise)
        {
            int w = heightmap.Width;
            int h = heightmap.Height;

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    float u = (float)x / (w - 1);
                    float v = (float)y / (h - 1);
                    heightmap.Heights[x, y] = noise.Sample(u, v, p.Seed);
                }
            }
        }
    }
}
