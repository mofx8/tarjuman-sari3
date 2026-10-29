using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TarjumanSari3.Core.Translation;
using Orientation = System.Windows.Controls.Orientation;
using Color = System.Windows.Media.Color;

namespace TarjumanSari3.App;

/// <summary>Shows the same text translated by every available engine side by side, so you can pick the best one.</summary>
internal sealed class CompareWindow : Window
{
    private readonly AppServices _svc;
    private readonly TextBox _input = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, MaxHeight = 120, FlowDirection = System.Windows.FlowDirection.LeftToRight, Padding = new Thickness(8) };
    private readonly StackPanel _cards = new();
    private readonly TextBlock _status = new() { Opacity = 0.7, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _run = new() { Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
    private readonly string _source, _target;
    private readonly Action<string>? _use;
    private CancellationTokenSource _cts = new();

    public CompareWindow(AppServices svc, string text, string source, string target, Action<string>? use)
    {
        _svc = svc; _source = source; _target = target; _use = use;
        Title = Loc.T("مقارنة المحركات"); Width = 720; Height = 640; MinWidth = 460; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = Loc.English ? System.Windows.FlowDirection.LeftToRight : System.Windows.FlowDirection.RightToLeft;
        _input.Text = text; _run.Content = Ui.IconText(MahApps.Metro.IconPacks.PackIconLucideKind.Play, Loc.T("قارن"), 14);

        var header = new StackPanel { Margin = new Thickness(16, 16, 16, 8) };
        header.Children.Add(new TextBlock { Text = Loc.T("النص:"), Margin = new Thickness(0, 0, 0, 4) });
        header.Children.Add(_input); header.Children.Add(_run); header.Children.Add(_status);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _cards, Margin = new Thickness(16, 0, 16, 16) };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(header, 0); Grid.SetRow(scroll, 1);
        root.Children.Add(header); root.Children.Add(scroll);
        Content = root;

        _run.Click += async (_, _) => await RunAsync();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += async (_, _) => await RunAsync();
        Closed += (_, _) => _cts.Cancel();
    }

    private async Task RunAsync()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0) return;
        _cts.Cancel(); _cts = new CancellationTokenSource();
        _cards.Children.Clear(); _run.IsEnabled = false; _status.Text = Loc.T("جارٍ الترجمة…");
        try
        {
            var results = await _svc.Pool.CompareAsync(text, _source, _target, _cts.Token);
            if (results.Count == 0) { _status.Text = Loc.T("لا يوجد محرك متاح."); return; }
            foreach (var r in results.OrderBy(r => r.Text == null).ThenBy(r => r.Milliseconds)) _cards.Children.Add(Card(r));
            _status.Text = $"{results.Count(r => r.Text != null)}/{results.Count} " + Loc.T("محركات أجابت");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _status.Text = Loc.T("تعذّرت الترجمة: ") + ex.Message; }
        finally { _run.IsEnabled = true; }
    }

    private UIElement Card(EngineComparison r)
    {
        var ok = r.Text != null;
        var title = new TextBlock { FontWeight = FontWeights.SemiBold, Text = $"{r.Name}   ·   {r.Milliseconds} ms" };
        var body = new TextBlock
        {
            Text = ok ? r.Text : Loc.T("فشل: ") + r.Error, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), FontSize = 16,
            Opacity = ok ? 1 : 0.55, FlowDirection = _target is "ar" or "fa" or "ur" or "he" ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight,
        };
        var panel = new StackPanel();
        panel.Children.Add(title); panel.Children.Add(body);
        if (ok)
        {
            var copy = new Button { Content = Ui.IconText(MahApps.Metro.IconPacks.PackIconLucideKind.Copy, Loc.T("نسخ"), 14, 6), Padding = new Thickness(12, 3, 12, 3) };
            copy.Click += (_, _) => { try { Clipboard.SetText(r.Text!); } catch { } };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(copy);
            if (_use != null)
            {
                var use = new Button { Content = Ui.IconText(MahApps.Metro.IconPacks.PackIconLucideKind.Check, Loc.T("استخدم هذه"), 14, 6), Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0) };
                use.Click += (_, _) => { _use(r.Text!); Close(); };
                row.Children.Add(use);
            }
            panel.Children.Add(row);
        }
        return new Border
        {
            Child = panel, Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)),
            Background = new SolidColorBrush(Color.FromArgb(18, 128, 128, 128)),
        };
    }
}


