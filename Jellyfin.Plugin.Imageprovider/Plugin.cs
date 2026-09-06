using System.Globalization;
using Jellyfin.Plugin.Imageprovider.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Imageprovider;

public sealed class Plugin : BasePlugin<PluginConfiguration>
{
    public static readonly Guid PluginId = Guid.Parse("14d96703-b889-4c67-88db-1a2bfd1cbf19");

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public override string Name => "Imageprovider";

    public override Guid Id => PluginId;

    public static Plugin? Instance { get; private set; }
}
