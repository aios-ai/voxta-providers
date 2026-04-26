namespace Voxta.Modules.Aios.PhilipsHue.Configuration;

public static class PhilipsHueAuthPath
{
    public static string GetUserAuthPath(string configuredAuthPath, Guid userId)
    {
        var authPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredAuthPath));
        if (!authPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("AuthPath must end with .json");

        return authPath[..^5] + $".{userId}.json";
    }
}
