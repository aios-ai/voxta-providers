using Microsoft.Extensions.DependencyInjection;
using Voxta.Abstractions.Modules;
using Voxta.Abstractions.Registration;
using Voxta.Model.Shared;
using Voxta.Modules.Aios.OpenWeather.ChatAugmentations;
using Voxta.Modules.Aios.OpenWeather.Clients;
using Voxta.Modules.Aios.OpenWeather.Configuration;

namespace Voxta.Modules.Aios.OpenWeather;

public class VoxtaModule : IVoxtaModule
{
    public const string ServiceName = "Aios.OpenWeather";
    public const string AugmentationKey = "openweather";
    
    public void Configure(IVoxtaModuleBuilder builder)
    {
        builder.Register(new()
        {
            ServiceName = ServiceName,
            Label = "Open Weather",
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
            Notes = "Open Weather augmentations.",
            HelpLink = "",
            Augmentations = [AugmentationKey],
            ModuleConfigurationProviderType = typeof(ModuleConfigurationProvider),
            ModuleConfigurationFieldsRequiringReload = ModuleConfigurationProvider.FieldsRequiringReload,
            ModuleTestingProviderType = typeof(ModuleTestingProvider),
        });
        
        builder.AddChatAugmentationsService<OpenWeatherChatAugmentationsService>(ServiceName);
        
        builder.Services.AddSingleton<IOpenWeatherClientFactory, OpenWeatherClientFactory>();
    }
}