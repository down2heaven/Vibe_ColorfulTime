using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SimWorkbench.App.Dialogs;

/// <summary>通用单行/多行文本输入对话框。</summary>
public sealed partial class TextPromptDialog : ContentDialog
{
    public TextPromptDialog(string title, string prompt, string? initialText = null, bool multiline = false)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initialText ?? string.Empty;
        InputBox.AcceptsReturn = multiline;
        InputBox.MinHeight = multiline ? 96 : 36;
    }

    public string? ResultText { get; private set; }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var text = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            args.Cancel = true;
            ErrorText.Text = "内容不能为空。";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        ResultText = text;
    }
}
