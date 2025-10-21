using Microsoft.Extensions.Logging;
using System.Drawing;

namespace Voxta.Modules.Aios.PhilipsHue.Clients;

public class ColorConverterService : IColorConverterService
{
    private readonly ILogger<ColorConverterService> _logger;

    public ColorConverterService(ILogger<ColorConverterService> logger)
    {
        _logger = logger;
    }

    public string? TranslateColorNameToHex(string colorName)
    {
        try
        {
            var color = Color.FromName(colorName);

            if (color.ToArgb() != 0)
            {
                return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }
        }
        catch
        {
            _logger.LogWarning("Color name '{ColorName}' could not be translated.", colorName);
        }
        return null;
    }
}
