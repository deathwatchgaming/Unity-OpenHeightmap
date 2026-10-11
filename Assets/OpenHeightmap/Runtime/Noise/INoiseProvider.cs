/*
 * Asset: OpenHeightmap
 * Filename: INoiseProvider.cs
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

namespace OpenHeightmap
{
    /// <summary>
    /// Interface for all noise generation algorithms. Implementations produce values
    /// in the range [-1, 1] (except MidpointDisplacement which produces [0, 1]).
    /// Each octave should use a different seed offset to avoid correlation.
    /// </summary>
    public interface INoiseProvider
    {
        /// <summary>Display name of the noise algorithm.</summary>
        string Name { get; }

        /// <summary>
        /// Samples the noise at the given 2D coordinates.
        /// </summary>
        /// <param name="x">X coordinate in noise space. Scale this by the noise frequency to control detail level.</param>
        /// <param name="y">Y coordinate in noise space.</param>
        /// <param name="seed">Seed offset for decorrelation between octaves. Add o*1000 for octave o.</param>
        /// <returns>Noise value, typically in [-1, 1] range.</returns>
        float Sample(float x, float y, int seed = 0);
    }
}
