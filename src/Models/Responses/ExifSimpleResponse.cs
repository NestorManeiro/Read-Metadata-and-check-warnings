namespace DeepFakeDetector.Models.Responses;

public class ExifSimpleResponse
{
    public List<string> Warnings { get; set; } = new();
    public string? FechaHoraCaptura { get; set; }
    public string? Ubicacion { get; set; }

}