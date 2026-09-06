using MediaBrowser.Providers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Imageprovider;

public class PosterizarrImageProvider : IImageProvider
{
    private readonly ILogger _logger;

    public PosterizarrImageProvider(ILogger logger)
    {
        _logger = logger;
    }

    public string Name => "Posterizarr";

    public int Order => 0;

    public bool Supports(IHasImages item)
    {
        return item is Movie || item is Series || item is Season || item is Episode;
    }

    public IReadOnlyList<ImageType> GetSupportedImages(IHasImages item)
    {
        if (item is Movie or Series)
        {
            return new[] { ImageType.Primary, ImageType.Backdrop };
        }
        if (item is Season)
        {
            return new[] { ImageType.Primary };
        }
        if (item is Episode)
        {
            return new[] { ImageType.Primary };
        }
        return new List<ImageType>();
    }

    public IEnumerable<ImageInfo> GetImages(IHasImages item, ImageType type, CancellationToken cancellationToken)
    {
        var results = new List<ImageInfo>();

        if (item is not BaseItem baseItem || string.IsNullOrEmpty(baseItem.Path))
        {
            return results;
        }

        string directory;
        if (item is Season season)
        {
            var series = season.Series;
            if (series == null || string.IsNullOrEmpty(series.Path))
            {
                return results;
            }
            directory = series.Path;
        }
        else
        {
            directory = baseItem.Path;
        }

        if (string.IsNullOrEmpty(directory))
        {
            return results;
        }

        if (File.Exists(directory))
        {
            directory = Path.GetDirectoryName(directory);
        }

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return results;
        }

        var patterns = GetSearchPatterns(type, item);

        foreach (var pattern in patterns)
        {
            try
            {
                var files = Directory.GetFiles(directory, pattern);
                foreach (var file in files)
                {
                    results.Add(new ImageInfo
                    {
                        Path = file,
                        Type = type
                    });
                }
            }
            catch (IOException ex)
            {
                _logger.ErrorException($"Error searching for images in {directory} with pattern {pattern}", ex);
            }
        }

        return results;
    }

    private IEnumerable<string> GetSearchPatterns(ImageType type, IHasImages item)
    {
        return type switch
        {
            ImageType.Primary when item is Movie or Series => new[] { "poster.*", "cover.*" },
            ImageType.Primary when item is Season => new[] { "Season*.jpg", "Season*.png", "season*.jpg", "season*.png" },
            ImageType.Primary when item is Episode => new[] { "thumb.*", "episode.*" },
            ImageType.Backdrop => new[] { "backdrop.*", "fanart.*", "-fanart.*" },
            _ => Enumerable.Empty<string>()
        };
    }
}
