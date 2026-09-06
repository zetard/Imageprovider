# Imageprovider

A Jellyfin plugin that serves local images from media item folders.

## Features

- Scans item directories for local image files
- Supports `poster.*`, `cover.*`, `backdrop.*`, `fanart.*`, `Season*.jpg/png`, `thumb.*`, `episode.*`
- Ideal for Posterizarr assets mounted inside the Jellyfin container
- Runs before Jellyfin's default local image provider (order -100)

## Installation

### From Plugin Repository

1. In Jellyfin, go to **Dashboard > Plugins > Repositories**
2. Add this URL: `https://raw.githubusercontent.com/zetard/Imageprovider/main/manifest.json`
3. Find **Imageprovider** in the catalog, install it, and restart Jellyfin

### Manual Installation

1. Build the plugin: `dotnet build Jellyfin.Plugin.Imageprovider.sln --configuration Release`
2. Copy `Jellyfin.Plugin.Imageprovider.dll` to your Jellyfin plugins directory
3. Restart Jellyfin

## Configuration

Open **Dashboard > Plugins > Imageprovider** to set the image root directory (default: `/custom-jellyfin-images`).

## Directory Layout

```
/custom-jellyfin-images/
├── Primary/
│   ├── tt1375666.jpg
│   └── 27205.png
├── Backdrop/
├── Logo/
└── Thumb/
```

## Build

Requires .NET 9 SDK and Jellyfin 10.11.11 or compatible.
