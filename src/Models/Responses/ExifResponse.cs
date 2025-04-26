namespace DeepFakeDetector.Models.Responses;

public class ExifResponse : ExifSimpleResponse // Herencia para reutilizar propiedades
{
    public string FileName { get; set; }
    public string FileType { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
    public Dictionary<string, string> SoftwareSignatures { get; set; } = new();
    public bool TemporalValid { get; set; }
}