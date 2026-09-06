using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Imageprovider.Providers;

public sealed class LocalFolderImageProvider : ILocalImageProvider, IHasOrder
{
    private static readonly string[] _supportedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly ILogger<LocalFolderImageProvider> _logger;

    public LocalFolderImageProvider(ILogger<LocalFolderImageProvider> logger)
    {
        _logger = logger;
    }

    public string Name => "Imageprovider";

    public int Order => -100;

    public bool Supports(BaseItem item)
    {
        return item is Movie or Series or Season or Episode;
    }

    public IEnumerable<LocalImageInfo> GetImages(BaseItem item, IDirectoryService directoryService)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(directoryService);

        var rootDirectory = Plugin.Instance?.Configuration.ImageRootDirectory;
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            yield break;
        }

        string directory;
        if (item is Season season && season.Series is { } series && !string.IsNullOrEmpty(series.Path))
        {
            directory = series.Path;
        }
        else if (!string.IsNullOrEmpty(item.Path))
        {
            directory = item.Path;
        }
        else
        {
            yield break;
        }

        if (File.Exists(directory))
        {
            directory = Path.GetDirectoryName(directory) ?? directory;
        }

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            yield break;
        }

        var patterns = GetSearchPatterns(item);
        foreach (var (type, patternList) in patterns)
        {
            foreach (var pattern in patternList)
            {
                var image = FindImage(directory, type, pattern, directoryService);
                if (image is not null)
                {
                    yield return image;
                    break;
                }
            }
        }
    }

    private IEnumerable<(ImageType Type, string[] Patterns)> GetSearchPatterns(BaseItem item)
    {
        if (item is Movie or Series)
        {
            yield return (ImageType.Primary, ["poster.*", "cover.*"]);
            yield return (ImageType.Backdrop, ["backdrop.*", "fanart.*", "-fanart.*"]);
        }
        else if (item is Season)
        {
            yield return (ImageType.Primary, ["Season*.jpg", "Season*.png", "season*.jpg", "season*.png"]);
        }
        else if (item is Episode)
        {
            yield return (ImageType.Primary, ["thumb.*", "episode.*"]);
        }
    }

    private LocalImageInfo? FindImage(string directory, ImageType type, string pattern, IDirectoryService directoryService)
    {
        try
        {
            var files = Directory.GetFiles(directory, pattern);
            foreach (var file in files)
            {
                var extension = Path.GetExtension(file);
                if (!_supportedExtensions.Contains(extension, System.StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fileInfo = directoryService.GetFile(file);
                if (fileInfo is null || fileInfo.IsDirectory || fileInfo.Length <= 0)
                {
                    continue;
                }

                _logger.LogInformation("Found custom image: {ImageType} / {Path}", type, file);

                return new LocalImageInfo
                {
                    FileInfo = fileInfo,
                    Type = type
                };
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Error searching for images in {Directory} with pattern {Pattern}", directory, pattern);
        }

        return null;
    }
}
