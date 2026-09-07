using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Imageprovider.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public Dictionary<string, string> PathMappings { get; set; } = new();
}
