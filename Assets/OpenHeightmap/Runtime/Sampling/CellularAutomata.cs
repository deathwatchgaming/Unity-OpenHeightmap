/*
 * Asset: OpenHeightmap
 * Filename: CellularAutomata.cs
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

using System;

namespace OpenHeightmap
{
    /// <summary>Cellular automata ruleset selector.</summary>
    public enum AutomataRule { Cave, Organic, Uniform }

    /// <summary>
    /// Cellular automata for binary grid generation. Starts with random fill, then applies
    /// neighbor-counting rules for N iterations. Produces organic-looking connected regions.
    /// Useful for cave systems, biome boundary masks, and overhang generation.
    /// </summary>
    [Serializable]
    public class CellularAutomata
    {
        /// <summary>Ruleset determining birth/survival thresholds.</summary>
        public AutomataRule Rule = AutomataRule.Cave;
        /// <summary>Number of simulation steps. More = more refined shape. Range: 1-20.</summary>
        public int Iterations = 5;
        /// <summary>Initial probability that a cell starts alive. Range: 0.3-0.6.</summary>
        public float InitialFill = 0.45f;
        /// <summary>Minimum neighbor count for a dead cell to become alive. Cave default: 5.</summary>
        public int BirthMin = 5;
        /// <summary>Maximum neighbor count for a dead cell to become alive. Cave default: 8.</summary>
        public int BirthMax = 8;
        /// <summary>Minimum neighbor count for a live cell to stay alive. Cave default: 4.</summary>
        public int SurviveMin = 4;
        /// <summary>Maximum neighbor count for a live cell to stay alive. Cave default: 7.</summary>
        public int SurviveMax = 7;
        /// <summary>When true, swaps alive/dead in the final output (0=alive, 1=dead).</summary>
        public bool InvertOutput = false;

        private Random rng;

        public void Apply(HeightmapData heightmap, int seed = 0)
        {
            rng = new Random(seed);
            int w = heightmap.Width;
            int h = heightmap.Height;

            bool[,] cells = new bool[w, h];
            bool[,] next = new bool[w, h];

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    cells[x, y] = (float)rng.NextDouble() < InitialFill;
                }
            }

            for (int iter = 0; iter < Iterations; iter++)
            {
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        int neighbors = CountNeighbors(cells, x, y, w, h);

                        if (Rule == AutomataRule.Cave)
                        {
                            next[x, y] = neighbors >= BirthMin && neighbors <= SurviveMax;
                        }

                        else if (Rule == AutomataRule.Organic)
                        {
                            next[x, y] = neighbors >= 4;
                        }

                        else
                        {
                            next[x, y] = neighbors >= 4 && neighbors <= 5;
                        }
                    }
                }

                bool[,] temp = cells;
                cells = next;
                next = temp;
            }

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    float val = cells[x, y] ? 1f : 0f;

                    if (InvertOutput) 
                    {
                        val = 1f - val;
                    }

                    heightmap.Heights[x, y] = val;
                }
            }

            heightmap.RecalculateBounds();
        }

        private int CountNeighbors(bool[,] cells, int x, int y, int w, int h)
        {
            int count = 0;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) 
                    {
                        continue;
                    }

                    int nx = x + dx;
                    int ny = y + dy;

                    if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                    {
                        if (cells[nx, ny]) 
                        {
                            count++;
                        }
                    }

                    else
                    {
                        count++;
                    }
                }
            }
            
            return count;
        }
    }
}
