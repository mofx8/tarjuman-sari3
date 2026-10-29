using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace TarjumanSari3.App;

/// <summary>Small themed dialogs that follow the app's light/dark theme and interface language.</summary>
internal static class Dialogs
{
    /// <summary>Two-button question. Returns true when the primary button is pressed.</summary>
    public static bool Confirm(string title, string message, string yes, string no, bool rtl, Window? owner = null)
    {
        var result = false;
        var win = Build(title, message, rtl, owner);
        var panel = (StackPanel)((Grid)win.Content).Children[1];

        var yesBtn = MakeButton(yes, accent: true);
        var noBtn = MakeButton(no, accent: false);
        yesBtn.IsDefault = true; noBtn.IsCancel = true;
        yesBtn.Click += (_, _) => { result = true; win.Close(); };
        noBtn.Click += (_, _) => win.Close();
        panel.Children.Add(yesBtn); panel.Children.Add(noBtn);
        win.ShowDialog();
        return result;
    }

    public static void Info(string title, string message, string ok, bool rtl, Window? owner = null)
    {
        var win = Build(title, message, rtl, owner);
        var panel = (StackPanel)((Grid)win.Content).Children[1];
        var btn = MakeButton(ok, accent: true);
        btn.IsDefault = true; btn.IsCancel = true;
        btn.Click += (_, _) => win.Close();
        panel.Children.Add(btn);
        win.ShowDialog();
    }

    private static Window Build(string title, string message, bool rtl, Window? owner)
    {
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 400, FontSize = 14, Margin = new Thickness(0, 0, 0, 22) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var grid = new Grid { Margin = new Thickness(24, 22, 24, 18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(text, 0); Grid.SetRow(buttons, 1);
        grid.Children.Add(text); grid.Children.Add(buttons);

        return new Window
        {
            Title = title, Content = grid, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, Topmost = true,
            Owner = owner is { IsVisible: true } ? owner : null,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            FlowDirection = rtl ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
        };
    }

    private static Button MakeButton(string label, bool accent)
    {
        var b = new Button { Content = label, MinWidth = 110, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand };
        b.Margin = new Thickness(8, 0, 0, 0);
        if (accent && Application.Current.TryFindResource("AccentButtonStyle") is Style s) b.Style = s;
        return b;
    }
}

