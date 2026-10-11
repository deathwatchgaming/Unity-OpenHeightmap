/*
 * Asset: OpenHeightmap
 * Filename: TerrainApplier.cs
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace OpenHeightmap
{
    /// <summary>
    /// Settings used only when Apply to Terrain has to create a new terrain.
    /// </summary>
    [Serializable]
    public class NewTerrainSettings
    {
        /// <summary>When true, width/length follow the heightmap resolution (1 unit per sample: 1025 -> 1024).</summary>
        public bool MatchSizeToResolution = true;

        /// <summary>
        /// When true, the height is worked out from the width (width x HeightRatio), so a new
        /// terrain always has believable proportions. Presets set their own HeightRatio.
        /// </summary>
        public bool AutoHeight = true;

        /// <summary>Height ratio used when no preset has set one.</summary>
        public const float DefaultHeightRatio = 0.4f;

        /// <summary>
        /// Height as a fraction of the terrain width while AutoHeight is on (0.4 = 40%).
        /// Applying a built-in preset sets this to the preset's suggested value.
        /// </summary>
        public float HeightRatio = DefaultHeightRatio;

        /// <summary>HeightRatio, falling back to the default if it was never set (e.g. older saved settings).</summary>
        public float EffectiveHeightRatio => HeightRatio > 0.001f && !float.IsNaN(HeightRatio) ? HeightRatio : DefaultHeightRatio;

        /// <summary>
        /// Above this many world units between heightmap samples, terrain detail looks soft and
        /// the settings show a warning suggesting a higher resolution or tiling.
        /// </summary>
        public const float SoftDetailSpacing = 4f;

        /// <summary>
        /// Width (X), max height (Y) and length (Z). X/Z are ignored while MatchSizeToResolution is on;
        /// Y is ignored while AutoHeight is on.
        /// </summary>
        public Vector3 Size = new Vector3(1000f, 600f, 1000f);

        /// <summary>World size for a new terrain made from a heightmap of this resolution.</summary>
        public Vector3 GetSize(int resolution)
        {
            float width = MatchSizeToResolution ? resolution - 1 : Size.x;
            float length = MatchSizeToResolution ? resolution - 1 : Size.z;
            float height = AutoHeight ? Mathf.Max(1f, Mathf.Round(Mathf.Max(width, length) * EffectiveHeightRatio)) : Size.y;

            return new Vector3(width, height, length);
        }

        /// <summary>
        /// Draws the new-terrain settings (shared by OpenHeightmap and OpenStamper).
        /// resolution = the heightmap resolution, used to show the resulting size.
        /// </summary>
        public void DrawGUI(int resolution)
        {
            MatchSizeToResolution = EditorGUILayout.Toggle(
                new GUIContent("Match Size To Resolution", "New terrains: width/length follow the heightmap resolution (e.g. 1025 -> 1024 x 1024)."),
                MatchSizeToResolution);

            float width = Size.x;
            float length = Size.z;
            float height = Size.y;

            if (!MatchSizeToResolution)
            {
                width = EditorGUILayout.FloatField(new GUIContent("New Terrain Width", "Width (X) of a new terrain."), width);
                length = EditorGUILayout.FloatField(new GUIContent("New Terrain Length", "Length (Z) of a new terrain."), length);
            }

            AutoHeight = EditorGUILayout.Toggle(
                new GUIContent("Auto Height", "Set the height from the width so new terrains have natural proportions. Turn off to type a height."),
                AutoHeight);

            if (AutoHeight)
            {
                HeightRatio = EditorGUILayout.Slider(
                    new GUIContent("Height (% of width)", "Terrain height as a fraction of its width. Presets set a suggested value: low for gentle land, higher for mountains."),
                    EffectiveHeightRatio, 0.01f, 0.6f);

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.FloatField(
                        new GUIContent("New Terrain Height", "Worked out from the width while Auto Height is on."),
                        GetSize(resolution).y);
                }
            }

            else
            {
                height = EditorGUILayout.FloatField(
                    new GUIContent("New Terrain Height", "Max height (Y) used when Apply to Terrain has to create a new terrain."),
                    height);
            }

            Size = new Vector3(Mathf.Max(1f, width), Mathf.Max(1f, height), Mathf.Max(1f, length));

            Vector3 result = GetSize(resolution);
            EditorGUILayout.LabelField($"New terrain: {result.x} x {result.y} x {result.z}", EditorStyles.miniLabel);

            // Make it obvious when Apply to Terrain will update an existing terrain instead.
            Terrain active = Terrain.activeTerrain;

            if (active != null && active.terrainData != null)
            {
                Vector3 existing = active.terrainData.size;
                EditorGUILayout.HelpBox(
                    $"The scene already has a terrain ('{active.name}', {existing.x} x {existing.y} x {existing.z}). " +
                    "Apply to Terrain updates it and keeps its size; these settings are only used when there is no terrain in the scene.",
                    MessageType.Info);

                DrawSpacingWarning(Mathf.Max(existing.x, existing.z), resolution);
            }

            else
            {
                DrawSpacingWarning(Mathf.Max(result.x, result.z), resolution);
            }
        }

        /// <summary>
        /// Warns when heightmap samples would be far apart on the terrain (e.g. a 513 map stretched
        /// over 64,000 units is 125 units per sample), which makes the terrain look soft and flat.
        /// </summary>
        private static void DrawSpacingWarning(float width, int resolution)
        {
            if (resolution < 2)
            {
                return;
            }

            float spacing = width / (resolution - 1);

            if (spacing > SoftDetailSpacing)
            {
                EditorGUILayout.HelpBox(
                    $"Each heightmap sample is {spacing:0.#} units apart at this size, so the terrain will look soft and flat from up close. " +
                    "Use a higher resolution, a smaller terrain, or split it into tiles.",
                    MessageType.Warning);
            }
        }
    }

    /// <summary>
    /// Writes a HeightmapData (and optionally a splatmap) to the active terrain, or creates a new
    /// terrain when there is none. Shared by OpenHeightmap and OpenStamper.
    ///
    /// Notes on Unity's terrain API that this class takes care of:
    /// - Height and alphamap arrays are indexed [z, x]; HeightmapData is [x, y]. Data is transposed so
    ///   the terrain matches the preview and exported files.
    /// - A TerrainData made from script has a height of 1 unit; the size is always set explicitly.
    /// - heightmapResolution must be set before size (changing resolution rescales size).
    /// - Alphamap resolution must be a power of two, so splatmaps are resampled to fit.
    /// - Generated terrain layers and their textures are saved as assets so they survive a reload.
    /// </summary>
    public static class TerrainApplier
    {
        /// <summary>
        /// Applies the heightmap. Must be called outside OnGUI layout (e.g. via EditorApplication.delayCall)
        /// because it may show dialogs. Returns a status message, or null if the user cancelled.
        /// </summary>
        /// <param name="assetFolder">Project folder for new TerrainData / layer assets, e.g. "Assets/OpenHeightmap".</param>
        /// <param name="objectName">Name for a newly created terrain GameObject.</param>
        /// <param name="splatmap">Optional weights [x, y, layer], same width/height as the heightmap.</param>
        /// <param name="layerColors">Colour per splat layer, used for any terrain layers that have to be created.</param>
        /// <param name="layerNames">Name per splat layer, used for created terrain layer assets.</param>
        public static string Apply(
            HeightmapData heightmap,
            NewTerrainSettings newTerrain,
            string assetFolder,
            string objectName,
            float[,,] splatmap = null,
            IList<Color> layerColors = null,
            IList<string> layerNames = null)
        {
            if (heightmap == null || heightmap.Heights == null)
            {
                EditorUtility.DisplayDialog("No Heightmap", "Generate or load a heightmap first.", "OK");
                return null;
            }

            if (!IsValidTerrainResolution(heightmap.Width, heightmap.Height))
            {
                EditorUtility.DisplayDialog("Unsupported Resolution",
                    $"Unity terrains need a square heightmap of 33, 65, 129, 257, 513, 1025, 2049 or 4097 pixels.\n\nThis heightmap is {heightmap.Width}x{heightmap.Height}.",
                    "OK");
                return null;
            }

            if (splatmap != null && (splatmap.GetLength(0) != heightmap.Width || splatmap.GetLength(1) != heightmap.Height))
            {
                Debug.LogWarning("[OpenHeightmap] Splatmap size does not match the heightmap; regenerate the splatmap. Applying heights only.");
                splatmap = null;
            }

            Terrain activeTerrain = Terrain.activeTerrain;

            if (activeTerrain == null || activeTerrain.terrainData == null)
            {
                if (!EditorUtility.DisplayDialog("Create Terrain",
                    "No active terrain found. Create a new Terrain in the scene?", "Create", "Cancel"))
                {
                    return null;
                }

                return CreateTerrain(heightmap, newTerrain ?? new NewTerrainSettings(), assetFolder, objectName, splatmap, layerColors, layerNames);
            }

            return ApplyToExisting(activeTerrain, heightmap, assetFolder, splatmap, layerColors, layerNames);
        }

        /// <summary>True for square maps of 33, 65, 129, 257, 513, 1025, 2049 or 4097 (2^n + 1).</summary>
        public static bool IsValidTerrainResolution(int width, int height)
        {
            if (width != height || width < 33 || width > 4097)
            {
                return false;
            }

            int n = width - 1;
            return (n & (n - 1)) == 0;
        }

        // ── New terrain ──

        /// <summary>Creates a TerrainData asset and terrain GameObject. Cleans up the asset if anything fails.</summary>
        private static string CreateTerrain(
            HeightmapData heightmap, NewTerrainSettings settings, string assetFolder, string objectName,
            float[,,] splatmap, IList<Color> layerColors, IList<string> layerNames)
        {
            EnsureFolder(assetFolder);

            // Create or reuse layer assets FIRST: adding assets can make Unity reload other assets,
            // which wiped heights that had already been written to the new TerrainData.
            List<TerrainLayer> layers = splatmap != null
                ? PrepareLayers(null, splatmap.GetLength(2), assetFolder, layerColors, layerNames)
                : null;

            string dataPath = AssetDatabase.GenerateUniqueAssetPath($"{assetFolder}/TerrainData.asset");

            var terrainData = new TerrainData();
            // Resolution MUST be set before size: changing heightmapResolution rescales size.
            terrainData.heightmapResolution = heightmap.Width;
            Vector3 size = settings.GetSize(heightmap.Width);
            terrainData.size = size;

            GameObject terrainObj = null;

            try
            {
                AssetDatabase.CreateAsset(terrainData, dataPath);

                if (splatmap != null)
                {
                    ApplySplatmap(terrainData, splatmap, layers);
                }

                // Heights are written last so nothing above can overwrite them, then saved straight away.
                terrainData.SetHeights(0, 0, ToTerrainHeights(heightmap));
                EditorUtility.SetDirty(terrainData);
                AssetDatabase.SaveAssets();

                terrainObj = Terrain.CreateTerrainGameObject(terrainData);
                terrainObj.name = objectName;
                terrainObj.transform.position = Vector3.zero;
                Undo.RegisterCreatedObjectUndo(terrainObj, $"Create {objectName}");
                Selection.activeGameObject = terrainObj;
            }

            catch (Exception ex)
            {
                // Don't leave a TerrainData asset behind with no terrain using it.
                if (terrainObj != null)
                {
                    UnityEngine.Object.DestroyImmediate(terrainObj);
                }

                AssetDatabase.DeleteAsset(dataPath);
                Debug.LogError($"[OpenHeightmap] Creating the terrain failed: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("Create Terrain Failed", ex.Message, "OK");
                return $"Create terrain failed: {ex.Message}";
            }

            return $"Created new terrain ({size.x}x{size.y}x{size.z}, res {heightmap.Width})" +
                   (splatmap != null ? " with heightmap and splatmap" : " with heightmap applied");
        }

        // ── Existing terrain ──

        /// <summary>Writes heights (and splatmap) into an existing terrain, with undo.</summary>
        private static string ApplyToExisting(
            Terrain terrain, HeightmapData heightmap, string assetFolder,
            float[,,] splatmap, IList<Color> layerColors, IList<string> layerNames)
        {
            TerrainData terrainData = terrain.terrainData;

            if (terrainData.heightmapResolution != heightmap.Width)
            {
                bool confirmed = EditorUtility.DisplayDialog("Resolution Mismatch",
                    $"Terrain heightmap resolution ({terrainData.heightmapResolution}) differs from the heightmap ({heightmap.Width}).\n\nResize terrain?",
                    "Resize", "Cancel");

                if (!confirmed)
                {
                    return null;
                }
            }

            // Create or reuse any missing layer assets before touching the terrain (see CreateTerrain).
            List<TerrainLayer> layers = null;
            string splatNote = "";

            if (splatmap != null)
            {
                try
                {
                    layers = PrepareLayers(terrainData.terrainLayers, splatmap.GetLength(2), assetFolder, layerColors, layerNames);
                }

                catch (Exception ex)
                {
                    Debug.LogError($"[OpenHeightmap] Preparing terrain layers failed: {ex.Message}\n{ex.StackTrace}");
                    splatNote = " (splatmap failed, see Console)";
                    splatmap = null;
                }
            }

            // Complete undo captures the heightmap and alphamaps reliably.
            Undo.RegisterCompleteObjectUndo(terrainData, "Apply Heightmap");

            if (terrainData.heightmapResolution != heightmap.Width)
            {
                // Changing resolution keeps the terrain's world size, so set it back explicitly.
                Vector3 size = terrainData.size;
                terrainData.heightmapResolution = heightmap.Width;
                terrainData.size = size;
            }

            if (splatmap != null)
            {
                try
                {
                    ApplySplatmap(terrainData, splatmap, layers);
                    splatNote = " + splatmap";
                }

                catch (Exception ex)
                {
                    Debug.LogError($"[OpenHeightmap] Applying the splatmap failed: {ex.Message}\n{ex.StackTrace}");
                    splatNote = " (splatmap failed, see Console)";
                }
            }

            // Heights are written last so nothing above can overwrite them.
            terrainData.SetHeights(0, 0, ToTerrainHeights(heightmap));
            EditorUtility.SetDirty(terrainData);
            Vector3 finalSize = terrainData.size;
            return $"Heightmap{splatNote} applied to existing terrain '{terrain.name}' ({finalSize.x}x{finalSize.y}x{finalSize.z})";
        }

        // ── Data conversion ──

        /// <summary>HeightmapData [x, y] -> Unity heights [z, x], clamped to 0-1.</summary>
        public static float[,] ToTerrainHeights(HeightmapData heightmap)
        {
            int w = heightmap.Width;
            int h = heightmap.Height;
            float[,] heights = new float[h, w];

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    heights[y, x] = Mathf.Clamp01(heightmap.Heights[x, y]);
                }
            }

            return heights;
        }

        /// <summary>
        /// Returns the terrain layers to use for a splatmap with splatLayers layers: the terrain's own
        /// layers first, then a reused or newly created solid-colour layer asset for each missing one.
        /// Does all asset creation, so call it before changing any TerrainData.
        /// </summary>
        private static List<TerrainLayer> PrepareLayers(
            TerrainLayer[] existingLayers, int splatLayers, string assetFolder,
            IList<Color> layerColors, IList<string> layerNames)
        {
            var layers = new List<TerrainLayer>();

            if (existingLayers != null)
            {
                foreach (var existing in existingLayers)
                {
                    if (existing != null)
                    {
                        layers.Add(existing);
                    }
                }
            }

            if (layers.Count < splatLayers)
            {
                string layerFolder = $"{assetFolder}/TerrainLayers";
                EnsureFolder(layerFolder);

                for (int i = layers.Count; i < splatLayers; i++)
                {
                    string layerName = layerNames != null && i < layerNames.Count && !string.IsNullOrEmpty(layerNames[i])
                        ? layerNames[i]
                        : $"Layer {i}";
                    Color color = layerColors != null && i < layerColors.Count ? layerColors[i] : Color.gray;
                    layers.Add(GetOrCreateLayerAsset(layerFolder, layerName, color));
                }
            }

            return layers;
        }

        /// <summary>
        /// Resamples the splatmap to a power-of-two alphamap and writes it using the given layers
        /// (from PrepareLayers). Creates no assets.
        /// </summary>
        private static void ApplySplatmap(TerrainData terrainData, float[,,] splatmap, List<TerrainLayer> layers)
        {
            int sw = splatmap.GetLength(0);
            int sh = splatmap.GetLength(1);
            int splatLayers = splatmap.GetLength(2);

            if (layers != null && (terrainData.terrainLayers == null || terrainData.terrainLayers.Length != layers.Count))
            {
                terrainData.terrainLayers = layers.ToArray();
            }

            // Alphamap resolution must be a power of two (16-4096); 1025 -> 1024 etc.
            int alphaRes = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(16, sw - 1)), 16, 4096);

            if (terrainData.alphamapResolution != alphaRes)
            {
                terrainData.alphamapResolution = alphaRes;
            }

            int totalLayers = terrainData.alphamapLayers;

            if (totalLayers < splatLayers && layers != null)
            {
                totalLayers = layers.Count;
            }

            float[,,] weights = new float[alphaRes, alphaRes, totalLayers];

            for (int ay = 0; ay < alphaRes; ay++)
            {
                float v = (ay + 0.5f) / alphaRes;

                for (int ax = 0; ax < alphaRes; ax++)
                {
                    float u = (ax + 0.5f) / alphaRes;
                    float sum = 0f;

                    for (int l = 0; l < splatLayers && l < totalLayers; l++)
                    {
                        float value = SampleBilinear(splatmap, sw, sh, l, u, v);
                        weights[ay, ax, l] = value;
                        sum += value;
                    }

                    if (sum > 0.0001f)
                    {
                        for (int l = 0; l < splatLayers && l < totalLayers; l++)
                        {
                            weights[ay, ax, l] /= sum;
                        }
                    }

                    else
                    {
                        weights[ay, ax, 0] = 1f;
                    }
                }
            }

            terrainData.SetAlphamaps(0, 0, weights);
        }

        /// <summary>Bilinear sample of one splat layer at normalized UV.</summary>
        private static float SampleBilinear(float[,,] map, int w, int h, int layer, float u, float v)
        {
            float fx = Mathf.Clamp01(u) * (w - 1);
            float fy = Mathf.Clamp01(v) * (h - 1);
            int x0 = (int)fx;
            int y0 = (int)fy;
            int x1 = Math.Min(x0 + 1, w - 1);
            int y1 = Math.Min(y0 + 1, h - 1);
            float tx = fx - x0;
            float ty = fy - y0;

            float a = map[x0, y0, layer] + (map[x1, y0, layer] - map[x0, y0, layer]) * tx;
            float b = map[x0, y1, layer] + (map[x1, y1, layer] - map[x0, y1, layer]) * tx;
            return a + (b - a) * ty;
        }

        /// <summary>
        /// Returns the solid-colour TerrainLayer for this name and colour, creating it (and its texture)
        /// only if it doesn't exist yet. The colour is part of the file name, so changing a rule's colour
        /// makes a new layer instead of recolouring terrains that already use the old one.
        /// </summary>
        private static TerrainLayer GetOrCreateLayerAsset(string folder, string layerName, Color color)
        {
            string baseName = $"{MakeSafeFileName(layerName)} {ToHex(color)}";
            string layerPath = $"{folder}/{baseName}.terrainlayer";

            TerrainLayer existing = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);

            if (existing != null)
            {
                return existing;
            }

            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color[16];

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }

            tex.SetPixels(pixels);
            tex.Apply();
            tex.name = $"{baseName} Color";
            AssetDatabase.CreateAsset(tex, $"{folder}/{baseName} Color.asset");

            var layer = new TerrainLayer { diffuseTexture = tex };
            layer.name = baseName;
            AssetDatabase.CreateAsset(layer, layerPath);
            return layer;
        }

        /// <summary>Colour as RRGGBB hex (used in layer file names).</summary>
        private static string ToHex(Color color)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            return $"{r:X2}{g:X2}{b:X2}";
        }

        /// <summary>Replaces characters that are not allowed in file names.</summary>
        private static string MakeSafeFileName(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return string.IsNullOrWhiteSpace(name) ? "Layer" : name.Trim();
        }

        /// <summary>Creates a project folder path like "Assets/A/B" if it doesn't exist yet.</summary>
        public static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/').TrimEnd('/');

            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string[] parts = folder.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";

                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
