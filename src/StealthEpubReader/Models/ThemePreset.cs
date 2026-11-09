namespace StealthEpubReader.Models;

public class ThemePreset
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string BackgroundHex { get; set; } = "#1E1E1E";
    public string TextHex { get; set; } = "#6A9955";
    public double BackgroundOpacity { get; set; } = 0.85;
    public double TextOpacity { get; set; } = 0.88;
    public string? RecommendedFont { get; set; }
}
