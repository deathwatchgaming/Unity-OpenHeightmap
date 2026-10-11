/*
 * Asset: OpenHeightmap
 * Filename: SplatmapGenerator.cs
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
    /// Defines a terrain biome layer rule. Each rule specifies a height range and maximum slope.
    /// During generation, each pixel is tested against all rules and weights are computed using
    /// smooth-step interpolation for natural blending between biomes.
    /// </summary>
    [Serializable]
    public class BiomeRule
    {
        /// <summary>Display name (Water, Grass, etc.).</summary>
        public string Name;
        /// <summary>Minimum normalized height (0-1) where this biome begins.</summary>
        public float MinHeight;
        /// <summary>Maximum normalized height (0-1) where this biome ends.</summary>
        public float MaxHeight;
        /// <summary>Maximum slope in degrees. Pixels steeper than this get zero weight.</summary>
        public float MaxSlope;
        /// <summary>Preview color for the splatmap visualization.</summary>
        public Color Color;
        /// <summary>Terrain layer index this biome maps to.</summary>
        public int LayerIndex;

        /// <summary>Returns an independent copy (rules are edited in the UI, so presets hand out copies).</summary>
        public BiomeRule Clone()
        {
            return new BiomeRule(Name, MinHeight, MaxHeight, MaxSlope, Color, LayerIndex);
        }

        public BiomeRule(string name, float minH, float maxH, float maxSlope, Color color, int layer)
        {
            Name = name;
            MinHeight = minH;
            MaxHeight = maxH;
            MaxSlope = maxSlope;
            Color = color;
            LayerIndex = layer;
        }
    }

    /// <summary>
    /// Generates terrain splatmaps (layer weight textures) from height + slope analysis.
    /// Each BiomeRule defines a height band and a maximum slope in degrees. Weights are smooth-stepped
    /// for natural blending. Produces float[x, y, layer] arrays (one weight per rule, summing to 1).
    /// </summary>
    [Serializable]
    public class SplatmapGenerator
    {
        /// <summary>List of biome rules defining terrain layers and their height/slope ranges.</summary>
        public List<BiomeRule> Rules = new List<BiomeRule>();

        /// <summary>
        /// How strictly rules' Max Slope is enforced. 0 = slope ignored, 1 = a layer fades out over
        /// 10 degrees past its Max Slope, higher = harder cut-off. Range: 0-3.
        /// </summary>
        public float SlopeWeight = 1f;

        /// <summary>
        /// How sharply height bands separate. 0 = height ignored (layers mix evenly), 1 = default
        /// blending, higher = crisper bands. Range: 0-3.
        /// </summary>
        public float HeightWeight = 1f;

        /// <summary>
        /// Terrain height divided by terrain width, used to turn height differences into real slope
        /// angles. Defaults to a 600-high, 1024-wide terrain. Set this from the terrain you apply to.
        /// </summary>
        public float TerrainHeightToWidth = 600f / 1024f;

        public int Resolution = 513;

        public SplatmapGenerator()
        {
            Rules.AddRange(CreateDefaultRules());
        }

        /// <summary>The default biome rules: water, sand, grass, rock and snow by height.</summary>
        public static List<BiomeRule> CreateDefaultRules()
        {
            return new List<BiomeRule>
            {
                new BiomeRule("Water", 0f, 0.15f, 90f, new Color(0.2f, 0.4f, 0.8f), 0),
                new BiomeRule("Sand", 0.15f, 0.25f, 20f, new Color(0.85f, 0.8f, 0.5f), 1),
                new BiomeRule("Grass", 0.25f, 0.55f, 30f, new Color(0.3f, 0.7f, 0.2f), 2),
                new BiomeRule("Rock", 0.55f, 0.8f, 60f, new Color(0.5f, 0.45f, 0.4f), 3),
                new BiomeRule("Snow", 0.8f, 1f, 90f, new Color(0.95f, 0.95f, 0.97f), 4)
            };
        }

        public float[,,] Generate(HeightmapData heightmap)
        {
            int w = heightmap.Width;
            int hgt = heightmap.Height;
            int ruleCount = Rules.Count;
            float[,,] splatmap = new float[w, hgt, Math.Max(1, ruleCount)];

            if (ruleCount == 0)
            {
                return splatmap;
            }

            float[,] slopes = CalculateSlopes(heightmap);
            float[] weights = new float[ruleCount];
            float[] heightFits = new float[ruleCount];
            float exponent = Math.Max(0f, 2f * HeightWeight);

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < hgt; y++)
                {
                    float h = heightmap.Heights[x, y];
                    float slope = slopes[x, y];
                    float totalWeight = 0f;
                    float totalHeightOnly = 0f;

                    for (int r = 0; r < ruleCount; r++)
                    {
                        BiomeRule rule = Rules[r];
                        float heightFit = SmoothStep(rule.MinHeight, rule.MaxHeight, h);
                        heightFit = 1f - Math.Abs(heightFit * 2f - 1f);
                        heightFit = exponent > 0f ? (float)Math.Pow(heightFit, exponent) : 1f;

                        heightFits[r] = heightFit;
                        weights[r] = heightFit * SlopeFit(slope, rule.MaxSlope);
                        totalWeight += weights[r];
                        totalHeightOnly += heightFit;
                    }

                    if (totalWeight > 0.0001f)
                    {
                        for (int r = 0; r < ruleCount; r++)
                        {
                            splatmap[x, y, r] = weights[r] / totalWeight;
                        }
                    }

                    else if (totalHeightOnly > 0.0001f)
                    {
                        // Too steep for every rule that fits this height: keep the height match
                        // instead of falling back to the first layer (which painted cliffs as water).
                        for (int r = 0; r < ruleCount; r++)
                        {
                            splatmap[x, y, r] = heightFits[r] / totalHeightOnly;
                        }
                    }

                    else
                    {
                        // Exactly on a band edge: use the rule whose band centre is nearest.
                        splatmap[x, y, NearestRule(h)] = 1f;
                    }
                }
            }

            return splatmap;
        }

        /// <summary>1 when the slope is within Max Slope, fading to 0 past it (speed set by SlopeWeight).</summary>
        private float SlopeFit(float slope, float maxSlope)
        {
            float over = slope - maxSlope;

            if (over <= 0f || SlopeWeight <= 0f)
            {
                return 1f;
            }

            return Math.Max(0f, 1f - over * SlopeWeight / 10f);
        }

        /// <summary>Index of the rule whose height band centre is closest to h.</summary>
        private int NearestRule(float h)
        {
            int best = 0;
            float bestDist = float.MaxValue;

            for (int r = 0; r < Rules.Count; r++)
            {
                float centre = (Rules[r].MinHeight + Rules[r].MaxHeight) * 0.5f;
                float dist = Math.Abs(h - centre);

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = r;
                }
            }

            return best;
        }

        /// <summary>Slope in degrees at every cell, independent of resolution.</summary>
        private float[,] CalculateSlopes(HeightmapData heightmap)
        {
            int w = heightmap.Width;
            int h = heightmap.Height;
            float[,] slopes = new float[w, h];

            // Normalized height change per normalized terrain width, then scaled to real proportions.
            float spanX = w > 1 ? (w - 1) : 1;
            float spanY = h > 1 ? (h - 1) : 1;

            for (int x = 0; x < w; x++)
            {
                int x0 = Math.Max(0, x - 1);
                int x1 = Math.Min(w - 1, x + 1);

                for (int y = 0; y < h; y++)
                {
                    int y0 = Math.Max(0, y - 1);
                    int y1 = Math.Min(h - 1, y + 1);

                    float dx = (heightmap.Heights[x1, y] - heightmap.Heights[x0, y]) * spanX / Math.Max(1, x1 - x0);
                    float dy = (heightmap.Heights[x, y1] - heightmap.Heights[x, y0]) * spanY / Math.Max(1, y1 - y0);
                    float gradient = (float)Math.Sqrt(dx * dx + dy * dy) * TerrainHeightToWidth;

                    slopes[x, y] = (float)(Math.Atan(gradient) * 180.0 / Math.PI);
                }
            }

            return slopes;
        }

        private float SmoothStep(float edge0, float edge1, float x)
        {
            if (edge1 <= edge0)
            {
                return x >= edge1 ? 1f : 0f;
            }

            float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Generates a splatmap and returns a colour preview of it.</summary>
        public Texture2D GeneratePreviewTexture(HeightmapData heightmap)
        {
            return GeneratePreviewTexture(heightmap, Generate(heightmap));
        }

        /// <summary>Builds a colour preview from an already generated splatmap.</summary>
        public Texture2D GeneratePreviewTexture(HeightmapData heightmap, float[,,] splatmap)
        {
            int w = splatmap.GetLength(0);
            int h = splatmap.GetLength(1);
            int layers = Math.Min(splatmap.GetLength(2), Rules.Count);
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float r = 0f, g = 0f, b = 0f;

                    for (int l = 0; l < layers; l++)
                    {
                        float weight = splatmap[x, y, l];
                        Color c = Rules[l].Color;
                        r += c.r * weight;
                        g += c.g * weight;
                        b += c.b * weight;
                    }

                    pixels[y * w + x] = new Color32(ToByte(r), ToByte(g), ToByte(b), 255);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        /// <summary>Converts 0-1 to a 0-255 byte.</summary>
        private static byte ToByte(float v)
        {
            return (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);
        }
    }
}
