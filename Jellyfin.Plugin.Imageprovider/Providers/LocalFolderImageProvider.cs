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
        var supported = item is Movie or Series or Season or Episode;
        _logger.LogDebug("Supports called for {ItemType} {ItemPath}: {Supported}", item.GetType().Name, item.Path, supported);
        return supported;
    }

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        _logger.LogDebug("GetSupportedImages called for {ItemType} {ItemPath}", item.GetType().Name, item.Path);

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
                _logger.LogDebug("Using series path for season: {Directory}", directory);
            }
            else if (!string.IsNullOrEmpty(item.Path))
            {
                directory = item.Path;
                _logger.LogDebug("Using item path: {Directory}", directory);
            }
            else
            {
                _logger.LogWarning("Item has no path: {ItemType}", item.GetType().Name);
                return Enumerable.Empty<RemoteImageInfo>();
            }

            if (File.Exists(directory))
            {
                directory = Path.GetDirectoryName(directory) ?? directory;
                _logger.LogDebug("Item path is a file, using parent directory: {Directory}", directory);
            }

            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                _logger.LogWarning("Directory does not exist: {Directory}", directory);
                return Enumerable.Empty<RemoteImageInfo>();
            }

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

            _logger.LogInformation("GetImages returning {Count} images for {ItemPath}", results.Count, item.Path);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetImages for {ItemPath}", item.Path);
            return Enumerable.Empty<RemoteImageInfo>();
        }
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _logger.LogInformation("GetImageResponse called for URL: {Url}", url);

            if (!url.StartsWith("imageprovider://local/", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Unexpected URL scheme: {Url}", url);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            }

            var encodedPath = url.Substring("imageprovider://local/".Length);
            _logger.LogDebug("Decoding path from Base64: {EncodedPath}", encodedPath);

            string path;
            try
            {
                path = Encoding.UTF8.GetString(FromUrlSafeBase64(encodedPath));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalid Base64 in URL: {Url}", url);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            }

            _logger.LogDebug("Decoded path: {Path}", path);

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                _logger.LogWarning("File not found: {Path}", path);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var fileBytes = File.ReadAllBytes(path);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(fileBytes)
            };

            var extension = Path.GetExtension(path).ToLowerInvariant();
            response.Content.Headers.ContentType = extension switch
            {
                ".png" => new System.Net.Http.Headers.MediaTypeHeaderValue("image/png"),
                ".webp" => new System.Net.Http.Headers.MediaTypeHeaderValue("image/webp"),
                _ => new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg")
            };

            _logger.LogInformation("Serving image: {Path} ({Length} bytes)", path, fileBytes.Length);
            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving image from URL: {Url}", url);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
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
            _logger.LogDebug("Searching for {Pattern} in {Directory}", pattern, directory);
            var files = Directory.GetFiles(directory, pattern);
            _logger.LogDebug("Found {Count} files matching {Pattern}", files.Length, pattern);

            foreach (var file in files)
            {
                var extension = Path.GetExtension(file);
                if (!_supportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("Skipping unsupported extension: {File}", file);
                    continue;
                }

                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.Length <= 0)
                    {
                        _logger.LogDebug("Skipping empty file: {File}", file);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking file: {File}", file);
                    continue;
                }

                _logger.LogInformation("Found custom image: {ImageType} / {Path}", type, file);

                return new RemoteImageInfo
                {
                    Url = $"imageprovider://local/{ToUrlSafeBase64(file)}",
                    Type = type
                };
            }
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
