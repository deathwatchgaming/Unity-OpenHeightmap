/*
 * Asset: OpenHeightmap
 * Filename: TerrainPresets.cs
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenHeightmap
{
    /// <summary>
    /// A named set of generator parameters plus the terrain proportions it is designed for.
    /// </summary>
    [Serializable]
    public class TerrainPreset
    {
        /// <summary>Display name shown in the Presets tab.</summary>
        public string Name;

        /// <summary>Generator settings for this look.</summary>
        public HeightmapGeneratorParameters Parameters;

        /// <summary>
        /// Suggested terrain height as a fraction of its width (used by Auto Height when this preset
        /// is applied). Gentle landscapes use small values, mountains larger ones.
        /// </summary>
        public float HeightRatio = NewTerrainSettings.DefaultHeightRatio;

        /// <summary>
        /// Splatmap rules that suit this preset (e.g. sand for dunes), or null to use the default
        /// water / sand / grass / rock / snow rules.
        /// </summary>
        public List<BiomeRule> SplatRules;

        /// <summary>Returns a fresh copy of this preset's splatmap rules (or the defaults).</summary>
        public List<BiomeRule> CreateSplatRules()
        {
            if (SplatRules == null || SplatRules.Count == 0)
            {
                return SplatmapGenerator.CreateDefaultRules();
            }

            var copy = new List<BiomeRule>();

            foreach (var rule in SplatRules)
            {
                copy.Add(rule.Clone());
            }

            return copy;
        }

        public TerrainPreset(string name, HeightmapGeneratorParameters parameters)
        {
            Name = name;
            Parameters = parameters;
        }

        public TerrainPreset(string name, float heightRatio, HeightmapGeneratorParameters parameters)
        {
            Name = name;
            HeightRatio = heightRatio;
            Parameters = parameters;
        }
    }

    /// <summary>
    /// Built-in presets. Each one was checked by rendering it as a shaded 3D terrain at its suggested
    /// height ratio, across several seeds, so the result matches its name.
    /// </summary>
    public static class TerrainPresets
    {
        public static List<TerrainPreset> GetAll()
        {
            return new List<TerrainPreset>
            {
                CreateRollingHills(),
                CreateMountainRange(),
                CreateIsland(),
                CreateCanyon(),
                CreatePlateau(),
                CreateDesert(),
                CreateTundra(),
                CreateVolcanic(),
                CreateRiverValley(),
                CreateCoastalCliffs()
            };
        }

        /// <summary>Broad, smooth hills with low relief.</summary>
        private static TerrainPreset CreateRollingHills()
        {
            return new TerrainPreset("Rolling Hills", 0.08f, new HeightmapGeneratorParameters
            {
                NoiseType = NoiseType.Perlin,
                Scale = 0.005f,
                Octaves = 4,
                Persistence = 0.4f,
                Lacunarity = 2f,
                HeightMultiplier = 1f,
                ApplyHydraulicErosion = false,
                ApplyThermalErosion = true,
                ThermalIterations = 10,
                ThermalTalus = 0.005f,
                ApplyGaussianBlur = true,
                BlurKernelSize = 5,
                BlurSigma = 1.5f,
                ApplyLaplacianSmooth = true,
                SmoothPasses = 3,
                SmoothStrength = 0.5f,
                ApplyFalloff = false
            });
        }

        /// <summary>Large mountain masses with eroded slopes and warped ridgelines.</summary>
        private static TerrainPreset CreateMountainRange()
        {
            return new TerrainPreset("Mountain Range", 0.3f, new HeightmapGeneratorParameters
            {
                NoiseType = NoiseType.Simplex,
                Scale = 0.005f,
                Octaves = 8,
                Persistence = 0.5f,
                Lacunarity = 2.0f,
                HeightMultiplier = 1f,
                ApplyHydraulicErosion = true,
                HydraulicIterations = 300,
                HydraulicLifetime = 35,
                HydraulicErosionRate = 0.4f,
                ApplyThermalErosion = true,
                ThermalIterations = 25,
                ThermalTalus = 0.015f,
                ApplyGaussianBlur = false,
                ApplyFalloff = false,
                UseDomainWarping = true,
                WarpStrength = 0.03f,
                WarpOctaves = 2,
                WarpFrequency = 0.006f
            });
        }

        /// <summary>A single island with beaches, rising to a mountainous centre.</summary>
        private static TerrainPreset CreateIsland()
        {
            return new TerrainPreset("Island", 0.15f, new HeightmapGeneratorParameters
            {
                NoiseType = NoiseType.Perlin,
                Scale = 0.01f,
                Octaves = 6,
                Persistence = 0.5f,
                Lacunarity = 2f,
                HeightMultiplier = 1f,
                ApplyHydraulicErosion = true,
                HydraulicIterations = 200,
                ApplyThermalErosion = true,
                ThermalIterations = 15,
                ApplyGaussianBlur = true,
                BlurKernelSize = 3,
                BlurSigma = 0.5f,
                ApplyFalloff = true,
                FalloffType = FalloffType.Circular,
                FalloffStrength = 1f,
                FalloffPower = 2.5f,
                FalloffOffset = 0.1f
            });
        }

        /// <summary>
        /// Flat-topped mesas cut by narrow, winding gorges. Voronoi cell edges form the gorges;
        /// a high Height Multiplier flattens the cell interiors into mesa tops.
        /// </summary>
        private static TerrainPreset CreateCanyon()
        {
            var preset = new TerrainPreset("Canyon", 0.12f, new HeightmapGeneratorParameters
            {
                NoiseType = NoiseType.Voronoi,
                Scale = 0.006f,
                Octaves = 1,
                VoronoiMetric = VoronoiDistanceMetric.Euclidean,
                VoronoiReturn = VoronoiReturnType.Distance2,
                HeightMultiplier = 6f,
                ApplyHydraulicErosion = true,
                HydraulicIterations = 250,
                HydraulicLifetime = 40,
                HydraulicErosionRate = 0.5f,
                HydraulicDeposition = 0.2f,
                ApplyThermalErosion = false,
                ApplyGaussianBlur = false,
                ApplyFalloff = false,
                UseDomainWarping = true,
                WarpStrength = 0.08f,
                WarpOctaves = 3,
                WarpFrequency = 0.006f
            });

            // Desert canyon colours: sandy gorge floors, red rock walls, pale sandstone mesa tops.
            preset.SplatRules = new List<BiomeRule>
            {
                new BiomeRule("Gorge Sand", 0f, 0.3f, 90f, new Color(0.79f, 0.66f, 0.47f), 0),
                new BiomeRule("Red Rock", 0.25f, 0.8f, 90f, new Color(0.61f, 0.29f, 0.17f), 1),
                new BiomeRule("Sandstone", 0.75f, 1f, 90f, new Color(0.81f, 0.54f, 0.32f), 2)
            };

            return preset;
        }

        /// <summary>A raised, mostly flat tableland with escarpments down to lower ground.</summary>
        private static TerrainPreset CreatePlateau()
        {
            var preset = new TerrainPreset("Plateau", 0.12f, new HeightmapGeneratorParameters
            {
                NoiseType = NoiseType.Value,
                Scale = 0.007f,
                Octaves = 3,
                Persistence = 0.3f,
                Lacunarity = 2f,
                HeightMultiplier = 2f,
                ApplyHydraulicErosion = false,
                ApplyThermalErosion = true,
                ThermalIterations = 30,
                ThermalTalus = 0.008f,
                ApplyGaussianBlur = true,
                BlurKernelSize = 5,
                BlurSigma = 1.5f,
                BlurPasses = 2,
                ApplyLaplacianSmooth = true,
                SmoothPasses = 4,
                SmoothStrength = 0.6f,
                ApplyFalloff = false
            });

            // Grassland below, rocky escarpments in between, dry upland grass on top (no snow).
            preset.SplatRules = new List<BiomeRule>
            {
                new BiomeRule("Lowland Grass", 0f, 0.4f, 90f, new Color(0.3f, 0.7f, 0.2f), 0),
                new BiomeRule("Rock", 0.35f, 0.75f, 90f, new Color(0.5f, 0.45f, 0.4f), 1),
                new BiomeRule("Upland Grass", 0.7f, 1f, 90f, new Color(0.55f, 0.65f, 0.27f), 2)
            };

            return preset;
        }

        /// <summary>Low, smooth, wind-shaped undulations.</summary>
        private static TerrainPreset CreateDesert()
        {
            var preset = new TerrainPreset("Desert Dunes", 0.05f, new HeightmapGeneratorParameters
            {
                NoiseType = NoiseType.Simplex,
                Scale = 0.008f,
                Octaves = 3,
                Persistence = 0.35f,
                Lacunarity = 2f,
                HeightMultiplier = 1.3f,
                ApplyHydraulicErosion = false,
                ApplyThermalErosion = true,
                ThermalIterations = 15,
                ThermalTalus = 0.003f,
                ApplyGaussianBlur = true,
                BlurKernelSize = 7,
                BlurSigma = 2.5f,
                ApplyLaplacianSmooth = true,
                SmoothPasses = 2,
                SmoothStrength = 0.4f,
                ApplyFalloff = false,
                UseDomainWarping = true,
                WarpStrength = 0.04f,
                WarpFrequency = 0.006f
            });

            // All sand: darker in the troughs, pale on the crests.
            preset.SplatRules = new List<BiomeRule>
            {
                new BiomeRule("Dark Sand", 0f, 0.4f, 90f, new Color(0.76f, 0.6f, 0.38f), 0),
                new BiomeRule("Sand", 0.3f, 0.8f, 90f, new Color(0.89f, 0.77f, 0.53f), 1),
                new BiomeRule("Pale Sand", 0.7f, 1f, 90f, new Color(0.95f, 0.88f, 0.68f), 2)
            };

            return preset;
        }

        /// <summary>Flat, cold lowland dotted with shallow lakes.</summary>
        private static TerrainPreset CreateTundra()
        {
            var preset = new TerrainPreset("Tundra", 0.06f, new HeightmapGeneratorParameters
            {
                NoiseType = NoiseType.Perlin,
                Scale = 0.007f,
                Octaves = 5,
                Persistence = 0.4f,
                Lacunarity = 1.8f,
                HeightMultiplier = 0.6f,
                ApplyHydraulicErosion = false,
                ApplyThermalErosion = true,
                ThermalIterations = 20,
                ThermalTalus = 0.01f,
                ApplyGaussianBlur = true,
                BlurKernelSize = 3,
                BlurSigma = 1f,
                ApplyFalloff = false
            });

            // Cold palette: lakes, gravel shores, muted moss, snow patches on the rises.
            preset.SplatRules = new List<BiomeRule>
            {
                new BiomeRule("Water", 0f, 0.15f, 90f, new Color(0.25f, 0.42f, 0.62f), 0),
                new BiomeRule("Gravel", 0.12f, 0.3f, 90f, new Color(0.6f, 0.58f, 0.54f), 1),
                new BiomeRule("Tundra Moss", 0.25f, 0.65f, 90f, new Color(0.49f, 0.55f, 0.35f), 2),
                new BiomeRule("Snow", 0.6f, 1f, 90f, new Color(0.93f, 0.95f, 0.96f), 3)
            };

            return preset;
        }

        /// <summary>
        /// A few large volcanic cones with eroded flanks. Worley F1Normalized peaks at each cell centre,
        /// and the low Scale keeps it to a handful of volcanoes.
        /// </summary>
        private static TerrainPreset CreateVolcanic()
        {
            var preset = new TerrainPreset("Volcanic", 0.25f, new HeightmapGeneratorParameters
            {
                NoiseType = NoiseType.Worley,
                Scale = 0.004f,
                Octaves = 1,
                WorleyReturn = WorleyReturnType.F1Normalized,
                HeightMultiplier = 1f,
                ApplyHydraulicErosion = true,
                HydraulicIterations = 150,
                HydraulicErosionRate = 0.35f,
                ApplyThermalErosion = true,
                ThermalIterations = 20,
                ThermalTalus = 0.01f,
                ApplyGaussianBlur = false,
                ApplyFalloff = false,
                UseDomainWarping = true,
                WarpStrength = 0.02f,
                WarpFrequency = 0.01f
            });

            // Volcanic palette: dark ash shores, sparse green foothills, black basalt cones, snowy summits.
            preset.SplatRules = new List<BiomeRule>
            {
                new BiomeRule("Water", 0f, 0.12f, 90f, new Color(0.2f, 0.4f, 0.8f), 0),
                new BiomeRule("Ash", 0.1f, 0.25f, 90f, new Color(0.35f, 0.33f, 0.31f), 1),
                new BiomeRule("Scrub", 0.12f, 0.34f, 20f, new Color(0.36f, 0.42f, 0.22f), 2),
                new BiomeRule("Basalt", 0.45f, 0.85f, 90f, new Color(0.24f, 0.22f, 0.21f), 3),
                new BiomeRule("Snow", 0.82f, 1f, 90f, new Color(0.95f, 0.95f, 0.97f), 4)
            };

            return preset;
        }

        /// <summary>
        /// Rounded hills separated by a network of winding river valleys. Voronoi cell edges form the
        /// valley floors; warping makes them meander.
        /// </summary>
        private static TerrainPreset CreateRiverValley()
        {
            return new TerrainPreset("River Valley", 0.12f, new HeightmapGeneratorParameters
            {
                NoiseType = NoiseType.Voronoi,
                Scale = 0.004f,
                Octaves = 1,
                VoronoiMetric = VoronoiDistanceMetric.Euclidean,
                VoronoiReturn = VoronoiReturnType.Distance2,
                HeightMultiplier = 1.5f,
                ApplyHydraulicErosion = true,
                HydraulicIterations = 400,
                HydraulicLifetime = 40,
                HydraulicErosionRate = 0.45f,
                HydraulicDeposition = 0.35f,
                ApplyThermalErosion = true,
                ThermalIterations = 15,
                ApplyGaussianBlur = true,
                BlurKernelSize = 7,
                BlurSigma = 2.5f,
                BlurPasses = 2,
                ApplyFalloff = false,
                UseDomainWarping = true,
                WarpStrength = 0.08f,
                WarpOctaves = 3,
                WarpFrequency = 0.006f
            });
        }

        /// <summary>A landmass whose edges drop steeply into the sea. A strong, offset falloff makes the cliffs.</summary>
        private static TerrainPreset CreateCoastalCliffs()
        {
            return new TerrainPreset("Coastal Cliffs", 0.12f, new HeightmapGeneratorParameters
            {
                NoiseType = NoiseType.Simplex,
                Scale = 0.008f,
                Octaves = 5,
                Persistence = 0.45f,
                Lacunarity = 2.1f,
                HeightMultiplier = 1.3f,
                ApplyHydraulicErosion = true,
                HydraulicIterations = 200,
                HydraulicErosionRate = 0.3f,
                ApplyThermalErosion = true,
                ThermalIterations = 10,
                ThermalTalus = 0.02f,
                ApplyGaussianBlur = false,
                ApplyFalloff = true,
                FalloffType = FalloffType.Circular,
                FalloffStrength = 1f,
                FalloffPower = 5f,
                FalloffOffset = 0.35f
            });
        }
    }
}
