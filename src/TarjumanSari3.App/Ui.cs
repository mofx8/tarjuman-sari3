using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.IconPacks;
using Orientation = System.Windows.Controls.Orientation;

namespace TarjumanSari3.App;

/// <summary>Small helpers for building icon (Lucide) + text content in code.</summary>
internal static class Ui
{
    public static PackIconLucide Icon(PackIconLucideKind kind, double size = 16) =>
        new() { Kind = kind, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Icon followed by a label, for use as a button's Content.</summary>
    public static StackPanel IconText(PackIconLucideKind kind, string text, double size = 16, double gap = 8)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(Icon(kind, size));
        panel.Children.Add(new TextBlock { Text = text, Margin = new Thickness(gap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }
}
