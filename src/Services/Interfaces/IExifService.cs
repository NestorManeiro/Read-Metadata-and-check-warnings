using DeepFakeDetector.Models.Responses;

namespace DeepFakeDetector.Services.Interfaces;

public interface IExifService
{
    Task<ExifResponse> ExtractExifData(IFormFile file);
    Task<bool> ValidateTemporalConsistency(IFormFile file);
    Task<Dictionary<string, string>> GetSoftwareSignatures(IFormFile file);
}
