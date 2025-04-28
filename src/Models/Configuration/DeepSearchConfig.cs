namespace DeepFakeDetector.Models.Configuration
{
    public class DeepSearchConfig
    {
        public bool Enabled { get; set; }
        public List<string> BinaryPatterns { get; set; }
        public int MaxDepth { get; set; }
    }
}