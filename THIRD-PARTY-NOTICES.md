# Third-Party Notices

smarcivaZIP 本体のコードは MIT License です。
ただし配布物には以下のサードパーティ製バイナリを**同梱**しており、それぞれ別のライセンスが適用されます。

---

## 7-Zip (`7z.dll`)

- 配布元: https://www.7-zip.org/
- 著作者: Igor Pavlov
- ライセンス: **GNU LGPL v2.1 以降**（一部 BSD 3-clause、および unRAR 制限付きライセンス）

smarcivaZIP は `7z.dll` を **動的リンク（LoadLibrary / COM インターフェイス）** で利用しており、
静的リンクや改変は行っていません。LGPL の要求どおり、利用者は `7z.dll` を
同一 ABI の別ビルドに差し替えることができます（アプリと同じフォルダの `7z.dll` を置き換えるだけです）。

`7z.dll` のソースコードは上記配布元から入手できます。

### unRAR 制限（重要）

`7z.dll` に含まれる RAR 展開コードは RARLAB の unRAR ソースに由来し、
以下の制限が課されています。

> The unRAR sources cannot be used to re-create the RAR compression algorithm,
> which is proprietary. Distribution of modified unRAR sources in separate form
> or as a part of other software is permitted, provided that it is clearly
> stated in the documentation and source comments that the code may not be used
> to develop a RAR (WinRAR) compatible archiver.

**smarcivaZIP に含まれるコードは、RAR (WinRAR) 互換のアーカイバを開発するために使用することはできません。**
smarcivaZIP は RAR 形式の**展開のみ**をサポートし、RAR 形式での圧縮は行いません。

---

## 差し替え・再配布について

`7z.dll` を同梱しないビルド（`-p:BundleSevenZip=false`）も可能です。
その場合、smarcivaZIP は起動時に以下の順でシステム上の `7z.dll` を探索します。

1. アプリケーションフォルダ
2. `HKLM\SOFTWARE\7-Zip` の `Path64` / `Path`
3. `%ProgramFiles%\7-Zip\7z.dll`
