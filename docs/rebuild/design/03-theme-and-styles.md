# 03. テーマ・共通スタイル・コンバーター

## 1. 基本方針

- 全体のトーンは Tailwind の Slate（灰青）＋ Blue を基調にしたフラットなデザイン。角丸 6px が標準
- フォントは WPF 既定（Windows の UI フォント。日本語は Yu Gothic UI / Meiryo UI にフォールバック）。アイコンは `Segoe MDL2 Assets`
- 本文 13px、補助 11〜12px、見出し 14〜17px、ダイアログ見出し 15〜20px
- 色は必ず `{DynamicResource トークン}`。ダーク切替で差し替わる

## 2. 配色トークン

`Themes/Colors.xaml`（ライト。`Styles.xaml` の MergedDictionaries に常駐）と `Themes/DarkColors.xaml`（ダーク。`App.ApplyTheme(true)` で末尾に追加）に**同じキー**を定義する。
各トークンは `<Color x:Key="XxxColor">` と `<SolidColorBrush x:Key="XxxBrush" Color="{StaticResource XxxColor}"/>` の対。

| トークン（Brush 名） | ライト | ダーク | 用途 |
| --- | --- | --- | --- |
| `PrimaryBrush` | #3B82F6 | #60A5FA | 主操作・選択・リンク |
| `PrimaryDarkBrush` | #2563EB | #93C5FD | 押下 |
| `PrimaryLightBrush` | #60A5FA | #3B82F6 | ホバー |
| `PrimarySubtleBrush` | #EBF5FF | #1E3A5F | 選択背景・今日の背景 |
| `WorkStartBrush` | #0EA5E9 | #38BDF8 | 出勤ライン・出勤ボタン |
| `WorkEndBrush` | #7C3AED | #A78BFA | 退勤ライン・退勤ボタン |
| `UnrecordedGapBrush` | #1F94A3B8 | #26CBD5E1 | 未記録の帯の塗り |
| `UnrecordedGapBorderBrush` | #8094A3B8 | #8094A3B8 | 未記録の帯の破線 |
| `TodoOverdueBrush` | #DC2626 | #F87171 | 期限超過 |
| `TodoDueTodayBrush` | #D97706 | #FBBF24 | 期限今日・今日やる印 |
| `TodoHighPriorityBrush` | #F97316 | #FB923C | 優先度高 |
| `TodoReminderSubtleBrush` | #FFFBEB | #3A2E17 | ToDo 通知カードの地 |
| `DangerBrush` | #EF4444 | #F87171 | 記録ボタン・削除・現在時刻線 |
| `DangerDarkBrush` | #DC2626 | #FCA5A5 | 記録中ホバー |
| `DangerSubtleBrush` | #FEF2F2 | #2D1F1F | 記録ボタンホバー |
| `WarningBrush` | #D97706 | #FBBF24 | 警告アイコン |
| `WarningBorderBrush` | #F59E0B | #D97706 | 警告カード枠 |
| `WarningSubtleBrush` | #FFFBEB | #3A2E17 | 警告カード地 |
| `WarningTextBrush` | #78350F | #FDE68A | 警告文字 |
| `BackgroundBrush` | #F8FAFC | #0F172A | ウィンドウ地 |
| `MutedBackgroundBrush` | #E2E8F0 | #0F172A | 控えめな地（月の範囲外セル等） |
| `SurfaceBrush` | #FFFFFF | #1E293B | カード・パネル・ツールバー |
| `BorderBrush` | #E2E8F0 | #334155 | 罫線 |
| `TextPrimaryBrush` | #1E293B | #F1F5F9 | 本文 |
| `TextSecondaryBrush` | #64748B | #94A3B8 | 補助文 |
| `TextMutedBrush` | #94A3B8 | #64748B | 薄い補助 |
| `HoverBackgroundBrush` | #F1F5F9 | #334155 | ホバー地 |
| `PressedBackgroundBrush` | #E2E8F0 | #1E293B | 押下・選択行 |
| `HoverBorderBrush` | #CBD5E1 | #475569 | ホバー枠 |
| `FocusVisualBrush` | #2563EB | #93C5FD | キーボードフォーカス枠 |
| `ScrollBarThumbBrush` | #33000000 | #40FFFFFF | スクロールつまみ |
| `ScrollBarThumbHoverBrush` | #73000000 | #8CFFFFFF | 同ホバー |

> `DangerColor`（Color）は日/週ビューの現在時刻線の `DropShadowEffect.Color` で直接参照する。

## 3. 共通スタイル（Themes/Styles.xaml）

`Styles.xaml` は先頭で `<ResourceDictionary Source="Colors.xaml"/>` をマージする。App.xaml は `Styles.xaml` → `Generic.xaml` の順にマージ。

### 3.1 ボタン系

| キー | TargetType | 内容 |
| --- | --- | --- |
| `BaseButtonStyle` | Button | Background `{SurfaceBrush}`, BorderBrush `{BorderBrush}`, Foreground `{TextPrimaryBrush}`, BorderThickness 1, Padding 12,6, FontSize 13, FontWeight Medium, Cursor Hand。テンプレート＝`Border`(CornerRadius 6, Padding=TemplateBinding)＋中央 ContentPresenter。Trigger：IsMouseOver→Background `{HoverBackgroundBrush}`/BorderBrush `{HoverBorderBrush}`、IsPressed→`{PressedBackgroundBrush}`、IsEnabled=False→Opacity 0.6, Cursor Arrow |
| `BaseToggleButtonStyle` | ToggleButton | 上と同じ＋ IsChecked→Background `{PrimarySubtleBrush}`, BorderBrush `{PrimaryBrush}`, Foreground `{PrimaryBrush}`（トリガー順：MouseOver, Checked, Pressed, Disabled） |
| `PrimaryButtonStyle` | Button (BasedOn Base) | Background/BorderBrush `{PrimaryBrush}`, Foreground White。MouseOver→`{PrimaryLightBrush}`、Pressed→`{PrimaryDarkBrush}` |
| `GhostButtonStyle` | Button (BasedOn Base) | Background Transparent, BorderThickness 0, Foreground `{TextSecondaryBrush}`。MouseOver→Background **#F1F5F9（固定）**, Foreground `{TextPrimaryBrush}`、Pressed→**#E2E8F0（固定）** |
| `IconButtonStyle` | Button (BasedOn Ghost) | Padding 4, Width 32, Height 32 |
| `IconToggleButtonStyle` | ToggleButton | Background Transparent, BorderThickness 0, Foreground `{TextSecondaryBrush}`, FontFamily Segoe MDL2 Assets, FontSize 15, 32×32, Cursor Hand。テンプレート `Border x:Name=border`(CornerRadius 6)。MouseOver→border.Background `{HoverBackgroundBrush}`、IsChecked→border.Background `{PrimarySubtleBrush}`＋Foreground `{PrimaryBrush}`、IsPressed→`{PressedBackgroundBrush}` |
| `ToolbarSeparatorStyle` | Border | Width 1, Background `{BorderBrush}`, Margin 10,6 |
| `TodoCheckButtonStyle` | Button | テンプレート `Border x:Name=box`(CornerRadius 3, BorderBrush `{TextMutedBrush}`, BorderThickness 1.5, Background=Template)。MouseOver→枠 `{TextSecondaryBrush}`＋地 `{HoverBackgroundBrush}`、Pressed→地 `{PressedBackgroundBrush}` |
| `NoteTagChipStyle` | Button | 地 `{MutedBackgroundBrush}`、枠 `{BorderBrush}` 1、文字 `{TextSecondaryBrush}`、Padding 10,4、CornerRadius 12。MouseOver→地 Hover/枠 HoverBorder。`DataTrigger {Binding IsSelected}=True`（後置で優先）→地/枠 `{PrimaryBrush}`, 文字 White |
| `NoteCardStyle` | Button | 地 `{MutedBackgroundBrush}`、枠 `{BorderBrush}` 1、Padding 12,10、CornerRadius 6、**ContentPresenter は HorizontalAlignment=Stretch**。MouseOver→Hover系、Pressed→Pressed |
| `SearchResultButtonStyle` | Button | 地 Transparent, BorderThickness 0, Padding 10,8, HorizontalContentAlignment Stretch。テンプレート Border bd。MouseOver→bd.Background `{PrimarySubtleBrush}` |

### 3.2 セグメントボタン（RadioButton）

```xml
<Style x:Key="SegmentedRadioButtonBase" TargetType="RadioButton">
  <Setter Property="Background" Value="Transparent"/>
  <Setter Property="BorderBrush" Value="{DynamicResource PrimaryBrush}"/>
  <Setter Property="Foreground" Value="{DynamicResource PrimaryBrush}"/>
  <Setter Property="Padding" Value="12,6"/><Setter Property="FontSize" Value="13"/><Setter Property="Cursor" Value="Hand"/>
  <Setter Property="Template"><Setter.Value>
    <ControlTemplate TargetType="RadioButton">
      <Border x:Name="border" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
              BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="{TemplateBinding Tag}">
        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" Margin="{TemplateBinding Padding}"/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property="IsChecked" Value="True"><Setter TargetName="border" Property="Background" Value="{DynamicResource PrimaryBrush}"/><Setter Property="Foreground" Value="White"/></Trigger>
        <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="border" Property="Background" Value="{DynamicResource PrimarySubtleBrush}"/></Trigger>
        <MultiTrigger><MultiTrigger.Conditions><Condition Property="IsChecked" Value="True"/><Condition Property="IsMouseOver" Value="True"/></MultiTrigger.Conditions>
          <Setter TargetName="border" Property="Background" Value="{DynamicResource PrimaryDarkBrush}"/></MultiTrigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
</Style>
<!-- Tag を CornerRadius として使う -->
SegmentedLeftStyle   : Tag="4,0,0,4", BorderThickness="1,1,0,1"
SegmentedMiddleStyle : Tag="0,0,0,0", BorderThickness="1,1,0,1", Margin="-1,0,0,0"
SegmentedRightStyle  : Tag="0,4,4,0", BorderThickness="1,1,1,1", Margin="-1,0,0,0"
```

### 3.3 ナビゲーション・通知

| キー | 内容 |
| --- | --- |
| `ViewNavigationItemStyle` (RadioButton) | Background/BorderBrush Transparent, BorderThickness 1, Foreground `{TextSecondaryBrush}`, FontSize 12, FontWeight Medium, Padding 10,4, Margin 1,0, Cursor Hand。テンプレート `Border NavBorder`(CornerRadius 5)。MouseOver→地 Hover・文字 TextPrimary、IsChecked→地 PrimarySubtle・枠 Primary・文字 Primary・SemiBold、IsKeyboardFocused→枠 `{FocusVisualBrush}` |
| `ViewNavigationGroupLabelStyle` (TextBlock) | FontSize 9, SemiBold, `{TextMutedBrush}`, Margin 3,0,3,2 |
| `NotificationCardStyle` (Border) | 地 Surface、枠 Border 1、CornerRadius 8、Padding 12、Margin 0,0,0,8、`DropShadowEffect Color=Black BlurRadius=12 ShadowDepth=2 Opacity=0.16` |
| `WarningNotificationCardStyle` (Border, BasedOn 上) | 地 `{WarningSubtleBrush}`、枠 `{WarningBorderBrush}` |

### 3.4 入力系

| キー | 内容 |
| --- | --- |
| `InputTextBoxStyle` (TextBox) | Padding 10,8, FontSize 14, 地 Surface, 枠 Border 1, 文字 TextPrimary。テンプレート `Border border`(CornerRadius 6) ＋ `ScrollViewer PART_ContentHost`。MouseOver→枠 HoverBorder、IsKeyboardFocused→枠 Primary・太さ 1.5 |
| `FieldLabelStyle` (TextBlock) | FontSize 13, SemiBold, `{TextSecondaryBrush}`, Margin 0,0,0,6 |
| `BaseComboBoxStyle` + 暗黙の ComboBox スタイル | 下記 XAML |
| 暗黙の `ComboBoxItem` | Foreground TextPrimary, FontSize 13, Padding 12,8, Cursor Hand。テンプレート `Border`(CornerRadius 4, Margin 4,2)。IsHighlighted→地 Hover、IsSelected→地 PrimarySubtle・文字 PrimaryDark・Medium |

```xml
<ControlTemplate x:Key="ComboBoxToggleButton" TargetType="ToggleButton">
  <Grid><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="32"/></Grid.ColumnDefinitions>
    <Border x:Name="Border" Grid.ColumnSpan="2" CornerRadius="6" Background="{DynamicResource SurfaceBrush}" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="1"/>
    <Border Grid.Column="0" CornerRadius="6,0,0,6" Margin="1" Background="Transparent"/>
    <Path x:Name="Arrow" Grid.Column="1" Fill="{DynamicResource TextSecondaryBrush}" HorizontalAlignment="Center" VerticalAlignment="Center" Data="M 0 0 L 4 4 L 8 0 Z"/>
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property="IsMouseOver" Value="true"><Setter TargetName="Border" Property="BorderBrush" Value="{DynamicResource HoverBorderBrush}"/></Trigger>
    <Trigger Property="IsChecked" Value="true"><Setter TargetName="Border" Property="BorderBrush" Value="{DynamicResource PrimaryBrush}"/><Setter TargetName="Arrow" Property="Fill" Value="{DynamicResource PrimaryBrush}"/></Trigger>
    <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Border" Property="Opacity" Value="0.6"/><Setter TargetName="Arrow" Property="Fill" Value="{DynamicResource TextMutedBrush}"/></Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>
<ControlTemplate x:Key="ComboBoxTextBox" TargetType="TextBox"><Border x:Name="PART_ContentHost" Focusable="False" Background="{TemplateBinding Background}"/></ControlTemplate>
<Style x:Key="BaseComboBoxStyle" TargetType="ComboBox">
  SnapsToDevicePixels=true, OverridesDefaultStyle=true, ScrollViewer.H/VScrollBarVisibility=Auto, ScrollViewer.CanContentScroll=true,
  MinWidth=50, MinHeight=32, Height=32, FontSize=13, Foreground={TextPrimaryBrush}
  Template:
  <Grid>
    <ToggleButton x:Name="ToggleButton" Template="{StaticResource ComboBoxToggleButton}" Focusable="false" ClickMode="Press"
                  IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"/>
    <ContentPresenter x:Name="ContentSite" IsHitTestVisible="False" Content="{TemplateBinding SelectionBoxItem}"
        ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"
        Margin="12,0,32,0" VerticalAlignment="Center" HorizontalAlignment="Left"/>
    <TextBox x:Name="PART_EditableTextBox" Style="{x:Null}" Template="{StaticResource ComboBoxTextBox}" HorizontalAlignment="Left"
        VerticalAlignment="Center" Margin="12,0,32,0" Focusable="True" Background="Transparent" Visibility="Hidden" IsReadOnly="{TemplateBinding IsReadOnly}"/>
    <Popup x:Name="Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False" PopupAnimation="Slide">
      <Grid x:Name="DropDown" SnapsToDevicePixels="True" MinWidth="{TemplateBinding ActualWidth}" MaxHeight="{TemplateBinding MaxDropDownHeight}">
        <Border x:Name="DropDownBorder" Background="{DynamicResource SurfaceBrush}" BorderThickness="1" BorderBrush="{DynamicResource BorderBrush}" CornerRadius="6" Margin="0,4,0,8">
          <Border.Effect><DropShadowEffect Color="Black" Opacity="0.1" BlurRadius="8" ShadowDepth="4"/></Border.Effect>
          <ScrollViewer Margin="0,4" SnapsToDevicePixels="True"><StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer>
        </Border></Grid></Popup>
  </Grid>
  Triggers: HasItems=false → DropDownBorder.MinHeight=95; IsEnabled=false → Foreground {TextMutedBrush};
            IsGrouping=true → CanContentScroll=false; IsEditable=true → IsTabStop=false, PART_EditableTextBox Visible, ContentSite Hidden
</Style>
<Style TargetType="ComboBox" BasedOn="{StaticResource BaseComboBoxStyle}"/>
```

### 3.5 スクロールバー（暗黙スタイル・アニメーションなし）

- `ScrollBarPageButton`（RepeatButton）：透明、Focusable/IsTabStop false、テンプレートは透明 Border
- `VerticalThumbStyle` / `HorizontalThumbStyle`：透明 Grid 内に `Border ThumbVisual`（地 `{ScrollBarThumbBrush}`、CornerRadius 3、Margin 縦=2,0,2,0／横=0,2,0,2）。IsMouseOver・IsDragging→`{ScrollBarThumbHoverBrush}`
- テンプレート：`Grid Bg(Transparent)` > `Track PART_Track`（縦は IsDirectionReversed=true）、Decrease/Increase に PageUp/PageDown（横は PageLeft/PageRight）コマンドの PageButton、Thumb
- 暗黙 `ScrollBar`：Stylus 無効、Background/Foreground Transparent。Orientation=Vertical→Width 10, MinHeight 20, 縦テンプレート／Horizontal→Height 10, MinWidth 20, 横テンプレート

### 3.6 パネル用

| キー | 内容 |
| --- | --- |
| `SettingsExpanderStyle` (Expander) | IsExpanded 既定 False。テンプレート＝StackPanel［ToggleButton HeaderToggle（IsChecked⇔IsExpanded、Focusable False、テンプレート：Border Padding 0,10 ＞ Grid［ContentPresenter(ヘッダー) ｜ TextBlock chevron `&#xE70D;` MDL2 12px `{TextSecondaryBrush}` RenderTransformOrigin .5,.5］。IsChecked→chevron 180°回転、MouseOver→chevron `{TextPrimaryBrush}`）、ContentPresenter ExpandSite（Collapsed, Margin 0,2,0,8、IsExpanded で Visible）、Border 下線 `{BorderBrush}` 0,0,0,1］ |
| `SettingsSectionHeaderStyle` (TextBlock) | SemiBold, 14, TextPrimary |
| `SettingsTabControlStyle` (TabControl) | Grid 2行：Border（下線 Border 0,0,0,1）＞ TabPanel IsItemsHost Margin 12,0,12,0 ／ ContentPresenter SelectedContent |
| `SettingsTabItemStyle` (TabItem) | Foreground TextSecondary, 13, Hand。テンプレート Border（BorderThickness 0,0,0,2 透明, Padding 10,10,10,8）＞ Header。IsSelected→下線 Primary・文字 Primary・SemiBold。非選択＋MouseOver→文字 TextPrimary・下線 HoverBorder |
| `StatsBarStyle` (ProgressBar) | Grid［Border 地 `{BackgroundBrush}` CornerRadius 4 枠 Border 1 ／ Border PART_Track ／ Border PART_Indicator 地=Foreground CornerRadius 4 HorizontalAlignment Left Margin 1］ |

### 3.7 アプリレベルの共有リソース（Styles.xaml 内）

```xml
<conv:TimeToPositionConverter x:Key="TimeToPositionConverter"/>
<conv:DurationToHeightConverter x:Key="DurationToHeightConverter"/>
<conv:DateToPagePositionConverter x:Key="DateToPagePositionConverter"/>
<conv:DateToPageVisibilityConverter x:Key="DateToPageVisibilityConverter"/>
<conv:IndexToTopMarginConverter x:Key="IndexToTopMarginConverter" ItemHeight="24"/>
<conv:DateToVisibleDaysConverter x:Key="DateToVisibleDaysConverter"/>
<sys:Int32 x:Key="Zero">0</sys:Int32>
<sys:Boolean x:Key="FalseValue">False</sys:Boolean>
<sys:Boolean x:Key="TrueValue">True</sys:Boolean>
<BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter"/>
<conv:InvertedBooleanToVisibilityConverter x:Key="InvertedBooleanToVisibilityConverter"/>
<conv:LShapeGeometryConverter x:Key="LShapeGeometryConverter"/>
<conv:DayOfWeekToBrushConverter x:Key="DayOfWeekToBrushConverter"/>
<conv:DateToBackgroundBrushConverter x:Key="DateToBackgroundBrushConverter"/>
<conv:BrushToContrastTextConverter x:Key="BrushToContrastTextConverter"/>
<conv:BrushToSubtleBackgroundConverter x:Key="BrushToSubtleBackgroundConverter"/>
<conv:ProjectCodeIdConverter x:Key="ProjectCodeIdConverter"/>
```
（`xmlns:sys="clr-namespace:System;assembly=mscorlib"`）

## 4. コンバーター仕様

| コンバーター | 入力 | 出力 |
| --- | --- | --- |
| `TimeToPositionConverter` (Multi) | [0]=DateTime, [1]=DisplayStartHour(int) | `(time.TimeOfDay.TotalHours - startHour) * PixelsPerHour(60)` |
| `DurationToHeightConverter` | double時間 / ScheduleItem / TimeSpan、param `"ADD_EXTENSION"` | `hours*60`（param 時は +15：L字の足） |
| `DateToPagePositionConverter` (Multi, 8値) | [0]StartTime [1]ColumnIndex [2]MaxColumnIndex [3]CurrentDate [4]ViewMode [5]Canvas.ActualWidth [6]IsAllDay [7]EnabledDays、param `"WIDTH"` で幅 | 下記 |
| `DateToPageVisibilityConverter` (Multi, 4値) | [0]日付 [1]CurrentDate [2]ViewMode [3]EnabledDays | Day：同日なら Visible（**曜日フィルタは見ない**）。Week：非表示曜日→Collapsed、その週（月曜始まり）内なら Visible |
| `DateToVisibleDaysConverter` (Multi, 3値) | [0]CurrentDate [1]ViewMode [2]EnabledDays | Day→[date]、他→その週の有効曜日の日付 |
| `IndexToTopMarginConverter` | int index | `index*ItemHeight(24) + MarginTop(2)` |
| `LShapeGeometryConverter` (Multi) | [0]幅 [1]高さ | 高さ≤15 または 幅≤30 → 矩形。それ以外：左下が欠けたL字（(0,0)→(W,0)→(W,H)→(W-30,H)→(W-30,H-15)→(0,H-15)）。Freeze |
| `DateToBackgroundBrushConverter` (Multi) | [0]DateTime（[1]は IsDarkMode：再評価用） | 今日→`PrimarySubtleBrush`、他→`SurfaceBrush`（ThemeHelper で取得） |
| `DayOfWeekToBrushConverter` (Multi) | [0]DateTime | 土→`PrimaryBrush`、日→`DangerBrush`、他→`TextPrimaryBrush` |
| `BrushToContrastTextConverter` | Brush/Color、param `"Muted"` | 輝度 `0.299R+0.587G+0.114B`（α&lt;255 は `L*a + 255*(1-a)`）が 140 超→暗文字 #1E293B（Muted は #B01E293B）、以下→明文字 #F8FAFC（Muted は #C0F8FAFC）。グラデーションは先頭ストップ。不明は暗文字 |
| `BrushToSubtleBackgroundConverter` | Brush | `CloneCurrentValue()` して `Opacity *= 0.3`、Freeze。null→Transparent。`static CreateSubtleBrush(Brush?)` も公開 |
| `InvertedBooleanToVisibilityConverter` | bool | true→Collapsed、false→Visible |
| `ProjectCodeIdConverter` | string?⇔string | Convert：null→""、ConvertBack：""→null |

`DateToPagePositionConverter`：
- 値が6未満、幅/開始が型不一致 → 0.0
- 終日なら Column/MaxColumn を 0 扱い。totalColumns = Max+1
- Day：日付不一致なら幅0 / X=-10000。一致なら 幅=W/total、X=col*幅
- Week：週外または非表示曜日なら 幅0 / X=-10000。有効曜日数 n、その日の有効曜日内インデックス k。列幅 = W/n、アイテム幅 = 列幅/total、X = k*列幅 + col*アイテム幅

## 5. 補助コントロール

### TransitioningContentControl（Controls）
- DP：`TransitionDirection`（Forward/Backward、既定 Forward）、`TransitionAxis`（Horizontal/Vertical、既定 Horizontal）
- `DefaultStyleKey` を上書き。テンプレート（Generic.xaml）：`Grid ClipToBounds=True` に `ContentPresenter PART_PreviousContentPresentationSite`（Content=null）と `PART_CurrentContentPresentationSite`（Content=TemplateBinding）を重ねる。どちらも `CacheMode=BitmapCache(EnableClearType, RenderAtScale=1)`, `RenderTransform=TranslateTransform`、ContentTemplate/配置は TemplateBinding
- `OnContentChanged(old,new)`：同値か old==null（初回）は何もしない。それ以外は遷移ID++、両方 Visible、current=new/previous=old、Transform を作り直し、300ms `QuarticEase EaseOut` で
  横：Forward なら old 0→-W、new W→0（Backward は逆）。縦は高さで同様。幅/高さ0なら500。350ms 後に遷移IDが同じなら previous を null・Collapsed

### ScrollViewerHelper.EnableMiddleButtonScroll（添付プロパティ）
- 中ボタン押下：オートスクロール開始（既に有効なら停止）。他ボタン押下は停止
- 開始時：32×32 の Popup（Absolute、マウス位置中心、HitTest なし）に、外円 28px（地 ARGB(160,240,240,240)、線 ARGB(180,100,100,100) 1.5）、中心点 4px（#3C3C3C）、スクロール可能方向に三角矢印（縦：`M16,5 L12,10 L20,10Z` / `M16,27 L12,22 L20,22Z`、横：`M5,16 L10,12 L10,20Z` / `M27,16 L22,12 L22,20Z`）
- 10ms タイマー（Background 優先度）：開始点からのずれ dx,dy がデッドゾーン 8 を超えた分 ×0.15 をオフセットに加算
- 中ボタンを 5px 未満の移動で離したら「トグルモード」で継続、ドラッグして離したら終了。キー入力・キャプチャ喪失で終了
- カーソル：縦横可→ScrollAll、縦のみ→ScrollNS、横のみ→ScrollWE、不可→No。終了で Arrow

## 6. MDL2 グリフ一覧（使用箇所）

| コード | 用途 |
| --- | --- |
| E7A7 / E7A6 | 元に戻す / やり直す |
| E76B / E76C | 前へ / 次へ（E76C はサブタスクの閉じた三角にも） |
| E70D | 下向きシェブロン（Expander、サブタスク展開、スヌーズ▾） |
| E721 | 検索 |
| E71C | 表示フィルタ |
| E73A | ToDo（チェックボックス） |
| E74C | 管理 |
| E713 | 設定 |
| E7BA | 警告（データ通知・予定リマインダー・見逃し通知） |
| EA8F | 通知ベル（ToDo 通知・通知日時） |
| E8FD | 一覧（まとめ通知） |
| E768 | 再生（自動記録開始通知・ToDo の記録開始ボタン） |
| E823 | 時計（離席通知） |
| E710 | 追加（クイック追加欄） |
| E73E | チェックマーク |
| E735 | 塗りつぶし星（今日やる） |
| E895 | 同期（繰り返し） |
| E916 | ストップウォッチ（記録時間・進捗） |
| E787 / E70F | 検索結果の種別：カレンダー（予定）/ ペン（ふりかえり） |
| E70B | ふりかえり空状態 |
| E8C8 | コピー（タイムシート） |
| E8C9 | 重要（繰り越し候補の優先度高） |

## 7. アプリアイコン（Assets/AppIcon.ico）

- マルチサイズ ICO（16/24/32/48/64/256px、各フレーム PNG 32bit RGBA）
- デザイン（256px 基準）：
  - 全面を覆う**角丸の正方形**（角丸半径は幅の約18%）。地は濃紺〜ほぼ黒の斜めグラデーション（左上 #252D3A 付近 → 右下 #0B0F17 付近）、外周に細い明るめのグレーの縁取り
  - 左側に**青い時計**：太いリング（#4A8FF0 前後の明るい青、外径は幅の約40%）、中心に白い点、白い短針が12時・長針が3時方向（L字）
  - 右側に**横長のカプセル3本**（予定バーの抽象）：上2本は同じ長さで淡い青〜水色のグラデーション＋上辺に白いハイライト、下の1本は短く濃い青（#2F6FE0 付近）
- 小サイズ（16/24px）でも時計のリングとバーが判別できるよう線を太めに
