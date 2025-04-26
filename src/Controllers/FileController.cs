using Microsoft.AspNetCore.Mvc;
using DeepFakeDetector.Models.Responses;
using DeepFakeDetector.Services.Interfaces;
using DeepFakeDetector.Models.Requests;
using DeepFakeDetector.Utils;

namespace DeepFakeDetector.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FileController : ControllerBase
{
    private readonly IExifService _exifService;
    private readonly ILogger<FileController> _logger;

    public FileController(
        IExifService exifService,
        ILogger<FileController> logger)
    {
        _exifService = exifService;
        _logger = logger;
    }

    [HttpPost("analyze")]
    [RequestSizeLimit(500_000_000)]
    public async Task<IActionResult> AnalyzeFile([FromForm] FileUploadRequest request)
    {
        if (!FileValidator.IsValidFile(request.File))
        {
            return BadRequest("Tipo de archivo no soportado");
        }

        try
        {
            var exifData = await _exifService.ExtractExifData(request.File);

            // Usando operador ternario para seleccionar la respuesta
            return request.DetailedAnalysis
                ? Ok(BuildFullResponse(exifData))
                : Ok(BuildSimpleResponse(exifData));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando archivo: {FileName}", request.File.FileName);
            return StatusCode(500, "Error interno del servidor");
        }
    }

    private object BuildSimpleResponse(ExifResponse exifData) => new ExifSimpleResponse
    {
        Warnings = exifData.Warnings,
        FechaHoraCaptura = exifData.FechaHoraCaptura,
        Ubicacion = exifData.Metadata.GetValueOrDefault("GPS.GPSLatitude") != null
            ? $"{exifData.Metadata["GPS.GPSLatitude"]}, {exifData.Metadata["GPS.GPSLongitude"]}"
            : exifData.Metadata.GetValueOrDefault("XMP.Location") // Ejemplo de alternativa
    };

    private object BuildFullResponse(ExifResponse exifData) => exifData;

    [HttpGet("test")]
    public IActionResult Test() => Ok("API operativa");
}