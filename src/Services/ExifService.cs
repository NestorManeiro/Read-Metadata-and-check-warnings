using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.FileSystem;
using DeepFakeDetector.Models.Responses;
using DeepFakeDetector.Services.Interfaces;
using System.Globalization;
using System.Text;
using Dir = MetadataExtractor.Directory;

namespace DeepFakeDetector.Services
{
    public class ExifService : IExifService
    {
        public ExifService() { }

        public async Task<ExifResponse> ExtractExifData(IFormFile file)
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            // Clonar el stream para evitar problemas de posición
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

            // Extraer todos los metadatos posibles
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

            // Extraer firmas IA y chunks si es PNG
            await ExtractDeepMetadata(buffer, response, file.ContentType);

            // Extraer ubicación GPS y fecha de captura
            ExtractLocation(directories, response);
            ExtractCaptureDate(directories, response);

            // Validar metadatos críticos
            ValidateEssentialMetadata(directories, response);

            return response;
        }

        private async Task ExtractDeepMetadata(byte[] buffer, ExifResponse response, string fileType)
        {
            // Buscar firmas IA en los metadatos, no en el binario crudo
            string[] aiSignatures = new[] { "c2pa", "JUMD", "Sora", "trainedAlgorithm", "GPT", "OpenAI" };

            // Buscar en los metadatos extraídos
            foreach (var signature in aiSignatures)
            {
                if (response.Metadata.Any(kv =>
                    kv.Key.Contains(signature, StringComparison.OrdinalIgnoreCase) ||
                    kv.Value.Contains(signature, StringComparison.OrdinalIgnoreCase)))
                {
                    response.Metadata[$"AI_Signature.{signature}"] = "Detectado";
                    response.Warnings.Add($"Posible contenido IA detectado: {signature}");
                }
            }

            // Si el archivo es PNG, buscar chunks especiales
            if (fileType.Contains("png", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    int offset = 8;
                    int chunkNum = 0;
                    while (offset < buffer.Length - 8)
                    {
                        int chunkLength = (buffer[offset] << 24) | (buffer[offset + 1] << 16) |
                                          (buffer[offset + 2] << 8) | buffer[offset + 3];
                        string chunkType = Encoding.ASCII.GetString(buffer, offset + 4, 4);

                        response.Metadata[$"PNG.Chunk{chunkNum}.Type"] = chunkType;
                        response.Metadata[$"PNG.Chunk{chunkNum}.Length"] = chunkLength.ToString();

                        // Si es texto, extraer parte del contenido
                        if (chunkType == "iTXt" || chunkType == "tEXt")
                        {
                            string chunkData = Encoding.ASCII.GetString(
                                buffer, offset + 8, Math.Min(chunkLength, 100));
                            response.Metadata[$"PNG.Chunk{chunkNum}.Data"] = chunkData;
                            if (chunkData.Contains("c2pa", StringComparison.OrdinalIgnoreCase) ||
                                chunkData.Contains("jumd", StringComparison.OrdinalIgnoreCase))
                            {
                                response.Metadata[$"PNG_Block.{chunkType}"] = "Contiene datos C2PA";
                                response.Warnings.Add($"Bloque PNG {chunkType} contiene marcadores C2PA");
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
                // Intentar con otras fuentes
                var fileDir = directories.OfType<FileMetadataDirectory>().FirstOrDefault();
                var fileDate = fileDir?.GetDescription(FileMetadataDirectory.TagFileModifiedDate);
                response.FechaHoraCaptura = fileDate ?? "No disponible";
            }
        }

        private void ValidateEssentialMetadata(IReadOnlyList<Dir> directories, ExifResponse response)
        {
            // Verificar metadatos críticos de cámara
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var make = ifd0?.GetDescription(ExifDirectoryBase.TagMake);
            var model = ifd0?.GetDescription(ExifDirectoryBase.TagModel);

            if (string.IsNullOrWhiteSpace(make) || string.IsNullOrWhiteSpace(model))
            {
                response.Warnings.Add("No se detectó información de cámara - posible imagen generada por IA");
            }

            // Verificar fecha de modificación de archivo
            var fileDir = directories.OfType<FileMetadataDirectory>().FirstOrDefault();
            if (fileDir == null || fileDir.GetDescription(FileMetadataDirectory.TagFileModifiedDate) == null)
            {
                response.Warnings.Add("Falta metadato crítico: File.FileModifiedDate");
            }

            // Verificar Make y Model explícitamente
            if (string.IsNullOrWhiteSpace(make))
                response.Warnings.Add("Falta metadato crítico: Exif IFD0.Make");
            if (string.IsNullOrWhiteSpace(model))
                response.Warnings.Add("Falta metadato crítico: Exif IFD0.Model");
        }

        // Implementación explícita de la interfaz
        async Task<bool> IExifService.ValidateTemporalConsistency(IFormFile file)
        {
            var exifData = await ExtractExifData(file);
            return !string.IsNullOrEmpty(exifData.FechaHoraCaptura) &&
                   exifData.FechaHoraCaptura != "No disponible";
        }

        // Implementación explícita de la interfaz  
        async Task<Dictionary<string, string>> IExifService.GetSoftwareSignatures(IFormFile file)
        {
            var exifData = await ExtractExifData(file);
            return exifData.Metadata
                .Where(kv => kv.Key.Contains("Software", StringComparison.OrdinalIgnoreCase) ||
                             kv.Key.Contains("Processing", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }
    }
}
