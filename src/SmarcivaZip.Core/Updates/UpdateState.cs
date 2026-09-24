using System.Text.Json;
using SmarcivaZip.Core.Settings;

namespace SmarcivaZip.Core.Updates;

/// <summary>
/// 更新確認の記録。settings.json とは別のファイルに置く。
///
/// 解凍や圧縮のついでに書くので、同じファイルにすると、そのとき開いている設定画面の
/// 変更を古い内容で上書きしてしまうことがある。
/// </summary>
public sealed class UpdateState
{
    /// <summary>最後に確認した時刻（成否を問わない。つながらない環境で毎回待たせないため）。</summary>
    public DateTime? LastCheckUtc { get; set; }

    /// <summary>すでに知らせたバージョン。同じバージョンを何度も知らせない。</summary>
    public string? NotifiedVersion { get; set; }

    private static string StatePath => Path.Combine(AppSettings.SettingsDirectory, "update.json");

    public static UpdateState Load()
    {
        try
        {
            if (!File.Exists(StatePath)) return new UpdateState();
            return JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(StatePath)) ?? new UpdateState();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new UpdateState();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.SettingsDirectory);
            string temporary = StatePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, StatePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 記録できなくても、次回もう一度確認するだけ。
        }
    }
}
