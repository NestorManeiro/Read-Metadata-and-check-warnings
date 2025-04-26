namespace DeepFakeDetector.Models.Responses;

public class TechWarnings
{
    private const string NoIssues = "Ninguna anomalía detectada";

    private List<string> _resolutionIssues = new();
    private List<string> _codecIssues = new();
    private List<string> _formatMismatches = new();
    private List<string> _compressionAnomalies = new();
    public List<string> _blacklistAlerts = new(); // Nueva propiedad


    public List<string> ResolutionIssues
    {
        get => _resolutionIssues.DefaultIfEmpty(NoIssues).ToList();
        set => _resolutionIssues = value;
    }

    public List<string> CodecIssues
    {
        get => _codecIssues.DefaultIfEmpty(NoIssues).ToList();
        set => _codecIssues = value;
    }

    public List<string> FormatMismatches
    {
        get => _formatMismatches.DefaultIfEmpty(NoIssues).ToList();
        set => _formatMismatches = value;
    }

    public List<string> CompressionAnomalies
    {
        get => _compressionAnomalies.DefaultIfEmpty(NoIssues).ToList();
        set => _compressionAnomalies = value;
    }

    public List<string> BlacklistAlerts
    {
        get => _blacklistAlerts.DefaultIfEmpty(NoIssues).ToList();
        set => _blacklistAlerts = value;
    }
}