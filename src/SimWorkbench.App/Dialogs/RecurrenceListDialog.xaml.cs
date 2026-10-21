using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SimWorkbench.App.ViewModels;
using SimWorkbench.Core.Models;
using SimWorkbench.Data;

namespace SimWorkbench.App.Dialogs;

/// <summary>周期规则列表：支持新建、编辑、删除（删除时说明作用范围）。</summary>
public sealed partial class RecurrenceListDialog : ContentDialog
{
    private readonly WorkbenchServices _services;

    public RecurrenceListDialog(WorkbenchServices services)
    {
        _services = services;
        InitializeComponent();
        Reload();
    }

    /// <summary>需要编辑的规则（由调用方在关闭后打开编辑对话框）。</summary>
    public RecurrenceRule? RuleToEdit { get; private set; }

    private void Reload()
    {
        var rules = _services.Recurrences.GetAll();
        var rows = rules.Select(r => new RecurrenceRow(r, _services.Recurrences.CountFutureInstances(r.Id, DateTime.Today))).ToList();
        RuleList.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnEditRule(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RecurrenceRule rule })
        {
            RuleToEdit = rule;
            Hide();
        }
    }

    private async void OnDeleteRule(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RecurrenceRule rule })
        {
            return;
        }

        var scope = _services.Recurrences.CountFutureInstances(rule.Id, DateTime.Today);
        var content = new TextBlock
        {
            Text = $"确定删除周期规则“{rule.Title}”吗？\n未来实例 {scope} 个。请选择删除范围：",
            TextWrapping = TextWrapping.Wrap,
        };
        var dialog = new ContentDialog
        {
            Title = "删除周期规则",
            Content = content,
            PrimaryButtonText = "仅删除规则（保留实例任务）",
            SecondaryButtonText = "删除规则和未来实例",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        var result = await dialog.ShowAsync();
        try
        {
            if (result == ContentDialogResult.Primary)
            {
                _services.Recurrences.Delete(rule.Id, RecurrenceDeleteScope.RuleOnly);
            }
            else if (result == ContentDialogResult.Secondary)
            {
                _services.Recurrences.Delete(rule.Id, RecurrenceDeleteScope.RuleAndFutureInstances);
            }
            else
            {
                return;
            }

            Reload();
        }
        catch (Exception ex)
        {
            App.LogCrash(ex);
            await App.ShowMessageAsync(App.Main?.Content as Page, "删除失败", ex.Message);
        }
    }
}
