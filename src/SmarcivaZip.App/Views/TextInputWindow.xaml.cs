using System.Windows;

namespace SmarcivaZip.App.Views;

/// <summary>一行だけ入力してもらう汎用ダイアログ。</summary>
public partial class TextInputWindow : Window
{
    public string Value => InputBox.Text.Trim();

    public TextInputWindow(string heading, string detail, string initialValue = "")
    {
        InitializeComponent();

        HeadingText.Text = heading;
        DetailText.Text = detail;
        InputBox.Text = initialValue;

        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    private void OnOkClicked(object sender, RoutedEventArgs e)
    {
        if (Value.Length == 0)
        {
            InputBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;
}
