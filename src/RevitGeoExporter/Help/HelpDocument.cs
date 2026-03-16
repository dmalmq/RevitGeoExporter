namespace RevitGeoExporter.Help;

public sealed class HelpDocument
{
    public HelpTopic Topic { get; set; }

    public HelpLanguage Language { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Html { get; set; } = string.Empty;

    public bool IsFallback { get; set; }
}
