using System.Windows;

namespace SmarcivaZip.App.Views;

/// <summary>
/// パスワードの入力。圧縮時は打ち間違いが致命的（開けないアーカイブができる）なので、
/// 確認用にもう一度入力してもらう。
/// </summary>
public partial class PasswordWindow : Window
{
    private readonly bool _requireConfirmation;

    public string Password => PasswordInput.Password;

    private PasswordWindow(string heading, string detail, bool requireConfirmation)
    {
        InitializeComponent();

        _requireConfirmation = requireConfirmation;
        HeadingText.Text = heading;
        DetailText.Text = detail;

        if (requireConfirmation)
        {
            ConfirmInput.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) => PasswordInput.Focus();
    }

    /// <summary>暗号化されたアーカイブを開くとき。</summary>
    public static PasswordWindow ForExtraction(string archiveName) =>
        new("パスワードが必要です",
            $"{archiveName} は暗号化されています。パスワードを入力してください。",
            requireConfirmation: false);

    /// <summary>パスワード付きで圧縮するとき。</summary>
    public static PasswordWindow ForCompression() =>
        new("パスワードを設定",
            "上の欄にパスワード、下の欄に確認用としてもう一度入力してください。" +
            "パスワードを忘れると中身を取り出せなくなります。",
            requireConfirmation: true);

    private void OnOkClicked(object sender, RoutedEventArgs e)
    {
        if (PasswordInput.Password.Length == 0)
        {
            DetailText.Text = "パスワードを入力してください。";
            DetailText.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
            return;
        }

        if (_requireConfirmation && PasswordInput.Password != ConfirmInput.Password)
        {
            DetailText.Text = "2 つの欄の内容が一致しません。入力し直してください。";
            DetailText.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
            ConfirmInput.Clear();
            ConfirmInput.Focus();
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;
}
