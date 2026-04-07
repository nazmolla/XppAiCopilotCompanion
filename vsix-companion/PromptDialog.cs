using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;

namespace XppAiCopilotCompanion
{
    internal sealed class InputDialog : DialogWindow
    {
        private readonly TextBox _textBox;

        public InputDialog(string title, string label, string defaultValue)
        {
            Title = title;
            Width = 500;
            Height = 180;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var stack = new StackPanel { Margin = new Thickness(12) };
            stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });

            _textBox = new TextBox { Text = defaultValue ?? string.Empty, Margin = new Thickness(0, 0, 0, 12) };
            stack.Children.Add(_textBox);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            var okBtn = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            okBtn.Click += (s, e) => { DialogResult = true; Close(); };
            buttonPanel.Children.Add(okBtn);

            var cancelBtn = new Button { Content = "Cancel", Width = 80, IsCancel = true };
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
            buttonPanel.Children.Add(cancelBtn);

            stack.Children.Add(buttonPanel);
            Content = stack;
        }

        public string ResponseText => _textBox.Text;
    }

    internal static class PromptDialog
    {
        public static string Show(string title, string label, string defaultValue)
        {
            var dialog = new InputDialog(title, label, defaultValue);
            bool? result = dialog.ShowModal();
            return result == true ? dialog.ResponseText : null;
        }
    }
}
