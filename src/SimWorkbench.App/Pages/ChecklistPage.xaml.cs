using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SimWorkbench.App.Common;
using SimWorkbench.App.Dialogs;
using SimWorkbench.App.ViewModels;
using SimWorkbench.Core.Models;
using SimWorkbench.Data;
using Windows.UI.Text;

namespace SimWorkbench.App.Pages;

/// <summary>Checklist 页面：命名清单、检查项增删改排序、进度与关联展示。</summary>
public sealed partial class ChecklistPage : Page, IPageRefresh
{
    private WorkbenchServices Services => App.Services;
    private Guid? _selectedChecklistId;
    private bool _loadedOnce;

    public ChecklistPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        NewItemBox.KeyDown += OnNewItemKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce)
        {
            return;
        }

        _loadedOnce = true;
        Refresh();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (App.ServicesAvailable && IsLoaded)
        {
            Refresh();
        }
    }

    public void RefreshPage() => Refresh();

    private void Refresh()
    {
        if (!App.ServicesAvailable)
        {
            return;
        }

        try
        {
            var checklists = Services.Checklists.GetAll();
            var rows = checklists.Select(c => new ChecklistProgressRow(c)).ToList();
            ChecklistList.ItemsSource = rows;
            CountText.Text = $"共 {rows.Count} 个清单";

            if (_selectedChecklistId is { } id && rows.Any(r => r.Checklist.Id == id))
            {
                ChecklistList.SelectedItem = rows.First(r => r.Checklist.Id == id);
                LoadEditor(Services.Checklists.Get(id)!);
            }
            else if (rows.Count > 0)
            {
                ChecklistList.SelectedItem = rows[0];
            }
            else
            {
                ClearEditor();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Checklist 加载失败", ex);
        }
    }

    private void ClearEditor()
    {
        _selectedChecklistId = null;
        EditorTitleText.Text = "选择或新建一个清单";
        NameBox.Text = string.Empty;
        CategoryBox.Text = string.Empty;
        NoteBox.Text = string.Empty;
        ItemsHost.Children.Clear();
        LinksHost.Children.Clear();
        SaveStatusText.Text = string.Empty;
        NewItemBox.Text = string.Empty;
    }

    private void LoadEditor(Checklist checklist)
    {
        _selectedChecklistId = checklist.Id;
        EditorTitleText.Text = $"编辑：{checklist.Name}　（{checklist.ProgressText}）";
        NameBox.Text = checklist.Name;
        CategoryBox.ItemsSource = Services.Checklists.GetAll()
            .Select(c => c.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToList();
        CategoryBox.Text = checklist.Category ?? string.Empty;
        NoteBox.Text = checklist.Note ?? string.Empty;
        SaveStatusText.Text = $"最近更新：{checklist.UpdatedAt:yyyy-MM-dd HH:mm}";
        BuildItems(checklist);
        BuildLinks(checklist.Id);
    }

    private void BuildItems(Checklist checklist)
    {
        ItemsHost.Children.Clear();
        if (checklist.Items.Count == 0)
        {
            ItemsHost.Children.Add(UiUtil.EmptyHint("还没有检查项。在上方输入框添加第一项。"));
            return;
        }

        foreach (var item in checklist.Items.OrderBy(i => i.SortOrder))
        {
            ItemsHost.Children.Add(CreateItemRow(checklist.Id, item));
        }
    }

    private FrameworkElement CreateItemRow(Guid checklistId, ChecklistItem item)
    {
        var grid = new Grid { ColumnSpacing = 8, Padding = new Thickness(4, 3, 4, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var checkBox = new CheckBox { IsChecked = item.IsDone, VerticalAlignment = VerticalAlignment.Center };
        checkBox.Click += (_, _) =>
        {
            try
            {
                Services.Checklists.SetItemDone(item.Id, checkBox.IsChecked == true);
                Refresh();
            }
            catch (Exception ex)
            {
                AppLog.Error("勾选检查项失败", ex);
            }
        };
        grid.Children.Add(checkBox);

        var text = new TextBlock
        {
            Text = item.Text,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (item.IsDone)
        {
            text.TextDecorations = TextDecorations.Strikethrough;
            text.Foreground = UiUtil.BrushFromHex("#8A8A8A");
        }

        var textStack = new StackPanel { Spacing = 1 };
        textStack.Children.Add(text);
        if (!string.IsNullOrWhiteSpace(item.Note))
        {
            textStack.Children.Add(new TextBlock
            {
                Text = item.Note!,
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            });
        }

        var textButton = new Button
        {
            Content = textStack,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        textButton.Click += async (_, _) => await EditItemAsync(item);
        Grid.SetColumn(textButton, 1);
        grid.Children.Add(textButton);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        buttons.Children.Add(UiUtil.MakeIconButton("\uE70E", "上移", (_, _) => MoveItem(item.Id, -1)));
        buttons.Children.Add(UiUtil.MakeIconButton("\uE70D", "下移", (_, _) => MoveItem(item.Id, 1)));
        buttons.Children.Add(UiUtil.MakeIconButton("\uE74D", "删除检查项", async (_, _) => await DeleteItemAsync(item)));
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        return new Border
        {
            Style = (Style)Application.Current.Resources["InsetCardBorderStyle"],
            Padding = new Thickness(4, 1, 4, 1),
            Child = grid,
        };
    }

    private void MoveItem(Guid itemId, int delta)
    {
        try
        {
            Services.Checklists.MoveItem(itemId, delta);
            Refresh();
        }
        catch (Exception ex)
        {
            AppLog.Error("移动检查项失败", ex);
        }
    }

    private async Task EditItemAsync(ChecklistItem item)
    {
        var dialog = new TextPromptDialog("编辑检查项", "可修改检查项内容；如需备注，可在内容后用换行添加。",
            string.IsNullOrWhiteSpace(item.Note) ? item.Text : $"{item.Text}\n\n{item.Note}", multiline: true)
        {
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.ResultText is null)
        {
            return;
        }

        var parts = dialog.ResultText.Split("\n\n", 2, StringSplitOptions.RemoveEmptyEntries);
        item.Text = parts[0].Trim();
        item.Note = parts.Length > 1 ? parts[1].Trim() : null;
        Services.Checklists.UpdateItem(item);
        Refresh();
    }

    private async Task DeleteItemAsync(ChecklistItem item)
    {
        var dialog = new ContentDialog
        {
            Title = "删除检查项",
            Content = new TextBlock { Text = $"确定删除“{item.Text}”吗？", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        Services.Checklists.DeleteItem(item.Id);
        Refresh();
    }

    private void BuildLinks(Guid checklistId)
    {
        LinksHost.Children.Clear();
        var links = Services.Checklists.GetLinksForChecklist(checklistId);
        if (links.Count == 0)
        {
            LinksHost.Children.Add(UiUtil.EmptyHint("尚未被任何对象关联。"));
            return;
        }

        foreach (var link in links)
        {
            var name = link.OwnerType switch
            {
                LinkOwnerTypes.Task => Services.Tasks.Get(link.OwnerId)?.Title,
                LinkOwnerTypes.DailySummary => $"{link.OwnerId:yyyy-MM-dd} 的每日总结",
                LinkOwnerTypes.TopicLog => Services.TopicLogs.Get(link.OwnerId)?.Title,
                LinkOwnerTypes.RecurrenceRule => Services.Recurrences.Get(link.OwnerId)?.Title,
                _ => null,
            };
            var type = link.OwnerType switch
            {
                LinkOwnerTypes.Task => "任务",
                LinkOwnerTypes.DailySummary => "每日总结",
                LinkOwnerTypes.TopicLog => "主题日志",
                LinkOwnerTypes.RecurrenceRule => "周期规则",
                _ => link.OwnerType,
            };
            LinksHost.Children.Add(new TextBlock
            {
                Text = $"{type}：{name ?? "（已删除）"}",
                Style = (Style)Application.Current.Resources["BodyTextStyle"],
            });
        }
    }

    private void OnChecklistSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ChecklistList.SelectedItem is ChecklistProgressRow row)
        {
            LoadEditor(Services.Checklists.Get(row.Checklist.Id) ?? row.Checklist);
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Refresh();

    private async void OnNewChecklist(object sender, RoutedEventArgs e)
    {
        var dialog = new TextPromptDialog("新建清单", "请输入清单名称，例如：仿真前检查 / 结果检查 / 交付检查。")
        {
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.ResultText is null)
        {
            return;
        }

        try
        {
            var checklist = Services.Checklists.Create(new Checklist { Name = dialog.ResultText });
            _selectedChecklistId = checklist.Id;
            Refresh();
        }
        catch (Exception ex)
        {
            AppLog.Error("新建清单失败", ex);
            await App.ShowMessageAsync(this, "新建失败", ex.Message);
        }
    }

    private async void OnSaveHeader(object sender, RoutedEventArgs e)
    {
        if (_selectedChecklistId is not { } id)
        {
            return;
        }

        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            await App.ShowMessageAsync(this, "无法保存", "清单名称不能为空。");
            return;
        }

        try
        {
            var checklist = Services.Checklists.Get(id);
            if (checklist is null)
            {
                return;
            }

            checklist.Name = name;
            checklist.Category = string.IsNullOrWhiteSpace(CategoryBox.Text) ? null : CategoryBox.Text.Trim();
            checklist.Note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();
            Services.Checklists.UpdateHeader(checklist);
            SaveStatusText.Text = $"已保存 {DateTime.Now:HH:mm:ss}";
            Refresh();
        }
        catch (Exception ex)
        {
            AppLog.Error("保存清单失败", ex);
            await App.ShowMessageAsync(this, "保存失败", ex.Message);
        }
    }

    private async void OnDeleteChecklist(object sender, RoutedEventArgs e)
    {
        if (_selectedChecklistId is not { } id)
        {
            return;
        }

        var checklist = Services.Checklists.Get(id);
        if (checklist is null)
        {
            return;
        }

        var linkCount = Services.Checklists.GetLinksForChecklist(id).Count;
        var dialog = new ContentDialog
        {
            Title = "删除清单",
            Content = new TextBlock
            {
                Text = $"确定删除清单“{checklist.Name}”及其 {checklist.Items.Count} 个检查项吗？" +
                       (linkCount > 0 ? $"\n\n该清单被 {linkCount} 个对象关联，删除后关联会一并移除。" : string.Empty),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        Services.Checklists.Delete(id);
        _selectedChecklistId = null;
        Refresh();
    }

    private void OnAddItem(object sender, RoutedEventArgs e) => AddItemFromInput();

    private void OnNewItemKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && !string.IsNullOrWhiteSpace(NewItemBox.Text))
        {
            AddItemFromInput();
            e.Handled = true;
        }
    }

    private void AddItemFromInput()
    {
        if (_selectedChecklistId is not { } id)
        {
            _ = App.ShowMessageAsync(this, "无法添加", "请先选择或新建一个清单。");
            return;
        }

        var text = NewItemBox.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            Services.Checklists.AddItem(id, text);
            NewItemBox.Text = string.Empty;
            Refresh();
        }
        catch (Exception ex)
        {
            AppLog.Error("添加检查项失败", ex);
        }
    }
}
