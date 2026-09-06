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

    public Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(item);

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
            return Task.FromResult(Enumerable.Empty<RemoteImageInfo>());
        }

        if (File.Exists(directory))
        {
            directory = Path.GetDirectoryName(directory) ?? directory;
        }

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return Task.FromResult(Enumerable.Empty<RemoteImageInfo>());
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
                    break;
                }
            }
        }

        return Task.FromResult<IEnumerable<RemoteImageInfo>>(results);
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!url.StartsWith("imageprovider://local/", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            }

            var encodedPath = url.Substring("imageprovider://local/".Length);
            var path = Encoding.UTF8.GetString(Convert.FromBase64String(encodedPath));

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
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

            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error serving image from URL: {Url}", url);
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
            var files = Directory.GetFiles(directory, pattern);
            foreach (var file in files)
            {
                var extension = Path.GetExtension(file);
                if (!_supportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fileInfo = new FileInfo(file);
                if (fileInfo.Length <= 0)
                {
                    continue;
                }

                _logger.LogInformation("Found custom image: {ImageType} / {Path}", type, file);

                return new RemoteImageInfo
                {
                    Url = $"imageprovider://local/{Convert.ToBase64String(Encoding.UTF8.GetBytes(file))}",
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
