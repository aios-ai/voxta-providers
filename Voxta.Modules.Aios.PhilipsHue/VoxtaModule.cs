using Voxta.Abstractions.Modules;
using Voxta.Abstractions.Registration;
using Voxta.Model.Shared;
using Voxta.Modules.Aios.PhilipsHue.ChatAugmentations;
using Voxta.Modules.Aios.PhilipsHue.Configuration;

namespace Voxta.Modules.Aios.PhilipsHue;

public class VoxtaModule : IVoxtaModule
{
    public const string ServiceName = "Aios.PhilipsHue";
    public const string AugmentationKey = "hue";
    
    public void Configure(IVoxtaModuleBuilder builder)
    {
        builder.Register(new()
        {
            ServiceName = ServiceName,
            Label = "Philips Hue",
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
            Notes = "Philips Hue augmentations.",
            HelpLink = "",
            Logo = ModuleLogo.EmbeddedResource(typeof(VoxtaModule), "Assets.PhilipsHue.png"),
            Augmentations = [AugmentationKey],
            ModuleConfigurationProviderType = typeof(ModuleConfigurationProvider),
            ModuleConfigurationFieldsRequiringReload = ModuleConfigurationProvider.FieldsRequiringReload,
        });
        
        builder.AddChatAugmentationsService<PhilipsHueChatAugmentationsService>(ServiceName);
    }
}
