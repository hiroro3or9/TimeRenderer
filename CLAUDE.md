# TimeRenderer 開発ルール

タイムラインによる作業記録アプリ（WPF / .NET 10 / MVVM）。

## Git 運用（必須）

**Claude は git コマンドを一切実行しない。** コミット・ブランチ操作・マージは
すべてユーザーが自分の手元で行う。

理由: サンドボックスの git は自分のロックファイル（`.lock`）を削除できず、
`git status` のような読み取りコマンドの後ですら残留する。残ると手元の git が
「Another git process seems to be running」で一切使えなくなる。
`.git/objects/**/tmp_obj_*` の残骸も同様に消せずに溜まっていく。

代わりに Claude は、作業が一区切りついたら**コミットメッセージ案を提示する**。

- ファイルを編集したら、**ブランチ名とコミットメッセージをセットで**応答の中に提示する
- 機能追加とリファクタリングは別コミットに分け、その旨も伝える
- `git add` すべきファイルが分かりにくい場合は、対象ファイルも列挙する

### コミットメッセージの書式（Conventional Commits）

```
<type>(<scope>): <要約>

- <何をしたか>
- <補足・なぜそうしたか>
```

- **type と scope は英語、要約と本文は日本語**で書く
- 要約は現在形の言い切りで、句点は付けない。**40文字以内**
  （例: `離席検知の閾値を設定から変えられるようにする`）
- **本文は必ず書く。** 要約の後に空行を1行入れ、`- ` の**箇条書きで2〜4項目**書く
- 各項目は1行で収める。「何をしたか」を先に並べ、要約だけでは意図が分からない
  場合は「なぜ必要だったか」の項目も添える
- 破壊的変更は本文の後に `BREAKING CHANGE: 内容` を置く

使う type:

| type | 用途 |
| --- | --- |
| `feat` | 機能追加・既存機能の振る舞いの拡張 |
| `fix` | バグ修正 |
| `refactor` | 振る舞いを変えない内部構造の変更 |
| `perf` | 性能改善 |
| `docs` | ドキュメント・コメントのみの変更（CLAUDE.md や docs/ 配下） |
| `style` | 整形のみ（書式・命名・using 整理など振る舞いに影響しないもの） |
| `build` | csproj・依存パッケージ・ビルド設定の変更 |
| `chore` | 上記に当てはまらない雑務 |

scope はコードの置き場所に合わせる。省略可だが、付けられるなら付ける:
`app-usage` / `away` / `timeline` / `dayweek` / `stats` / `memo` / `settings` /
`routines` / `undo` / `persistence` / `workday` / `theme`

例（何をしたかだけ。これが基本）:

```
fix(app-usage): UWP アプリが記録されない問題を修正

- 前面ウィンドウの取得を ApplicationFrameHost の子プロセスまで辿るようにした
- 取得できなかった場合は直前のアプリを引き継ぐ
```

例（理由も添える場合）:

```
refactor(app-usage): P/Invoke のコールバックを関数ポインタにする

- コールバックの受け口を delegate* unmanaged[Stdcall] の static メソッドに変えた
- デリゲートだと LibraryImport が使えず、GC 対策の保持も必要だった
```

なお `5f2a130` 以前の履歴はこの書式ではない。過去に合わせる必要はなく、
これ以降のコミットから Conventional Commits に揃える。

### ブランチ名の書式

```
feature/<英語のケバブケース>
```

- プレフィックスは基本 `feature/`。バグ修正でもリファクタでもこれでよい
- 名前は英小文字・数字・ハイフンのみ。2〜4語程度で短く
- scope に相当する語があれば先頭に置く（`feature/app-usage-...` のように）
- 作業内容そのものを表す。日付・番号・自分の名前は入れない

```
feature/away-threshold-setting
feature/app-usage-uwp-detection
feature/pinvoke-function-pointers
feature/commit-convention
```

ユーザー側の運用方針（Claude が実行するわけではないが、提案はこれに沿わせる）:

1. 作業開始前に作業ブランチを切る
2. 変更は作業ブランチへコミットする
3. 完了したら master へマージする（`git merge --no-ff <branch>`）

## この環境（Cowork サンドボックス）の注意

- サンドボックスからはファイル削除（unlink）ができない。
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
- P/Invoke は `LibraryImport` に統一する（`DllImport` と SYSLIB1054 の抑制は使わない）。
  コールバックはデリゲートではなく `delegate* unmanaged[Stdcall]` ＋ `[UnmanagedCallersOnly]`
  の static メソッドで受ける。ネイティブへ例外を返すと落ちるので中身は try/catch で囲む
