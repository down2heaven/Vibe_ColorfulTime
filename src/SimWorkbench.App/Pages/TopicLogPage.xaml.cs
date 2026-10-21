using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SimWorkbench.App.Common;
using SimWorkbench.App.Dialogs;
using SimWorkbench.App.ViewModels;
using SimWorkbench.Core.Logic;
using SimWorkbench.Core.Models;
using SimWorkbench.Data;

namespace SimWorkbench.App.Pages;

/// <summary>主题日志 / 断点记录：更新断点时自动保留历史，支持类别与标签筛选。</summary>
public sealed partial class TopicLogPage : Page, IPageRefresh
{
    private WorkbenchServices Services => App.Services;
    private Guid? _selectedTopicId;
    private bool _loadedOnce;
    private bool _suppressFilter;

    public TopicLogPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce)
        {
            return;
        }

        _loadedOnce = true;
        ReloadCategories();
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

    private void ReloadCategories()
    {
        var previous = CategoryFilter.SelectedItem as string;
        _suppressFilter = true;
        try
        {
            CategoryFilter.Items.Clear();
            CategoryFilter.Items.Add("全部类别");
            foreach (var category in Services.TopicLogs.GetCategories())
            {
                CategoryFilter.Items.Add(category);
            }

            CategoryFilter.SelectedItem = previous is not null && CategoryFilter.Items.Contains(previous)
                ? previous
                : CategoryFilter.Items[0];
            CategoryBox.ItemsSource = Services.TopicLogs.GetCategories();
        }
        finally
        {
            _suppressFilter = false;
        }
    }

    private void Refresh()
    {
        if (!App.ServicesAvailable)
        {
            return;
        }

        try
        {
            var category = CategoryFilter.SelectedItem as string;
            if (category == "全部类别")
            {
                category = null;
            }

            var topics = Services.TopicLogs.Query(SearchBox.Text, category, TagFilter.Text);
            var rows = topics.Select(t => new TopicRow(t)).ToList();
            TopicList.ItemsSource = rows;
            CountText.Text = $"共 {rows.Count} 个主题";

            if (_selectedTopicId is { } id)
            {
                var row = rows.FirstOrDefault(r => r.Topic.Id == id);
                if (row is not null)
                {
                    TopicList.SelectedItem = row;
                }
                else
                {
                    ClearEditor();
                }
            }
            else if (rows.Count > 0)
            {
                TopicList.SelectedItem = rows[0];
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("主题日志加载失败", ex);
        }
    }

    private void ClearEditor()
    {
        _selectedTopicId = null;
        EditorTitleText.Text = "选择一个主题，或新建主题";
        TitleBox.Text = string.Empty;
        CategoryBox.Text = string.Empty;
        TagsBox.Text = string.Empty;
        BreakpointBox.Text = string.Empty;
        ContentBox.Text = string.Empty;
        HistoryList.ItemsSource = null;
        HistoryEmptyText.Visibility = Visibility.Collapsed;
        SaveStatusText.Text = string.Empty;
    }

    private void LoadTopic(TopicLog topic)
    {
        _selectedTopicId = topic.Id;
        EditorTitleText.Text = $"编辑：{topic.Title}";
        TitleBox.Text = topic.Title;
        CategoryBox.Text = topic.Category ?? string.Empty;
        TagsBox.Text = topic.Tags ?? string.Empty;
        BreakpointBox.Text = topic.CurrentBreakpoint ?? string.Empty;
        ContentBox.Text = topic.Content ?? string.Empty;
        SaveStatusText.Text = $"最近更新：{topic.UpdatedAt:yyyy-MM-dd HH:mm}";
        LoadHistory(topic.Id);
    }

    private void LoadHistory(Guid topicId)
    {
        var rows = Services.TopicLogs.GetHistory(topicId).Select(h => new HistoryRow(h)).ToList();
        HistoryList.ItemsSource = rows;
        HistoryEmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTopicSelected(object sender, SelectionChangedEventArgs e)
    {
        if (TopicList.SelectedItem is TopicRow row)
        {
            LoadTopic(row.Topic);
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadedOnce)
        {
            Refresh();
        }
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressFilter && _loadedOnce)
        {
            Refresh();
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        ReloadCategories();
        Refresh();
    }

    private async void OnNewTopic(object sender, RoutedEventArgs e)
    {
        try
        {
            var topic = Services.TopicLogs.Create(new TopicLog
            {
                Title = "新建主题",
                Category = null,
                CurrentBreakpoint = null,
                Content = null,
            });
            ReloadCategories();
            Refresh();
            _selectedTopicId = topic.Id;
            LoadTopic(topic);
            TitleBox.Focus(FocusState.Programmatic);
            TitleBox.SelectAll();
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            AppLog.Error("新建主题失败", ex);
            await App.ShowMessageAsync(this, "新建失败", ex.Message);
        }
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_selectedTopicId is not { } id)
        {
            await App.ShowMessageAsync(this, "无法保存", "请先选择或新建一个主题。");
            return;
        }

        var title = TitleBox.Text.Trim();
        if (string.IsNullOrEmpty(title))
        {
            await App.ShowMessageAsync(this, "无法保存", "主题名称不能为空。");
            return;
        }

        try
        {
            var topic = Services.TopicLogs.Get(id);
            if (topic is null)
            {
                await App.ShowMessageAsync(this, "无法保存", "主题已不存在，请刷新后重试。");
                return;
            }

            topic.Title = title;
            topic.Category = string.IsNullOrWhiteSpace(CategoryBox.Text) ? null : CategoryBox.Text.Trim();
            topic.Tags = string.IsNullOrWhiteSpace(TagsBox.Text) ? null : TagsBox.Text.Trim();
            topic.CurrentBreakpoint = string.IsNullOrWhiteSpace(BreakpointBox.Text) ? null : BreakpointBox.Text.Trim();
            topic.Content = string.IsNullOrWhiteSpace(ContentBox.Text) ? null : ContentBox.Text.Trim();

            var historyBefore = Services.TopicLogs.GetHistory(id).Count;
            Services.TopicLogs.Update(topic, saveBreakpointHistory: true);
            var historyAfter = Services.TopicLogs.GetHistory(id).Count;

            SaveStatusText.Text = historyAfter > historyBefore
                ? $"已保存，旧断点已写入历史（{DateTime.Now:HH:mm}）"
                : $"已保存（{DateTime.Now:HH:mm}）";
            LoadHistory(id);
            ReloadCategories();
            Refresh();
        }
        catch (Exception ex)
        {
            AppLog.Error("主题日志保存失败", ex);
            await App.ShowMessageAsync(this, "保存失败", ex.Message);
        }
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_selectedTopicId is not { } id)
        {
            return;
        }

        var topic = Services.TopicLogs.Get(id);
        if (topic is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "删除主题日志",
            Content = new TextBlock
            {
                Text = $"确定删除“{topic.Title}”吗？其断点历史记录会一并删除，且不可撤销。",
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

        try
        {
            Services.TopicLogs.Delete(id);
            ClearEditor();
            ReloadCategories();
            Refresh();
        }
        catch (Exception ex)
        {
            AppLog.Error("删除主题失败", ex);
        }
    }

    private async void OnLinkChecklists(object sender, RoutedEventArgs e)
    {
        if (_selectedTopicId is not { } id)
        {
            return;
        }

        try
        {
            var dialog = new ChecklistLinkDialog(Services, LinkOwnerTypes.TopicLog, id) { XamlRoot = XamlRoot };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                dialog.ApplyChanges();
                await App.ShowMessageAsync(this, "已保存关联",
                    "清单关联已更新。可在 Checklist 页面查看具体条目。");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("关联清单失败", ex);
        }
    }
}
