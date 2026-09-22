using System.Windows.Markup;
using SmarcivaZip.Core.Localization;

namespace SmarcivaZip.App.Localization;

/// <summary>
/// XAML から文字列リソースを引くための拡張。
///
///     Text="{loc:L Setup_Register}"
///
/// 表示言語はウィンドウが作られる前（App.OnStartup）に確定させているので、
/// 実行中に切り替わることは想定しない。言語を変えたら再起動してもらう。
/// 動的に差し替える仕組みを入れることもできるが、そのために全テキストを
/// バインディング経由にするのは、この規模のアプリでは割に合わない。
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LExtension : MarkupExtension
{
    public LExtension() { }

    public LExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}
