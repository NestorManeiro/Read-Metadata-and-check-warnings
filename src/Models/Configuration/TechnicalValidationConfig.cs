namespace DeepFakeDetector.Models.Configuration
{
    public class TechnicalValidationConfig
    {
        public Dictionary<string, string> ExpectedResolutions { get; set; }
        public BlacklistConfig Blacklist { get; set; }
        public List<string> CriticalExifFields { get; set; }
    }
}