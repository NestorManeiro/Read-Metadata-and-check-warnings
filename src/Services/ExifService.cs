using MetadataExtractor;
using DeepFakeDetector.Models.Responses;
using DeepFakeDetector.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DeepFakeDetector.Services
{
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

            // Extracción estándar de metadatos
            foreach (var directory in directories)
            {
                foreach (var tag in directory.Tags)
                {
                    response.Metadata[$"{directory.Name}.{tag.Name}"] = tag.Description;
                }

                // Capturar errores que pueden contener info valiosa sobre estructura interna
                foreach (var error in directory.Errors)
                {
                    response.Metadata[$"{directory.Name}.Error"] = error;
                }
            }

            // NUEVO: Buscar específicamente bloques C2PA/JUMD
            await DetectC2PABlocks(stream, response);

            ValidateEssentialMetadata(response);
            ExtractLocation(response);
            ExtractCaptureDate(response);
            return response;
        }

        // Nuevo método para detectar bloques C2PA
        private async Task DetectC2PABlocks(MemoryStream stream, ExifResponse response)
        {
            stream.Position = 0;
            byte[] buffer = stream.ToArray();

            // 1. Buscar marcadores C2PA en el contenido binario
            string fileContent = System.Text.Encoding.ASCII.GetString(buffer);

            // Buscar cadenas típicas de C2PA/JUMD/Sora
            string[] aiSignatures = new[] { "c2pa", "JUMD", "Sora", "trainedAlgorithm", "GPT", "OpenAI" };

            foreach (var signature in aiSignatures)
            {
                if (fileContent.IndexOf(signature, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    response.Metadata[$"AI_Signature.{signature}"] = "Detectado";
                    response.Warnings.Add($"Posible contenido IA detectado: {signature}");
                }
            }

            // 2. Buscar bloques específicos PNG que puedan contener C2PA
            try
            {
                // Para PNG, buscar bloques iTXt o tEXt con datos C2PA
                // Este es un enfoque simplificado, podría requerir una biblioteca especializada
                int offset = 8; // Saltar la cabecera PNG
                while (offset < buffer.Length - 8)
                {
                    // Leer tamaño y tipo del bloque
                    int chunkLength = (buffer[offset] << 24) | (buffer[offset + 1] << 16) |
                                      (buffer[offset + 2] << 8) | buffer[offset + 3];

                    string chunkType = System.Text.Encoding.ASCII.GetString(
                        buffer, offset + 4, 4);

                    // Verificar si es un bloque de texto (donde podría estar C2PA)
                    if (chunkType == "iTXt" || chunkType == "tEXt")
                    {
                        string chunkData = System.Text.Encoding.ASCII.GetString(
                            buffer, offset + 8, Math.Min(chunkLength, 100)); // Leer parte del contenido

                        if (chunkData.Contains("c2pa", StringComparison.OrdinalIgnoreCase) ||
                            chunkData.Contains("jumd", StringComparison.OrdinalIgnoreCase))
                        {
                            response.Metadata[$"PNG_Block.{chunkType}"] = "Contiene datos C2PA";
                            response.Warnings.Add($"Bloque PNG {chunkType} contiene marcadores C2PA");
                        }
                    }

                    // Avanzar al siguiente bloque
                    offset += 12 + chunkLength; // 8 bytes header + 4 bytes CRC + datos
                }
            }
            catch (Exception ex)
            {
                // Error al analizar estructura PNG
                response.Metadata["PNG_Block.Error"] = ex.Message;
            }
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
                   System.DateTime.TryParse(exifData.Metadata["Exif SubIFD.DateTimeOriginal"], out _);
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
            var baseCriticalTags = new[] {
        "Exif IFD0.Make",
        "Exif IFD0.Model",
        "File.FileModifiedDate"
    };

            // Solo verificar XMP si hay indicios de IA
            var xmpCriticalTags = new[] {
        "XMP-xmp:CreatorTool",
        "XMP-digitalsourcetype",
        "XMP-xmpMM:InstanceID"
    };

            foreach (var tag in baseCriticalTags.Where(t => !response.Metadata.ContainsKey(t)))
            {
                response.Warnings.Add($"Falta metadato crítico: {tag}");
            }

            // Verificar XMP solo si hay metadata de IA
            if (response.Metadata.Any(kv => kv.Key.Contains("C2PA", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var tag in xmpCriticalTags.Where(t => !response.Metadata.ContainsKey(t)))
                {
                    response.Warnings.Add($"Falta metadato crítico para IA: {tag}");
                }
            }

            // Detección robusta de C2PA/JUMD
            var c2paEvidence = response.Metadata
                .Where(kv => kv.Key.Contains("C2PA", StringComparison.OrdinalIgnoreCase) ||
                            kv.Key.Contains("JUMD", StringComparison.OrdinalIgnoreCase) ||
                            kv.Value.Contains("c2pa", StringComparison.OrdinalIgnoreCase))
                .Select(kv => $"[{kv.Key}] = {kv.Value}");

            if (c2paEvidence.Any())
            {
                response.Warnings.Add($"Evidencia C2PA detectada: {string.Join("; ", c2paEvidence)}");
            }
        }
    }
}
