using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Linq;

namespace DeepFakeDetector.Utils;

public static class ConfigParser
{
    public static Dictionary<string, List<(int width, int height)>> ParseResolutionConfig(IConfiguration config)
    {
        var section = config.GetSection("TechnicalValidation:ExpectedResolutions");
        var resolutions = section.Get<Dictionary<string, string>>();

        return resolutions?.ToDictionary(
            kvp => kvp.Key,
            kvp => ParseResolutionValues(kvp.Value)
        ) ?? new Dictionary<string, List<(int, int)>>();
    }

    public static (List<string> cameras, List<string> manufacturers) ParseBlacklist(IConfiguration config)
    {
        var blacklistSection = config.GetSection("TechnicalValidation:Blacklist");
        return (
            blacklistSection.GetSection("Cameras").Get<List<string>>() ?? new List<string>(),
            blacklistSection.GetSection("Manufacturers").Get<List<string>>() ?? new List<string>()
        );
    }

    private static List<(int, int)> ParseResolutionValues(string resolutionString)
    {
        return resolutionString.Split('|')
            .Select(s => s.Split('x'))
            .Where(parts => parts.Length == 2)
            .Select(parts => (int.Parse(parts[0]), int.Parse(parts[1])))
            .ToList();
    }
}
