using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Imageprovider.Providers;

public sealed class LocalFolderImageProvider : IRemoteImageProvider, IHasOrder
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

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(item);

        try
        {
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
                return Enumerable.Empty<RemoteImageInfo>();
            }

            if (File.Exists(directory))
            {
                directory = Path.GetDirectoryName(directory) ?? directory;
                _logger.LogInformation("Resolved directory from file path: {Directory}", directory);
            }

            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                _logger.LogWarning("Directory does not exist: {Directory}", directory);
                return Enumerable.Empty<RemoteImageInfo>();
            }

            _logger.LogInformation("Scanning directory: {Directory}", directory);

            var results = new List<RemoteImageInfo>();
            var patterns = GetSearchPatterns(item);

            foreach (var (type, patternList) in patterns)
            {
                foreach (var pattern in patternList)
                {
                    var image = FindImage(directory, type, pattern);
                    if (image is not null)
                    {
                        results.Add(image);
                        _logger.LogInformation("Added image for {ImageType}: {Url}", type, image.Url);
                        break;
                    }
                }
            }

            if (results.Count == 0)
            {
                _logger.LogInformation("No images found in item directory, trying assets directory fallback");
                var assetsDirectory = GetAssetsDirectory(item, directory);
                if (!string.IsNullOrEmpty(assetsDirectory) && Directory.Exists(assetsDirectory))
                {
                    _logger.LogInformation("Scanning assets directory: {Directory}", assetsDirectory);
                    foreach (var (type, patternList) in patterns)
                    {
                        foreach (var pattern in patternList)
                        {
                            var image = FindImage(assetsDirectory, type, pattern);
                            if (image is not null)
                            {
                                results.Add(image);
                                _logger.LogInformation("Added image for {ImageType} from assets: {Url}", type, image.Url);
                                break;
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogInformation("No assets directory found or directory does not exist");
                }
            }

            _logger.LogInformation("GetImages returning {Count} images for {ItemPath}", results.Count, item.Path);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetImages for {ItemPath}", item.Path);
            return Enumerable.Empty<RemoteImageInfo>();
        }
    }

    private string? GetAssetsDirectory(BaseItem item, string itemDirectory)
    {
        try
        {
            var folderName = new DirectoryInfo(itemDirectory).Name;
            string assetsRoot = "/assets";
            string? libraryFolder = item switch
            {
                Movie => "Movies",
                Series => "Shows",
                Season => "Shows",
                Episode => "Shows",
                _ => null
            };

            if (string.IsNullOrEmpty(libraryFolder))
            {
                return null;
            }

            var assetsDirectory = Path.Combine(assetsRoot, libraryFolder, folderName);
            _logger.LogInformation("Trying assets directory: {Directory}", assetsDirectory);
            return assetsDirectory;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error determining assets directory for {ItemPath}", item.Path);
            return null;
        }
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _logger.LogInformation("GetImageResponse called for URL: {Url}", url);

            byte[] fileBytes;
            string contentType;

            if (url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var base64Data = url.Substring(url.IndexOf("base64,", StringComparison.OrdinalIgnoreCase) + 7);
                fileBytes = Convert.FromBase64String(base64Data);
                contentType = url.Substring(5, url.IndexOf(";") - 5);
                _logger.LogInformation("Serving data URL image ({Length} bytes, {ContentType})", fileBytes.Length, contentType);
            }
            else if (url.StartsWith("imageprovider://local/", StringComparison.OrdinalIgnoreCase))
            {
                var encodedPath = url.Substring("imageprovider://local/".Length);
                string path;
                try
                {
                    path = Encoding.UTF8.GetString(FromUrlSafeBase64(encodedPath));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Invalid Base64 in URL: {Url}", url);
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                }

                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    _logger.LogWarning("File not found: {Path}", path);
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                fileBytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                contentType = Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".png" => "image/png",
                    ".webp" => "image/webp",
                    _ => "image/jpeg"
                };
                _logger.LogInformation("Serving image: {Path} ({Length} bytes)", path, fileBytes.Length);
            }
            else
            {
                _logger.LogWarning("Unexpected URL scheme: {Url}", url);
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(fileBytes)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving image from URL: {Url}", url);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
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

    private RemoteImageInfo? FindImage(string directory, ImageType type, string pattern)
    {
        try
        {
            _logger.LogInformation("Searching for {Pattern} in {Directory}", pattern, directory);

            var files = new DirectoryInfo(directory).EnumerateFiles();
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

                return new RemoteImageInfo
                {
                    ProviderName = Name,
                    Url = $"data:image/{GetImageMimeType(extension)};base64,{Convert.ToBase64String(File.ReadAllBytes(file.FullName))}",
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

    private static string GetImageMimeType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".png" => "png",
            ".webp" => "webp",
            _ => "jpeg"
        };
    }

    private static string ToUrlSafeBase64(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static byte[] FromUrlSafeBase64(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        switch (value.Length % 4)
        {
            case 2: value += "=="; break;
            case 3: value += "="; break;
        }
        return Convert.FromBase64String(value);
    }
}
