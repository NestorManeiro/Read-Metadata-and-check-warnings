using DeepFakeDetector.Models.Responses;
using DeepFakeDetector.Utils;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepFakeDetector.Services
{
    public class TechnicalValidator
    {
        private readonly IConfiguration _config;
        private readonly Dictionary<string, List<(int width, int height)>> _deviceResolutions;
        private readonly List<string> _bannedCameras;
        private readonly List<string> _bannedManufacturers;
        private readonly List<string> _bannedSoftwareAgents;
        private readonly List<string> _bannedKeywords;

        public TechnicalValidator(IConfiguration config)
        {
            _config = config;
            _deviceResolutions = ConfigParser.ParseResolutionConfig(_config);

            var (cameras, manufacturers, softwareAgents, keywords) = ConfigParser.ParseBlacklist(_config);
            _bannedCameras = cameras;
            _bannedManufacturers = manufacturers;
            _bannedSoftwareAgents = softwareAgents;
            _bannedKeywords = keywords;
        }

    }
}
