/*
 * Asset: OpenHeightmap
 * Filename: FalloffGenerator.cs
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

using System;

namespace OpenHeightmap
{
    /// <summary>Falloff shape selector. Controls the edge-dimming pattern for island/bounded terrain.</summary>
    public enum FalloffType { Circular, Square, Diamond, Gaussian }

    /// <summary>
    /// Multiplicative edge falloff mask. Darkens terrain edges to produce island-like shapes.
    /// Applied after erosion and smoothing but before final normalization.
    /// Formula: height *= (1 - falloff * Strength).
    /// </summary>
    [Serializable]
    public class FalloffGenerator
    {
        /// <summary>Falloff shape: Circular (radial), Square (box), Diamond (manhattan), or Gaussian (bell curve).</summary>
        public FalloffType Type = FalloffType.Circular;
        /// <summary>Overall falloff intensity multiplier. Range: 0-3.</summary>
        public float Strength = 1f;
        /// <summary>
        /// Power curve exponent applied to the falloff value before Strength.
        /// Higher = steeper drop at edges, flatter center. Range: 0.5-5.
        /// </summary>
        public float Power = 2f;
        /// <summary>
        /// Shifts the falloff origin inward. Higher = more flat area in the center before
        /// falloff begins. Range: 0-0.9.
        /// </summary>
        public float Offset = 0f;
        /// <summary>Reserved for future inner radius control.</summary>
        public float InnerRadius = 0f;

        /// <summary>Applies the falloff mask to the heightmap in-place.</summary>
        public void Apply(HeightmapData heightmap)
        {
            int w = heightmap.Width;
            int h = heightmap.Height;
            float centerX = w * 0.5f;
            float centerY = h * 0.5f;
            float maxDist = Math.Max(centerX, centerY);

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    float dx = (x - centerX) / maxDist;
                    float dy = (y - centerY) / maxDist;

                    float falloff = Type switch
                    {
                        FalloffType.Circular => CircularFalloff(dx, dy),
                        FalloffType.Square => SquareFalloff(dx, dy),
                        FalloffType.Diamond => DiamondFalloff(dx, dy),
                        FalloffType.Gaussian => GaussianFalloff(dx, dy),
                        _ => 1f
                    };

                    falloff = ApplyPowerCurve(falloff);
                    falloff = ApplyInnerRadius(falloff);
                    falloff = Math.Clamp(falloff * Strength, 0f, 1f);

                    heightmap.Heights[x, y] *= (1f - falloff);
                }
            }

            heightmap.RecalculateBounds();
        }

        private float CircularFalloff(float dx, float dy)
        {
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private float SquareFalloff(float dx, float dy)
        {
            return Math.Max(Math.Abs(dx), Math.Abs(dy));
        }

        private float DiamondFalloff(float dx, float dy)
        {
            return Math.Abs(dx) + Math.Abs(dy);
        }

        private float GaussianFalloff(float dx, float dy)
        {
            float dist = dx * dx + dy * dy;
            return 1f - (float)Math.Exp(-dist * 2f);
        }

        private float ApplyPowerCurve(float falloff)
        {
            if (Power == 1f) 
            {
                return falloff;
            }

            return (float)Math.Pow(Math.Max(0f, falloff), Power);
        }

        private float ApplyInnerRadius(float falloff)
        {
            if (Offset <= 0f) 
            {
                return falloff;
            }
            
            return Math.Max(0f, falloff - Offset) / (1f - Offset);
        }
    }
}
