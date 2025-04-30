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
            // Read the file into a memory stream to extract metadata
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;
            var buffer = stream.ToArray();
            using var metadataStream = new MemoryStream(buffer);

            // Extract all metadata directories from the image
            var directories = ImageMetadataReader.ReadMetadata(metadataStream);

            var response = new ExifResponse
            {
                FileName = file.FileName,
                FileType = file.ContentType,
                Metadata = new Dictionary<string, string>(),
                Warnings = new List<string>()
            };

            // Iterate through all metadata tags and errors
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

            // Perform deep metadata extraction and various validations
            await ExtractDeepMetadata(buffer, response, file.ContentType);
            ExtractLocation(directories, response);
            ExtractCaptureDate(directories, response);
            ValidateEssentialMetadata(directories, response);
            ValidateResolution(directories, response);
            CheckBlacklist(response);
            CheckC2PAMetadata(directories, response);
            ValidateMetadataConsistency(directories, response);

            // New: Check for anomalous or suspicious metadata patterns
            CheckAnomalousMetadata(response);

            return response;
        }

        private void CheckAnomalousMetadata(ExifResponse response)
        {
            // 1. Suspicious ICC Profile Date/Time
            if (response.Metadata.TryGetValue("IccDirectory.Profile Date/Time", out string iccDate) && iccDate == "2016:01:01 00:00:00")
            {
                response.Warnings.Add("ICC profile date/time is 2016:01:01 00:00:00: This is commonly found in AI-generated images and does not match a recent capture.");
            }
            // 2. Generic Profile Date/Time (if present)
            if (response.Metadata.TryGetValue("Profile Date/Time", out string profileDate) && profileDate == "2016:01:01 00:00:00")
            {
                response.Warnings.Add("Profile date/time is exactly January 1, 2016: This default value is often used by AI; only recent photos should be accepted.");
            }
            // 3. Suspicious Profile Copyright
            if (response.Metadata.TryGetValue("IccDirectory.Profile Copyright", out string copyright) &&
                copyright.Contains("Google Inc. 2016", StringComparison.OrdinalIgnoreCase))
            {
                response.Warnings.Add("Profile copyright is 'Google Inc. 2016': This is common in Pixel phones but also frequently used in AI-generated images.");
            }
            // 4. Atypical resolution values
            bool hasResNone = response.Metadata.TryGetValue("JfifDirectory.Resolution Units", out string resUnits) && resUnits == "none";
            bool hasXRes = response.Metadata.TryGetValue("JfifDirectory.X Resolution", out string xRes) && xRes == "1 dot";
            bool hasYRes = response.Metadata.TryGetValue("JfifDirectory.Y Resolution", out string yRes) && yRes == "1 dot";
            if (hasResNone && hasXRes && hasYRes)
            {
                response.Warnings.Add("Atypical resolution values: 'Resolution Units': 'none', 'X Resolution': '1 dot', 'Y Resolution': '1 dot'. These do not usually appear in real camera photos.");
            }
        }

        private async Task ExtractDeepMetadata(byte[] buffer, ExifResponse response, string fileType)
        {
            // Detection of C2PA and SynthID signatures
            foreach (var signature in _config.Blacklist.Keywords)
            {
                if (response.Metadata.Any(kv =>
                    kv.Key.Contains(signature, StringComparison.OrdinalIgnoreCase) ||
                    kv.Value.Contains(signature, StringComparison.OrdinalIgnoreCase)))
                {
                    response.Metadata[$"AI_Signature.{signature}"] = "Detected";
                    response.Warnings.Add($"Possible AI content detected: {signature}");
                }
            }
            // If PNG and deep search enabled, analyze PNG chunks for suspicious data
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

                        // WARNING FOR CUSTOM CHUNK
                        if (chunkType != "IHDR" && chunkType != "IDAT" && chunkType != "IEND" && chunkType != "PLTE" && chunkType != "tEXt" && chunkType != "iTXt" && chunkType != "zTXt")
                        {
                            if (chunkType == "caBX" && chunkLength >= 60000)
                            {
                                response.Warnings.Add($"Anomalous PNG structure: Custom chunk '{chunkType}' of {chunkLength} bytes (unusual in natural images)");
                            }
                            else
                            {
                                response.Warnings.Add($"PNG structure: Custom chunk detected '{chunkType}' of {chunkLength} bytes");
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
                                    response.Metadata[$"PNG_Block.{chunkType}"] = $"Contains {pattern} data";
                                    response.Warnings.Add($"PNG block {chunkType} contains marker {pattern}");
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

        // New function for specific C2PA detection
        private void CheckC2PAMetadata(IReadOnlyList<Dir> directories, ExifResponse response)
        {
            const int IptcTagDigitalSourceType = 0x0237; // ID according to IPTC standard

            var iptcDir = directories.OfType<IptcDirectory>().FirstOrDefault();
            if (iptcDir != null && iptcDir.ContainsTag(IptcTagDigitalSourceType))
            {
                var digitalSource = iptcDir.GetDescription(IptcTagDigitalSourceType);
                if (!string.IsNullOrEmpty(digitalSource) && digitalSource.Contains("generativeAI", StringComparison.OrdinalIgnoreCase))
                {
                    response.Metadata["AI.GenerativeSource"] = digitalSource;
                    response.Warnings.Add($"Generative source detected: {digitalSource}");
                }
            }
        }

        // Improved to include GUID validation
        private void ValidateMetadataConsistency(IReadOnlyList<Dir> directories, ExifResponse response)
        {
            // Check for Midjourney GUID
            var xmpDir = directories.OfType<XmpDirectory>().FirstOrDefault();
            var guid = xmpDir?.XmpMeta?.Properties
                .FirstOrDefault(p => p.Path?.Contains("ImageGUID", StringComparison.OrdinalIgnoreCase) == true)?.Value;

            if (!string.IsNullOrEmpty(guid))
            {
                response.Metadata["AI.ImageGUID"] = guid;
                response.Warnings.Add($"Generated image GUID detected: {guid}");
            }

            // Validate consistency between metadata
            var hasCameraInfo = directories.OfType<ExifIfd0Directory>().Any(d =>
                d.ContainsTag(ExifDirectoryBase.TagMake) ||
                d.ContainsTag(ExifDirectoryBase.TagModel));

            var hasSoftwareTags = response.Metadata.Any(kv =>
                kv.Key.Contains("Software", StringComparison.OrdinalIgnoreCase));

            if (!hasCameraInfo && hasSoftwareTags)
            {
                response.Warnings.Add("Inconsistency detected: Camera metadata missing but software tags present");
            }
        }

        private void ValidateResolution(IReadOnlyList<Dir> directories, ExifResponse response)
        {
            var exifDir = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var width = exifDir?.GetInt32(ExifDirectoryBase.TagExifImageWidth);
            var height = exifDir?.GetInt32(ExifDirectoryBase.TagExifImageHeight);

            if (width == null || height == null) return;

            // Standard aspect ratios according to technical sources
            var validRatios = new[] { "3:2", "4:3", "1:1", "5:4", "16:9" };
            var currentRatio = AspectRatioSimplificado(width.Value, height.Value);

            bool isRatioValido = validRatios.Contains(currentRatio);
            bool tienePerfilCamara = HasValidCameraProfile(directories);

            if (!isRatioValido && !tienePerfilCamara)
            {
                response.Warnings.Add($"Unusual aspect ratio for cameras: {currentRatio} ({width}x{height})");
            }
            else if (isRatioValido && !tienePerfilCamara)
            {
                response.Warnings.Add($"Ratio {currentRatio} is common but no valid camera profile found");
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
            // Extract GPS location from metadata if available
            var gpsDir = directories.OfType<GpsDirectory>().FirstOrDefault();
            var location = gpsDir?.GetGeoLocation();

            response.Ubicacion = location != null
                ? $"{location.Latitude:0.000000},{location.Longitude:0.000000}"
                : "Not available";
        }

        private void ExtractCaptureDate(IReadOnlyList<Dir> directories, ExifResponse response)
        {
            // Try to extract original capture date from EXIF, fallback to file modification date
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
                response.FechaHoraCaptura = fileDate ?? "Not available";
            }
        }

        private void ValidateEssentialMetadata(IReadOnlyList<Dir> directories, ExifResponse response)
        {
            // Check for the presence of all critical EXIF fields configured
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
                    response.Warnings.Add($"Missing critical metadata: {field}");
                    continue;
                }

                var tag = directory.Tags.FirstOrDefault(t =>
                    t.Name.Replace("/", "").Replace(" ", "")
                        .Equals(expectedTag, StringComparison.OrdinalIgnoreCase));

                if (tag == null || string.IsNullOrEmpty(tag.Description))
                {
                    response.Warnings.Add($"Missing critical metadata: {field}");
                }
                else
                {
                    response.Warnings.Add($"Critical metadata verified: {field} - {tag.Description}");
                }
            }
        }

        // Updated to include AI camera manufacturers
        private void CheckBlacklist(ExifResponse response)
        {
            var model = response.Metadata.FirstOrDefault(kv => kv.Key == "ExifIfd0Directory.Model").Value;
            if (!string.IsNullOrWhiteSpace(model) && _config.Blacklist.Cameras.Contains(model))
            {
                response.Warnings.Add($"Blacklisted camera: {model}");
            }

            // Detection of specific AI models
            var aiSoftwarePatterns = new[] { "Stable Diffusion", "DALL-E", "Midjourney", "Firefly" };
            foreach (var pattern in aiSoftwarePatterns)
            {
                if (response.Metadata.Any(kv => kv.Value.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
                {
                    response.Warnings.Add($"AI software detected: {pattern}");
                }
            }
        }

        public async Task<bool> ValidateTemporalConsistency(IFormFile file)
        {
            // Validate if the image has a valid capture date
            var exifData = await ExtractExifData(file);
            return !string.IsNullOrEmpty(exifData.FechaHoraCaptura) &&
                   exifData.FechaHoraCaptura != "Not available";
        }

        public async Task<Dictionary<string, string>> GetSoftwareSignatures(IFormFile file)
        {
            // Retrieve all software-related metadata tags
            var exifData = await ExtractExifData(file);
            return exifData.Metadata
                .Where(kv => kv.Key.Contains("Software", StringComparison.OrdinalIgnoreCase) ||
                             kv.Key.Contains("Processing", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }
    }
}
