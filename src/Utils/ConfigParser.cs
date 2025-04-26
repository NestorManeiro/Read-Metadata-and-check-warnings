using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

namespace DeepFakeDetector.Utils
{
    public static class ConfigParser
    {
        public static Dictionary<string, List<(int width, int height)>> ParseResolutionConfig(IConfiguration config)
        {
            var section = config.GetSection("TechnicalValidation:ExpectedResolutions");
            var result = new Dictionary<string, List<(int width, int height)>>();

            foreach (var device in section.GetChildren())
            {
                var resList = new List<(int, int)>();
                var resStrings = device.Value.Split('|');
                foreach (var res in resStrings)
                {
                    var parts = res.Split('x');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                        resList.Add((w, h));
                }
                result[device.Key] = resList;
            }
            return result;
        }

        public static (
            List<string> cameras,
            List<string> manufacturers,
            List<string> softwareAgents,
            List<string> keywords
        ) ParseBlacklist(IConfiguration config)
        {
            var section = config.GetSection("TechnicalValidation:Blacklist");
            return (
                section.GetSection("Cameras").Get<List<string>>() ?? new List<string>(),
                section.GetSection("Manufacturers").Get<List<string>>() ?? new List<string>(),
                section.GetSection("SoftwareAgents").Get<List<string>>() ?? new List<string>(),
                section.GetSection("Keywords").Get<List<string>>() ?? new List<string>()
            );
        }
    }
}
