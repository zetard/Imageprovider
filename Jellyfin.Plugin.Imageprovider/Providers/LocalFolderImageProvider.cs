using System;
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

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        if (item is Movie or Series)
        {
            yield return ImageType.Primary;
            yield return ImageType.Backdrop;
        }
        else if (item is Season)
        {
            yield return ImageType.Primary;
        }
        else if (item is Episode)
        {
            yield return ImageType.Primary;
        }
    }

    public IEnumerable<LocalImageInfo> GetImages(BaseItem item, IDirectoryService directoryService)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(directoryService);

        _logger.LogInformation("GetImages called for {ItemType} {ItemPath}", item.GetType().Name, item.Path);

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
            _logger.LogWarning("Item has no path: {ItemType}", item.GetType().Name);
            yield break;
        }

        if (File.Exists(directory))
        {
            directory = Path.GetDirectoryName(directory) ?? directory;
            _logger.LogInformation("Resolved directory from file path: {Directory}", directory);
        }

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            _logger.LogWarning("Directory does not exist: {Directory}", directory);
            yield break;
        }

        _logger.LogInformation("Scanning directory: {Directory}", directory);

        var patterns = GetSearchPatterns(item).ToList();
        var foundImages = new List<LocalImageInfo>();

        try
        {
            foreach (var (type, patternList) in patterns)
            {
                foreach (var pattern in patternList)
                {
                    var image = FindImage(directoryService, directory, type, pattern);
                    if (image is not null)
                    {
                        foundImages.Add(image);
                        _logger.LogInformation("Added image for {ImageType}: {Path}", type, image.FileInfo.FullName);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning directory {Directory} for {ItemPath}", directory, item.Path);
        }

        foreach (var image in foundImages)
        {
            yield return image;
        }

        _logger.LogInformation("GetImages finished for {ItemPath}", item.Path);
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

    private LocalImageInfo? FindImage(IDirectoryService directoryService, string directory, ImageType type, string pattern)
    {
        try
        {
            _logger.LogInformation("Searching for {Pattern} in {Directory}", pattern, directory);

            var files = directoryService.GetFiles(directory);
            _logger.LogInformation("Directory contains {Count} files", files.Count());

            foreach (var file in files)
            {
                var fileName = file.Name;
                var extension = Path.GetExtension(fileName);

                if (!_supportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!IsMatch(fileName, pattern))
                {
                    continue;
                }

                if (file.Length <= 0)
                {
                    _logger.LogDebug("Skipping empty file: {File}", file.FullName);
                    continue;
                }

                _logger.LogInformation("Found custom image: {ImageType} / {Path}", type, file.FullName);

                return new LocalImageInfo
                {
                    FileInfo = file,
                    Type = type
                };
            }

            _logger.LogInformation("No match found for pattern {Pattern} in {Directory}", pattern, directory);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Error searching for images in {Directory} with pattern {Pattern}", directory, pattern);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied searching for images in {Directory} with pattern {Pattern}", directory, pattern);
        }

        return null;
    }

    private static bool IsMatch(string fileName, string pattern)
    {
        var patternParts = pattern.Split('*');
        if (patternParts.Length == 2)
        {
            var prefix = patternParts[0];
            var suffix = patternParts[1];
            return fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                   fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                   fileName.Length > prefix.Length + suffix.Length;
        }

        return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
