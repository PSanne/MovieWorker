using System.Windows;

namespace MovieWorker.App.Views;

public partial class TextInputWindow : Window
{
    public TextInputWindow(string message, string defaultValue)
    {
        InitializeComponent();
        MessageText.Text = message;
        Input.Text = defaultValue;
        Loaded += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    public string Value => Input.Text.Trim();

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = Value.Length > 0;
}
