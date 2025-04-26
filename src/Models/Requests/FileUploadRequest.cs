namespace DeepFakeDetector.Models.Requests;

public class FileUploadRequest
{
    public IFormFile File { get; set; }
    public bool DetailedAnalysis { get; set; } = false;
    public string? CustomMetadataFilters { get; set; }
}