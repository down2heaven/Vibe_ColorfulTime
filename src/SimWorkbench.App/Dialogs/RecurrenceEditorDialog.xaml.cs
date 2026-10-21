using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SimWorkbench.App.Common;
using SimWorkbench.App.ViewModels;
using SimWorkbench.Core.Models;
using SimWorkbench.Data;

namespace SimWorkbench.App.Dialogs;

/// <summary>周期规则新建/编辑。</summary>
public sealed partial class RecurrenceEditorDialog : ContentDialog
{
    private readonly WorkbenchServices _services;
    private readonly RecurrenceRule? _existing;

    public RecurrenceEditorDialog(WorkbenchServices services, RecurrenceRule? existing)
    {
        _services = services;
        _existing = existing;
        InitializeComponent();
        Title = existing is null ? "新建周期任务" : "编辑周期规则";
        CategoryBox.ItemsSource = _services.Tasks.GetCategories();

        if (existing is not null)
        {
            LoadExisting(existing);
        }
        else
        {
            KindBox.SelectedIndex = 0;
            FrequencyBox.SelectedIndex = 1;
            UiUtil.SetDate(StartDatePicker, DateTime.Today);
            StartDatePicker.Date = new DateTimeOffset(DateTime.Today);
            LoadStatusChoices();
            UpdateFrequencyPanels();
        }
    }

    public RecurrenceRule? ResultRule { get; private set; }

    private void LoadExisting(RecurrenceRule rule)
    {
        TitleBox.Text = rule.Title;
        KindBox.SelectedIndex = rule.Kind == TaskKind.Simulation ? 1 : 0;
        FrequencyBox.SelectedIndex = (int)rule.Frequency;
        IntervalBox.Value = Math.Max(1, rule.Interval);
        PriorityBox.SelectedIndex = (int)rule.Priority;
        CategoryBox.Text = rule.Category ?? string.Empty;
        TagsBox.Text = rule.Tags ?? string.Empty;
        DescriptionBox.Text = rule.Description ?? string.Empty;
        CaseNameBox.Text = rule.CaseName ?? string.Empty;
        CaseConditionBox.Text = rule.CaseCondition ?? string.Empty;
        if (rule.EstimatedMinutes is { } minutes)
        {
            HoursBox.Value = minutes / 60.0;
        }

        UiUtil.SetDate(StartDatePicker, rule.StartDate);
        UiUtil.SetDate(EndDatePicker, rule.EndDate);
        var days = rule.GetWeekDayList();
        MonButton.IsChecked = days.Contains(DayOfWeek.Monday);
        TueButton.IsChecked = days.Contains(DayOfWeek.Tuesday);
        WedButton.IsChecked = days.Contains(DayOfWeek.Wednesday);
        ThuButton.IsChecked = days.Contains(DayOfWeek.Thursday);
        FriButton.IsChecked = days.Contains(DayOfWeek.Friday);
        SatButton.IsChecked = days.Contains(DayOfWeek.Saturday);
        SunButton.IsChecked = days.Contains(DayOfWeek.Sunday);

        _selectedStatusId = rule.StatusId;
        LoadStatusChoices();
        UpdateFrequencyPanels();
        UpdateKindPanels();
    }

    private Guid _selectedStatusId;

    private TaskKind SelectedKind() => KindBox.SelectedIndex == 1 ? TaskKind.Simulation : TaskKind.General;

    private RecurrenceFrequency SelectedFrequency() => FrequencyBox.SelectedIndex switch
    {
        0 => RecurrenceFrequency.Daily,
        2 => RecurrenceFrequency.Monthly,
        _ => RecurrenceFrequency.Weekly,
    };

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

    private void OnFrequencyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IntervalUnitText is null)
        {
            return;
        }

        UpdateFrequencyPanels();
    }

    private void UpdateFrequencyPanels()
    {
        var frequency = SelectedFrequency();
        IntervalUnitText.Text = frequency switch
        {
            RecurrenceFrequency.Daily => "天",
            RecurrenceFrequency.Monthly => "月",
            _ => "周",
        };
        WeekDayPanel.Visibility = frequency == RecurrenceFrequency.Weekly ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnWeekDayToggled(object sender, RoutedEventArgs e)
    {
        // 仅用于切换选中状态，保存时统一读取
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
            ShowError("请选择默认状态。");
            return;
        }

        var startDate = UiUtil.GetDate(StartDatePicker) ?? DateTime.Today;
        var endDate = UiUtil.GetDate(EndDatePicker);
        if (endDate is { } end && end.Date < startDate.Date)
        {
            args.Cancel = true;
            ShowError("结束日期不能早于开始日期。");
            return;
        }

        try
        {
            var rule = _existing?.Clone() ?? new RecurrenceRule();
            var kind = SelectedKind();
            rule.Title = title;
            rule.Kind = kind;
            rule.StatusId = statusChoice.Id;
            rule.Frequency = SelectedFrequency();
            rule.Interval = double.IsNaN(IntervalBox.Value) ? 1 : Math.Max(1, (int)Math.Round(IntervalBox.Value));
            rule.Priority = (Priority)PriorityBox.SelectedIndex;
            rule.Category = NullIfEmpty(CategoryBox.Text);
            rule.Tags = NullIfEmpty(TagsBox.Text);
            rule.Description = NullIfEmpty(DescriptionBox.Text);
            rule.StartDate = startDate.Date;
            rule.EndDate = endDate?.Date;
            rule.IsActive = true;
            rule.WeekDays = rule.Frequency == RecurrenceFrequency.Weekly ? CollectWeekDays() : null;

            if (kind == TaskKind.Simulation)
            {
                rule.CaseName = NullIfEmpty(CaseNameBox.Text);
                rule.CaseCondition = NullIfEmpty(CaseConditionBox.Text);
                var hours = double.IsNaN(HoursBox.Value) ? 0 : HoursBox.Value;
                var minutes = (int)Math.Round(hours * 60);
                rule.EstimatedMinutes = minutes > 0 ? minutes : null;
            }
            else
            {
                rule.CaseName = null;
                rule.CaseCondition = null;
                rule.EstimatedMinutes = null;
            }

            ResultRule = _existing is null
                ? _services.Recurrences.Create(rule)
                : _services.Recurrences.Update(rule);
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            App.LogCrash(ex);
            ShowError(ex.Message);
        }
    }

    private string? CollectWeekDays()
    {
        var days = new List<DayOfWeek>();
        if (MonButton.IsChecked == true) days.Add(DayOfWeek.Monday);
        if (TueButton.IsChecked == true) days.Add(DayOfWeek.Tuesday);
        if (WedButton.IsChecked == true) days.Add(DayOfWeek.Wednesday);
        if (ThuButton.IsChecked == true) days.Add(DayOfWeek.Thursday);
        if (FriButton.IsChecked == true) days.Add(DayOfWeek.Friday);
        if (SatButton.IsChecked == true) days.Add(DayOfWeek.Saturday);
        if (SunButton.IsChecked == true) days.Add(DayOfWeek.Sunday);
        return days.Count == 0 ? null : string.Join(";", days.Select(d => ((int)d).ToString()).Distinct());
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static string? NullIfEmpty(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
