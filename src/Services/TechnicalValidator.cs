using DeepFakeDetector.Models.Responses;
using DeepFakeDetector.Utils;
using Microsoft.Extensions.Configuration;

namespace DeepFakeDetector.Services;

public class TechnicalValidator
{
    private readonly IConfiguration _config;
    private readonly Dictionary<string, List<(int width, int height)>> _deviceResolutions;
    private readonly List<string> _bannedCameras;
    private readonly List<string> _bannedManufacturers;

    public TechnicalValidator(IConfiguration config)
    {
        _config = config;
        _deviceResolutions = ConfigParser.ParseResolutionConfig(_config);

        var (cameras, manufacturers) = ConfigParser.ParseBlacklist(_config);
        _bannedCameras = cameras;
        _bannedManufacturers = manufacturers;
    }
    public TechWarnings ValidateTechnicalMetadata(Dictionary<string, string> metadata, string fileType)
    {
        var warnings = new TechWarnings();

        ValidateBlacklist(metadata, warnings);
        ValidateResolution(metadata, warnings, fileType);
        ValidateCodecs(metadata, fileType, warnings);
        ValidateCompression(metadata, warnings);

        return warnings;
    }

    private void ValidateBlacklist(Dictionary<string, string> metadata, TechWarnings warnings)
    {
        var model = metadata.GetValueOrDefault("Exif IFD0.Model");
        var manufacturer = metadata.GetValueOrDefault("Exif IFD0.Make");

        // Detección de modelos de cámaras IA
        if (!string.IsNullOrEmpty(model) && _bannedCameras.Any(b => model.Contains(b, System.StringComparison.OrdinalIgnoreCase)))
        {
            warnings.BlacklistAlerts.Add($"Modelo de cámara en lista negra: {model}");
        }

        // Detección de fabricantes de IA
        if (!string.IsNullOrEmpty(manufacturer) && _bannedManufacturers.Any(m => manufacturer.Contains(m, System.StringComparison.OrdinalIgnoreCase)))
        {
            warnings.BlacklistAlerts.Add($"Fabricante en lista negra: {manufacturer}");
        }
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
