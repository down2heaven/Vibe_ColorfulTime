using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimWorkbench.Core.Models;
using SimWorkbench.Data;
using Windows.UI;

namespace SimWorkbench.App.Dialogs;

/// <summary>状态新建/编辑：名称 + 颜色（调色板或自定义十六进制颜色）。</summary>
public sealed partial class StatusEditorDialog : ContentDialog
{
    private static readonly string[] Palette =
    {
        "#9AA0A6", "#3A7BFF", "#F58A1F", "#12A5A5", "#2EA043", "#D13438",
        "#8E5CD9", "#E3008C", "#00B7C3", "#498205", "#C239B3", "#986F0B",
        "#5D5A58", "#0078D4", "#8764B8", "#EF6950", "#7A7574", "#4C4A48",
    };

    private readonly WorkbenchServices _services;
    private readonly WorkStatus? _existing;
    private string _selectedColor = "#3A7BFF";

    public StatusEditorDialog(WorkbenchServices services, WorkStatus? existing, StatusScope scope)
    {
        _services = services;
        _existing = existing;
        InitializeComponent();
        Title = existing is null ? "新建状态" : "编辑状态";
        ScopeText.Text = scope == StatusScope.Simulation
            ? "仿真任务状态集合：修改名称/颜色后，任务列表、日历与详情会同步使用该颜色。"
            : "一般任务状态集合：可自定义通用状态（待办/进行中/已完成等）。";
        Scope = scope;
        _selectedColor = existing?.Color ?? "#3A7BFF";
        NameBox.Text = existing?.Name ?? string.Empty;
        BuildPalette();
        UpdatePreview();
        NameBox.TextChanged += (_, _) => UpdatePreview();
    }

    public StatusScope Scope { get; }

    public WorkStatus? ResultStatus { get; private set; }

    private void BuildPalette()
    {
        PaletteGrid.Children.Clear();
        PaletteGrid.ColumnDefinitions.Clear();
        PaletteGrid.RowDefinitions.Clear();
        for (var c = 0; c < 6; c++)
        {
            PaletteGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        for (var r = 0; r < 3; r++)
        {
            PaletteGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var i = 0; i < Palette.Length; i++)
        {
            var colorHex = Palette[i];
            var swatch = new Border
            {
                Width = 34,
                Height = 24,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Parse(colorHex)),
                BorderBrush = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(colorHex == _selectedColor ? 2 : 0),
                Tag = colorHex,
            };
            swatch.Tapped += OnSwatchTapped;
            ToolTipService.SetToolTip(swatch, colorHex);
            Grid.SetRow(swatch, i / 6);
            Grid.SetColumn(swatch, i % 6);
            PaletteGrid.Children.Add(swatch);
        }
    }

    private void OnSwatchTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is Border { Tag: string hex })
        {
            _selectedColor = hex;
            foreach (var child in PaletteGrid.Children)
            {
                if (child is Border border && border.Tag is string value)
                {
                    border.BorderThickness = new Thickness(value == _selectedColor ? 2 : 0);
                }
            }

            UpdatePreview();
        }
    }

    private void UpdatePreview()
    {
        PreviewDot.Background = new SolidColorBrush(Parse(_selectedColor));
        PreviewText.Text = string.IsNullOrWhiteSpace(NameBox.Text) ? "状态名称" : NameBox.Text.Trim();
    }

    private static Color Parse(string hex)
    {
        var text = hex.TrimStart('#');
        return Color.FromArgb(255,
            Convert.ToByte(text[..2], 16),
            Convert.ToByte(text.Substring(2, 2), 16),
            Convert.ToByte(text.Substring(4, 2), 16));
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            args.Cancel = true;
            ShowError("请填写状态名称。");
            return;
        }

        var sameScope = _services.Statuses.GetByScope(Scope);
        if (sameScope.Any(s => s.Id != _existing?.Id && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            args.Cancel = true;
            ShowError("同一状态集合中已存在同名状态。");
            return;
        }

        if (_selectedColor.Length != 7 || !_selectedColor.StartsWith('#'))
        {
            args.Cancel = true;
            ShowError("颜色格式应为 #RRGGBB。");
            return;
        }

        try
        {
            if (_existing is null)
            {
                var created = _services.Statuses.Create(new WorkStatus
                {
                    Scope = Scope,
                    Name = name,
                    Color = _selectedColor,
                    SortOrder = 0,
                });
                ResultStatus = created;
            }
            else
            {
                var updated = _existing.Clone();
                updated.Name = name;
                updated.Color = _selectedColor;
                ResultStatus = _services.Statuses.Update(updated);
            }
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            App.LogCrash(ex);
            ShowError(ex.Message);
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
