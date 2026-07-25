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

- サンドボックスからはファイル削除（unlink）ができない。
- ファイルの削除が必要なリファクタリングでは、削除の代わりにリネーム（`mv`）で対応する

### git 操作の厳守事項（ロック残骸でユーザー側の git が壊れるため）

サンドボックスでは git が自分のロックファイルを削除できず、**`git status` を含む
ほぼ全てのコマンドの後に `.lock` が残留する**。これが残るとユーザー側の git が
「Another git process seems to be running」で一切使えなくなる。よって:

1. **git 操作を伴う作業の最後に、必ずロックを掃討してから応答を終える**:
   ```sh
   mkdir -p .git/zz-trash
   for l in $(find .git -name '*.lock'); do mv "$l" ".git/zz-trash/$(basename $l).$RANDOM"; done
   find .git -name '*.lock' | wc -l   # 0 を確認。この確認以降 git コマンドを実行しない
   ```
2. **退避先は必ず `.git/zz-trash/`**。`.git/refs/` 配下や `.git/` 直下に
   リネームして置き去りにしない。特に refs/ 配下に残すと git が ref として
   解釈し `fatal: bad object` で fetch/push まで壊れる（実際に起きた）
3. **`git checkout` でファイルの削除・入れ替えが必要になる切り替えはしない**
   （unlink 失敗で中途半端な状態になる）。master へのマージは checkout せずに:
   ```sh
   T=$(git rev-parse feature/xxx^{tree})
   M=$(git commit-tree $T -p $(git rev-parse master) -p $(git rev-parse feature/xxx) -m "Merge branch 'feature/xxx'")
   git update-ref refs/heads/master $M
   git symbolic-ref HEAD refs/heads/master
   git reset --mixed
   ```
   （成功パスはリネームで確定するため安全。作業ツリーは feature の内容のまま一致する）
4. user.name / user.email は未設定なのでコミットは
   `git -c user.name="hiroro" -c user.email="hiroki02130307@gmail.com" commit ...` の形で行う
5. `.git/objects/**/tmp_obj_*` などの残骸はサンドボックスからは消せない。
   溜まったらユーザーに PowerShell での削除を依頼する
- .NET SDK が無いためビルド検証ができない。C#/XAML の大きな変更後は、ユーザーに
  Visual Studio でのビルド・動作確認を依頼すること
- 文字コードは UTF-8(BOM付き)・改行は CRLF で保存する

## コード方針

- MainViewModel は責務ごとの partial 分割（一覧は MainViewModel.cs の冒頭コメント参照）
- ビューは UserControl 単位（DayWeekView / TimelineView / StatsView / MemoPanel / SettingsPanel）
- 日/週ビューの縦スケールは `Helpers/LayoutConstants.PixelsPerHour`（1時間=60px）に集約
- 設定項目の追加時は AppSettings / BuildSettings / ApplySettings の3箇所をセットで更新
- データ保存はデバウンス＋アトミック書き込み＋バックアップ世代管理（JsonFileRepository）を壊さない
