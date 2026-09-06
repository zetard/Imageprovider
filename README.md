# Imageprovider

Jellyfin plugin that serves images from the media item's local folder. Designed for Posterizarr assets mounted inside the Jellyfin container.

## Supported image types

- **Movies:** `poster.*`, `cover.*` (Primary); `backdrop.*`, `fanart.*` (Backdrop)
- **Series:** `poster.*`, `cover.*` (Primary); `backdrop.*`, `fanart.*` (Backdrop)
- **Seasons:** `Season*.jpg`, `Season*.png` (Primary)
- **Episodes:** `thumb.*`, `episode.*` (Primary)

## Installation

Add the repository URL to Jellyfin Dashboard → Plugins → Repositories:

```
https://raw.githubusercontent.com/zetard/Imageprovider/main/manifest.json
```

Replace `YOUR_USERNAME` with your GitHub username.

## Files

- `/Movies/3 Idiots (2009) {imdb-tt1187043}/poster.jpg`
- `/Shows/House (2004) {imdb-tt0412142}/poster.jpg`
- `/Shows/House (2004) {imdb-tt0412142}/Season01.jpg`
- `/Shows/House (2004) {imdb-tt0412142}/backdrop.jpg`
