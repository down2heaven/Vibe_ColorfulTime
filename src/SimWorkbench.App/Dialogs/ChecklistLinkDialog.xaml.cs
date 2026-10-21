using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SimWorkbench.App.Common;
using SimWorkbench.Core.Models;
using SimWorkbench.Data;

namespace SimWorkbench.App.Dialogs;

/// <summary>把清单关联到任务 / 每日总结 / 主题日志。</summary>
public sealed partial class ChecklistLinkDialog : ContentDialog
{
    private readonly WorkbenchServices _services;
    private readonly string _ownerType;
    private readonly Guid _ownerId;
    private readonly Dictionary<Guid, CheckBox> _boxes = new();

    public ChecklistLinkDialog(WorkbenchServices services, string ownerType, Guid ownerId)
    {
        _services = services;
        _ownerType = ownerType;
        _ownerId = ownerId;
        InitializeComponent();
        BuildList();
    }

    private void BuildList()
    {
        var checklists = _services.Checklists.GetAll();
        var linked = _services.Checklists.GetLinkedChecklists(_ownerType, _ownerId).Select(c => c.Id).ToHashSet();
        if (checklists.Count == 0)
        {
            EmptyText.Visibility = Visibility.Visible;
            return;
        }

        foreach (var checklist in checklists)
        {
            var box = new CheckBox
            {
                Content = new TextBlock
                {
                    Text = $"{checklist.Name}　（{checklist.ProgressText}）",
                    TextWrapping = TextWrapping.Wrap,
                },
                IsChecked = linked.Contains(checklist.Id),
                Tag = checklist.Id,
            };
            _boxes[checklist.Id] = box;
            ChecklistHost.Children.Add(box);
        }
    }

    /// <summary>应用勾选结果（关闭对话框后由调用方触发刷新）。</summary>
    public void ApplyChanges()
    {
        var linked = _services.Checklists.GetLinkedChecklists(_ownerType, _ownerId).Select(c => c.Id).ToHashSet();
        foreach (var (id, box) in _boxes)
        {
            var shouldLink = box.IsChecked == true;
            if (shouldLink && !linked.Contains(id))
            {
                _services.Checklists.Link(_ownerType, _ownerId, id);
            }
            else if (!shouldLink && linked.Contains(id))
            {
                _services.Checklists.Unlink(_ownerType, _ownerId, id);
            }
        }
    }
}
