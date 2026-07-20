# TimeRenderer 開発ルール

タイムラインによる作業記録アプリ（WPF / .NET 10 / MVVM）。

## Git 運用（必須）

コミットは必ず次の手順で行うこと。**master へ直接コミットしない**。

1. 作業開始前に `feature/xxx` ブランチを切る（例: `feature/refactor-views`）
2. 変更は featureブランチへコミットする
3. 完了したら master へマージする（`git merge --no-ff feature/xxx`）

- コミットメッセージは日本語で、既存の履歴のトーンに合わせる
- 機能追加とリファクタリングは別コミットに分ける

## この環境（Cowork サンドボックス）の注意

- サンドボックスからはファイル削除（unlink）ができない。`.git` 内に古いロックファイル
  （`index.lock` / `HEAD.lock`）が残って git が失敗する場合は、削除ではなく
  `mv .git/index.lock .git/stale-任意名.old` のようにリネームで退避してから再実行する
- ファイルの削除が必要なリファクタリングでは、削除の代わりにリネーム（`mv`）で対応する
- .NET SDK が無いためビルド検証ができない。C#/XAML の大きな変更後は、ユーザーに
  Visual Studio でのビルド・動作確認を依頼すること
- 文字コードは UTF-8(BOM付き)・改行は CRLF で保存する

## コード方針

- MainViewModel は責務ごとの partial 分割（一覧は MainViewModel.cs の冒頭コメント参照）
- ビューは UserControl 単位（DayWeekView / TimelineView / StatsView / MemoPanel / SettingsPanel）
- 日/週ビューの縦スケールは `Helpers/LayoutConstants.PixelsPerHour`（1時間=60px）に集約
- 設定項目の追加時は AppSettings / BuildSettings / ApplySettings の3箇所をセットで更新
- データ保存はデバウンス＋アトミック書き込み＋バックアップ世代管理（JsonFileRepository）を壊さない
