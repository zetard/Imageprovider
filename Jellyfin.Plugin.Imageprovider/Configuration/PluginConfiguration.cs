using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Imageprovider.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public PluginConfiguration()
    {
        ImageRootDirectory = "/custom-jellyfin-images";
    }

    public string ImageRootDirectory { get; set; }
}
