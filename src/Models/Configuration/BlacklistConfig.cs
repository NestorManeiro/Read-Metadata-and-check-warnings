namespace DeepFakeDetector.Models.Configuration
{
    public class BlacklistConfig
    {
        public List<string> Cameras { get; set; }
        public List<string> Manufacturers { get; set; }
        public List<string> SoftwareAgents { get; set; }
        public List<string> Keywords { get; set; }
        public DeepSearchConfig DeepSearch { get; set; }
    }
}