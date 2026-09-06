using MediaBrowser.Model.Plugins;
using Jellyfin.Plugin;
using Microsoft.Extensions.DependencyInjection;

namespace Imageprovider;

public class Plugin : BasePlugin, IHasServiceRegistration
{
    public const string PluginId = "Imageprovider";

    public Plugin(IApplicationPaths applicationPaths, IConfiguration configuration)
        : base(PluginId, applicationPaths, configuration)
    {
    }

    public void RegisterServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<IImageProvider, PosterizarrImageProvider>();
    }
}
