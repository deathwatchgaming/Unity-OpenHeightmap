/*
 * Asset: OpenHeightmap
 * Filename: UserPreset.cs
 * Asset Version: 1.0
 * Unity Version: Unity 6+ (URP)
 * Author: DeathwatchGaming
 * License: MIT
 */

using UnityEngine;

namespace OpenHeightmap
{
    /// <summary>
    /// ScriptableObject that stores a named set of heightmap generation parameters.
    /// Created via Assets > Create > OpenHeightmap > Preset. Used for save/load
    /// of user-defined presets in the editor window.
    /// </summary>
    [CreateAssetMenu(fileName = "NewHeightmapPreset", menuName = "OpenHeightmap/Preset", order = 200)]
    public class UserPreset : ScriptableObject
    {
        public HeightmapGeneratorParameters Parameters = new HeightmapGeneratorParameters();

        /// <summary>
        /// Creates a UserPreset asset at the given path with the specified parameters.
        /// </summary>
        public static UserPreset Create(string assetPath, HeightmapGeneratorParameters parameters)
        {
            UserPreset preset = CreateInstance<UserPreset>();
            preset.Parameters = parameters;
            preset.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);

#if UNITY_EDITOR
            UnityEditor.AssetDatabase.CreateAsset(preset, assetPath);
            UnityEditor.AssetDatabase.SaveAssets();
#endif

            return preset;
        }

        /// <summary>
        /// Loads a UserPreset from the AssetDatabase by GUID or asset path.
        /// </summary>
        public static UserPreset Load(string assetPath)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<UserPreset>(assetPath);
#else
            return null;
#endif
        }
    }
}
