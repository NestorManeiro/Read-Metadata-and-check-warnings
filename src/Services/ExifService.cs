using MetadataExtractor;
using DeepFakeDetector.Models.Responses;
using DeepFakeDetector.Services.Interfaces;
using Microsoft.AspNetCore.Http;

namespace DeepFakeDetector.Services;

public class ExifService : IExifService
{
    private readonly TechnicalValidator _technicalValidator;

    public ExifService(TechnicalValidator technicalValidator)
    {
        _technicalValidator = technicalValidator;
    }

    public async Task<ExifResponse> ExtractExifData(IFormFile file)
    {
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        stream.Position = 0;

        var directories = ImageMetadataReader.ReadMetadata(stream);

        var response = new ExifResponse
        {
            FileName = file.FileName,
            FileType = file.ContentType,
            Metadata = new Dictionary<string, string>()
        };

        foreach (var directory in directories)
        {
            foreach (var tag in directory.Tags)
            {
                response.Metadata[$"{directory.Name}.{tag.Name}"] = tag.Description;
            }
        }

        ValidateEssentialMetadata(response);
        ExtractLocation(response);
        ExtractCaptureDate(response);

        response.TechnicalWarnings = _technicalValidator.ValidateTechnicalMetadata(response.Metadata, file.ContentType);

        return response;
    }

    private void ExtractLocation(ExifResponse response)
    {
        response.Ubicacion = response.Metadata.TryGetValue("GPS.GPSLatitude", out var lat) &&
                            response.Metadata.TryGetValue("GPS.GPSLongitude", out var lon)
            ? $"{lat}, {lon}"
            : response.Metadata.GetValueOrDefault("XMP.Location") ?? "No disponible";
    }

    private void ExtractCaptureDate(ExifResponse response)
    {
        response.FechaHoraCaptura = response.Metadata.GetValueOrDefault("Exif SubIFD.DateTimeOriginal")
                                 ?? response.Metadata.GetValueOrDefault("ICC Profile.Profile Date/Time");
    }

    public async Task<bool> ValidateTemporalConsistency(IFormFile file)
    {
        var exifData = await ExtractExifData(file);
        return exifData.Metadata.ContainsKey("Exif SubIFD.DateTimeOriginal") &&
               DateTime.TryParse(exifData.Metadata["Exif SubIFD.DateTimeOriginal"], out _);
    }

    public async Task<Dictionary<string, string>> GetSoftwareSignatures(IFormFile file)
    {
        var exifData = await ExtractExifData(file);
        return exifData.Metadata
            .Where(kv => kv.Key.Contains("Software") || kv.Key.Contains("Processing"))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private void ValidateEssentialMetadata(ExifResponse response)
    {
        var criticalTags = new[] { "Exif IFD0.Make", "Exif IFD0.Model", "File.FileModifiedDate" };

        foreach (var tag in criticalTags.Where(t => !response.Metadata.ContainsKey(t)))
        {
            response.Warnings.Add($"Falta metadato crítico: {tag}");
        }
    }
}
