# プライバシーポリシー / Privacy Policy

最終更新日 / Last updated: 2026-09-23

[English follows the Japanese text.](#english)

## 日本語

smarcivaZIP は、利用者の個人情報を収集しません。

### 送信しないもの

- smarcivaZIP は**インターネットと通信しません**。利用状況の統計（テレメトリ）、クラッシュ報告、広告、アカウント登録のいずれもありません。
- 解凍・圧縮するファイルの中身やファイル名を、どこにも送りません。処理はすべてお使いの PC の中で完結します。

### PC の中に保存するもの

次の 2 つを `%APPDATA%\smarcivaZIP\` に保存します。どちらも PC の外には送られません。

| ファイル | 内容 |
|---|---|
| `settings.json` | 設定画面で選んだ内容（表示言語、関連付けた拡張子など） |
| `smarcivazip.log` | 動作の記録とエラーの内容。原因を調べるために、処理したファイルのパスが含まれることがあります |

インストーラで入れた場合、アンインストール時にログは削除されます。設定は、アンインストールの最後に削除するかどうかを選べます。

### Mark of the Web について

インターネットから入手した書庫を解凍するとき、その書庫に Windows が付けている「インターネットから来た」という印（ゾーン情報）を、解凍したファイルにも引き継ぎます。これは Windows の安全機能を保つためのもので、PC の中だけで行われます。

### 外部サイトへのリンク

設定画面の「情報」タブには、smarciva.com と GitHub へのリンクがあります。クリックしたときだけ既定のブラウザで開きます。開いた先のサイトでの情報の扱いは、それぞれのサイトのポリシーに従います。

### 配布元について

smarcivaZIP を GitHub、winget、Microsoft Store などから入手するとき、それぞれのサービスがダウンロードに関する情報を扱うことがあります。これは各サービスのポリシーに従い、smarcivaZIP の作者はその情報を受け取りません。

### お問い合わせ

https://github.com/comnote-max/smarcivaZIP/issues

このポリシーを変更するときは、このページを更新します。

---

## English

smarcivaZIP does not collect personal information.

### What is not sent

- smarcivaZIP **does not communicate over the internet**. It has no telemetry, crash reporting, advertising or accounts.
- The contents and names of the files you extract or compress are never sent anywhere. All processing happens on your PC.

### What is stored on your PC

Two files are kept in `%APPDATA%\smarcivaZIP\`. Neither leaves your PC.

| File | Contents |
|---|---|
| `settings.json` | The options you chose in the settings window (display language, associated file types, etc.) |
| `smarcivazip.log` | A record of operations and errors, used for troubleshooting. It may contain the paths of files that were processed |

When installed with the installer, the log is deleted on uninstall, and you can choose whether to delete the settings as well.

### Mark of the Web

When you extract an archive that came from the internet, smarcivaZIP copies the zone information Windows attached to the archive onto the extracted files, so that Windows' safety checks still apply to them. This happens entirely on your PC.

### Links to other sites

The About tab of the settings window links to smarciva.com and GitHub. They open in your default browser only when you click them, and those sites' own policies apply there.

### Where you download it

GitHub, winget, the Microsoft Store and similar services may handle information about your download under their own policies. The author of smarcivaZIP does not receive that information.

### Contact

https://github.com/comnote-max/smarcivaZIP/issues

Any change to this policy will be made by updating this page.
