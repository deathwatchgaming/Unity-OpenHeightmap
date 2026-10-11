/*
 * Asset: OpenHeightmap
 * Filename: PoissonDiskSampler.cs
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

using System;
using System.Collections.Generic;

namespace OpenHeightmap
{
    /// <summary>
    /// Generates a set of points in [0,1] x [0,1] space with guaranteed minimum distance
    /// between any two points (blue noise distribution). Useful for placing terrain features
    /// (rocks, trees, water sources) with natural spacing.
    /// Uses Bridson's algorithm with spatial hashing for O(n) average performance.
    /// </summary>
    [Serializable]
    public class PoissonDiskSampler
    {
        /// <summary>Minimum distance between any two generated points. Range: 0.01-0.5.</summary>
        public float MinDistance = 0.05f;
        /// <summary>Max rejection attempts before a point is considered dead. Higher = more uniform distribution.</summary>
        public int MaxAttempts = 30;
        /// <summary>Maximum number of points to generate.</summary>
        public int MaxPoints = 1000;

        private Random rng;
        private float cellSize;
        private int gridWidth, gridHeight;
        private int[,] grid;
        private List<Vector2> points;
        private List<int> activeList;

        public struct Vector2
        {
            public float X;
            public float Y;
            public Vector2(float x, float y) { X = x; Y = y; }
        }

        public List<Vector2> Generate(int seed = 0)
        {
            rng = new Random(seed);
            points = new List<Vector2>();
            activeList = new List<int>();

            cellSize = MinDistance / (float)Math.Sqrt(2);
            gridWidth = (int)Math.Ceiling(1.0 / cellSize);
            gridHeight = (int)Math.Ceiling(1.0 / cellSize);
            grid = new int[gridWidth, gridHeight];

            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    grid[x, y] = -1;
                }
            }

            Vector2 first = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
            AddPoint(first);

            while (activeList.Count > 0 && points.Count < MaxPoints)
            {
                int idx = rng.Next(activeList.Count);
                Vector2 point = points[activeList[idx]];
                bool found = false;

                for (int i = 0; i < MaxAttempts; i++)
                {
                    float angle = (float)rng.NextDouble() * 2f * (float)Math.PI;
                    float dist = MinDistance + (float)rng.NextDouble() * MinDistance;

                    Vector2 candidate = new Vector2(
                        point.X + (float)Math.Cos(angle) * dist,
                        point.Y + (float)Math.Sin(angle) * dist
                    );

                    if (IsValid(candidate))
                    {
                        AddPoint(candidate);
                        found = true;
                    }
                }

                if (!found)
                {
                    activeList.RemoveAt(idx);
                }
            }

            return points;
        }

        private void AddPoint(Vector2 point)
        {
            int gx = (int)(point.X / cellSize);
            int gy = (int)(point.Y / cellSize);
            gx = Math.Clamp(gx, 0, gridWidth - 1);
            gy = Math.Clamp(gy, 0, gridHeight - 1);

            grid[gx, gy] = points.Count;
            points.Add(point);
            activeList.Add(points.Count - 1);
        }

        private bool IsValid(Vector2 point)
        {
            if (point.X < 0 || point.X >= 1 || point.Y < 0 || point.Y >= 1)
            {
                return false;
            }

            int gx = (int)(point.X / cellSize);
            int gy = (int)(point.Y / cellSize);
            gx = Math.Clamp(gx, 0, gridWidth - 1);
            gy = Math.Clamp(gy, 0, gridHeight - 1);

            int searchRange = 2;

            for (int dx = -searchRange; dx <= searchRange; dx++)
            {
                for (int dy = -searchRange; dy <= searchRange; dy++)
                {
                    int nx = gx + dx;
                    int ny = gy + dy;
                    if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight) 
                    {
                        continue;
                    }

                    int idx = grid[nx, ny];

                    if (idx >= 0)
                    {
                        Vector2 existing = points[idx];
                        float d = (point.X - existing.X) * (point.X - existing.X) +
                                  (point.Y - existing.Y) * (point.Y - existing.Y);
                                  
                        if (d < MinDistance * MinDistance)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }
    }
}
