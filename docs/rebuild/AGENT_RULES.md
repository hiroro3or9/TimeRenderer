# AGENT_RULES — TimeRenderer 再構築の実装規約

このファイルは毎セッションの先頭で読むこと。ガイドと矛盾したら**ガイドを優先**し、矛盾があったことを報告する。

## 1. 技術スタック（固定）

- .NET 10 / C# 最新 / WPF。TargetFramework `net10.0-windows`
- 本体 csproj：`OutputType=WinExe`, `UseWPF=true`, `UseWindowsForms=true`（トレイ・フォルダ選択に使う）,
  `Nullable=enable`, `ImplicitUsings=enable`, `AllowUnsafeBlocks=true`, `ApplicationIcon=Assets\AppIcon.ico`
- 本体に NuGet パッケージを追加しない（MVVM は `ObservableObject` / `RelayCommand` を自作）
- テストは別プロジェクト `TimeRenderer.Tests`（TUnit 単体パッケージ、`OutputType=Exe`、`Microsoft.NET.Test.Sdk` は入れない）
- ソリューション直下に `global.json` を置き `{"test":{"runner":"Microsoft.Testing.Platform"}}` を書く（無いと `dotnet test` が TUnit を実行できない）
- 本体に `[assembly: InternalsVisibleTo("TimeRenderer.Tests")]`
- アイコン `Assets\AppIcon.ico` は `<Resource>` としても含める。本物が無ければ仮の ico を置く（無いとビルドが通らない）

## 2. 構成の規約

- 名前空間 = フォルダ：`TimeRenderer.Models / Services / Helpers / ViewModels / Views / Views.Dialogs / Controls / Converters / Infrastructure`
- ViewModel は **`MainViewModel` 1つを責務ごとの partial ファイルに分割**する（`MainViewModel.Todos.cs` など）。
  別の ViewModel クラスを増やさない（小さな表示用 record / class は可）
- 画面は UserControl 単位（`DayWeekView`, `TimelineView`, `StatsView`, `TodoPanel` …）。DataContext は MainWindow から継承
- ダイアログは `IDialogService` 経由で開く（VM から Window を直接 new しない）。テスト用に `TestDialogService` を作る
- 画面状態に依存しない計算は `Helpers/` の static クラスに純粋関数として置き、テストを書く
- ドラッグ・ヒットテスト・フォーカス・スクロールなど**ビュー固有の処理はコードビハインドで書いてよい**
- コンテキストメニューは視覚ツリー外なので、`MenuItem.Parent(ContextMenu).PlacementTarget.DataContext` から対象を解決してコマンドを実行する

## 3. 見た目の規約

- 色は `{DynamicResource トークン}` で参照する（トークンは `guide/03`）。ハードコードしてよいのはガイドに明記された箇所だけ
- テーマ切替は `DarkColors.xaml` を MergedDictionaries の末尾へ追加/削除する方式（`App.ApplyTheme`）
- UI文字列は日本語。ガイドに「」付きで例が出ている文言はそのまま使い、出ていないものは同じ調子（丁寧語・短文・句点あり）で揃える
- アイコン文字は `Segoe MDL2 Assets` のグリフ（`&#xE721;` 形式）を使い、画像ファイルは増やさない

## 4. 振る舞いの規約（壊してはいけないもの）

- 予定データと ToDo の保存は **700ms デバウンス＋アトミック書き込み＋バックアップ世代管理**。書き込みに失敗したら未保存のまま持ち、30秒後に再試行する（`guide/15`）
- 読み込みに失敗した（`LoadStatus.Failed`）セッションでは**保存しない**
- 取り消し履歴は予定と ToDo で**1本**。復元は必ず**元のインスタンスへ書き戻す**（複製で置き換えない）
- 一括更新中・読み込み中・Undo 適用中は、変更ハンドラで再計算と保存を抑止し、最後に1回だけ行う
- 設定項目を追加したら `AppSettings` と `MainViewModel.SettingsMapping.cs` の binding 一覧を**両方**更新する（契約テストで検出）
- JSON に保存される enum の数値・プロパティ名を変えない（`guide/02` の「保存形式」の表を守る。表に無い項目を保存対象に足すときは報告する）

## 5. Win32 / P/Invoke

- `LibraryImport` に統一（`DllImport` は使わない）。`bool` 戻り値は `[return: MarshalAs(UnmanagedType.Bool)]`
- コールバックはデリゲートではなく `delegate* unmanaged[Stdcall]<...>` ＋ `[UnmanagedCallersOnly(CallConvs = new[]{typeof(CallConvStdcall)})]` の static メソッド。中身は必ず try/catch（ネイティブへ例外を返すと落ちる）

## 6. WinForms 併用による名前衝突

`UseWindowsForms=true` のため、WPF 側の型は using エイリアスで明示する：
`using Brush = System.Windows.Media.Brush;` `using Point = System.Windows.Point;` `using MessageBox = System.Windows.MessageBox;`
`using ContextMenu = System.Windows.Controls.ContextMenu;` `using MenuItem = System.Windows.Controls.MenuItem;`
`using UserControl = System.Windows.Controls.UserControl;` `using KeyEventArgs = System.Windows.Input.KeyEventArgs;` など。

## 7. 作業の進め方

- **今回のフェーズの範囲だけ**実装する。後のフェーズの機能は、フェーズ節の「スタブ」欄に挙がった空実装だけ置く
- フェーズ終了時に：ビルドが警告0・エラー0 → テスト全件成功 → フェーズ節の「手動確認」を1巡 → 変更ファイル一覧を報告
- **自分で決めてよいこと**：寸法・余白・配置・色の割り当て、ガイドに例の無い UI 文言、クラスやメソッドの名前、内部の実装方法。
  決めたものはフェーズ終了時の報告に一言添える
- **質問すること**：振る舞い（操作したら何が起きるか）、保存形式、取り消し履歴への積み方がガイドから決まらないとき。
  推測で仕様を足さず、ガイドの該当箇所を示して聞く
- ファイルは UTF-8（BOM付き）・改行 CRLF
- コメントは「なぜそうしたか」を日本語で残す（ガイドの（理由）を転記してよい）
