using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SimWorkbench.App.Common;
using SimWorkbench.App.ViewModels;
using SimWorkbench.Core.Logic;
using SimWorkbench.Core.Models;
using SimWorkbench.Core.Services;
using SimWorkbench.Data;

namespace SimWorkbench.App.Dialogs;

/// <summary>任务新建/编辑对话框（含仿真专属字段与周期实例操作）。</summary>
public sealed partial class TaskEditorDialog : ContentDialog
{
    private readonly WorkbenchServices _services;
    private readonly TaskItem? _existing;
    private Guid _selectedStatusId;
    private bool _skipPending;
    private DateTime? _plannedStart;
    private DateTime? _dueAt;
    private DateTime? _actualDelivery;

    public TaskEditorDialog(WorkbenchServices services, TaskItem? existing, TaskKind? defaultKind = null)
    {
        _services = services;
        _existing = existing;
        InitializeComponent();

        Title = existing is null ? "新建任务" : "编辑任务";
        CategoryBox.ItemsSource = _services.Tasks.GetCategories();

        if (existing is not null)
        {
            _selectedStatusId = existing.StatusId;
            LoadExisting(existing);
        }
        else
        {
            KindBox.SelectedIndex = defaultKind == TaskKind.Simulation ? 1 : 0;
            PriorityBox.SelectedIndex = 0;
            UiUtil.SetDate(CreatedPicker, DateTime.Today);
            LoadStatusChoices();
            CreatedPicker.Date = new DateTimeOffset(DateTime.Today);
            UiUtil.SetTime(StartTimePicker, new TimeSpan(9, 0, 0));
            UiUtil.SetTime(DueTimePicker, new TimeSpan(18, 0, 0));
            UiUtil.SetTime(ActualTimePicker, new TimeSpan(18, 0, 0));
            UpdateKindPanels();
        }
    }

    /// <summary>保存后的任务（保存成功时赋值）。</summary>
    public TaskItem? ResultTask { get; private set; }

    /// <summary>是否已跳过本次周期实例。</summary>
    public bool SkippedOccurrence { get; private set; }

    private void LoadExisting(TaskItem task)
    {
        TitleBox.Text = task.Title;
        KindBox.SelectedIndex = task.Kind == TaskKind.Simulation ? 1 : 0;
        PriorityBox.SelectedIndex = (int)task.Priority;
        CategoryBox.Text = task.Category ?? string.Empty;
        TagsBox.Text = task.Tags ?? string.Empty;
        DescriptionBox.Text = task.Description ?? string.Empty;

        UiUtil.SetDate(CreatedPicker, task.CreatedAt);
        _plannedStart = task.PlannedStart;
        _dueAt = task.DueAt;
        _actualDelivery = task.ActualDeliveryAt;
        UiUtil.Split(task.PlannedStart, StartDatePicker, StartTimePicker);
        UiUtil.Split(task.DueAt, DueDatePicker, DueTimePicker);
        UiUtil.Split(task.ActualDeliveryAt, ActualDatePicker, ActualTimePicker);

        CaseNameBox.Text = task.CaseName ?? string.Empty;
        CaseConditionBox.Text = task.CaseCondition ?? string.Empty;
        CalcNotesBox.Text = task.CalculationNotes ?? string.Empty;
        if (task.EstimatedMinutes is { } minutes)
        {
            HoursBox.Value = minutes / 60;
            MinutesBox.Value = minutes % 60;
        }

        LoadStatusChoices();
        UpdateKindPanels();

        if (task.RecurrenceId is { } ruleId)
        {
            RecurrencePanel.Visibility = Visibility.Visible;
            var rule = _services.Recurrences.Get(ruleId);
            RecurrenceInfoText.Text = rule is null
                ? "本任务由周期规则生成。"
                : $"本任务由周期规则“{rule.Title}”（{rule.GetFrequencyText()}）生成，执行日期：{task.OccurrenceDate:yyyy-MM-dd}。";
        }
    }

    private TaskKind SelectedKind() => KindBox.SelectedIndex == 1 ? TaskKind.Simulation : TaskKind.General;

    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StatusBox is null)
        {
            return;
        }

        UpdateKindPanels();
        LoadStatusChoices();
    }

    private void UpdateKindPanels()
    {
        if (SimulationPanel is null)
        {
            return;
        }

        SimulationPanel.Visibility = SelectedKind() == TaskKind.Simulation ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadStatusChoices()
    {
        var kind = SelectedKind();
        var scope = kind == TaskKind.Simulation ? StatusScope.Simulation : StatusScope.General;
        var statuses = _services.Statuses.GetByScope(scope).ToList();

        if (kind == TaskKind.General && _services.Settings.Load().GeneralTasksShareSimulationStatuses)
        {
            foreach (var extra in _services.Statuses.GetByScope(StatusScope.Simulation))
            {
                if (statuses.All(s => s.Id != extra.Id))
                {
                    statuses.Add(extra);
                }
            }
        }

        // 保证当前选中的状态始终在列表中（例如已切换类型的历史任务）
        if (_selectedStatusId != Guid.Empty && statuses.All(s => s.Id != _selectedStatusId)
            && _services.Statuses.Get(_selectedStatusId) is { } current)
        {
            statuses.Insert(0, current);
        }

        var choices = statuses.Select(s => new StatusChoice(s)).ToList();
        StatusBox.ItemsSource = choices;
        if (choices.Count == 0)
        {
            return;
        }

        var match = choices.FirstOrDefault(c => c.Id == _selectedStatusId) ?? choices[0];
        StatusBox.SelectedItem = match;
        _selectedStatusId = match.Id;
    }

    private void OnFillNow(object sender, RoutedEventArgs e)
    {
        var now = DateTime.Now;
        UiUtil.SetDate(ActualDatePicker, now.Date);
        UiUtil.SetTime(ActualTimePicker, now.TimeOfDay);
    }

    private void OnSkipOccurrence(object sender, RoutedEventArgs e)
    {
        if (_existing?.RecurrenceId is not { } ruleId || _existing.OccurrenceDate is not { } date)
        {
            return;
        }

        if (!_skipPending)
        {
            _skipPending = true;
            SkipOccurrenceButton.Content = "确认跳过本次执行？";
            return;
        }

        try
        {
            _services.Recurrences.SkipOccurrence(ruleId, date);
            SkippedOccurrence = true;
            Hide();
        }
        catch (Exception ex)
        {
            App.LogCrash(ex);
            ShowError(ex.Message);
        }
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        var title = TitleBox.Text.Trim();
        if (string.IsNullOrEmpty(title))
        {
            args.Cancel = true;
            ShowError("请填写任务名称。");
            return;
        }

        if (StatusBox.SelectedItem is not StatusChoice statusChoice)
        {
            args.Cancel = true;
            ShowError("请选择任务状态。");
            return;
        }

        try
        {
            var task = _existing?.Clone() ?? new TaskItem();
            var kind = SelectedKind();
            task.Title = title;
            task.Kind = kind;
            task.StatusId = statusChoice.Id;
            task.Priority = (Priority)PriorityBox.SelectedIndex;
            task.Category = NullIfEmpty(CategoryBox.Text);
            task.Tags = NormalizeTags(TagsBox.Text);
            task.Description = NullIfEmpty(DescriptionBox.Text);

            var createdDate = UiUtil.GetDate(CreatedPicker) ?? DateTime.Today;
            var createdTime = _existing?.CreatedAt.TimeOfDay ?? DateTime.Now.TimeOfDay;
            task.CreatedAt = createdDate.Date + createdTime;

            task.PlannedStart = UiUtil.Combine(StartDatePicker, StartTimePicker);
            task.DueAt = UiUtil.Combine(DueDatePicker, DueTimePicker);
            task.ActualDeliveryAt = UiUtil.Combine(ActualDatePicker, ActualTimePicker);

            if (kind == TaskKind.Simulation)
            {
                task.CaseName = NullIfEmpty(CaseNameBox.Text);
                task.CaseCondition = NullIfEmpty(CaseConditionBox.Text);
                task.CalculationNotes = NullIfEmpty(CalcNotesBox.Text);
                var hours = double.IsNaN(HoursBox.Value) ? 0 : HoursBox.Value;
                var minutes = double.IsNaN(MinutesBox.Value) ? 0 : MinutesBox.Value;
                var total = (int)Math.Round(hours * 60 + minutes);
                task.EstimatedMinutes = total > 0 ? total : null;
            }
            else
            {
                task.CaseName = null;
                task.CaseCondition = null;
                task.CalculationNotes = null;
                task.EstimatedMinutes = null;
            }

            if (_existing is null)
            {
                task.CreatedAt = DateTime.Today + DateTime.Now.TimeOfDay;
                _services.Tasks.Create(task);
            }
            else
            {
                _services.Tasks.Update(task);
            }

            ResultTask = task;
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

    private static string? NullIfEmpty(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string? NormalizeTags(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Replace("，", ";").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : string.Join(";", parts.Distinct());
    }
}
