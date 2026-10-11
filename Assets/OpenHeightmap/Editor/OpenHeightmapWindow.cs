/*
 * Asset: OpenHeightmap
 * Filename: OpenHeightmapWindow.cs
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OpenHeightmap
{
    public class OpenHeightmapWindow : EditorWindow
    {
        /// <summary>
        /// Returns the current heightmap from an open OpenHeightmap window, or null if none is open
        /// or nothing has been generated. Never opens a new window.
        /// </summary>
        public static HeightmapData CurrentHeightmap
        {
            get
            {
                if (!HasOpenInstances<OpenHeightmapWindow>())
                {
                    return null;
                }

                foreach (var window in Resources.FindObjectsOfTypeAll<OpenHeightmapWindow>())
                {
                    if (window != null && window.currentHeightmap != null)
                    {
                        return window.currentHeightmap;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Height ratio (terrain height as a fraction of its width) from an open OpenHeightmap window,
        /// or the default when none is open. Lets OpenStamper keep a preset's suggested proportions.
        /// </summary>
        public static float CurrentHeightRatio
        {
            get
            {
                if (HasOpenInstances<OpenHeightmapWindow>())
                {
                    foreach (var window in Resources.FindObjectsOfTypeAll<OpenHeightmapWindow>())
                    {
                        if (window != null && window.newTerrain != null)
                        {
                            return window.newTerrain.EffectiveHeightRatio;
                        }
                    }
                }

                return NewTerrainSettings.DefaultHeightRatio;
            }
        }

        // ── Core ──
        [SerializeField] private HeightmapGeneratorParameters parameters = new HeightmapGeneratorParameters();
        private HeightmapGenerator generator = new HeightmapGenerator();
        private HeightmapData currentHeightmap;
        private PreviewRenderer previewRenderer;

        // Survives script recompiles so the heightmap can be regenerated (it is deterministic).
        [SerializeField] private bool hadHeightmap;
        [SerializeField] private HeightmapGeneratorParameters lastGeneratedParameters;
        [SerializeField] private bool hadSplatmap;

        // ── UI State ──
        private Vector2 scrollPos;
        private Vector2 previewScrollPos;
        [SerializeField] private int selectedTab;
        private string[] tabNames = { "Noise", "Erosion", "Filters", "Splatmap", "Presets", "History" };
        [SerializeField] private bool showPreview = true;
        [SerializeField] private bool previewColorMode = true;
        [SerializeField] private bool useGPUPreview = true;
        [SerializeField] private float previewZoom = 1f;
        private Rect previewRect;
        private string statusMessage = "Ready";

        // ── Preview cache (rendered once per change, not on every GUI event) ──
        private Texture2D cachedPreview;
        /// <summary>The heightmap the cached preview was rendered from.</summary>
        private HeightmapData cachedPreviewSource;
        /// <summary>Display mode (colour/grayscale/gradient + GPU) the cached preview was rendered with.</summary>
        private int cachedPreviewKey = -1;
        /// <summary>Set when the preview must be re-rendered (new data or a display setting changed).</summary>
        private bool previewDirty = true;

        // ── Generation ──
        private bool isGenerating;

        // ── Export ──
        [SerializeField] private ExportFormat exportFormat = ExportFormat.PNG;

        // ── Splatmap ──
        [SerializeField] private bool showSplatmapPreview;
        [SerializeField] private SplatmapGenerator splatmapGenerator = new SplatmapGenerator();
        private Texture2D splatmapPreview;
        private float[,,] currentSplatmap;

        // ── Presets ──
        [SerializeField] private int selectedPresetIndex = 0;
        private List<TerrainPreset> presets;
        private List<UserPreset> userPresets = new List<UserPreset>();

        // ── History ──
        [SerializeField] private List<HeightmapGeneratorParameters> history = new List<HeightmapGeneratorParameters>();
        [SerializeField] private int historyIndex = -1;
        private const int MaxHistory = 30;

        // ── Gradient ──
        [SerializeField] private bool useCustomGradient = false;
        [SerializeField] private Gradient previewGradient;

        // ── Batch ──
        [SerializeField] private int batchCount = 4;
        private List<HeightmapData> batchResults = new List<HeightmapData>();
        /// <summary>Seed used for each batch result, so selecting one updates the Seed field.</summary>
        private List<int> batchSeeds = new List<int>();
        /// <summary>One small thumbnail texture per batch result (owned by this window).</summary>
        private List<Texture2D> batchThumbnails = new List<Texture2D>();
        private int selectedBatchIndex = -1;
        /// <summary>Size in pixels of batch thumbnails.</summary>
        private const int ThumbnailSize = 64;

        // ── New Terrain (used only when Apply to Terrain has to create one) ──
        [SerializeField] private NewTerrainSettings newTerrain = new NewTerrainSettings();

        /// <summary>
        /// Project folder where new TerrainData and terrain layer assets are saved. Kept outside the
        /// tool folder so updating or removing the tool never touches the user's terrains.
        /// </summary>
        private const string TerrainAssetFolder = "Assets/OpenHeightmap Generated";

        [MenuItem("Tools/OpenHeightmap %&h")]
        public static void ShowWindow()
        {
            var window = GetWindow<OpenHeightmapWindow>("OpenHeightmap Generator");
            window.minSize = new Vector2(900, 650);
            window.Show();
        }

        private void OnEnable()
        {
            previewRenderer = new PreviewRenderer();
            presets = TerrainPresets.GetAll();
            Undo.undoRedoPerformed += OnUndoRedo;

            if (parameters == null)
            {
                parameters = new HeightmapGeneratorParameters();
            }

            if (splatmapGenerator == null)
            {
                splatmapGenerator = new SplatmapGenerator();
            }

            if (newTerrain == null)
            {
                newTerrain = new NewTerrainSettings();
            }

            if (history == null)
            {
                history = new List<HeightmapGeneratorParameters>();
            }


            if (previewGradient == null)
            {
                previewGradient = new Gradient();
                InitDefaultGradient();
            }

            RefreshUserPresets();

            if (history.Count == 0)
            {
                PushToHistory();
            }

            previewDirty = true;

            // After a script recompile the heightmap data is gone; rebuild it from the saved settings.
            if (hadHeightmap && currentHeightmap == null)
            {
                EditorApplication.delayCall += RestoreAfterReload;
            }
        }

        private void OnDisable()
        {
            previewRenderer?.Cleanup();
            Undo.undoRedoPerformed -= OnUndoRedo;

            if (splatmapPreview != null)
            {
                DestroyImmediate(splatmapPreview);
            }

            ClearBatchThumbnails();
        }

        /// <summary>Regenerates the last heightmap (and splatmap) after a script recompile cleared them.</summary>
        private void RestoreAfterReload()
        {
            if (this == null || currentHeightmap != null)
            {
                return;
            }

            bool restoreSplat = hadSplatmap;
            GenerateHeightmap(pushHistory: false, source: lastGeneratedParameters);

            if (restoreSplat && currentHeightmap != null)
            {
                GenerateSplatmap();
            }

            statusMessage = "Restored heightmap after script reload";
            Repaint();
        }

        private void OnUndoRedo() => Repaint();

        private void InitDefaultGradient()
        {
            GradientColorKey[] keys = new GradientColorKey[]
            {
                new GradientColorKey(new Color(0f, 0f, 0.3f), 0f),
                new GradientColorKey(new Color(0f, 0.1f, 0.5f), 0.1f),
                new GradientColorKey(new Color(0.1f, 0.4f, 0.7f), 0.2f),
                new GradientColorKey(new Color(0.6f, 0.65f, 0.35f), 0.35f),
                new GradientColorKey(new Color(0.2f, 0.55f, 0.15f), 0.5f),
                new GradientColorKey(new Color(0.45f, 0.4f, 0.35f), 0.7f),
                new GradientColorKey(new Color(0.55f, 0.5f, 0.45f), 0.8f),
                new GradientColorKey(new Color(0.95f, 0.95f, 0.97f), 1f)
            };
            previewGradient.SetKeys(keys, new GradientAlphaKey[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
        }

        /// <summary>
        /// Runs an action after the current GUI pass. Anything that opens a dialog, file panel or
        /// progress bar, or creates assets/objects, must not run inside OnGUI's layout groups
        /// ("EndLayoutGroup: BeginLayoutGroup must be called first").
        /// </summary>
        private void Defer(Action action)
        {
            EditorApplication.delayCall += () =>
            {
                if (this == null)
                {
                    return;
                }

                action();
                Repaint();
            };
        }

        private void OnGUI()
        {
            DrawToolbar();
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawSidebar();
                DrawMainArea();
            }
            DrawStatusBar();
        }

        // ═══════════════════════════════════════════════════
        //  TOOLBAR
        // ═══════════════════════════════════════════════════

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(isGenerating))
                {
                    if (GUILayout.Button("Generate", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    {
                        Defer(() => GenerateHeightmap());
                    }

                    if (GUILayout.Button("Regenerate", EditorStyles.toolbarButton, GUILayout.Width(80)))
                    {
                        Defer(() =>
                        {
                            ReleaseFieldFocus();
                            parameters.Seed = UnityEngine.Random.Range(0, 99999);
                            GenerateHeightmap();
                        });
                    }
                }

                GUILayout.Space(10);

                if (GUILayout.Button("Apply to Terrain", EditorStyles.toolbarButton, GUILayout.Width(100)))
                {
                    Defer(ApplyToTerrain);
                }

                GUILayout.Space(5);

                // Export format selector + button
                exportFormat = (ExportFormat)EditorGUILayout.EnumPopup(exportFormat, GUILayout.Width(60));

                if (GUILayout.Button("Export", EditorStyles.toolbarButton, GUILayout.Width(60)))
                {
                    Defer(ExportHeightmap);
                }

                if (currentSplatmap != null)
                {
                    if (GUILayout.Button("Export Splat", EditorStyles.toolbarButton, GUILayout.Width(80)))
                    {
                        Defer(ExportSplatmap);
                    }
                }

                GUILayout.Space(10);

                EditorGUI.BeginChangeCheck();
                useGPUPreview = GUILayout.Toggle(useGPUPreview, new GUIContent("GPU", "Render the preview with a compute shader (Color/Grayscale modes)."), EditorStyles.toolbarButton, GUILayout.Width(40));
                previewColorMode = GUILayout.Toggle(previewColorMode, new GUIContent("Color", "On: terrain colours. Off: grayscale."), EditorStyles.toolbarButton, GUILayout.Width(50));
                showPreview = GUILayout.Toggle(showPreview, "Preview", EditorStyles.toolbarButton, GUILayout.Width(60));
                showSplatmapPreview = GUILayout.Toggle(showSplatmapPreview, "Splat", EditorStyles.toolbarButton, GUILayout.Width(50));
                useCustomGradient = GUILayout.Toggle(useCustomGradient, new GUIContent("Gradient", "Colour the preview with the custom gradient below it."), EditorStyles.toolbarButton, GUILayout.Width(65));

                if (EditorGUI.EndChangeCheck())
                {
                    previewDirty = true;
                }

                GUILayout.FlexibleSpace();

                // History navigation
                using (new EditorGUI.DisabledScope(historyIndex <= 0))
                {
                    if (GUILayout.Button("◀", EditorStyles.toolbarButton, GUILayout.Width(25)))
                    {
                        UndoHistory();
                    }
                }

                GUILayout.Label($"{historyIndex + 1}/{history.Count}", EditorStyles.miniLabel, GUILayout.Width(35));

                using (new EditorGUI.DisabledScope(historyIndex >= history.Count - 1))
                {
                    if (GUILayout.Button("▶", EditorStyles.toolbarButton, GUILayout.Width(25)))
                    {
                        RedoHistory();
                    }
                }

                GUILayout.Space(10);
            }
        }

        // ═══════════════════════════════════════════════════
        //  SIDEBAR TABS
        // ═══════════════════════════════════════════════════

        private void DrawSidebar()
        {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(scrollPos, GUILayout.Width(300), GUILayout.ExpandHeight(true)))
            {
                scrollPos = scrollScope.scrollPosition;
                selectedTab = GUILayout.Toolbar(selectedTab, tabNames);
                GUILayout.Space(5);

                switch (selectedTab)
                {
                    case 0: DrawNoiseTab(); break;
                    case 1: DrawErosionTab(); break;
                    case 2: DrawFiltersTab(); break;
                    case 3: DrawSplatmapTab(); break;
                    case 4: DrawPresetsTab(); break;
                    case 5: DrawHistoryTab(); break;
                }
            }
        }

        private void DrawNoiseTab()
        {
            EditorGUILayout.LabelField("Base Terrain", EditorStyles.boldLabel);
            parameters.Resolution = PowerOfTwoPlusOnePopup("Resolution", parameters.Resolution);

            EditorGUILayout.BeginHorizontal();
            parameters.Seed = EditorGUILayout.IntField("Seed", parameters.Seed);

            if (GUILayout.Button("Random", GUILayout.Width(60)))
            {
                ReleaseFieldFocus();
                parameters.Seed = UnityEngine.Random.Range(0, 99999);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("New Terrain", EditorStyles.boldLabel);
            newTerrain.DrawGUI(parameters.Resolution);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Noise", EditorStyles.boldLabel);
            parameters.NoiseType = (NoiseType)EditorGUILayout.EnumPopup("Type", parameters.NoiseType);
            parameters.Scale = EditorGUILayout.FloatField(new GUIContent("Scale", "Feature frequency. Lower = larger features. The same value gives the same terrain at any resolution."), parameters.Scale);
            parameters.Octaves = EditorGUILayout.IntSlider("Octaves", parameters.Octaves, 1, 12);
            parameters.Persistence = EditorGUILayout.Slider("Persistence", parameters.Persistence, 0f, 1f);
            parameters.Lacunarity = EditorGUILayout.Slider("Lacunarity", parameters.Lacunarity, 1f, 4f);
            parameters.HeightMultiplier = EditorGUILayout.Slider(
                new GUIContent("Height Multiplier", "1 = full range. Below 1 lowers the terrain. Above 1 raises lowlands and slopes without flattening peaks."),
                parameters.HeightMultiplier, 0.1f, 5f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Offset", EditorStyles.boldLabel);
            parameters.OffsetX = EditorGUILayout.FloatField("Offset X", parameters.OffsetX);
            parameters.OffsetY = EditorGUILayout.FloatField("Offset Y", parameters.OffsetY);

            if (parameters.NoiseType == NoiseType.Voronoi)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Voronoi", EditorStyles.boldLabel);
                parameters.VoronoiMetric = (VoronoiDistanceMetric)EditorGUILayout.EnumPopup("Metric", parameters.VoronoiMetric);
                parameters.VoronoiReturn = (VoronoiReturnType)EditorGUILayout.EnumPopup("Return", parameters.VoronoiReturn);
            }

            else if (parameters.NoiseType == NoiseType.Worley)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Worley", EditorStyles.boldLabel);
                parameters.WorleyReturn = (WorleyReturnType)EditorGUILayout.EnumPopup("Return", parameters.WorleyReturn);
            }

            else if (parameters.NoiseType == NoiseType.MidpointDisplacement)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Midpoint Displacement", EditorStyles.boldLabel);
                parameters.MidpointRoughness = EditorGUILayout.Slider("Roughness", parameters.MidpointRoughness, 0.1f, 1f);
            }

            EditorGUILayout.Space();
            parameters.UseDomainWarping = EditorGUILayout.Toggle("Domain Warping", parameters.UseDomainWarping);

            if (parameters.UseDomainWarping)
            {
                EditorGUI.indentLevel++;
                parameters.WarpStrength = EditorGUILayout.Slider(new GUIContent("Strength", "How far terrain is pushed around, as a fraction of the map width."), parameters.WarpStrength, 0.001f, 0.1f);
                parameters.WarpOctaves = EditorGUILayout.IntSlider("Octaves", parameters.WarpOctaves, 1, 4);
                parameters.WarpFrequency = EditorGUILayout.Slider(new GUIContent("Frequency", "Size of the warp swirls, in the same units as Scale."), parameters.WarpFrequency, 0.001f, 0.1f);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Batch", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            batchCount = EditorGUILayout.IntSlider("Variations", batchCount, 2, 8);

            using (new EditorGUI.DisabledScope(isGenerating))
            {
                if (GUILayout.Button("Generate", GUILayout.Width(70)))
                {
                    Defer(GenerateBatch);
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Generates several random seeds with the current settings. Click a thumbnail to use it.", MessageType.None);
        }

        private void DrawErosionTab()
        {
            EditorGUILayout.LabelField("Hydraulic Erosion", EditorStyles.boldLabel);
            parameters.ApplyHydraulicErosion = EditorGUILayout.Toggle("Enable", parameters.ApplyHydraulicErosion);

            if (parameters.ApplyHydraulicErosion)
            {
                EditorGUI.indentLevel++;
                parameters.HydraulicIterations = EditorGUILayout.IntSlider(
                    new GUIContent("Iterations", "Erosion amount, in hundreds of droplets on a 513 map (200 = 20,000). Scales with resolution."),
                    parameters.HydraulicIterations, 10, 1000);
                parameters.HydraulicLifetime = EditorGUILayout.IntSlider("Max Lifetime", parameters.HydraulicLifetime, 5, 80);
                parameters.HydraulicInertia = EditorGUILayout.Slider("Inertia", parameters.HydraulicInertia, 0f, 0.2f);
                parameters.HydraulicGravity = EditorGUILayout.Slider("Gravity", parameters.HydraulicGravity, 0f, 20f);
                parameters.HydraulicEvaporation = EditorGUILayout.Slider("Evaporation", parameters.HydraulicEvaporation, 0f, 0.1f);
                parameters.HydraulicDeposition = EditorGUILayout.Slider("Deposition Rate", parameters.HydraulicDeposition, 0f, 1f);
                parameters.HydraulicErosionRate = EditorGUILayout.Slider("Erosion Rate", parameters.HydraulicErosionRate, 0f, 1f);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Thermal Erosion", EditorStyles.boldLabel);
            parameters.ApplyThermalErosion = EditorGUILayout.Toggle("Enable", parameters.ApplyThermalErosion);

            if (parameters.ApplyThermalErosion)
            {
                EditorGUI.indentLevel++;
                parameters.ThermalIterations = EditorGUILayout.IntSlider("Iterations", parameters.ThermalIterations, 1, 50);
                parameters.ThermalTalus = EditorGUILayout.Slider("Talus Angle", parameters.ThermalTalus, 0.001f, 0.1f);
                parameters.ThermalC = EditorGUILayout.Slider("Material Constant", parameters.ThermalC, 0.1f, 5f);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawFiltersTab()
        {
            EditorGUILayout.LabelField("Gaussian Blur", EditorStyles.boldLabel);
            parameters.ApplyGaussianBlur = EditorGUILayout.Toggle("Enable", parameters.ApplyGaussianBlur);

            if (parameters.ApplyGaussianBlur)
            {
                EditorGUI.indentLevel++;
                parameters.BlurKernelSize = EditorGUILayout.IntSlider("Kernel Size", parameters.BlurKernelSize, 3, 15);

                if (parameters.BlurKernelSize % 2 == 0)
                {
                    parameters.BlurKernelSize++;
                }

                parameters.BlurSigma = EditorGUILayout.Slider("Sigma", parameters.BlurSigma, 0.1f, 10f);
                parameters.BlurPasses = EditorGUILayout.IntSlider("Passes", parameters.BlurPasses, 1, 5);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Laplacian Smoothing", EditorStyles.boldLabel);
            parameters.ApplyLaplacianSmooth = EditorGUILayout.Toggle("Enable", parameters.ApplyLaplacianSmooth);

            if (parameters.ApplyLaplacianSmooth)
            {
                EditorGUI.indentLevel++;
                parameters.SmoothPasses = EditorGUILayout.IntSlider("Passes", parameters.SmoothPasses, 1, 10);
                parameters.SmoothStrength = EditorGUILayout.Slider("Strength", parameters.SmoothStrength, 0.01f, 1f);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Falloff", EditorStyles.boldLabel);
            parameters.ApplyFalloff = EditorGUILayout.Toggle("Enable", parameters.ApplyFalloff);

            if (parameters.ApplyFalloff)
            {
                EditorGUI.indentLevel++;
                parameters.FalloffType = (FalloffType)EditorGUILayout.EnumPopup("Type", parameters.FalloffType);
                parameters.FalloffStrength = EditorGUILayout.Slider("Strength", parameters.FalloffStrength, 0f, 3f);
                parameters.FalloffPower = EditorGUILayout.Slider("Power", parameters.FalloffPower, 0.5f, 5f);
                parameters.FalloffOffset = EditorGUILayout.Slider("Offset", parameters.FalloffOffset, 0f, 0.9f);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawSplatmapTab()
        {
            EditorGUILayout.LabelField("Splatmap Generation", EditorStyles.boldLabel);
            splatmapGenerator.HeightWeight = EditorGUILayout.Slider(
                new GUIContent("Height Weight", "How sharply height bands separate. 0 = height ignored, higher = crisper bands."),
                splatmapGenerator.HeightWeight, 0f, 3f);
            splatmapGenerator.SlopeWeight = EditorGUILayout.Slider(
                new GUIContent("Slope Weight", "How strictly Max Slope is enforced. 0 = slope ignored, 1 = layers fade out over 10° past Max Slope."),
                splatmapGenerator.SlopeWeight, 0f, 3f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Biome Rules", EditorStyles.boldLabel);

            for (int i = 0; i < splatmapGenerator.Rules.Count; i++)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var rule = splatmapGenerator.Rules[i];
                    rule.Name = EditorGUILayout.TextField("Name", rule.Name);
                    rule.MinHeight = EditorGUILayout.Slider("Min Height", rule.MinHeight, 0f, 1f);
                    rule.MaxHeight = EditorGUILayout.Slider("Max Height", rule.MaxHeight, 0f, 1f);
                    rule.MaxSlope = EditorGUILayout.Slider(new GUIContent("Max Slope", "Steepest slope for this layer, in degrees on the terrain."), rule.MaxSlope, 0f, 90f);
                    rule.Color = EditorGUILayout.ColorField("Color", rule.Color);
                }
            }

            if (GUILayout.Button("Reset to Defaults"))
            {
                splatmapGenerator = new SplatmapGenerator();
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(currentHeightmap == null))
            {
                if (GUILayout.Button("Generate Splatmap"))
                {
                    Defer(GenerateSplatmap);
                }
            }

            if (currentHeightmap == null)
            {
                EditorGUILayout.HelpBox("Generate a heightmap first.", MessageType.None);
            }
        }

        private void DrawPresetsTab()
        {
            EditorGUILayout.LabelField("Built-in Presets", EditorStyles.boldLabel);

            for (int i = 0; i < presets.Count; i++)
            {
                bool isSelected = selectedPresetIndex == i;

                using (new EditorGUILayout.VerticalScope(isSelected ? EditorStyles.helpBox : GUIStyle.none))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool clicked = GUILayout.Toggle(isSelected, presets[i].Name, EditorStyles.boldLabel);

                        if (clicked && !isSelected)
                        {
                            selectedPresetIndex = i;
                        }
                    }

                    if (isSelected)
                    {
                        EditorGUILayout.LabelField($"{presets[i].Parameters.NoiseType} | Scale: {presets[i].Parameters.Scale} | Height: {presets[i].HeightRatio:P0} of width", EditorStyles.miniLabel);

                        if (GUILayout.Button("Apply Preset"))
                        {
                            // Presets describe the look; keep the user's resolution and seed.
                            int resolution = parameters.Resolution;
                            int seed = parameters.Seed;
                            ReleaseFieldFocus();
                            parameters = CloneParameters(presets[i].Parameters);
                            parameters.Resolution = resolution;
                            parameters.Seed = seed;
                            // Each preset suggests terrain proportions that suit it (gentle vs mountainous).
                            newTerrain.HeightRatio = presets[i].HeightRatio;
                            // Presets also bring splatmap colors that suit them (no snow in the desert).
                            splatmapGenerator.Rules = presets[i].CreateSplatRules();
                            PushToHistory();
                            statusMessage = $"Loaded: {presets[i].Name}";
                        }
                    }
                }

                EditorGUILayout.Space(2);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("User Presets", EditorStyles.boldLabel);

            for (int i = 0; i < userPresets.Count; i++)
            {
                if (userPresets[i] == null)
                {
                    continue;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(userPresets[i].name, EditorStyles.label);

                    if (GUILayout.Button("Load", GUILayout.Width(50)))
                    {
                        ReleaseFieldFocus();
                        parameters = CloneParameters(userPresets[i].Parameters);
                        PushToHistory();
                        statusMessage = $"Loaded user preset: {userPresets[i].name}";
                    }

                    if (GUILayout.Button("Delete", GUILayout.Width(55)))
                    {
                        UserPreset toDelete = userPresets[i];
                        Defer(() => DeleteUserPreset(toDelete));
                    }
                }
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Save Current as Preset"))
            {
                Defer(SaveUserPreset);
            }
        }

        private void DrawHistoryTab()
        {
            EditorGUILayout.LabelField("Parameter History", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Navigate through previous parameter states. Click any entry to restore it.", MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(historyIndex <= 0))
            {
                if (GUILayout.Button("◀ Undo"))
                {
                    UndoHistory();
                }
            }

            using (new EditorGUI.DisabledScope(historyIndex >= history.Count - 1))
            {
                if (GUILayout.Button("Redo ▶"))
                {
                    RedoHistory();
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            for (int i = history.Count - 1; i >= 0; i--)
            {
                bool isCurrent = (i == historyIndex);
                var entry = history[i];

                using (new EditorGUILayout.VerticalScope(isCurrent ? EditorStyles.helpBox : GUIStyle.none))
                {
                    string label = $"[{i}] {entry.NoiseType} | Scale: {entry.Scale} | Seed: {entry.Seed}";

                    if (isCurrent)
                    {
                        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                    }

                    else
                    {
                        if (GUILayout.Button(label, EditorStyles.label))
                        {
                            ReleaseFieldFocus();
                            parameters = CloneParameters(entry);
                            historyIndex = i;
                            Repaint();
                        }
                    }
                }

                EditorGUILayout.Space(1);
            }
        }

        // ═══════════════════════════════════════════════════
        //  MAIN PREVIEW AREA
        // ═══════════════════════════════════════════════════

        private void DrawMainArea()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                // Batch preview thumbnails
                if (batchResults.Count > 0)
                {
                    DrawBatchBar();
                }

                // The Splat toggle takes priority so a freshly generated splatmap is actually visible.
                if (showSplatmapPreview && splatmapPreview != null)
                {
                    DrawSplatmapPreviewArea();
                }

                else if (showPreview && currentHeightmap != null && currentHeightmap.Heights != null)
                {
                    DrawPreviewArea();
                }

                else
                {
                    DrawEmptyState();
                }

                // Custom gradient bar
                if (useCustomGradient)
                {
                    EditorGUILayout.Space(2);
                    EditorGUI.BeginChangeCheck();
                    previewGradient = EditorGUILayout.GradientField("Preview Gradient", previewGradient);

                    if (EditorGUI.EndChangeCheck())
                    {
                        previewDirty = true;
                    }
                }
            }
        }

        /// <summary>Returns the cached preview, re-rendering only when the data or display mode changed.</summary>
        private Texture2D GetPreviewTexture()
        {
            PreviewMode mode = useCustomGradient
                ? PreviewMode.Gradient
                : (previewColorMode ? PreviewMode.Color : PreviewMode.Grayscale);
            int key = (int)mode * 2 + (useGPUPreview ? 1 : 0);

            if (cachedPreview == null || previewDirty || cachedPreviewSource != currentHeightmap || cachedPreviewKey != key)
            {
                try
                {
                    cachedPreview = previewRenderer.Render(currentHeightmap, mode, previewGradient, useGPUPreview);
                }

                catch (Exception ex)
                {
                    Debug.LogWarning($"[OpenHeightmap] Preview failed: {ex.Message}");
                    cachedPreview = null;
                }

                cachedPreviewSource = currentHeightmap;
                cachedPreviewKey = key;
                previewDirty = false;
            }

            return cachedPreview;
        }

        private void DrawPreviewArea()
        {
            if (currentHeightmap.Heights == null)
            {
                DrawEmptyState();
                return;
            }

            EditorGUILayout.LabelField(
                $"Preview: {currentHeightmap.Width}x{currentHeightmap.Height} | Min: {currentHeightmap.MinHeight:F4} | Max: {currentHeightmap.MaxHeight:F4}",
                EditorStyles.miniLabel);

            previewScrollPos = EditorGUILayout.BeginScrollView(previewScrollPos, GUILayout.ExpandHeight(true));

            Texture2D previewTex = GetPreviewTexture();

            if (previewTex != null)
            {
                float availableWidth = position.width - 320;
                float availableHeight = position.height - 120;
                float texAspect = (float)previewTex.width / previewTex.height;

                float displayWidth = availableWidth * previewZoom;
                float displayHeight = displayWidth / texAspect;

                if (displayHeight > availableHeight)
                {
                    displayHeight = availableHeight;
                    displayWidth = displayHeight * texAspect;
                }

                previewRect = GUILayoutUtility.GetRect(displayWidth, displayHeight);
                GUI.DrawTexture(previewRect, previewTex, ScaleMode.ScaleToFit, false);
                HandlePreviewInteraction();
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Zoom:");
            previewZoom = EditorGUILayout.Slider(previewZoom, 0.2f, 3f);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawBatchBar()
        {
            EditorGUILayout.LabelField($"Batch Results ({batchResults.Count})", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();

            for (int i = 0; i < batchResults.Count; i++)
            {
                bool selected = (i == selectedBatchIndex);
                Rect r = GUILayoutUtility.GetRect(ThumbnailSize, ThumbnailSize, GUILayout.Width(ThumbnailSize));

                if (GUI.Button(r, GUIContent.none))
                {
                    SelectBatchResult(i);
                }

                if (i < batchThumbnails.Count && batchThumbnails[i] != null)
                {
                    GUI.DrawTexture(r, batchThumbnails[i], ScaleMode.ScaleToFit, false);
                }

                if (selected)
                {
                    DrawOutline(r, new Color(1f, 0.85f, 0f, 1f), 2f);
                }
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Clear", GUILayout.Width(50)))
            {
                ClearBatch();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }

        /// <summary>Draws a rectangle outline (used to highlight the selected batch thumbnail).</summary>
        private static void DrawOutline(Rect r, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, thickness), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y + r.height - thickness, r.width, thickness), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y, thickness, r.height), color);
            EditorGUI.DrawRect(new Rect(r.x + r.width - thickness, r.y, thickness, r.height), color);
        }

        private void DrawSplatmapPreviewArea()
        {
            EditorGUILayout.LabelField("Splatmap Preview (toggle 'Splat' off to see the heightmap)", EditorStyles.miniLabel);
            previewScrollPos = EditorGUILayout.BeginScrollView(previewScrollPos, GUILayout.ExpandHeight(true));

            if (splatmapPreview != null)
            {
                float availableWidth = position.width - 320;
                float availableHeight = position.height - 80;
                float texAspect = (float)splatmapPreview.width / splatmapPreview.height;
                float displayWidth = availableWidth * previewZoom;
                float displayHeight = displayWidth / texAspect;

                if (displayHeight > availableHeight)
                {
                    displayHeight = availableHeight; displayWidth = displayHeight * texAspect;
                }

                Rect r = GUILayoutUtility.GetRect(displayWidth, displayHeight);
                GUI.DrawTexture(r, splatmapPreview, ScaleMode.ScaleToFit, false);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawEmptyState()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.HelpBox("Click 'Generate' to create a heightmap.", MessageType.Info);
            GUILayout.FlexibleSpace();
        }

        /// <summary>Click on the preview to read the height under the cursor in the status bar.</summary>
        private void HandlePreviewInteraction()
        {
            if (Event.current.type == EventType.MouseDown && previewRect.Contains(Event.current.mousePosition))
            {
                float u = (Event.current.mousePosition.x - previewRect.x) / previewRect.width;
                float v = 1f - (Event.current.mousePosition.y - previewRect.y) / previewRect.height;
                u = Mathf.Clamp01(u);
                v = Mathf.Clamp01(v);
                float height = currentHeightmap.GetHeightBilinear(u, v);
                statusMessage = $"Position: ({u:F3}, {v:F3}) | Height: {height:F4}";
                Repaint();
            }
        }

        // ═══════════════════════════════════════════════════
        //  STATUS BAR
        // ═══════════════════════════════════════════════════

        private void DrawStatusBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(statusMessage, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();

                if (currentHeightmap != null)
                {
                    GUILayout.Label($"{currentHeightmap.Width}x{currentHeightmap.Height}", EditorStyles.miniLabel);
                    GUILayout.Label($"[{currentHeightmap.MinHeight:F4}, {currentHeightmap.MaxHeight:F4}]", EditorStyles.miniLabel);
                }
            }
        }

        // ═══════════════════════════════════════════════════
        //  ACTIONS
        // ═══════════════════════════════════════════════════

        /// <summary>Generates a heightmap from the current parameters and records it in history.</summary>
        private void GenerateHeightmap() => GenerateHeightmap(pushHistory: true);

        /// <summary>Generates a heightmap with a progress bar. source = settings to use (defaults to the current parameters).</summary>
        private void GenerateHeightmap(bool pushHistory, HeightmapGeneratorParameters source = null)
        {
            HeightmapGeneratorParameters toUse = source ?? parameters;

            isGenerating = true;
            statusMessage = "Generating...";

            try
            {
                generator.Progress = (stage, progress) =>
                    EditorUtility.DisplayProgressBar("OpenHeightmap", stage, progress);

                SetHeightmap(generator.Generate(toUse));
                lastGeneratedParameters = CloneParameters(toUse);
                ClearBatch();

                if (pushHistory)
                {
                    PushToHistory();
                }

                statusMessage = $"Generated {toUse.Resolution}x{toUse.Resolution}";
            }

            catch (Exception ex)
            {
                statusMessage = $"Failed: {ex.Message}";
                Debug.LogError($"[OpenHeightmap] {ex.Message}\n{ex.StackTrace}");
            }

            finally
            {
                generator.Progress = null;
                EditorUtility.ClearProgressBar();
                isGenerating = false;
                Repaint();
            }
        }

        /// <summary>Sets a new current heightmap and drops anything derived from the old one.</summary>
        private void SetHeightmap(HeightmapData heightmap)
        {
            currentHeightmap = heightmap;
            hadHeightmap = heightmap != null;
            previewDirty = true;
            ClearSplatmap();
        }

        /// <summary>Drops the splatmap and its preview (called whenever the heightmap changes).</summary>
        private void ClearSplatmap()
        {
            currentSplatmap = null;
            hadSplatmap = false;

            if (splatmapPreview != null)
            {
                DestroyImmediate(splatmapPreview);
                splatmapPreview = null;
            }
        }

        /// <summary>Generates the splatmap and its preview from the current heightmap.</summary>
        private void GenerateSplatmap()
        {
            if (currentHeightmap == null)
            {
                return;
            }

            // Real slope angles depend on the terrain's proportions: use the active terrain if there
            // is one, otherwise the size a new terrain would get.
            Terrain terrain = Terrain.activeTerrain;
            Vector3 size = terrain != null && terrain.terrainData != null
                ? terrain.terrainData.size
                : newTerrain.GetSize(currentHeightmap.Width);
            splatmapGenerator.TerrainHeightToWidth = size.x > 0f ? size.y / size.x : 0.6f;

            currentSplatmap = splatmapGenerator.Generate(currentHeightmap);

            if (splatmapPreview != null)
            {
                DestroyImmediate(splatmapPreview);
            }

            splatmapPreview = splatmapGenerator.GeneratePreviewTexture(currentHeightmap, currentSplatmap);
            showSplatmapPreview = true;
            hadSplatmap = true;
            statusMessage = "Splatmap generated";
        }

        private void GenerateBatch()
        {
            ClearBatch();
            isGenerating = true;
            statusMessage = $"Generating batch of {batchCount}...";

            try
            {
                for (int i = 0; i < batchCount; i++)
                {
                    EditorUtility.DisplayProgressBar("OpenHeightmap", $"Batch {i + 1} of {batchCount}", (float)i / batchCount);

                    var batchParams = CloneParameters(parameters);
                    batchParams.Seed = UnityEngine.Random.Range(0, 99999);
                    var result = generator.Generate(batchParams);

                    batchResults.Add(result);
                    batchSeeds.Add(batchParams.Seed);
                    batchThumbnails.Add(PreviewRenderer.CreateThumbnail(result, ThumbnailSize));
                }

                if (batchResults.Count > 0)
                {
                    SelectBatchResult(0);
                }

                statusMessage = $"Batch complete: {batchResults.Count} heightmaps generated";
            }

            catch (Exception ex)
            {
                statusMessage = $"Batch failed: {ex.Message}";
                Debug.LogError($"[OpenHeightmap] {ex.Message}");
            }

            finally
            {
                EditorUtility.ClearProgressBar();
                isGenerating = false;
                Repaint();
            }
        }

        /// <summary>Makes a batch result the current heightmap and syncs the Seed field to it.</summary>
        private void SelectBatchResult(int index)
        {
            if (index < 0 || index >= batchResults.Count)
            {
                return;
            }

            selectedBatchIndex = index;
            SetHeightmap(batchResults[index]);

            // Keep the seed in sync so Generate / history reproduce the chosen variation.
            ReleaseFieldFocus();
            parameters.Seed = batchSeeds[index];
            lastGeneratedParameters = CloneParameters(parameters);
            PushToHistory();
            statusMessage = $"Batch variation {index + 1} selected (seed {parameters.Seed})";
        }

        /// <summary>Removes all batch results and their thumbnails.</summary>
        private void ClearBatch()
        {
            batchResults.Clear();
            batchSeeds.Clear();
            ClearBatchThumbnails();
            selectedBatchIndex = -1;
        }

        /// <summary>Destroys the batch thumbnail textures.</summary>
        private void ClearBatchThumbnails()
        {
            foreach (var thumb in batchThumbnails)
            {
                if (thumb != null)
                {
                    DestroyImmediate(thumb);
                }
            }

            batchThumbnails.Clear();
        }

        private void ExportHeightmap()
        {
            if (currentHeightmap == null)
            {
                EditorUtility.DisplayDialog("No Heightmap", "Generate a heightmap first.", "OK");
                return;
            }

            string ext = exportFormat switch
            {
                ExportFormat.PNG => "png",
                ExportFormat.RAW16 => "raw",
                ExportFormat.EXR => "exr",
                _ => "png"
            };

            string path = HeightmapExporter.GetExportPath("Heightmap", ext);

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            bool ok = HeightmapExporter.Export(currentHeightmap, path, exportFormat);
            statusMessage = ok ? $"Exported {exportFormat}: {path}" : "Export failed (see Console)";
        }

        private void ExportSplatmap()
        {
            if (currentSplatmap == null)
            {
                EditorUtility.DisplayDialog("No Splatmap", "Generate a splatmap first.", "OK");
                return;
            }

            string path = HeightmapExporter.GetExportPath("Splatmap", "png");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            bool ok = HeightmapExporter.ExportSplatmap(currentSplatmap, path);
            statusMessage = ok ? $"Splatmap exported: {path}" : "Splatmap export failed (see Console)";
        }

        private void ApplyToTerrain()
        {
            var colors = new List<Color>();
            var names = new List<string>();

            foreach (var rule in splatmapGenerator.Rules)
            {
                colors.Add(rule.Color);
                names.Add(rule.Name);
            }

            string result = TerrainApplier.Apply(
                currentHeightmap,
                newTerrain,
                TerrainAssetFolder,
                "OpenHeightmap Terrain",
                currentSplatmap,
                colors,
                names);

            if (result != null)
            {
                statusMessage = result;
            }
        }

        /// <summary>Saves the current parameters as a UserPreset asset (asks for a path).</summary>
        private void SaveUserPreset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Preset", "NewHeightmapPreset", "asset", "Save heightmap preset"
            );

            if (!string.IsNullOrEmpty(path))
            {
                UserPreset.Create(path, CloneParameters(parameters));
                AssetDatabase.Refresh();
                RefreshUserPresets();
                statusMessage = "Preset saved";
            }
        }

        /// <summary>Deletes a UserPreset asset after confirming.</summary>
        private void DeleteUserPreset(UserPreset preset)
        {
            if (preset == null)
            {
                return;
            }

            if (EditorUtility.DisplayDialog("Delete Preset", $"Delete '{preset.name}'?", "Delete", "Cancel"))
            {
                string path = AssetDatabase.GetAssetPath(preset);
                AssetDatabase.DeleteAsset(path);
                RefreshUserPresets();
                statusMessage = "Preset deleted";
            }
        }

        // ═══════════════════════════════════════════════════
        //  HISTORY
        // ═══════════════════════════════════════════════════

        private void PushToHistory()
        {
            // Remove any redo states
            if (historyIndex < history.Count - 1)
            {
                history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
            }

            history.Add(CloneParameters(parameters));

            if (history.Count > MaxHistory)
            {
                history.RemoveAt(0);
            }

            historyIndex = history.Count - 1;
        }

        private void UndoHistory()
        {
            if (historyIndex > 0)
            {
                historyIndex--;
                ReleaseFieldFocus();
                parameters = CloneParameters(history[historyIndex]);
                Repaint();
            }
        }

        private void RedoHistory()
        {
            if (historyIndex < history.Count - 1)
            {
                historyIndex++;
                ReleaseFieldFocus();
                parameters = CloneParameters(history[historyIndex]);
                Repaint();
            }
        }

        // ═══════════════════════════════════════════════════
        //  UTILITIES
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// Drops keyboard focus from any text/number field. A focused IMGUI field keeps showing its own
        /// typed text, so without this a Seed changed by code (Regenerate, Random, presets, history)
        /// would still display the old value.
        /// </summary>
        private static void ReleaseFieldFocus()
        {
            GUIUtility.keyboardControl = 0;
            EditorGUIUtility.editingTextField = false;
        }

        private int PowerOfTwoPlusOnePopup(string label, int current)
        {
            int[] options = { 33, 65, 129, 257, 513, 1025, 2049, 4097 };
            string[] labels = { "33", "65", "129", "257", "513", "1025", "2049", "4097" };
            int selectedIndex = Array.IndexOf(options, current);

            if (selectedIndex < 0)
            {
                selectedIndex = 4;
            }

            selectedIndex = EditorGUILayout.Popup(label, selectedIndex, labels);
            return options[selectedIndex];
        }

        private HeightmapGeneratorParameters CloneParameters(HeightmapGeneratorParameters source)
        {
            string json = JsonUtility.ToJson(source);
            return JsonUtility.FromJson<HeightmapGeneratorParameters>(json);
        }

        private void RefreshUserPresets()
        {
            userPresets.Clear();
            string[] guids = AssetDatabase.FindAssets("t:UserPreset");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var preset = AssetDatabase.LoadAssetAtPath<UserPreset>(path);

                if (preset != null)
                {
                    userPresets.Add(preset);
                }
            }
        }
    }
}
