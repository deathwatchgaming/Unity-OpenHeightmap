/*
 * Asset: OpenHeightmap
 * Filename: HeightmapExporter.cs
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OpenHeightmap
{
    public enum ExportFormat { PNG, RAW16, EXR }

    public static class HeightmapExporter
    {
        /// <summary>
        /// Writes the heightmap to disk. Paths normally come from a save dialog, which already asks
        /// about overwriting, so no second prompt is shown unless confirmOverwrite is true.
        /// </summary>
        public static bool Export(HeightmapData heightmap, string filePath, ExportFormat format, bool confirmOverwrite = false)
        {
            if (confirmOverwrite && !ConfirmOverwrite(filePath)) 
            {
                return false;
            }

            try
            {
                string directory = Path.GetDirectoryName(filePath);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                bool success = format switch
                {
                    ExportFormat.PNG => ExportPNG(heightmap, filePath),
                    ExportFormat.RAW16 => ExportRAW16(heightmap, filePath),
                    ExportFormat.EXR => ExportEXR(heightmap, filePath),
                    _ => false
                };

                if (success)
                {
                    AssetDatabase.Refresh();
                    ConfigureImporter(filePath, format);
                    Debug.Log($"[OpenHeightmap] Exported {format}: {filePath} ({heightmap.Width}x{heightmap.Height})");
                }

                return success;
            }

            catch (Exception ex)
            {
                Debug.LogError($"[OpenHeightmap] Export failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// PNG: 16-bit grayscale (65,536 height levels instead of 256, so no terracing).
        /// Row 0 is the bottom of the image, matching the preview.
        /// </summary>
        public static bool ExportPNG(HeightmapData heightmap, string filePath)
        {
            int w = heightmap.Width;
            int h = heightmap.Height;
            Texture2D tex = new Texture2D(w, h, TextureFormat.R16, false, true);
            ushort[] data = new ushort[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float val = Mathf.Clamp01(heightmap.Heights[x, y]);
                    data[y * w + x] = (ushort)Mathf.RoundToInt(val * 65535f);
                }
            }

            tex.SetPixelData(data, 0);
            tex.Apply();
            File.WriteAllBytes(filePath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            return true;
        }

        /// <summary>
        /// RAW16: 16-bit unsigned integer heightmap, row-major order (bottom-to-top).
        /// Standard format for terrain heightmap import in Unity and most 3D engines.
        /// No header — just raw 16-bit values. File size = width * height * 2 bytes.
        /// </summary>
        public static bool ExportRAW16(HeightmapData heightmap, string filePath)
        {
            int w = heightmap.Width;
            int h = heightmap.Height;
            byte[] data = new byte[w * h * 2];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float val = Mathf.Clamp01(heightmap.Heights[x, y]);
                    ushort height16 = (ushort)Mathf.RoundToInt(val * 65535f);
                    int idx = (y * w + x) * 2;
                    data[idx] = (byte)(height16 & 0xFF);
                    data[idx + 1] = (byte)((height16 >> 8) & 0xFF);
                }
            }

            File.WriteAllBytes(filePath, data);
            return true;
        }

        /// <summary>
        /// EXR: 32-bit float per channel. Full precision heightmap for professional workflows.
        /// Uses Unity's built-in EXR encoder.
        /// </summary>
        public static bool ExportEXR(HeightmapData heightmap, string filePath)
        {
            int w = heightmap.Width;
            int h = heightmap.Height;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RFloat, false, true);
            float[] data = new float[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    data[y * w + x] = heightmap.Heights[x, y];
                }
            }

            tex.SetPixelData(data, 0);
            tex.Apply();
            byte[] exrData = tex.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);
            File.WriteAllBytes(filePath, exrData);
            UnityEngine.Object.DestroyImmediate(tex);
            return true;
        }

        /// <summary>
        /// Exports splatmap weights as a multi-channel texture (RGBA = 4 layers).
        /// For 5+ layers, exports multiple files (splat_0.png, splat_1.png).
        /// </summary>
        public static bool ExportSplatmap(float[,,] splatmap, string filePath)
        {

            try
            {
                string directory = Path.GetDirectoryName(filePath);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                int w = splatmap.GetLength(0);
                int h = splatmap.GetLength(1);
                int layers = splatmap.GetLength(2);

                string basePath = Path.ChangeExtension(filePath, null);
                string ext = Path.GetExtension(filePath);

                int channelsPerFile = 4;
                int fileCount = Mathf.CeilToInt((float)layers / channelsPerFile);

                // With 5+ layers the files get _splat0/_splat1 suffixes the save dialog never saw,
                // so ask before replacing any that already exist.
                if (fileCount > 1)
                {
                    for (int f = 0; f < fileCount; f++)
                    {
                        string candidate = $"{basePath}_splat{f}{ext}";

                        if (!ConfirmOverwrite(candidate))
                        {
                            return false;
                        }
                    }
                }

                for (int f = 0; f < fileCount; f++)
                {
                    Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    int startLayer = f * channelsPerFile;

                    for (int x = 0; x < w; x++)
                    {
                        for (int y = 0; y < h; y++)
                        {
                            Color c = Color.black;

                            for (int ch = 0; ch < channelsPerFile && (startLayer + ch) < layers; ch++)
                            {
                                c[ch] = splatmap[x, y, startLayer + ch];
                            }
                            
                            tex.SetPixel(x, y, c);
                        }
                    }

                    tex.Apply();
                    string outPath = fileCount > 1
                        ? $"{basePath}_splat{f}{ext}"
                        : filePath;
                    File.WriteAllBytes(outPath, tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);
                }

                AssetDatabase.Refresh();
                Debug.Log($"[OpenHeightmap] Splatmap exported: {fileCount} file(s) for {layers} layers");
                return true;
            }

            catch (Exception ex)
            {
                Debug.LogError($"[OpenHeightmap] Splatmap export failed: {ex.Message}");
                return false;
            }
        }

        public static string GetExportPath(string defaultName, string extension)
        {
            return EditorUtility.SaveFilePanel(
                "Export Heightmap",
                Application.dataPath,
                defaultName,
                extension
            );
        }

        private static bool ConfirmOverwrite(string filePath)
        {
            if (!File.Exists(filePath)) 
            {
                return true;
            }

            return EditorUtility.DisplayDialog(
                "Overwrite Existing File",
                $"A file already exists at:\n{filePath}\n\nDo you want to overwrite it?",
                "Overwrite",
                "Cancel"
            );
        }

        private static void ConfigureImporter(string filePath, ExportFormat format)
        {
            string assetPath = filePath;

            if (filePath.StartsWith(Application.dataPath))
            {
                assetPath = "Assets" + filePath.Substring(Application.dataPath.Length);
            }

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            
            if (importer == null) 
            {
                return;
            }

            switch (format)
            {
                case ExportFormat.PNG:
                case ExportFormat.EXR:
                    importer.textureType = TextureImporterType.SingleChannel;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.isReadable = true;
                    importer.sRGBTexture = false;
                    importer.mipmapEnabled = false;

                    // Single Channel textures read Alpha by default; the height lives in Red.
                    var settings = new TextureImporterSettings();
                    importer.ReadTextureSettings(settings);
                    settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
                    importer.SetTextureSettings(settings);

                    // Keep the full 2^n+1 size (Unity would otherwise rescale 513 to 512) and the
                    // full precision (16-bit for PNG, 32-bit float for EXR) instead of 8-bit.
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.maxTextureSize = 8192;

                    TextureImporterPlatformSettings platform = importer.GetDefaultPlatformTextureSettings();
                    platform.maxTextureSize = 8192;
                    platform.textureCompression = TextureImporterCompression.Uncompressed;
                    platform.format = format == ExportFormat.EXR ? TextureImporterFormat.RFloat : TextureImporterFormat.R16;
                    importer.SetPlatformTextureSettings(platform);
                    break;
                case ExportFormat.RAW16:
                    // RAW is not a texture format — no importer config needed
                    break;
            }

            importer.SaveAndReimport();
        }
    }
}
