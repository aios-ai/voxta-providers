using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Voxta.Abstractions.Modules;
using Voxta.Abstractions.Registration;
using Voxta.Model.Shared;
using Voxta.Modules.Aios.Spotify.ChatAugmentations;
using Voxta.Modules.Aios.Spotify.Clients.Services;
using Voxta.Modules.Aios.Spotify.Configuration;
using Voxta.Modules.Aios.Spotify.Controllers;

namespace Voxta.Modules.Aios.Spotify;

public class VoxtaModule : IVoxtaModule
{
    public const string ServiceName = "Aios.Spotify";
    public const string AugmentationKey = "spotify";
    
    public void Configure(IVoxtaModuleBuilder builder)
    {
        builder.Register(new()
        {
            ServiceName = ServiceName,
            Label = "Spotify",
            Experimental = true,
            CanBeInstalledByAdminsOnly = false,
            Supports = new()
            {
                { ServiceTypes.ChatAugmentations, ServiceDefinitionCategoryScore.Medium },
            },
            Pricing = ServiceDefinitionPricing.Medium,
            Hosting = ServiceDefinitionHosting.Online,
            SupportsExplicitContent = true,
            Recommended = true,
            Notes = "Spotify augmentations.",
            HelpLink = "",
            Logo = ModuleLogo.EmbeddedResource(typeof(VoxtaModule), "Assets.spotify.png"),
            Augmentations = [AugmentationKey],
            ModuleConfigurationProviderType = typeof(ModuleConfigurationProvider),
            ModuleConfigurationFieldsRequiringReload = ModuleConfigurationProvider.FieldsRequiringReload,
            ModuleTestingProviderType = typeof(ModuleTestingProvider),
        });
        
        builder.AddChatAugmentationsService<SpotifyChatAugmentationsService>(ServiceName);
        
        builder.Services.AddSingleton<ISpotifyManagerFactory, SpotifyManagerFactory>();
        builder.Services.AddSingleton<ISpotifyAuthCallbackManager, SpotifyAuthCallbackManager>();
        
        builder.Services.AddControllers().PartManager.ApplicationParts.Add(new AssemblyPart(typeof(SpotifyController).Assembly));
    }
}
