using System.Windows;
using System.Windows.Controls;

namespace NotificationReader.Windows;

/// <summary>
/// A tiny code-defined modal dialog that prompts the user for a single line of
/// text. Used by the "Test Pattern..." feature in the settings window.
/// </summary>
public class TextInputDialog : Window
{
    private readonly TextBox _textBox;

    public string? InputText => _textBox.Text;

    public TextInputDialog(string title, string prompt)
    {
        Title = title;
        Width = 420;
        Height = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var promptText = new TextBlock
        {
            Text = prompt,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(promptText, 0);
        grid.Children.Add(promptText);

        _textBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(_textBox, 1);
        grid.Children.Add(_textBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(4, 0, 4, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(4, 0, 0, 0), IsCancel = true };
        ok.Click += (_, _) => { DialogResult = true; Close(); };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);

        Content = grid;
        _textBox.Focus();
    }
}
