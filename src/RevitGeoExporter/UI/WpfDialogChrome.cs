using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RevitGeoExporter.UI;

internal static class WpfDialogChrome
{
    public static Brush WindowBackgroundBrush { get; } = CreateBrush(245, 246, 248);

    public static Brush CardBackgroundBrush { get; } = Brushes.White;

    public static Brush CardBorderBrush { get; } = CreateBrush(214, 219, 226);

    public static Brush MutedTextBrush { get; } = CreateBrush(97, 105, 118);

    public static Brush StatusBackgroundBrush { get; } = CreateBrush(247, 248, 250);

    public static Brush StatusWarningBackgroundBrush { get; } = CreateBrush(255, 248, 235);

    public static Brush StatusWarningBorderBrush { get; } = CreateBrush(230, 193, 105);

    public static Brush StatusTextBrush { get; } = CreateBrush(43, 52, 63);

    public static Brush StatusWarningTextBrush { get; } = CreateBrush(140, 75, 0);

    public static Border CreateCard(Thickness? padding = null)
    {
        return new Border
        {
            Background = CardBackgroundBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = padding ?? new Thickness(16),
        };
    }

    public static Border CreateSectionCard(TextBlock title, TextBlock description, UIElement content)
    {
        Border card = CreateCard();
        card.Margin = new Thickness(0, 0, 0, 12);

        StackPanel layout = new();
        StyleSectionTitle(title);
        StyleDescriptionText(description);
        layout.Children.Add(title);
        layout.Children.Add(description);

        if (content is FrameworkElement contentElement)
        {
            contentElement.Margin = new Thickness(0, 12, 0, 0);
        }

        layout.Children.Add(content);
        card.Child = layout;
        return card;
    }

    public static FrameworkElement CreateFieldBlock(TextBlock label, UIElement control, double bottomMargin = 12)
    {
        StackPanel block = new()
        {
            Margin = new Thickness(0, 0, 0, bottomMargin),
        };
        StyleFieldLabel(label);
        block.Children.Add(label);
        block.Children.Add(control);
        return block;
    }

    public static void StyleSectionTitle(TextBlock textBlock)
    {
        textBlock.FontSize = 15;
        textBlock.FontWeight = FontWeights.SemiBold;
        textBlock.Foreground = StatusTextBrush;
    }

    public static void StyleDescriptionText(TextBlock textBlock)
    {
        textBlock.Margin = new Thickness(0, 4, 0, 0);
        textBlock.Foreground = MutedTextBrush;
        textBlock.TextWrapping = TextWrapping.Wrap;
    }

    public static void StyleFieldLabel(TextBlock textBlock)
    {
        textBlock.Margin = new Thickness(0, 0, 0, 4);
        textBlock.Foreground = StatusTextBrush;
        textBlock.FontWeight = FontWeights.Medium;
    }

    public static void StyleExpanderHeader(TextBlock textBlock)
    {
        textBlock.Foreground = StatusTextBrush;
        textBlock.FontWeight = FontWeights.SemiBold;
    }

    public static void ConfigureInfoLine(TextBlock block, double bottomMargin = 4)
    {
        block.TextWrapping = TextWrapping.Wrap;
        block.Foreground = StatusTextBrush;
        block.Margin = new Thickness(0, 0, 0, bottomMargin);
    }

    private static Brush CreateBrush(byte red, byte green, byte blue)
    {
        return new SolidColorBrush(Color.FromRgb(red, green, blue));
    }
}
