# Timeline Notice Editor for ACT

SPESPE / ACT.HojoringのタイムラインXMLを開き、`i-notice`の画像や`v-notice`のアイコン・文字を見ながら表示位置を調整するACTプラグインです。

## インストール

1. [Releases](https://github.com/Roxyz0501/timeline-notice-editor-act/releases/latest)から`TimelineNoticeEditor-vX.Y.Z.zip`をダウンロードし、任意の専用フォルダへ展開します。
2. Windowsのプロパティに「許可する」が表示される場合はZIPを許可してから展開してください。
3. ACTのPluginsで`TimelineNoticeEditor.dll`を選び、Add/Enable Pluginで追加します。
4. 「編集」タブからタイムラインフォルダまたはXMLを開きます。標準的な`ACT.Hojoring*/resources/timeline`は初回に検出します。複数のHojoringがある場合は使用中のフォルダを選択してください。

Windows / ACT / .NET Framework 4.8が必要です。Hojoring DLLへの直接参照は不要です。プラグイン自体はFFXIVのログ、スクリプト、音声を実行しません。

## 編集と保存

- ファイル一覧からXMLを開き、行番号・セクション・時刻・通知内容から目的の通知を選びます。絞り込み検索もできます。
- 画像はXMLと同じフォルダ、Hojoringの`resources/images`（i-notice）、`resources/icon`（v-notice）のサブフォルダから検索します。絶対パス・相対パス・追加画像フォルダにも対応します。同名が複数で曖昧な場合は通知します。
- **i-notice**：プレビューの金枠をドラッグ、またはX・Y・倍率を入力します。矢印キーは1、Shift+矢印は10移動します。選択通知の`left`・`top`・`scale`だけを変更します。両座標が`-1`の場合はSPESPEと同様に中央配置としてプレビューします。
- **v-notice**：SPESPEには通知ごとの座標属性がありません。位置は同じフォルダの**Timeline.configのNoticeLeft / NoticeTopで全タイムライン共通**です。「共通位置を保存」を押すとこの設定を変更します。起動中のSPESPEが同じ設定を使用している場合は公開プロパティ経由でメモリ上にも反映し、後の自動保存による巻き戻りを防ぎます。Hojoring側で全オーバーレイが固定されているときは解除が必要です。
- 「画像を拡大して確認」で画像を大きく表示します。「デスクトップでプレビュー」は実際の画面に重ねます。「固定・クリック透過」でそのプレビューを固定します。プレビューのON/OFFや固定切替は同じウィンドウを維持します。表示は静止画で、継続的な再描画タイマーはありません。
- 「バックアップしてXML保存」は開いているXMLの変更をまとめて保存します。「位置を元に戻す」は選択通知を最後の読み込み時点へ戻します。ファイルを切り替える前に未保存変更があれば確認します。ACT終了やプラグインのアンロード前には必ず保存してください。
- XML保存後はSPESPEでタイムラインを再読み込みしてください。SPESPEの自動再読み込み設定によっては自動反映されます。

### バックアップと復元

保存前のファイルは、同じフォルダの`元のファイル名.backup-yyyyMMdd-HHmmss-fff-識別子.bak`に残します。XMLは一時ファイルを検証してからWindowsの原子的置換でバックアップと保存を行います。コメント、CDATA、スクリプト、改行、BOM、選択外の属性は保持します。UTF-8 / UTF-16に対応します。DTD・外部エンティティは拒否します。

外部ソフトによる変更を検出した場合は上書きを止めます。「再読み込み」で最新を開いてから調整してください。保存先に書き込み権限がなければ元ファイルを変更しません。SPESPE動作中の共通設定はSPESPE自身の保存APIを利用するため、その設定ファイルの整形はSPESPEの形式になります。

復元はSPESPEをアンロードした状態で対象の`.bak`を元のXML／`Timeline.config`名へコピーし、SPESPEを再ロードしてください。復元前の現行ファイルも別名で残すと安心です。バックアップは自動削除しません。

### プレビューの範囲

- i-noticeは画像のピクセル寸法×倍率、SPESPEの3 DIP枠余白、WPFのデスクトップ座標を使用します。画像ファイル自体は変更しません。
- v-noticeは`Timeline.config`の通知スタイル、フォント、文字色、アウトライン、アイコンサイズを参照する近似表示です。独自XAML、動的なジョブアイコン、実行時変数の展開、通知の積み重ね、カウントダウンの進行は再現しません。選択した1件を表示します。
- 複数モニターの縮小図はACTのDPI基準で描画します。モニターごとにDPIが異なる場合はデスクトッププレビューを使って最終確認してください。
- URLの画像は自動取得しません。「HTTPS画像を取得」を押した場合に限り取得します。認証情報を含まない直接HTTPS URLのみ対応し、リダイレクトは拒否します。画像は20 MB、32メガピクセルまで、静止画の先頭フレームを表示します。画像が見つからない場合は参照文字列を表示します。
- タイムラインの内容を操作指示として扱いません。XML内のコードやリンクの自動実行はありません。

## 言語と支援

英語・日本語・簡体字中国語・韓国語に対応します。初回はOSのUI言語を参照し、それ以降は保存した選択を使用します。未翻訳キーは英語へフォールバックします。

支援は完全に任意です。支援しなくてもすべての機能を同じように利用できます。[Roxyz0501の開発をKo-fiで支援](https://ko-fi.com/roxyz0501)。支援タブのボタンを明示的に押したときだけブラウザを開きます。

## 更新と配布仕様

専用の「更新」タブに現在版、最新版、更新内容、確認状態を表示します。起動時確認は既定ONで変更可能です。通信エラーでも編集は継続できます。draft・prereleaseを除外し、SemVerで安定版を比較します。「後で」でその版の通知を保留できます。

「更新準備」を選んだときだけGitHub Release assetをダウンロードします。HTTPS、所有者・リポジトリ・タグ・asset名、ZIPのSHA-256、ファイル一覧、DLL／Updaterのアセンブリ名・会社名・バージョンを検証します。トークンは不要で、プラグインへ資格情報を埋め込みません。

Releaseのファイル名は`TimelineNoticeEditor-vX.Y.Z.zip`と`TimelineNoticeEditor-vX.Y.Z.sha256`です。マニフェストは`sha256  ZIPファイル名`の1行です。ZIP直下には次の3ファイルのみを含みます。

- `TimelineNoticeEditor.dll`
- `TimelineNoticeEditor.Updater.exe`
- `README.md`

アップデータは検証したZIP内の実行ファイルを使用します。ACTのプラグイン登録情報から取得したDLLの実パスを対象とし、ACT終了後にバックアップして置換します。失敗時は復元します。更新準備後にACTを終了・再起動してください。

## ビルド・テスト・Release

```powershell
./build.ps1 -ActPath 'path/to/Advanced Combat Tracker.exe'
```

.NET SDKと.NET Framework 4.8の開発環境が必要です。ビルド、テスト、ZIP作成、SHA-256作成、配布物の検証を実行します。ACT本体は同梱しません。

任意の追加検証：

```powershell
./tests/TimelineNoticeEditor.Tests/bin/Release/net48/TimelineNoticeEditor.Tests.exe --ui
./tests/TimelineNoticeEditor.Tests/bin/Release/net48/TimelineNoticeEditor.Tests.exe --timeline 'path/to/timeline.xml'
./tests/TimelineNoticeEditor.Tests/bin/Release/net48/TimelineNoticeEditor.Tests.exe --release
```

`--ui`は4言語の画面画像を`artifacts/`へ生成します。`--timeline`は参照画像を読み込み、コピーに保存テストを行います。元のXMLは変更しません。`--release`は実際の公開Release ZIPとSHA-256を取得・検証します。

バージョンを本体とUpdaterのcsprojで揃え、`main`へpushした後に`vX.Y.Z`タグをpushします。GitHub Actionsはビルド・テスト・配布物検証が成功した場合だけ安定版Releaseを公開します。外部ActionsはコミットSHA固定、ビルド権限はread、Releaseジョブのみcontents:writeです。

## English summary

An ACT plugin for selecting SPESPE timeline notices, resolving their referenced images, and adjusting positions with numeric controls or draggable previews. Image notices store individual coordinates in XML; visual notices share the global position in Timeline.config. Every save creates a backup. External modifications stop saving. A separate Update tab downloads only explicitly requested, verified GitHub Release packages. Support through Ko-fi is optional and does not change feature availability.

## Compatibility references

Behavior was checked against the public Hojoring implementation: [image notices](https://github.com/anoyetta/ACT.Hojoring/blob/master/source/ACT.SpecialSpellTimer/ACT.SpecialSpellTimer.Core/RaidTimeline/TimelineImageNoticeModel.cs), [image overlay](https://github.com/anoyetta/ACT.Hojoring/blob/master/source/ACT.SpecialSpellTimer/ACT.SpecialSpellTimer.Core/RaidTimeline/Views/TimelineImageNoticeOverlay.xaml), [visual overlay](https://github.com/anoyetta/ACT.Hojoring/blob/master/source/ACT.SpecialSpellTimer/ACT.SpecialSpellTimer.Core/RaidTimeline/Views/TimelineNoticeOverlay.xaml). Hojoring source, timelines, images and ACT binaries are not distributed in this repository. No license has been selected for this new project.
