# 13. 設定パネル・管理パネル

どちらも MainWindow の右端に重ねるオーバーレイ（`design/04` §4 の排他・幅アニメーション）。**設定パネル＝アプリの振る舞い**、**管理パネル＝データのマスター**（カテゴリ・プロジェクトコード・定型タイトル・定期予定・スプリント）。

共通の外枠（両パネル同じ。幅だけ違う）：
```xml
<Border Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1,0,0,0" ClipToBounds="True">
  <Border.Effect><DropShadowEffect Color="Black" BlurRadius="10" ShadowDepth="-4" Opacity="0.1"/></Border.Effect>
  <Border.Style>
    <Style TargetType="Border">
      <Setter Property="Width" Value="400"/>   <!-- 管理パネルは 440 -->
      <Style.Triggers>
        <DataTrigger Binding="{Binding IsSettingsPanelVisible}" Value="False">   <!-- 管理は IsManagementPanelVisible -->
          <DataTrigger.EnterActions><BeginStoryboard><Storyboard>
            <DoubleAnimation Storyboard.TargetProperty="Width" To="0" Duration="0:0:0.15"><DoubleAnimation.EasingFunction><QuadraticEase EasingMode="EaseOut"/></DoubleAnimation.EasingFunction></DoubleAnimation>
          </Storyboard></BeginStoryboard></DataTrigger.EnterActions>
          <DataTrigger.ExitActions><BeginStoryboard><Storyboard>
            <DoubleAnimation Storyboard.TargetProperty="Width" To="400" Duration="0:0:0.15"><!-- 同じ Easing --></DoubleAnimation>
          </Storyboard></BeginStoryboard></DataTrigger.ExitActions>
        </DataTrigger>
      </Style.Triggers>
    </Style>
  </Border.Style>
  <Grid Width="400"> … </Grid>   <!-- 内側は固定幅（縮むアニメ中に中身が折り返さないように） -->
</Border>
```
設定行の定番レイアウト（以下「行」と書く）：
```xml
<Grid Margin="0,0,0,16">   <!-- 下マージンは項目ごとに 16 / 6 / 12 -->
  <Grid.ColumnDefinitions><ColumnDefinition Width="Auto" SharedSizeGroup="LabelG"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
  <TextBlock Text="ラベル" Style="{StaticResource FieldLabelStyle}" VerticalAlignment="Center" Margin="0,0,16,0"/>
  <CheckBox Grid.Column="1" IsChecked="{Binding …}" VerticalAlignment="Center"/>   <!-- または ComboBox Width 70 FontSize 13 ＋ 単位 TextBlock 13 TextSecondary Margin 8,0,0,0 -->
</Grid>
```
説明文（以下「注」）：`TextBlock TextWrapping=Wrap FontSize=11 Foreground={TextSecondaryBrush}`。区切り線：`Border BorderBrush={BorderBrush} BorderThickness=0,0,0,1 Margin=0,0,0,12`。

---

## 1. 設定パネル（Views/SettingsPanel.xaml、幅 400）

### 1.1 骨格
`Grid Width=400` 行 `[Auto | Auto | *]`
1. **ヘッダー** `Border 地 Background Padding 16,12 下線1` ＞ Grid［「設定」SemiBold 16 TextPrimary 縦中央 ｜ `Button「✕」IconButtonStyle 右 縦中央 TextSecondary → ToggleSettingsPanelCommand`］
2. **検索欄** `Border Height 36 Margin 14,10,14,6 CornerRadius 6 地 Background 枠 Border 1` ＞ Grid[Auto|*|Auto]：
   - `&#xE721;` MDL2 12 TextSecondary Margin 9,0,5,0
   - `TextBox x:Name=SettingsSearchBox` 枠0 透明 VerticalContentAlignment Center 13 TextPrimary ToolTip「設定名や説明を検索」TextChanged=SettingsSearchBox_TextChanged
   - プレースホルダ「設定を検索」13 TextMuted IsHitTestVisible False（Text が "" のときだけ Visible）
   - `Button x:Name=SettingsSearchClearButton「✕」24×24 Margin 0,0,5,0 IconButtonStyle TextSecondary Collapsed ToolTip「検索をクリア」Click`
3. **本文** `TabControl 地 Transparent 枠0 SelectedIndex 0`、テンプレートは `ContentPresenter ContentSource=SelectedContent` のみ（タブ見出しを出さない）＞ TabItem 1つ ＞ `ScrollViewer 縦Auto 横Disabled` ＞ `StackPanel Margin 24,4,24,24 地 Surface`：
   - `TextBlock x:Name=NoSettingsResultsText「該当する設定がありません」中央揃え 13 Margin 0,24 Collapsed TextSecondary`
   - 以下 8 つの `Expander x:Name=… Style=SettingsExpanderStyle`（Header は `TextBlock Style=SettingsSectionHeaderStyle`）。**既定はすべて閉じている**

### 1.2 セクション

**① AppearanceSettingsSection「外観」**
`Grid Margin 0,0,0,4 [*|Auto]`：StackPanel 縦中央［「ダークモード」FieldLabelStyle Margin 0 ／「暗い配色テーマを使用します」11 TextMuted］｜ トグルスイッチ：
```xml
<ToggleButton Grid.Column="1" IsChecked="{Binding IsDarkMode}" VerticalAlignment="Center" Width="44" Height="24" Padding="0" BorderThickness="0" Cursor="Hand">
  <!-- Template: Grid [ Border x:Name=track 44×24 CornerRadius 12 地 {MutedBackgroundBrush} 枠 {BorderBrush} 1
                      Ellipse x:Name=thumb 18×18 Fill {TextMutedBrush} 左 Margin 3,0,0,0 ]
       IsChecked=True → track 地/枠 {PrimaryBrush}, thumb Fill White, 右寄せ Margin 0,0,3,0
       IsMouseOver → thumb Opacity 0.85 -->
</ToggleButton>
```

**② DisplaySettingsSection「表示設定」** StackPanel：
- 行 Margin 0,0,0,16（`IsTimeRangeSettingsVisible` = 日/週のときだけ）：「表示時間範囲」｜ 横並び［ComboBox `{StartHourOptions}`(0〜23) `SelectedItem={DisplayStartHour, PropertyChanged}` Width 70 13 ｜「～」TextSecondary Margin 8,0 ｜ ComboBox `{EndHourOptions}`(1〜24) `{DisplayEndHour}` Width 70］
- 行 16：「時刻の刻み幅」｜［ComboBox `{SnapMinutesOptions}`(5,10,15,30) `{SnapMinutes}` Width 70 ToolTip「ドラッグでの移動・伸縮・範囲作成で時刻を丸める単位」｜「分単位」］
- 行 6：「隣の予定へ吸い付ける」｜ CheckBox `{IsMagnetSnapEnabled}` ToolTip「ドラッグ中、隣の予定の端・出勤退勤の線・現在時刻に近づくと吸い付きます」
- 注 Margin 0,0,0,16「吸い付いたときは細い線が出ます。Alt を押しながらドラッグすると、吸着も刻み幅も外れて1分単位で置けます。」
- 行 16（`IsDayOfWeekSettingsVisible` = タイムライン・統計・ふりかえり・今日以外）：「表示曜日」（VerticalAlignment Top、Margin 0,4,16,0）｜ WrapPanel：CheckBox「月」`{ShowMonday}`…「日」`{ShowSunday}`、各 Margin 0,0,12,6 13px。**「土」は Foreground Primary、「日」は Danger**

`ShowMonday` 等は `EnabledDaysOfWeek` の含有で get、set で `SetDayEnabled(day, value)`（`design/05` §1）。

**③ MiniBarSettingsSection「記録中のミニバー」**
- 注 Margin 0,0,0,12「記録を開始している間だけ、タイトルと経過時間を小さなバーで常に最前面に出します。他のアプリを使っている間も、何を記録中かが分かります。」
- 行 6：「記録中にミニバーを出す」｜ CheckBox `{IsMiniRecordingBarEnabled}`
- 注 Margin 0,0,0,4「バーはドラッグで好きな場所へ移せ、次回もその位置に出ます。クリックすると TimeRenderer を前面に出し、右の■ で記録を停止できます。」
- 注 Margin 0,0,0,4「バーをクリックしても、それまで作業していたアプリから入力先は移りません。Alt+Tab の一覧にも現れません。」

**④ AwaySettingsSection「離席・中断の検知」**
- 注 12「記録中の無操作・スリープ・画面ロックを検知します。記録は自動では変更されず、停止したときに除外するかを確認します。」
- 行 16：「離席を検知する」｜ CheckBox `{IsAwayDetectionEnabled}`
- 以下は `IsAwayDetectionEnabled` のときだけ表示：
  - 行 16：「離席とみなす時間」｜［ComboBox `{AwayThresholdOptions}` `{AwayThresholdMinutes}` Width 70 ｜「分の無操作」］
  - 行 6：「検知したときの扱い」｜ ComboBox 左寄せ Width 180 13 `{AwayHandlingOptions}` `{SelectedAwayHandlingOption}`
  - 注 4「「常に除外する」を選ぶと確認画面を出さずに離席分を差し引きます。差し引いた内容は通知され、Ctrl+Z で元に戻せます。」

**⑤ AppUsageSettingsSection「使用アプリの記録」**
- 注 12「記録の実行中に前面にあったアプリを自動で控えます。予定を右クリック →「この時間の使用アプリ」で、その時間帯に実際に何を使っていたかの内訳を確認できます。」
- 行 6：「使用アプリを記録する」｜ CheckBox `{IsAppUsageTrackingEnabled}`
- 注 4「収集するのは出勤から退勤までの間です。未記録の帯を埋めるときの手がかりに使います。データはこの PC の中（データフォルダ）にのみ保存され、60日を過ぎた分は自動で削除されます。」
- 注 4「ウィンドウタイトル（ファイル名・URL など中身が出るもの）を残すのは記録中だけです。記録していない時間は、どのアプリを何分使ったかだけが残ります。」

**⑥ GitSettingsSection「Git のコミット履歴」**
- 注 12「登録したリポジトリのコミットを、未記録の時間を埋めるときのタイトル候補と、退勤時のふりかえりの材料に使います。使用アプリは「どこで作業していたか」しか分かりませんが、コミットメッセージは「何をしていたか」を残しています。」
- 行 6：「コミット履歴を使う」｜ CheckBox `{IsGitCommitLookupEnabled}`
- `{GitStatusText}` Wrap 11 Margin 0,2,0,10 TextMuted
- 「リポジトリ」FieldLabelStyle Margin 0,0,0,6
- ItemsControl `{GitRepositories}` Margin 0,0,0,8：
  ```
  Border 枠1 CornerRadius 4 Padding 8,6 Margin 0,0,0,6 地 Surface > StackPanel
    Grid[Auto|*|Auto]: CheckBox {IsEnabled} Margin 0,0,8,0 Hand ToolTip「このリポジトリを見に行く」
                       ｜ {DisplayName} 12 SemiBold 省略 TextPrimary
                       ｜ Button「✕」22×22 IconButtonStyle TextSecondary ToolTip「このリポジトリを登録から外す」→ DataContext.DeleteGitRepositoryCommand(UserControl)
    {Path} 10 Margin 0,3,0,0 省略 ToolTip={Path} TextMuted
    ComboBox Margin 0,6,0,0 Height 26 11px ItemsSource=DataContext.SelectableProjectCodes DisplayMemberPath DisplayName SelectedValuePath Id
             SelectedValue={ProjectCodeId, Converter=ProjectCodeIdConverter}
             ToolTip「このリポジトリの作業に割り当てるプロジェクトコード（未設定にすると推測に使いません）」
  ```
- `Button「＋ リポジトリを追加」BaseButtonStyle Stretch → AddGitRepositoryCommand`
- 注 Margin 0,10,0,4「読むのは git log だけで、リポジトリには一切書き込みません。手元の git 操作と衝突することもありません。」
- 注 4「対象は自分（user.email）のコミットだけで、マージコミットは除きます。コミットそのものは保存せず、必要になったときに読み直します。」
- 注 4「プロジェクトコードを設定しておくと、そのリポジトリだけの時間帯を埋めるときに自動で選ばれます。」

**⑦ TodoSettingsSection「ToDo」**
- 行 6：「入力欄で記法を使う」｜ CheckBox `{IsTodoQuickSyntaxEnabled}`
- 注 16「ToDo パネルの入力欄で「資料作成 @明日 !高 #開発 ~30m *前日」のように書くと、期限・優先度・カテゴリ・見積もり・通知をその場で指定できます。解釈できない記号はそのまま本文に残ります。入力中は欄の下に解釈結果が出ます。」
- 区切り線
- 行 6：「既定の通知時刻」｜［ComboBox `{TodoDefaultRemindHourOptions}`(0〜23) `{TodoDefaultRemindHour}` Width 70 ｜「時」］
- 注 12「通知の時刻を省略したときに使います（編集ダイアログで通知を入れた直後の初期値、入力欄の「*前日」など）。」
- 行 12：「「あとで」の時間」｜［ComboBox `{TodoSnoozeOptions}` `{TodoSnoozeMinutes}` Width 70 ｜「分（バナーの ▾ から他の時間も選べます）」］
- 行 6：「通知音を鳴らす」｜ CheckBox `{IsTodoReminderSoundEnabled}`
- 注 16「通知時刻から15分以上遅れて気づいた通知は、個別のバナーにはせず「見逃した通知」として1本にまとめて知らせます。」
- 区切り線
- 注 12「ToDo ごとの通知日時とは別に、1日1回「今日が期限」「期限超過」の件数をまとめて知らせます。個別の通知日時を付け忘れていても、その日に片付ける量が一度は目に入ります。」
- 行 16：「まとめて通知する」｜ CheckBox `{IsTodoDigestEnabled}`
- 行 6（Digest 有効時）：「通知する時刻」｜［ComboBox `{TodoDigestHourOptions}` `{TodoDigestHour}` Width 70 ｜「時ごろ」］
- 注 16（Digest 有効時）「この時刻より後にアプリを起動した日も、その日ぶんをまだ出していなければ1回だけ通知します。対象が0件の日は通知しません。」
- 区切り線
- 行 6：「完了済みを残す期間」｜［ComboBox `{TodoArchiveRetentionOptions}` `{TodoArchiveRetentionDays}` Width 70 ｜「日」］
- 注 4「この期間を過ぎた完了済みの ToDo は、起動時にアーカイブ（todos-archive.json）へ移します。一覧からは消えますが削除はされず、見積もりの傾向の集計には引き続き使われます。」

※ `TodoSnoozeOptions` は `[5, 10, 15, 30, 60, 120]`（`AppSettingsNormalizer.TodoSnoozeOptions` と共有）。

**⑧ WorkSettingsSection「勤務の記録（出勤・退勤）」**
- 注 12「ツールバーの「出勤 / 退勤」ボタン、またはホットキー（既定: Ctrl+Alt+S / Ctrl+Alt+E）で登録します。登録した時刻は日・週ビューに横線として表示され、その線のラベルをクリックするとあとから修正できます。押し忘れた日は「出勤 / 退勤」ボタンの右クリックから追加できます。」
- 行 16：「終了を検知する」｜ CheckBox `{IsWorkEndDetectionEnabled}`
- 行 6（検知有効時）：「終了とみなす時間」｜［ComboBox `{WorkEndThresholdOptions}` `{WorkEndThresholdMinutes}` Width 70 ｜「分の離席・スリープ」］
- 行 16（検知有効時）：「確認する時間帯」｜ ComboBox 左 Width 180 13 `{WorkEndEarliestOptions}` `{SelectedWorkEndEarliestOption}`
- 注 16（検知有効時）「スリープや長い離席から戻ったときに「最後に操作した時刻で退勤にするか」を確認します。勝手には確定しません。「確認する時間帯」より前に始まった離席（日中の会議・外出など）では確認しません。ただし日付をまたいで戻った場合は時間帯に関係なく確認します。日付をまたいでも退勤が登録されていない場合だけ、その日の最後の記録の時刻で自動的に締めます。」
- 区切り線
- 行 6：「退勤時にふりかえる」｜ CheckBox `{IsWorkEndReviewEnabled}`
- 注 4「退勤したときに、その日の勤務時間・記録時間・完了した ToDo の件数を出し、片付かなかった ToDo を明日へ送るか確認します。「今日やる」は日をまたぐと自動で外れるため、手を付けなかったものが黙って消えるのを防ぎます。送るのは「今日やる」の印だけで、期限は変わりません。自動で締めた退勤では出しません。」

> フェーズ途中では、まだ実装していない機能のセクションは XAML ごと置かない（フェーズ18で8つ揃う）。検索のキーワード判定はあるセクションだけに対して行う（`x:Name` が無いと null になるので null チェックする）。

### 1.3 検索（SettingsPanel.xaml.cs）
```csharp
private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
{
    if (AppearanceSettingsSection is null) return;   // 初期化途中（後続の要素がまだ無い）は無視
    var query = SettingsSearchBox.Text.Trim(); bool searching = query.Length > 0;
    SettingsSearchClearButton.Visibility = searching ? Visible : Collapsed;
    bool appearance = Matches(query, "外観", "ダーク", "テーマ", "配色");
    bool display    = Matches(query, "表示", "時間", "時刻", "曜日", "刻み", "ドラッグ", "カレンダー", "吸着", "マグネット", "スナップ");
    bool miniBar    = Matches(query, "ミニバー", "最前面", "記録中", "常時表示", "小窓", "バー");
    bool away       = Matches(query, "離席", "中断", "無操作", "スリープ", "ロック", "除外");
    bool appUsage   = Matches(query, "アプリ", "使用", "前面", "ウィンドウ", "記録", "プライバシー");
    bool git        = Matches(query, "git", "コミット", "リポジトリ", "履歴", "ソース", "開発");
    bool todo       = Matches(query, "todo", "通知", "先送り", "クイック", "期限", "まとめ", "完了", "アーカイブ");
    bool work       = Matches(query, "勤務", "出勤", "退勤", "ふりかえり", "終了", "自動締め");
    ApplySearchResult(AppearanceSettingsSection, appearance, searching); … 8つ
    NoSettingsResultsText.Visibility = searching && !(いずれか) ? Visible : Collapsed;
}
static bool Matches(string query, params string[] keywords)   // 空なら true。keyword.Contains(query) || query.Contains(keyword)（OrdinalIgnoreCase）
static void ApplySearchResult(Expander section, bool isMatch, bool searching)
{   section.Visibility = isMatch ? Visible : Collapsed; if (searching && isMatch) section.IsExpanded = true; }   // 検索を消しても開いたまま
private void SettingsSearchClearButton_Click(...) { SettingsSearchBox.Clear(); SettingsSearchBox.Focus(); }
```

---

## 2. 管理パネル（Views/ManagementPanel.xaml、幅 440）

コードビハインドは InitializeComponent のみ。

### 2.1 骨格
`Grid Width=440` 行 `[Auto | *]`
1. ヘッダー `Border 地 Background Padding 16,12 下線1` ＞ Grid［StackPanel［「管理」SemiBold 16 TextPrimary ／「分類・定期予定・スプリント」11 Margin 0,2,0,0 TextSecondary］｜ `Button「✕」IconButtonStyle 右 上 TextSecondary → ToggleManagementPanelCommand`］
2. `TabControl Style=SettingsTabControlStyle`、TabItem は `SettingsTabItemStyle`：「分類」「定期予定」「スプリント」

### 2.2 タブ「分類」
`ScrollViewer 縦Auto 横Disabled` ＞ `StackPanel Margin 20,6,20,24`

**Expander「カテゴリ」（IsExpanded=True）**
- 注 Margin 0,0,0,10「色に名前を付けて分類できます。カテゴリは統計にも反映されます」
- ItemsControl `{Categories}` Margin 0,0,0,8、各行 `Grid Margin 0,0,0,6 [Auto|*|Auto]`：
  - `ComboBox Width 58 Margin 0,0,8,0 ItemsSource=DataContext.PaletteColors(UserControl) SelectedValuePath=Code SelectedValue={ColorCode}`、項目 `Border 22×14 地 {Brush} CornerRadius 3 枠 Border 1 ToolTip={Name}`
  - `TextBox {Name, UpdateSourceTrigger=LostFocus} InputTextBoxStyle 12`
  - `Button「✕」Margin 4,0,0,0 24×24 IconButtonStyle TextSecondary → DeleteCategoryCommand`
- `Button「＋ カテゴリを追加」BaseButtonStyle Stretch → AddCategoryCommand`
- `Separator Margin 0,14,0,10 地 Border`
- 「記録開始時の既定カテゴリ」FieldLabelStyle ＋ `ComboBox {Categories} SelectedItem={SelectedRecordingCategory} Height 32 Margin 0,0,0,4`（項目 横並び［Border 20×14 地 Brush CornerRadius 3 枠1 Margin 0,0,8,0 ｜ Name 12］）

**Expander「プロジェクトコード」（閉）**
- 注 10「案件・プロジェクト単位で集計するためのコードです」
- ItemsControl `{ProjectCodes}` Margin 0,0,0,8、各行 `Grid Margin 0,0,0,6 [88|*|Auto|Auto]`：
  - `TextBox {Code, LostFocus} InputTextBoxStyle 12 IsEnabled={IsActive}`
  - `TextBox {Name, LostFocus} InputTextBoxStyle 12 Margin 6,0,0,0 IsEnabled={IsActive}`
  - `Button Margin 4,0,0,0 56×26 → ToggleProjectCodeActiveCommand`、Style BasedOn GhostButtonStyle：Content「無効化」11px Padding 4,2、`IsActive=False` で Content「有効化」＋ Foreground Primary
  - `Button「✕」24×24 IconButtonStyle TextSecondary → DeleteProjectCodeCommand`
- `Button「＋ プロジェクトコードを追加」Base Stretch → AddProjectCodeCommand`
- 「既定のプロジェクトコード」FieldLabelStyle Margin 0,14,0,6 ＋ `ComboBox {SelectableProjectCodes} SelectedItem={SelectedDefaultProjectCode} DisplayMemberPath DisplayName Height 32`
- 10px TextMuted Wrap Margin 0,6,0,0「「（未設定）」にすると、新しい予定・実績と記録開始はコードを付けずに始まります」
- `Grid Margin 0,14,0,0 [*|Auto]`：「未記録時間を集計に含める」FieldLabelStyle 縦中央 Margin 0,0,16,0 ｜ CheckBox `{IsUnrecordedTimeProjectAggregationEnabled}`
- 「未記録時間の加算先（既定）」FieldLabelStyle Margin 0,12,0,6 ＋ `ComboBox {ActiveProjectCodes} SelectedItem={SelectedUnrecordedTimeProjectCode} DisplayMemberPath DisplayName Height 32 IsEnabled={IsUnrecordedTimeProjectAggregationEnabled}`
- 10px TextMuted「出勤から退勤までのうち、実績がない時間だけをプロジェクトコード別の統計と月次表へ加算します」
- 「期間ごとの加算先」FieldLabelStyle Margin 0,14,0,6
- ItemsControl `{UnrecordedTimeAssignments}` IsEnabled=集計有効 Margin 0,0,0,8：
  ```
  Border 枠1 CornerRadius 6 Padding 8 Margin 0,0,0,6 > Grid[*|Auto]
    StackPanel: {RangeText, OneWay} 11 Margin 0,0,0,5 TextSecondary
                Grid[Auto|8|*]: DatePicker SelectedDate={StartDate} 縦中央 ｜ ComboBox 列2 Height 30 ItemsSource=DataContext.AssignmentProjectCodeChoices
                                SelectedValue={ProjectCodeId, Converter=ProjectCodeIdConverter} SelectedValuePath Id DisplayMemberPath DisplayName
    Button「✕」Margin 6,0,0,0 24×24 縦中央 IconButtonStyle TextSecondary → DeleteUnrecordedTimeAssignmentCommand
  ```
- `Button「＋ 期間を追加」IsEnabled=集計有効 Base Stretch → AddUnrecordedTimeAssignmentCommand`
- 10px TextMuted「案件が変わる日を足していくと、その日から次の行の前日までがそのコードになります。「（未設定）」にした期間は加算しません。行が無い期間と最初の行より前は、上の既定を使います」

**Expander「定型タイトル」（閉）**
- 11px TextSecondary Margin 0,0,0,10「タイトル入力欄の候補に常に表示されます」
- ItemsControl `{PinnedTitles}`：`Grid [*|Auto] Margin 0,0,0,6`［TextBox `{Text, LostFocus}` InputTextBoxStyle 12 ｜「✕」24×24 → DeletePinnedTitleCommand］
- `Button「＋ 定型タイトルを追加」Base Stretch → AddPinnedTitleCommand`

### 2.3 タブ「定期予定」
`StackPanel Margin 20,16,20,24`：
- 注 12「決まった曜日や日付の予定を自動生成します。通知や自動記録開始も設定できます」
- ItemsControl `{Routines}` Margin 0,0,0,8：
  ```
  Border 枠1 CornerRadius 7 Padding 10 Margin 0,0,0,8 > Grid[*|Auto]
    StackPanel: {Title} SemiBold 13 TextPrimary
                TextBlock 11 Margin 0,3,0,0 TextSecondary: {RecurrenceDisplay} + "  " + {TimeRangeDisplay}
                横並び Margin 0,4,0,0: 「自動開始」10 Primary (IsAutoStart) ｜「無効」10 TextMuted (!IsEnabled)
    横並び 縦中央: Button「編集」Padding 7,4 Margin 0,0,4,0 GhostButtonStyle → EditRoutineCommand
                  Button「削除」Padding 7,4 GhostButtonStyle Foreground Danger → DeleteRoutineCommand
  ```
- `Button「＋ 定期予定を追加」PrimaryButtonStyle Stretch → AddRoutineCommand`

### 2.4 タブ「スプリント」
`StackPanel Margin 20,16,20,24`：
- 注 12「手動で期間を区切り、カレンダーとタイムラインの表示範囲に使います」
- ItemsControl `{ManualSprints}` Margin 0,0,0,10：定期予定と同じカード。2行目は `{StartDate:yyyy/MM/dd}` + " - " + `{EndDate:yyyy/MM/dd}`。ボタン「編集」→ EditManualSprintCommand、「削除」(Danger) → DeleteManualSprintCommand
- `Button「＋ スプリントを追加」PrimaryButtonStyle Stretch → ShowAddSprintFormCommand`（`IsAddSprintFormVisible` のときは隠す）
- フォーム（`IsAddSprintFormVisible` のとき）`Border 枠1 CornerRadius 7 Padding 14 地 Background` ＞ StackPanel：
  - `{FormTitle}` SemiBold 14 Margin 0,0,0,12
  - 「スプリント名」FieldLabelStyle ＋ TextBox `{NewSprintName, PropertyChanged}` InputTextBoxStyle Margin 0,0,0,12
  - Grid[*|12|*] Margin 0,0,0,14：「開始日」＋ DatePicker `{NewSprintStartDate}` ｜「終了日」＋ DatePicker `{NewSprintEndDate}`
  - Grid[*|8|*]：`Button「キャンセル」Base → HideAddSprintFormCommand` ｜ `Button「保存」Primary → SaveNewSprintCommand`

---

## 3. マスター管理の ViewModel

### 3.1 カテゴリ（MainViewModel.Categories.cs）
- `record PaletteColor(string Name, string Code) { Brush Brush = CategoryInfo.CreateBrush(Code) }`、`static PaletteColors`（16色。名前と Brushes の対応は `design/02` §5）
- `Categories`（ObservableCollection）：CollectionChanged で索引破棄＋`RecordingCategory` 系を通知
- `AddCategoryCommand`：未使用のパレット色（無ければ先頭）で `{ Name = "新しいカテゴリ" }` を購読して追加 → SaveSettings → UpdateStats → `IsDisplayFilterActive` 通知
- `DeleteCategoryCommand`（CanExecute: 2件以上）：確認「カテゴリ「{Name}」を削除しますか？\n（この色を使っている既存の記録は残ります）」「削除確認」→ 購読解除・削除 → SaveSettings → フィルタ通知 → RecalculateLayout
- `OnCategoryPropertyChanged`：ColorCode なら索引破棄。Name 変更かつ既定カテゴリ未設定なら RecordingCategory 通知。読込中は return。`IsFilterEnabled` はフィルタ通知＋RecalculateLayout のみ（**保存しない**＝セッション限り）。他は SaveSettings＋UpdateStats
- `LoadCategories(loaded)`：null/空なら `CategoryInfo.CreateDefaults()`
- `ResolveCategory(item)` / `ResolveCategory(categoryId, colorCode)`：索引（Id→、Color→先勝ち）で Id 優先→色
- `IsItemVisible(item)`：カテゴリが解決でき `IsFilterEnabled=false` なら非表示。プロジェクトが解決でき `IsFilterEnabled=false` なら非表示。解決できないものは常に表示
- `IsTodoVisible(todo)`：カテゴリのフィルタのみ（ToDo パネルには効かせない）
- `RecordingCategory`：設定 Id が解決できればそれ、無ければ名前「記録」、無ければ先頭
- `SelectedRecordingCategory`：get=RecordingCategory。set は null・同 Id を無視、Id を保存して通知・SaveSettings
- `LoadRecordingCategoryId(id)`：空なら null

### 3.2 プロジェクトコード（MainViewModel.ProjectCodes.cs）
- `ProjectCodes`（CollectionChanged で選択肢通知・フィルタ通知・`CommandManager.InvalidateRequerySuggested()`）
- `ActiveProjectCodes` = 有効なもの、`SelectableProjectCodes = [Unassigned, ..Active]`、`AssignmentProjectCodeChoices = [Unassigned, ..有効または割り当てで使用中のもの]`
- `DefaultProjectCode`：`_defaultProjectCodeId == ""`（明示的な未設定）なら null、Id が有効なコードならそれ、他は先頭の有効コード
- `SelectedDefaultProjectCode`：get は未設定なら `ProjectCodeInfo.Unassigned`、他 DefaultProjectCode。set：null 無視、Id 空→""、有効→Id、無効→無視。変わったら通知・SaveSettings
- `AddProjectCodeCommand`：使われていない `PROJECT-{n}`（n=1..、大小無視）で `{ Code, Name = "新しいプロジェクト" }` → 購読・追加・SaveSettings
- `ToggleProjectCodeActiveCommand`（CanExecute: 無効なもの、または有効が2件以上）：`IsActive` 反転
- `DeleteProjectCodeCommand`（CanExecute: 2件以上かつ〔無効 or 有効が2件以上〕）：
  - 予定・実績・定期予定で使用中なら `ShowMessage("プロジェクトコード「{DisplayName}」は予定・実績・定期予定で使用中のため削除できません。\n対象アイテムや定期予定を別のプロジェクトコードへ変更してから削除してください。", "プロジェクトコードの削除")`
  - 確認「プロジェクトコード「{DisplayName}」を削除しますか？」「削除確認」→ 購読解除・削除 → 既定だったら先頭の有効へ → `ClearAssignmentProjectCodeReferences` → `EnsureUnrecordedTimeProjectCode` → 通知 → SaveSettings → UpdateStats
- `OnProjectCodePropertyChanged`：DisplayName は無視。`IsFilterEnabled` は読込中でなければフィルタ通知＋RecalculateLayout のみ（保存しない）。`IsActive` は既定コードが無効化されたら先頭の有効へ、Ensure、選択肢通知、Requery。読込中は return。SaveSettings → UpdateStats → 選択肢通知
- `LoadProjectCodes(loaded)`：空なら `ProjectCodeInfo.CreateDefaults()`、全部無効なら先頭を有効に、Id 空は採番
- `LoadDefaultProjectCodeId(id)`：""→""、有効な Id→そのまま、他→先頭の有効
- `LoadUnrecordedTimeProjectAggregation(enabled, id)`：加算先 = 有効な Id か、`(DefaultProjectCode ?? 先頭の有効)?.Id`。`有効 = enabled && 加算先がある`
- `IsUnrecordedTimeProjectAggregationEnabled` set：Ensure → SaveSettings → UpdateStats
- `SelectedUnrecordedTimeProjectCode`：get は有効な加算先 ?? Default ?? 先頭。set は有効なものだけ、保存・UpdateStats
- `ResolveProjectCode(id)`、`GetSelectableProjectCodes(currentId)`（有効なもの＋編集中の無効コード）
- 期間割り当ては `design/12` §6

### 3.3 定型タイトル（MainViewModel.Titles.cs）
- `class TitleEntry : INotifyPropertyChanged { string Text }`、`PinnedTitles`
- `AddPinnedTitleCommand`：「新しい定型タイトル」を購読・追加・SaveSettings／`DeletePinnedTitleCommand`：購読解除・削除・SaveSettings
- `OnPinnedTitleChanged`：読込中でなければ SaveSettings
- `LoadPinnedTitles(loaded)`：null なら「打ち合わせ」「休憩」、[] は空のまま
- `GetTitleSuggestions()`：定型（Trim・空と重複を除く）→ 直近1か月（`StartTime >= 今-1か月`）のアイテムのタイトルを新しい順に、重複を除いて最大 30 件

### 3.4 スプリント・定期予定のコマンド
`design/11` §1.2 / §4.2。

## 4. テスト観点

- 設定の契約テスト（全 AppSettings プロパティが binding にちょうど1回ずつ）
- カテゴリ：最後の1件は削除できない、未使用パレット色の割り当て、ResolveCategory の Id 優先・色の先勝ち、フィルタ変更で保存しない
- プロジェクトコード：`PROJECT-n` の採番、最後の有効コードは無効化/削除できない、使用中は削除できない、既定の未設定（""）と未指定（null）の区別
- GetTitleSuggestions：順序・重複・30件上限・null と [] の区別
- 設定検索：Matches の双方向部分一致
