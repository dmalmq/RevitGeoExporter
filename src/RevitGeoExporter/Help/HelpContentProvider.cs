using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using RevitGeoExporter.Resources;
using RevitGeoExporter.UI;

namespace RevitGeoExporter.Help;

public sealed class HelpContentProvider
{
    private readonly Assembly _assembly;

    public HelpContentProvider(Assembly? assembly = null)
    {
        _assembly = assembly ?? typeof(HelpContentProvider).Assembly;
    }

    public HelpDocument GetDocument(HelpTopic topic, HelpLanguage language)
    {
        string? html = LoadHtml(topic, language);
        if (!string.IsNullOrWhiteSpace(html))
        {
            return new HelpDocument
            {
                Topic = topic,
                Language = language,
                Title = GetTopicLabel(topic, language),
                Html = html!,
                IsFallback = false,
            };
        }

        string? englishHtml = language == HelpLanguage.English ? null : LoadHtml(topic, HelpLanguage.English);
        if (!string.IsNullOrWhiteSpace(englishHtml))
        {
            return new HelpDocument
            {
                Topic = topic,
                Language = HelpLanguage.English,
                Title = GetTopicLabel(topic, HelpLanguage.English),
                Html = englishHtml!,
                IsFallback = true,
            };
        }

        return new HelpDocument
        {
            Topic = topic,
            Language = language,
            Title = GetTopicLabel(topic, language),
            Html = BuildUnavailableHtml(topic, language),
            IsFallback = true,
        };
    }

    public IReadOnlyList<HelpTopic> GetTopicList(HelpLanguage language)
    {
        return Enum.GetValues(typeof(HelpTopic))
            .Cast<HelpTopic>()
            .OrderBy(topic => topic)
            .ToList();
    }

    public string GetTopicLabel(HelpTopic topic, HelpLanguage language)
    {
        UiLanguage uiLanguage = language == HelpLanguage.Japanese ? UiLanguage.Japanese : UiLanguage.English;
        return LocalizedTextProvider.Get(uiLanguage, $"Help.Topic.{topic}", topic.ToString());
    }

    public static HelpLanguage FromUiLanguage(UiLanguage language)
    {
        return language == UiLanguage.Japanese ? HelpLanguage.Japanese : HelpLanguage.English;
    }

    public static UiLanguage ToUiLanguage(HelpLanguage language)
    {
        return language == HelpLanguage.Japanese ? UiLanguage.Japanese : UiLanguage.English;
    }

    private string? LoadHtml(HelpTopic topic, HelpLanguage language)
    {
        string languageSegment = language == HelpLanguage.Japanese ? "ja" : "en";
        string resourceName = $"RevitGeoExporter.Resources.Help.{languageSegment}.{topic}.html";
        using Stream? stream = _assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return null;
        }

        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private string BuildUnavailableHtml(HelpTopic topic, HelpLanguage language)
    {
        UiLanguage uiLanguage = ToUiLanguage(language);
        string title = LocalizedTextProvider.Get(uiLanguage, "Help.Viewer.TopicUnavailableTitle", "Help topic unavailable");
        string body = LocalizedTextProvider.Get(
            uiLanguage,
            "Help.Viewer.TopicUnavailableBody",
            "The requested help topic could not be found in this add-in build.");
        return $@"<!DOCTYPE html>
<html>
<head>
  <meta charset=""utf-8"" />
  <title>{Escape(title)}</title>
  <style>body{{font-family:'Segoe UI',sans-serif;padding:20px;line-height:1.5;color:#333}} h1{{font-size:24px}}</style>
</head>
<body>
  <h1>{Escape(title)}</h1>
  <p>{Escape(body)}</p>
  <p><strong>{Escape(GetTopicLabel(topic, language))}</strong></p>
</body>
</html>";
    }

    private static string Escape(string value)
    {
        return (value ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
