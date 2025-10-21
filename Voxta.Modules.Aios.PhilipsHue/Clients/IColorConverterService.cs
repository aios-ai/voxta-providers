namespace Voxta.Modules.Aios.PhilipsHue.Clients;

public interface IColorConverterService
{
    string? TranslateColorNameToHex(string colorName);
}
