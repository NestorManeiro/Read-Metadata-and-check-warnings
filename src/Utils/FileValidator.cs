namespace DeepFakeDetector.Utils;

public static class FileValidator
{
    private static readonly string[] AllowedExtensions =
        { ".jpg", ".jpeg", ".png", ".mp4", ".mov" };

    public static bool IsValidFile(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        return AllowedExtensions.Contains(extension);
    }
}
