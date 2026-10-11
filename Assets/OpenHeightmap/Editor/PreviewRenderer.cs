/*
 * Asset: OpenHeightmap
 * Filename: PreviewRenderer.cs
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

using UnityEngine;

namespace OpenHeightmap
{
    /// <summary>How the preview colours heights.</summary>
    public enum PreviewMode { Color, Grayscale, Gradient }

    /// <summary>
    /// Renders HeightmapData previews. Callers should render once per change and reuse the returned
    /// texture (the window caches it); every method writes into a texture owned by this renderer.
    /// </summary>
    public class PreviewRenderer
    {
        private ComputeShader computeShader;
        private Material previewMaterial;
        private RenderTexture previewRT;
        private Texture2D previewTexture;
        private Texture2D readbackTexture;
        /// <summary>Float texture used to upload heights to the compute shader (reused between renders).</summary>
        private Texture2D heightUploadTexture;
        /// <summary>Reused pixel buffer for CPU previews (avoids allocating every render).</summary>
        private Color32[] pixelBuffer;
        /// <summary>Reused height buffer for GPU uploads.</summary>
        private float[] heightBuffer;

        public ComputeShader ComputeShader => computeShader;
        public Material PreviewMaterial => previewMaterial;

        public PreviewRenderer()
        {
            computeShader = FindAsset<ComputeShader>("Assets/OpenHeightmap/Shaders/PreviewCompute.compute", "PreviewCompute t:ComputeShader");
            Shader previewShader = FindAsset<Shader>("Assets/OpenHeightmap/Shaders/PreviewShader.shader", "PreviewShader t:Shader");

            if (previewShader != null)
            {
                previewMaterial = new Material(previewShader) { hideFlags = HideFlags.HideAndDontSave };
            }
        }

        /// <summary>Loads from the default path, or searches the project if the folder was moved.</summary>
        private static T FindAsset<T>(string defaultPath, string searchFilter) where T : Object
        {
            T asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(defaultPath);

            if (asset != null)
            {
                return asset;
            }

            foreach (string guid in UnityEditor.AssetDatabase.FindAssets(searchFilter))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);

                if (path.Contains("OpenHeightmap"))
                {
                    asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);

                    if (asset != null)
                    {
                        return asset;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Renders the heightmap in the requested mode. The GPU path is used for Color and Grayscale
        /// when requested and available; Gradient always uses the CPU (Unity Gradients are CPU-only).
        /// </summary>
        public Texture2D Render(HeightmapData heightmap, PreviewMode mode, Gradient gradient, bool useGpu)
        {
            if (heightmap == null || heightmap.Heights == null)
            {
                return null;
            }

            if (mode == PreviewMode.Gradient && gradient != null)
            {
                return RenderWithGradient(heightmap, gradient);
            }

            if (useGpu)
            {
                return RenderToTextureGPU(heightmap, mode == PreviewMode.Color);
            }

            return mode == PreviewMode.Grayscale ? RenderGrayscale(heightmap) : RenderToTexture(heightmap);
        }

        /// <summary>
        /// GPU-accelerated preview via compute shader. Uploads heights in one call, dispatches the
        /// kernel, and reads the result back once. Falls back to CPU if anything is unavailable.
        /// </summary>
        public Texture2D RenderToTextureGPU(HeightmapData heightmap, bool color = true)
        {
            if (computeShader == null || heightmap == null || heightmap.Heights == null || !SystemInfoSupportsCompute())
            {
                return color ? RenderToTexture(heightmap) : RenderGrayscale(heightmap);
            }

            int w = heightmap.Width;
            int h = heightmap.Height;

            try
            {
                if (previewRT == null || previewRT.width != w || previewRT.height != h)
                {
                    ReleaseRT();
                    previewRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    previewRT.enableRandomWrite = true;
                    previewRT.hideFlags = HideFlags.HideAndDontSave;
                    previewRT.Create();
                }

                if (!previewRT.IsCreated())
                {
                    return color ? RenderToTexture(heightmap) : RenderGrayscale(heightmap);
                }

                // Upload heights in one call (row-major y * w + x).
                if (heightUploadTexture == null || heightUploadTexture.width != w || heightUploadTexture.height != h)
                {
                    DestroyTex(ref heightUploadTexture);
                    heightUploadTexture = new Texture2D(w, h, TextureFormat.RFloat, false, true);
                    heightUploadTexture.hideFlags = HideFlags.HideAndDontSave;
                }

                EnsureBuffer(ref heightBuffer, w * h);

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        heightBuffer[y * w + x] = heightmap.Heights[x, y];
                    }
                }

                heightUploadTexture.SetPixelData(heightBuffer, 0);
                heightUploadTexture.Apply(false, false);

                int kernel = computeShader.FindKernel(color ? "CSMain" : "CSGeneratePreview");
                computeShader.SetTexture(kernel, "_Heightmap", heightUploadTexture);
                computeShader.SetTexture(kernel, "_Preview", previewRT);
                computeShader.SetInt("_Resolution", Mathf.Max(w, h));
                // Fixed 0-1 range so the GPU and CPU previews show the same colours for the same data.
                computeShader.SetFloat("_MinHeight", 0f);
                computeShader.SetFloat("_MaxHeight", 1f);

                computeShader.Dispatch(kernel, Mathf.CeilToInt(w / 8f), Mathf.CeilToInt(h / 8f), 1);

                if (readbackTexture == null || readbackTexture.width != w || readbackTexture.height != h)
                {
                    DestroyTex(ref readbackTexture);
                    readbackTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    readbackTexture.hideFlags = HideFlags.HideAndDontSave;
                }

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = previewRT;
                readbackTexture.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                readbackTexture.Apply();
                RenderTexture.active = previous;

                return readbackTexture;
            }

            catch
            {
                // GPU path failed — fall back to CPU
                return color ? RenderToTexture(heightmap) : RenderGrayscale(heightmap);
            }
        }

        /// <summary>True when the current graphics device can run compute shaders.</summary>
        private static bool SystemInfoSupportsCompute()
        {
            return SystemInfo.supportsComputeShaders;
        }

        /// <summary>CPU colour preview using the built-in terrain gradient.</summary>
        public Texture2D RenderToTexture(HeightmapData heightmap)
        {
            return RenderCpu(heightmap, h => HeightToEditorColor(h));
        }

        /// <summary>CPU grayscale preview.</summary>
        public Texture2D RenderGrayscale(HeightmapData heightmap)
        {
            return RenderCpu(heightmap, h =>
            {
                byte b = ToByte(h);
                return new Color32(b, b, b, 255);
            });
        }

        /// <summary>CPU preview coloured by a user Gradient.</summary>
        public Texture2D RenderWithGradient(HeightmapData heightmap, Gradient gradient)
        {
            if (gradient == null)
            {
                return RenderToTexture(heightmap);
            }

            return RenderCpu(heightmap, h => (Color32)gradient.Evaluate(h));
        }

        /// <summary>Shared CPU renderer: maps each height to a colour and uploads all pixels in one call.</summary>
        private Texture2D RenderCpu(HeightmapData heightmap, System.Func<float, Color32> colorFor)
        {
            if (heightmap == null || heightmap.Heights == null)
            {
                return null;
            }

            int w = heightmap.Width;
            int h = heightmap.Height;

            if (w <= 0 || h <= 0)
            {
                return null;
            }

            if (previewTexture == null || previewTexture.width != w || previewTexture.height != h)
            {
                DestroyTex(ref previewTexture);
                previewTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
                previewTexture.hideFlags = HideFlags.HideAndDontSave;
            }

            EnsureBuffer(ref pixelBuffer, w * h);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    pixelBuffer[y * w + x] = colorFor(Mathf.Clamp01(heightmap.Heights[x, y]));
                }
            }

            previewTexture.SetPixels32(pixelBuffer);
            previewTexture.Apply();
            return previewTexture;
        }

        /// <summary>
        /// Creates a small, independent colour thumbnail (the caller owns and must destroy it).
        /// Used for batch results so each thumbnail keeps its own image.
        /// </summary>
        public static Texture2D CreateThumbnail(HeightmapData heightmap, int size)
        {
            if (heightmap == null || heightmap.Heights == null || size <= 0)
            {
                return null;
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                float v = size > 1 ? (float)y / (size - 1) : 0f;

                for (int x = 0; x < size; x++)
                {
                    float u = size > 1 ? (float)x / (size - 1) : 0f;
                    pixels[y * size + x] = HeightToEditorColor(Mathf.Clamp01(heightmap.GetHeightBilinear(u, v)));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>Built-in terrain colour ramp (water, sand, grass, rock, snow). Matches PreviewCompute.compute.</summary>
        private static Color32 HeightToEditorColor(float h)
        {
            Color c;

            if (h < 0.1f)
            {
                c = Color.Lerp(new Color(0f, 0f, 0.3f), new Color(0f, 0.1f, 0.5f), h / 0.1f);
            }

            else if (h < 0.2f)
            {
                c = Color.Lerp(new Color(0f, 0.1f, 0.5f), new Color(0.1f, 0.4f, 0.7f), (h - 0.1f) / 0.1f);
            }

            else if (h < 0.3f)
            {
                c = Color.Lerp(new Color(0.76f, 0.70f, 0.50f), new Color(0.60f, 0.65f, 0.35f), (h - 0.2f) / 0.1f);
            }

            else if (h < 0.5f)
            {
                c = Color.Lerp(new Color(0.20f, 0.55f, 0.15f), new Color(0.15f, 0.40f, 0.10f), (h - 0.3f) / 0.2f);
            }

            else if (h < 0.7f)
            {
                c = Color.Lerp(new Color(0.45f, 0.40f, 0.35f), new Color(0.55f, 0.50f, 0.45f), (h - 0.5f) / 0.2f);
            }

            else if (h < 0.85f)
            {
                c = Color.Lerp(new Color(0.55f, 0.50f, 0.45f), new Color(0.65f, 0.62f, 0.60f), (h - 0.7f) / 0.15f);
            }

            else
            {
                c = Color.Lerp(new Color(0.65f, 0.62f, 0.60f), new Color(0.95f, 0.95f, 0.97f), (h - 0.85f) / 0.15f);
            }

            return c;
        }

        /// <summary>Converts 0-1 to a 0-255 byte.</summary>
        private static byte ToByte(float v)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
        }

        private static void EnsureBuffer<T>(ref T[] buffer, int length)
        {
            if (buffer == null || buffer.Length != length)
            {
                buffer = new T[length];
            }
        }

        /// <summary>Destroys a texture if it exists and clears the reference.</summary>
        private static void DestroyTex(ref Texture2D tex)
        {
            if (tex != null)
            {
                Object.DestroyImmediate(tex);
                tex = null;
            }
        }

        /// <summary>Releases and destroys the compute render texture.</summary>
        private void ReleaseRT()
        {
            if (previewRT != null)
            {
                previewRT.Release();
                Object.DestroyImmediate(previewRT);
                previewRT = null;
            }
        }

        public void Cleanup()
        {
            DestroyTex(ref previewTexture);
            DestroyTex(ref readbackTexture);
            DestroyTex(ref heightUploadTexture);
            ReleaseRT();

            if (previewMaterial != null)
            {
                Object.DestroyImmediate(previewMaterial);
                previewMaterial = null;
            }

            pixelBuffer = null;
            heightBuffer = null;
        }
    }
}
