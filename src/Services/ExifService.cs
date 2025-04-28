using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.FileSystem;
using DeepFakeDetector.Models.Responses;
using DeepFakeDetector.Services.Interfaces;
using DeepFakeDetector.Models.Configuration;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;
using Dir = MetadataExtractor.Directory;
using System.Linq;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.Xmp;

namespace DeepFakeDetector.Services
{
    public class ExifService : IExifService
    {
        private readonly TechnicalValidationConfig _config;

        public ExifService(IOptions<TechnicalValidationConfig> configOptions)
        {
            _config = configOptions.Value;
            if (_config == null) throw new ArgumentNullException(nameof(configOptions));
        }
        public async Task<ExifResponse> ExtractExifData(IFormFile file)
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;
            var buffer = stream.ToArray();
            using var metadataStream = new MemoryStream(buffer);

            var directories = ImageMetadataReader.ReadMetadata(metadataStream);

            var response = new ExifResponse
            {
                FileName = file.FileName,
                FileType = file.ContentType,
                Metadata = new Dictionary<string, string>(),
                Warnings = new List<string>()
            };

            foreach (var directory in directories)
            {
                foreach (var tag in directory.Tags)
                {
                    response.Metadata[$"{directory.GetType().Name}.{tag.Name}"] = tag.Description;
                }
                foreach (var error in directory.Errors)
                {
                    response.Metadata[$"{directory.GetType().Name}.Error"] = error;
                }
            }

            await ExtractDeepMetadata(buffer, response, file.ContentType);
            ExtractLocation(directories, response);
            ExtractCaptureDate(directories, response);
            ValidateEssentialMetadata(directories, response);
            ValidateResolution(directories, response);
            CheckBlacklist(response);
            CheckC2PAMetadata(directories, response); // Nueva función para C2PA
            ValidateMetadataConsistency(directories, response); // Mejora de consistencia

            return response;
        }
        private async Task ExtractDeepMetadata(byte[] buffer, ExifResponse response, string fileType)
        {
            // Detección de firmas C2PA y SynthID
            foreach (var signature in _config.Blacklist.Keywords)
            {
                if (response.Metadata.Any(kv =>
                    kv.Key.Contains(signature, StringComparison.OrdinalIgnoreCase) ||
                    kv.Value.Contains(signature, StringComparison.OrdinalIgnoreCase)))
                {
                    response.Metadata[$"AI_Signature.{signature}"] = "Detectado";
                    response.Warnings.Add($"Posible contenido IA detectado: {signature}");
                }
            }
            if (fileType.Contains("png", StringComparison.OrdinalIgnoreCase) && _config.Blacklist.DeepSearch.Enabled)
            {
                try
                {
                    int offset = 8;
                    int chunkNum = 0;
                    int maxDepth = _config.Blacklist.DeepSearch.MaxDepth;
                    while (offset < buffer.Length - 8 && offset < maxDepth)
                    {
                        int chunkLength = (buffer[offset] << 24) | (buffer[offset + 1] << 16) |
                                          (buffer[offset + 2] << 8) | buffer[offset + 3];
                        string chunkType = Encoding.ASCII.GetString(buffer, offset + 4, 4);

                        response.Metadata[$"PNG.Chunk{chunkNum}.Type"] = chunkType;
                        response.Metadata[$"PNG.Chunk{chunkNum}.Length"] = chunkLength.ToString();

                        // ADVERTENCIA POR CHUNK PERSONALIZADO
                        if (chunkType != "IHDR" && chunkType != "IDAT" && chunkType != "IEND" && chunkType != "PLTE" && chunkType != "tEXt" && chunkType != "iTXt" && chunkType != "zTXt")
                        {
                            if (chunkType == "caBX" && chunkLength >= 60000)
                            {
                                response.Warnings.Add($"Estructura PNG anómala: Chunk personalizado '{chunkType}' de {chunkLength} bytes (inusual en imágenes naturales)");
                            }
                            else
                            {
                                response.Warnings.Add($"Estructura PNG: Chunk personalizado detectado '{chunkType}' de {chunkLength} bytes");
                            }
                        }

                        if (chunkType == "iTXt" || chunkType == "tEXt")
                        {
                            string chunkData = Encoding.ASCII.GetString(
                                buffer, offset + 8, Math.Min(chunkLength, 100));
                            response.Metadata[$"PNG.Chunk{chunkNum}.Data"] = chunkData;

                            foreach (var pattern in _config.Blacklist.DeepSearch.BinaryPatterns)
                            {
                                if (chunkData.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                                {
                                    response.Metadata[$"PNG_Block.{chunkType}"] = $"Contiene datos {pattern}";
                                    response.Warnings.Add($"Bloque PNG {chunkType} contiene marcadores {pattern}");
                                }
                            }
                        }
                        offset += 12 + chunkLength;
                        chunkNum++;
                    }
                }
                catch (Exception ex)
                {
                    response.Metadata["PNG_Block.Error"] = ex.Message;
                }
            }

            await Task.CompletedTask;
        }

        // Nueva función para detección específica de C2PA
        private void CheckC2PAMetadata(IReadOnlyList<Dir> directories, ExifResponse response)
        {
            const int IptcTagDigitalSourceType = 0x0237; // ID según estándar IPTC

            var iptcDir = directories.OfType<IptcDirectory>().FirstOrDefault();
            if (iptcDir != null && iptcDir.ContainsTag(IptcTagDigitalSourceType))
            {
                var digitalSource = iptcDir.GetDescription(IptcTagDigitalSourceType);
                if (!string.IsNullOrEmpty(digitalSource) && digitalSource.Contains("generativeAI", StringComparison.OrdinalIgnoreCase))
                {
                    response.Metadata["AI.GenerativeSource"] = digitalSource;
                    response.Warnings.Add($"Fuente generativa detectada: {digitalSource}");
                }
            }
        }

        // Mejorada para incluir validación de GUID
        private void ValidateMetadataConsistency(IReadOnlyList<Dir> directories, ExifResponse response)
        {
            // Verificar GUID de Midjourney
            var xmpDir = directories.OfType<XmpDirectory>().FirstOrDefault();
            var guid = xmpDir?.XmpMeta?.Properties
                .FirstOrDefault(p => p.Path?.Contains("ImageGUID", StringComparison.OrdinalIgnoreCase) == true)?.Value;

            if (!string.IsNullOrEmpty(guid))
            {
                response.Metadata["AI.ImageGUID"] = guid;
                response.Warnings.Add($"GUID de imagen generada detectado: {guid}");
            }

            // Validar consistencia entre metadatos
            var hasCameraInfo = directories.OfType<ExifIfd0Directory>().Any(d =>
                d.ContainsTag(ExifDirectoryBase.TagMake) ||
                d.ContainsTag(ExifDirectoryBase.TagModel));

            var hasSoftwareTags = response.Metadata.Any(kv =>
                kv.Key.Contains("Software", StringComparison.OrdinalIgnoreCase));

            if (!hasCameraInfo && hasSoftwareTags)
            {
                response.Warnings.Add("Inconsistencia detectada: Metadatos de cámara ausentes con tags de software presentes");
            }
        }

        private void ValidateResolution(IReadOnlyList<Dir> directories, ExifResponse response)
        {
            var exifDir = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var width = exifDir?.GetInt32(ExifDirectoryBase.TagExifImageWidth);
            var height = exifDir?.GetInt32(ExifDirectoryBase.TagExifImageHeight);

            if (width == null || height == null) return;

            // Ratios estándar según fuentes técnicas [2][3][4][6]
            var validRatios = new[] { "3:2", "4:3", "1:1", "5:4", "16:9" };
            var currentRatio = AspectRatioSimplificado(width.Value, height.Value);

            bool isRatioValido = validRatios.Contains(currentRatio);
            bool tienePerfilCamara = HasValidCameraProfile(directories);

            if (!isRatioValido && !tienePerfilCamara)
            {
                response.Warnings.Add($"Relación de aspecto inusual para cámaras: {currentRatio} ({width}x{height})");
            }
            else if (isRatioValido && !tienePerfilCamara)
            {
                response.Warnings.Add($"Ratio {currentRatio} común pero sin perfil de cámara válido");
            }
        }

        private string AspectRatioSimplificado(int width, int height)
        {
            var gcd = GreatestCommonDivisor(width, height);
            return $"{width / gcd}:{height / gcd}";
        }

        private int GreatestCommonDivisor(int a, int b) => b == 0 ? a : GreatestCommonDivisor(b, a % b);

        private bool HasValidCameraProfile(IReadOnlyList<Dir> directories)
        {
            return directories.OfType<ExifIfd0Directory>().Any(d =>
                d.ContainsTag(ExifDirectoryBase.TagMake) &&
                d.ContainsTag(ExifDirectoryBase.TagModel));
        }


        private void ExtractLocation(IReadOnlyList<Dir> directories, ExifResponse response)
        {
            var gpsDir = directories.OfType<GpsDirectory>().FirstOrDefault();
            var location = gpsDir?.GetGeoLocation();

            response.Ubicacion = location != null
                ? $"{location.Latitude:0.000000},{location.Longitude:0.000000}"
                : "No disponible";
        }

        private void ExtractCaptureDate(IReadOnlyList<Dir> directories, ExifResponse response)
        {
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var dateString = subIfd?.GetDescription(ExifDirectoryBase.TagDateTimeOriginal);

            if (DateTime.TryParseExact(dateString, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                response.FechaHoraCaptura = date.ToString("yyyy-MM-dd HH:mm:ss");
            }
            else
            {
                var fileDir = directories.OfType<FileMetadataDirectory>().FirstOrDefault();
                var fileDate = fileDir?.GetDescription(FileMetadataDirectory.TagFileModifiedDate);
                response.FechaHoraCaptura = fileDate ?? "No disponible";
            }
        }

        private void ValidateEssentialMetadata(IReadOnlyList<Dir> directories, ExifResponse response)
        {
            foreach (var field in _config.CriticalExifFields)
            {
                var parts = field.Split('.');
                if (parts.Length != 2) continue;

                var dirKey = parts[0];
                var expectedTag = parts[1];

                var directory = directories.FirstOrDefault(d =>
                    d.Name.Replace(" ", "").Equals(dirKey, StringComparison.OrdinalIgnoreCase));

                if (directory == null)
                {
                    response.Warnings.Add($"Falta metadato crítico: {field}");
                    continue;
                }

                var tag = directory.Tags.FirstOrDefault(t =>
                    t.Name.Replace("/", "").Replace(" ", "")
                        .Equals(expectedTag, StringComparison.OrdinalIgnoreCase));

                if (tag == null || string.IsNullOrEmpty(tag.Description))
                {
                    response.Warnings.Add($"Falta metadato crítico: {field}");
                }
                else
                {
                    response.Warnings.Add($"Metadato crítico verificado: {field} - {tag.Description}");
                }
            }
        }
        // Actualizado para incluir fabricantes de IA
        private void CheckBlacklist(ExifResponse response)
        {
            var model = response.Metadata.FirstOrDefault(kv => kv.Key == "ExifIfd0Directory.Model").Value;
            if (!string.IsNullOrWhiteSpace(model) && _config.Blacklist.Cameras.Contains(model))
            {
                response.Warnings.Add($"Cámara en lista negra: {model}");
            }

            // Detección de modelos específicos de IA
            var aiSoftwarePatterns = new[] { "Stable Diffusion", "DALL-E", "Midjourney", "Firefly" };
            foreach (var pattern in aiSoftwarePatterns)
            {
                if (response.Metadata.Any(kv => kv.Value.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
                {
                    response.Warnings.Add($"Software de IA detectado: {pattern}");
                }
            }
        }

        public async Task<bool> ValidateTemporalConsistency(IFormFile file)
        {
            var exifData = await ExtractExifData(file);
            return !string.IsNullOrEmpty(exifData.FechaHoraCaptura) &&
                   exifData.FechaHoraCaptura != "No disponible";
        }

        public async Task<Dictionary<string, string>> GetSoftwareSignatures(IFormFile file)
        {
            var exifData = await ExtractExifData(file);
            return exifData.Metadata
                .Where(kv => kv.Key.Contains("Software", StringComparison.OrdinalIgnoreCase) ||
                             kv.Key.Contains("Processing", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }
    }
}
