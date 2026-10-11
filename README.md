# OpenHeightmap

A free Unity editor tool for generating terrain heightmaps. Pick a preset or set up the noise yourself, see the result in a live preview, then export it or apply it straight to a scene terrain.

![Mountain Range](Previews/Heightmap%20-%20Mountain%20Range.png)

## Features

- **6 noise types:** Perlin, Simplex, Voronoi, Worley, Value and Midpoint Displacement
- **Erosion:** hydraulic (droplet-based) and thermal
- **10 presets:** from Rolling Hills to Volcanic, each with a suggested terrain height and splatmap colors
- **Splatmaps:** terrain layers painted by height and slope
- **Apply to Terrain:** updates the scene terrain, or creates a correctly sized one
- **Export:** 16-bit PNG, RAW16 or EXR

## Requirements

- Unity 6 or newer (URP)

## Installation

1. Download or clone this repository.
2. Copy the `Assets/OpenHeightmap` folder into your project's `Assets` folder.
3. Open the tool from **Tools > OpenHeightmap** (Ctrl+Alt+H).

## Quick Start

1. Choose a preset in the **Presets** tab and click **Apply Preset**.
2. Click **Generate**.
3. Click **Apply to Terrain**, or **Export** to save the heightmap.

Generated terrains are saved to `Assets/OpenHeightmap Generated`, outside the tool folder, so updating the tool never touches them.

## Documentation

Full documentation, covering every setting, preset and the API, is included with the tool:
`Assets/OpenHeightmap/Documentation/Index.html` (open it in your browser).

- [Read Online](https://deathwatchgaming.github.io/Unity-OpenHeightmap/Assets/OpenHeightmap/Documentation/Index.html)

## Previews

See the [Previews](https://github.com/deathwatchgaming/Unity-OpenHeightmap/tree/main/Previews) folder for a rendered image of every preset.

## Companion Tool

[OpenStamper](https://github.com/deathwatchgaming/Unity-OpenStamper) layers craters, ridges, rivers and more onto your heightmaps. It requires OpenHeightmap.

## License

MIT. See [LICENSE](LICENSE).
