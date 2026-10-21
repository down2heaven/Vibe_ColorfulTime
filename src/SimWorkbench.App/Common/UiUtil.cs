using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimWorkbench.Core.Logic;
using SimWorkbench.Core.Models;
using Windows.UI;

namespace SimWorkbench.App.Common;

/// <summary>界面层通用工具：颜色、日期组合、卡片与提示构建。</summary>
public static class UiUtil
{
    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

    public static SolidColorBrush BrushFromHex(string? hex)
    {
        var key = string.IsNullOrWhiteSpace(hex) ? "#8A8A8A" : hex.Trim();
        if (BrushCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var color = ParseColor(key);
        var brush = new SolidColorBrush(color);
        BrushCache[key] = brush;
        return brush;
    }

    public static Color ParseColor(string hex)
    {
        try
        {
            var text = hex.TrimStart('#');
            if (text.Length == 6)
            {
                return Color.FromArgb(255,
                    Convert.ToByte(text[..2], 16),
                    Convert.ToByte(text.Substring(2, 2), 16),
                    Convert.ToByte(text.Substring(4, 2), 16));
            }

            if (text.Length == 8)
            {
                return Color.FromArgb(
                    Convert.ToByte(text[..2], 16),
                    Convert.ToByte(text.Substring(2, 2), 16),
                    Convert.ToByte(text.Substring(4, 2), 16),
                    Convert.ToByte(text.Substring(6, 2), 16));
            }
        }
        catch
        {
            // 使用默认灰色
        }

        return Colors.Gray;
    }

    public static SolidColorBrush WithOpacity(string hex, byte alpha)
    {
        var color = ParseColor(hex);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    /// <summary>各功能模块的主题色（UWP 强调色风格，导航图标与页面标题统一使用）。</summary>
    public static string ModuleAccent(string moduleId) => moduleId switch
    {
        ModuleKeys.Workbench => "#0078D4",
        ModuleKeys.Tasks => "#2B88D8",
        ModuleKeys.Calendar => "#7A3FD1",
        ModuleKeys.DailySummary => "#12A5A5",
        ModuleKeys.TopicLog => "#C239B3",
        ModuleKeys.Checklist => "#2EA043",
        ModuleKeys.Settings => "#6B7280",
        _ => "#0078D4",
    };

    public static SolidColorBrush ModuleAccentBrush(string moduleId) => BrushFromHex(ModuleAccent(moduleId));

    /// <summary>Windows Phone 风格色块标题：左侧彩色方块 + 标题文本。</summary>
    public static FrameworkElement AccentHeader(string title, string colorHex, string? subtitle = null)
    {
        var panel = new StackPanel { Spacing = 2 };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(new Border
        {
            Width = 6,
            Height = 26,
            CornerRadius = new CornerRadius(3),
            Background = BrushFromHex(colorHex),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["PageTitleTextStyle"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(row);
        if (!string.IsNullOrEmpty(subtitle))
        {
            panel.Children.Add(new TextBlock
            {
                Text = subtitle,
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                Margin = new Thickness(16, 0, 0, 0),
            });
        }

        return panel;
    }

    /// <summary>Windows Phone 风格磁贴：纯色方块 + 图标 + 标题，用于首页快捷入口。</summary>
    public static Button LiveTile(string glyph, string title, string colorHex, RoutedEventHandler onClick,
        double width = 132, double height = 84)
    {
        var content = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 22,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.White),
            TextWrapping = TextWrapping.Wrap,
        });
        var button = new Button
        {
            Content = content,
            Width = width,
            Height = height,
            Padding = new Thickness(12, 10, 12, 10),
            Background = new SolidColorBrush(ParseColor(colorHex)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Click += onClick;
        return button;
    }

    /// <summary>卡片顶部的强调色条（让每个卡片有独立的主题色）。</summary>
    public static Border AccentStrip(string colorHex, double height = 4) => new()
    {
        Height = height,
        Background = new SolidColorBrush(ParseColor(colorHex)),
        CornerRadius = new CornerRadius(8, 8, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>带强调色色条的卡片：顶部色条 + 内容区。</summary>
    public static Border AccentCard(UIElement content, string colorHex)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var strip = AccentStrip(colorHex);
        Grid.SetRow(strip, 0);
        grid.Children.Add(strip);
        var body = new Border { Padding = new Thickness(16, 12, 16, 16), Child = content };
        Grid.SetRow(body, 1);
        grid.Children.Add(body);
        return new Border
        {
            Background = (SolidColorBrush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (SolidColorBrush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = grid,
        };
    }

    /// <summary>条目底部的状态颜色条（更醒目地表达任务状态）。</summary>
    public static Border StatusBar(string colorHex, double height = 5) => new()
    {
        Height = height,
        CornerRadius = new CornerRadius(3),
        Background = new SolidColorBrush(ParseColor(colorHex)),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Margin = new Thickness(0, 5, 0, 0),
    };
    public static string Greeting() => DateTime.Now.Hour switch
    {
        < 6 => "凌晨好",
        < 11 => "早上好",
        < 13 => "中午好",
        < 18 => "下午好",
        _ => "晚上好",
    };

    public static DateTime? GetDate(CalendarDatePicker picker) =>
        picker.Date is { } value ? value.LocalDateTime.Date : null;

    public static void SetDate(CalendarDatePicker picker, DateTime? value) =>
        picker.Date = value is null ? null : new DateTimeOffset(value.Value.Date);

    public static TimeSpan GetTime(TimePicker picker) =>
        picker.SelectedTime ?? picker.Time;

    public static void SetTime(TimePicker picker, TimeSpan value)
    {
        picker.SelectedTime = value;
        picker.Time = value;
    }

    /// <summary>把日期 + 时间控件组合为 DateTime?。</summary>
    public static DateTime? Combine(CalendarDatePicker datePicker, TimePicker timePicker)
    {
        if (GetDate(datePicker) is not { } date)
        {
            return null;
        }

        var time = GetTime(timePicker);
        return date.Date.AddHours(time.Hours).AddMinutes(time.Minutes);
    }

    /// <summary>拆分为日期/时间控件。</summary>
    public static void Split(DateTime? value, CalendarDatePicker datePicker, TimePicker timePicker)
    {
        SetDate(datePicker, value?.Date);
        SetTime(timePicker, value is { } dt ? dt.TimeOfDay : new TimeSpan(9, 0, 0));
    }

    public static Border Card(UIElement? child = null) => new()
    {
        Style = (Style)Application.Current.Resources["CardBorderStyle"],
        Child = child,
    };

    public static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["SectionTitleTextStyle"],
    };

    public static TextBlock Caption(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["CaptionTextStyle"],
    };

    public static TextBlock Body(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["BodyTextStyle"],
    };

    public static TextBlock EmptyHint(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        return block;
    }

    public static Border StatusPill(string text, string colorHex)
    {
        return new Border
        {
            Background = WithOpacity(colorHex, 40),
            BorderBrush = BrushFromHex(colorHex),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 1, 8, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = BrushFromHex(colorHex),
            },
        };
    }

    public static Border ColorDot(string colorHex, double size = 10) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 2),
        Background = BrushFromHex(colorHex),
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static StackPanel Horizontal(params UIElement[] children)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return panel;
    }

    public static Button MakeButton(string text, RoutedEventHandler? handler, bool accent = false)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(10, 5, 10, 5),
            MinWidth = 0,
        };
        if (accent)
        {
            button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        }

        if (handler is not null)
        {
            button.Click += handler;
        }

        return button;
    }

    public static Button MakeIconButton(string glyph, string tooltip, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 14 },
            Padding = new Thickness(6, 2, 6, 2),
            MinWidth = 0,
        };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += handler;
        return button;
    }

    public static string ShortText(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return flat.Length <= max ? flat : flat[..max] + "…";
    }

    public static DateTime? ParseLocalDate(object? value) =>
        value is DateTimeOffset offset ? offset.LocalDateTime.Date : null;

    public static string FormatDateText(DateTime date) =>
        $"{date:yyyy 年 M 月 d 日} {Presentation.FormatWeekDay(date)}";

    public static string FormatShortDate(DateTime date) => date.ToString("M 月 d 日");

    /// <summary>创建“模块已停用 / 不可用”的占位视图。</summary>
    public static FrameworkElement EmptyState(string title, string description)
    {
        var panel = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 460,
        };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = description,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        return new Grid { Children = { panel } };
    }
}
