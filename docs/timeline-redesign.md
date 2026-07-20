# タイムラインビュー 再設計案

対象: `ViewMode.SprintTimeline`

> **実装状況**: Phase 1〜6 をすべて実装済み。実装後の構成は末尾の「実装後のファイル構成」を参照。
> 以下の 1章（現状の問題）は再設計前の記録として残している。

## 1. 現状の問題

### 1-1. スケール（根本原因）

`DateToSprintTimelinePositionConverter` が全体を画面幅に押し込む:

```csharp
double sprintWidth = actualWidth / sprints.Count;   // 5固定
```

- 5スプリント ≒ 105日を約1400pxに圧縮 → **1日あたり約13px**
- 数時間の作業アイテムは1〜2px。掴めない、読めない、存在に気づけない

### 1-2. 時間軸が非線形（設計バグ）

`GetPositionForDate` はスプリントごとに幅を均等配分し、内部を日数比で割る。
結果、**1週間スプリントと3週間スプリントが同じ幅**になる。
バーの長さが実時間量を表さないため、タイムラインとして機能していない。

### 1-3. 時刻が捨てられている

```csharp
var displayStart = startTime.Date < totalStart ? totalStart : startTime.Date;
var displayEnd   = endTime.Date > totalEnd ? totalEnd : endTime.Date;
```

`.Date` で丸めているため、30分の記録も終日予定も**幅は同じ1日分**。
作業記録アプリとして最も重要な「どれだけ時間を使ったか」が可視化されていない。
ズームで解像度を上げても、この丸めがある限り直らない。

### 1-4. 縦の密度

`ItemsPanelTemplate` が `StackPanel`、`Height="35" Margin="0,4"` → 43px/件。

- 100件で4300pxの縦スクロール
- 各行は横方向に99%が空白
- `VirtualizingStackPanel` でないため件数増加で描画コストが線形に増える

### 1-5. バーの可読性・操作性

| 問題 | 箇所 |
|---|---|
| MinWidth なし | Style の Width Setter |
| ToolTip なし | DataTemplate 全体 |
| タイトルがバー内固定（`TextTrimming`） | 内側 TextBlock |
| `Foreground="#1E293B"` ハードコード | ダークモード・濃色カテゴリで潰れる |
| ドラッグ移動・リサイズ不可 | `MainWindow.Drag.cs` は Canvas 前提で日/週専用 |
| 現在時刻ライン・日付罫線なし | 背景 ItemsControl はスプリント境界のみ |
| ホバー強調なし | — |

---

## 2. 設計方針

**時間軸を「px/日」の一次元スケールに統一し、横スクロール＋ズームで解像度を確保する。**

画面幅に合わせる（fit-to-width）のをやめる。これが全ての起点。

---

## 3. 実装フェーズ

### Phase 1: スケールの作り直し（最優先）

**3-1. `TimelineScale` クラスを新設**

`Helpers/TimelineScale.cs`:

```csharp
public sealed class TimelineScale
{
    public DateTime Origin { get; }        // 表示範囲の開始（日付境界）
    public double PixelsPerDay { get; }    // ズーム倍率

    public double ToX(DateTime t)
        => (t - Origin).TotalDays * PixelsPerDay;   // 時刻を含めて連続変換

    public DateTime ToTime(double x)
        => Origin.AddDays(x / PixelsPerDay);

    public double TotalWidth(DateTime end) => ToX(end);
}
```

ポイント:

- `.Date` で丸めない。`TotalDays` は小数を返すので**時刻がそのまま反映される**
- スプリント単位ではなく**日単位の連続軸**。スプリント長の違いが自然に幅へ反映される
- 逆変換 `ToTime` があるのでドラッグ実装が可能になる

**3-2. ズームレベル**

| レベル | PixelsPerDay | 用途 |
|---|---|---|
| 時間 | 480 | 1時間20px。日/週ビュー相当の精度 |
| 日 | 120 | 既定。1日が読める幅 |
| 週 | 40 | スプリント全体の俯瞰 |
| スプリント | 12 | 現在と同等。全体把握用 |

- `Ctrl + ホイール` でズーム。**カーソル位置の時刻を固定点**にする（ズーム後も同じ日付がカーソル下に来る）
- ツールバーにズームスライダ or `− ⊡ ＋` ボタン
- `AppSettings.TimelinePixelsPerDay` として永続化

**3-3. 横スクロール**

`ScrollViewer` に `HorizontalScrollBarVisibility="Auto"` を追加。
既存の `ScrollViewerHelper.EnableMiddleButtonScroll` を付ければ中ボタンドラッグで縦横同時にパンできる（既に実装済みの資産を流用）。

**3-4. Converter の置き換え**

`DateToSprintTimelinePositionConverter` は廃止し、専用 `Panel` に置き換える。

MultiBinding + Style Setter でアイテムごとに `ActualWidth` を購読する現在の方式は、
リサイズのたびに全アイテムの MultiBinding が再評価されて重い。
`MeasureOverride` / `ArrangeOverride` を持つ `TimelineCanvas : Panel` を作り、
子要素の DataContext から一括配置するほうが速く、コードも短くなる。

---

### Phase 2: レーン詰め（縦の密度）

1アイテム1行をやめ、**時間的に重ならないアイテムを同じ行にまとめる**。

`Helpers/ScheduleLayoutHelper.AssignColumnsToCluster` と同じ貪欲法がそのまま使える
（あちらは縦方向の列割り当て、こちらは横方向の行割り当てで、アルゴリズムは同一）。

```csharp
// laneEndTimes[i] = レーン i の最終終了時刻
int lane = laneEndTimes.FindIndex(t => t <= item.StartTime + Gap);
```

- `Gap` は「px換算で最低8px空く時間」= ラベルが重ならない余白
- 行数が激減するので、1行を 28px → 40px と厚くしても総高さは大幅に減る

**グルーピングの切替**を用意する:

| モード | 挙動 |
|---|---|
| 詰める（既定） | 重ならないものを同一レーンへ。最も密度が高い |
| カテゴリ別 | カテゴリごとにレーン群を分け、左に固定ヘッダ列。何にどれだけ使ったかが一目で分かる |
| フラット | 現状どおり1件1行。件数が少ないときの見やすさ用 |

カテゴリ別は作業記録アプリとして特に価値が高い。`MainViewModel.Stats.cs` の集計と
視覚的に対応するため、統計ビューとの行き来がしやすくなる。

---

### Phase 3: バーの表現

**3-1. 最小幅とラベル外出し**

```
実幅 = ToX(End) - ToX(Start)
描画幅 = Math.Max(実幅, 3)      // 3px 未満でも必ず見える
ヒット領域 = Math.Max(実幅, 12)  // 掴める幅を別に確保
```

描画幅とヒット領域を分けるのが要点。見た目の正確さを保ったまま操作可能にする。

ラベルは「バー内に収まるか」で出し分ける:

- 収まる → バー内に描画（現状どおり）
- 収まらない → **バーの右隣に描画**（バー自体は正しい幅のまま）
- 右隣も画面外 → 省略し、ToolTip に委ねる

**3-2. ToolTip**

全バーに付ける。内容: タイトル / カテゴリ / `MM/dd HH:mm 〜 HH:mm` / 所要時間 / メモ冒頭。
狭いバーの唯一の情報源になるので必須。

**3-3. 文字色の自動判定**

`#1E293B` 固定をやめ、背景輝度から白/黒を選ぶ:

```csharp
double luminance = 0.299*r + 0.587*g + 0.114*b;
return luminance > 140 ? Colors.Black : Colors.White;
```

`Converters/` に `BrushToContrastTextConverter` として追加。
既存のカテゴリ色・ダークモードの両方で読めるようになる。

**3-4. 状態表現**

- ホバー: 明度を上げ、境界線を強調
- 選択中: 太い外枠（キーボード操作の前提になる）
- 記録中のアイテム: 右端をアニメーションで脈動、または点線で「継続中」を示す

---

### Phase 4: 時間軸の文脈

**4-1. 二段ヘッダー（ルーラー）**

現状はスプリント名のみ。ズームレベルに応じて下段を切り替える:

| ズーム | 上段 | 下段 |
|---|---|---|
| 時間 | 日付 | 時刻（3時間ごと） |
| 日 | スプリント / 月 | 日付（`d(曜)`） |
| 週 | スプリント | 週（`MM/dd週`） |
| スプリント | 年月 | スプリント名 |

ヘッダーは `ScrollViewer` の水平オフセットに追従させる（縦は固定、横だけ同期）。

**4-2. 背景の罫線**

- 日境界: 細い線
- 週境界（月曜）: やや濃い線
- スプリント境界: 現状の縦線を維持
- 土日・無効曜日（`EnabledDaysOfWeek` 外）: 背景を淡くシェード
- 今日: 列全体を淡く強調

**4-3. 現在時刻ライン**

赤い縦線 + 上端に時刻ラベル。1分ごとに更新。
「今どこにいるか」が分かるだけで体感が大きく変わる。

**4-4. 範囲外インジケータ**

現在の実装は範囲外アイテムを `-10000` に飛ばして隠している。
代わりに、左右の端に `◀ 3件` のようなチップを出し、クリックでその位置へスクロール。

---

### Phase 5: 直接操作

**5-1. ドラッグ移動・リサイズ**

`TimelineScale.ToTime` があるので日/週ビューと同じロジックが使える。
`MainWindow.Drag.cs` の `PixelsPerHour` 固定・Y軸前提を抽象化して共有する:

```csharp
// 現状: double deltaHours = (pos.Y - _dragStartPos.Y) / PixelsPerHour;
// 共通化: axis.DeltaTime(pos - _dragStartPos)
```

- スナップ単位はズーム連動（時間ズーム→15分、日ズーム→1時間、週ズーム→1日）
- 端をつまんで伸縮、中央を掴んで移動。既存の `GetZone` を横向きに転用

**5-2. 空き領域のドラッグで新規作成**

何もない場所を横にドラッグ → その時間範囲で新規アイテム作成ダイアログ。
記録し忘れた作業を後から埋める操作が一気に速くなる。

**5-3. キーボード**

| キー | 動作 |
|---|---|
| `←` `→` | 前後のアイテムを選択 |
| `Enter` | 編集ダイアログ |
| `Delete` | 削除 |
| `Ctrl` `+` / `−` | ズーム |
| `Home` / `T` | 今日へスクロール |
| `F` | 選択アイテムに合わせてズーム |

**5-4. スクロール操作**

| 操作 | 動作 |
|---|---|
| ホイール | 縦スクロール |
| `Shift` + ホイール | 横スクロール |
| `Ctrl` + ホイール | ズーム（カーソル位置固定） |
| 中ボタンドラッグ | 全方向パン（`ScrollViewerHelper` 流用） |

---

### Phase 6: 情報密度

**6-1. レーン左端の固定ラベル列**

カテゴリ別モードのとき、横スクロールしても消えないカテゴリ名 + 合計時間。

**6-2. スプリント別サマリー**

ヘッダーのスプリント名の下に、そのスプリントの総記録時間と上位カテゴリを小さく表示。
`MainViewModel.Stats.cs` の集計をそのまま使える。

**6-3. 密度ヒートバー**

タイムライン最下部に、日ごとの総記録時間を細い棒グラフで敷く。
ズームアウト時に「どこが忙しかったか」が個々のバーを読まずに分かる。

**6-4. 検索連動**

`MainViewModel.Search.cs` の検索語にマッチしたバーをハイライトし、
他を減光する。ヒット位置へジャンプするボタンも添える。

---

## 4. 推奨する着手順

| 順 | 内容 | 効果 | 規模 |
|---|---|---|---|
| 1 | Phase 1（スケール + 横スクロール + ズーム） | 極大 | 中 |
| 2 | Phase 3（最小幅・ラベル外出し・ToolTip・文字色） | 大 | 小 |
| 3 | Phase 4-2/4-3（罫線・現在時刻ライン） | 大 | 小 |
| 4 | Phase 2（レーン詰め） | 大 | 中 |
| 5 | Phase 4-1（二段ルーラー） | 中 | 中 |
| 6 | Phase 5（ドラッグ・キーボード） | 中 | 大 |
| 7 | Phase 6（サマリー・ヒートバー・検索連動） | 中 | 中 |

1〜3 までで「使いづらい」はほぼ解消する。
4 以降は「快適」「速い」の領域。

## 5. 実装後のファイル構成

| ファイル | 役割 |
|---|---|
| `Helpers/TimelineScale.cs` | px/日の連続スケール。`ToX` / `ToTime` / `PixelsToDuration` |
| `Helpers/TimelineLaneHelper.cs` | レーン詰め（貪欲法）。カテゴリ別のグループ割り当ても担当 |
| `ViewModels/TimelineBar.cs` | バー1本の計算済みレイアウト。選択・減光のみ変更通知つき |
| `ViewModels/TimelineLaneGroup.cs` | レーン群（カテゴリ別）とスプリント帯 |
| `ViewModels/TimelineTick.cs` | ルーラーの目盛り・日単位の列・密度バー |
| `ViewModels/MainViewModel.Timeline.cs` | スケール適用・レーン割り当て・選択・ズーム・キーボードコマンド |
| `ViewModels/MainViewModel.TimelineDecorations.cs` | 目盛り・罫線・現在時刻ライン・範囲外・密度・サマリー |
| `Converters/BrushToContrastTextConverter.cs` | 背景輝度から文字色を自動判定 |
| `Views/MainWindow.Timeline.cs` | ズーム・スクロール同期・横ドラッグ・範囲作成・キーボード |
| `Views/MainWindow.xaml` | タイムライン領域（操作バー / ルーラー / 本体 / 密度バー） |
| `Models/AppSettings.cs` | `TimelinePixelsPerDay`, `TimelineGroupMode` |
| `Converters/DateToSprintTimelinePositionConverter.cs` | **削除** |

### 実装上の判断（設計案からの変更点）

- **配置用 Panel は作らず、VM で座標を計算して `Canvas` に流す方式にした。**
  `MeasureOverride` を持つカスタム Panel より、計算済みの `TimelineBar` を
  `ItemContainerStyle` の `Canvas.Left` / `Canvas.Top` に流すほうが単純で、
  スケール変更時の再計算箇所も1か所に収まるため。

- **横ドラッグは `MainWindow.Drag.cs` を一般化せず、独立した実装にした。**
  日/週ビューのドラッグは Canvas と縦軸に強く結びついており、
  軸を抽象化すると既存の動作を壊すリスクが割に合わないと判断した。

- **ルーラー上段はスプリント固定にした。**
  設計案ではズームに応じて上段も切り替える想定だったが、
  このアプリではスプリントが主要な文脈なので常に見えているほうが有用。
  切り替えるのは下段（時刻 / 日 / 週 / 月）のみ。

- **目盛りには生成上限（1200個）を設けた。**
  `Canvas` は仮想化しないため、上限を超える場合は1段階粗い粒度へ落とす。

- **ズーム倍率の保存は600msデバウンスした。**
  Ctrl+ホイールは1操作で何度も発火するため、都度保存するとI/Oで引っかかる。

## 6. 追加改善（パフォーマンス・表示範囲）

### 6-1. 描画の仮想化

`Canvas` は仮想化を行わないため、バー・目盛り・日単位の背景列・密度バーが
表示範囲全体ぶん実体化されていた。最大構成（25スプリント＝約525日、最大ズーム）では
目盛りだけで数千個になる。

`MainViewModel.TimelineViewport.cs` を追加し、**実体化済みの窓**という考え方を導入した。

- 全件は `_allBars` / `_allTicks` / `_allDayColumns` / `_allDensityBars` に保持する
- 公開プロパティ（`TimelineBars` など）には、窓に交差するものだけを切り出して入れる
- 窓はビューポートの前後に1画面ぶんの余裕（`ViewportOverscan`）を持つ
- **ビューポートが窓の内側にある限り再構築しない**（ヒステリシス）ので、
  通常のスクロールでは数回に1度しか切り出しが起きない

ビューポートは `TimelineBodyScroll_ScrollChanged` から `SetTimelineViewport` で通知する。

注意点:

- `TimelineContentWidth` / `TimelineContentHeight` は全範囲のままなので、
  切り出してもスクロールバーの長さは変わらない
- 選択移動（←→）と検索の減光は画面外も対象にする必要があるため、
  `AllTimelineBars`（切り出し前）を辿る
- ズームやスプリント数の変更で窓の座標系の意味が変わるため、
  `InvalidateRealizedWindow()` で無効化し、次の通知で張り直す
- ビューポートが未通知の間は絞り込まない。何も描かれないより多く描くほうが安全側

### 6-2. ドラッグ中の再計算の抑制

従来はマウス移動のたびに `RecalculateLayout` → `UpdateTimelineItems` が走り、
レーンの再割り当てと全装飾の再生成が起きていた。
そのため掴んだバーが行を飛び、要素の再生成でマウス移動も引っかかっていた。

`BeginTimelineDragLayout()` / `EndTimelineDragLayout()` で軽量経路に切り替える。
ドラッグ中は:

- レーンは直前の割り当て（`_lastLanes`）をそのまま使う（行が飛ばない）
- 目盛り・背景・スプリント帯・密度バーは作り直さない（時間軸は動かないため）
- `SetTimelineBarsOnly` でバーだけを差し替える

確定・取り消し時に `EndTimelineDragLayout()` で通常経路に戻して組み直す。

### 6-3. 表示範囲の可変化

5スプリント固定をやめ、`TimelineSprintCount`（3 / 5 / 9 / 15 / 25）で切り替えられるようにした。
px/日スケールにしたことで範囲の広さと解像度が独立したため、
「25スプリントをズームアウトして年単位で俯瞰する」といった使い方ができる。

`UpdateVisibleDays` のスプリント取得範囲も、要求数に応じて広げている
（従来は前後3か月固定で、9スプリント以上を要求すると足りなかった）。

## 7. 未実装・今後の候補

- ~~取り消し（Undo/Redo）~~ → 実装済み。[undo-redo.md](undo-redo.md) を参照
- 自動テスト。`TimelineScale` の座標変換、レーン詰め、日またぎ分割、
  スプリント算出はいずれも純粋関数でテストしやすい
- 現在時刻ラインの日/週ビューへの展開
