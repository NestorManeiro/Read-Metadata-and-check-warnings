using DeepFakeDetector.Models.Responses;
using Microsoft.Extensions.Configuration;

namespace DeepFakeDetector.Services;

public class TechnicalValidator
{
    private readonly IConfiguration _config;
    private readonly Dictionary<string, List<(int width, int height)>> _deviceResolutions;

    public TechnicalValidator(IConfiguration config)
    {
        _config = config;
        _deviceResolutions = ParseResolutionConfig();
    }

    private Dictionary<string, List<(int, int)>> ParseResolutionConfig()
    {
        var resolutions = _config.GetSection("TechnicalValidation:ExpectedResolutions")
            .Get<Dictionary<string, string>>();

        return resolutions?.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Split('|')
                .Select(s => s.Split('x'))
                .Select(parts => (int.Parse(parts[0]), int.Parse(parts[1])))
                .ToList()
        ) ?? new Dictionary<string, List<(int, int)>>();
    }

    public TechWarnings ValidateTechnicalMetadata(Dictionary<string, string> metadata, string fileType)
    {
        var warnings = new TechWarnings();

        ValidateResolution(metadata, warnings, fileType);
        ValidateCodecs(metadata, fileType, warnings);
        ValidateCompression(metadata, warnings);

        return warnings;
    }

    private void ValidateResolution(Dictionary<string, string> metadata, TechWarnings warnings, string fileType)
    {
        if (!metadata.TryGetValue("JPEG.Image Width", out var widthStr) ||
            !metadata.TryGetValue("JPEG.Image Height", out var heightStr))
            return;

        var width = int.Parse(widthStr.Split(' ')[0]);
        var height = int.Parse(heightStr.Split(' ')[0]);

        if (metadata.TryGetValue("Exif IFD0.Model", out var deviceModel))
        {
            if (_deviceResolutions.TryGetValue(deviceModel, out var expectedResolutions))
            {
                if (!expectedResolutions.Any(r => r.width == width && r.height == height))
                {
                    warnings.ResolutionIssues.Add(
                        $"Resolución {width}x{height} no coincide con configuraciones típicas para {deviceModel}. " +
                        $"Valores esperados: {string.Join(", ", expectedResolutions.Select(r => $"{r.width}x{r.height}"))}");
                }
            }
            else
            {
                warnings.ResolutionIssues.Add(
                    $"El dispositivo {deviceModel} no tiene perfiles de resolución configurados");
            }
        }

        if ((width * height) > 20_000_000 && fileType == "image/jpeg")
        {
            warnings.ResolutionIssues.Add("Resolución extremadamente alta para formato JPEG estándar");
        }
    }

    private void ValidateCodecs(Dictionary<string, string> metadata, string fileType, TechWarnings warnings)
    {
        var codecKey = fileType.StartsWith("video/") ? "Video Codec" : "Compression Type";

        if (metadata.TryGetValue($"JPEG.{codecKey}", out var codec))
        {
            if (fileType == "image/jpeg" && codec != "Baseline")
            {
                warnings.CodecIssues.Add($"Códec JPEG no estándar: {codec}");
            }

            if (metadata.ContainsKey("Huffman.Number of Tables") &&
                int.Parse(metadata["Huffman.Number of Tables"].Split(' ')[0]) > 4)
            {
                warnings.CompressionAnomalies.Add("Tablas Huffman excesivas para JPEG estándar");
            }
        }
    }

    private void ValidateCompression(Dictionary<string, string> metadata, TechWarnings warnings)
    {
        if (metadata.TryGetValue("Adobe JPEG.Color Transform", out var colorTransform))
        {
            if (colorTransform != "YCbCr")
            {
                warnings.CompressionAnomalies.Add(
                    $"Transformación de color inusual: {colorTransform} (Esperado: YCbCr)");
            }
        }

        if (metadata.TryGetValue("ICC Profile.Profile Description", out var profile))
        {
            if (!profile.Contains("sRGB"))
            {
                warnings.FormatMismatches.Add(
                    $"Espacio de color no estándar: {profile}");
            }
        }
    }
}
