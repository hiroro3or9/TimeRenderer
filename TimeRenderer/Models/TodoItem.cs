using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace TimeRenderer.Models;

/// <summary>
/// ToDo の優先度。数値で保存されるため、既存の値の割り当ては変更しないこと
/// （未設定の旧データは 1 = Normal として読み込ませたいので、既定値を明示的に代入する）。
/// </summary>
public enum TodoPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
}

/// <summary>
/// ToDo の繰り返しの単位。数値で保存されるため、既存の値の割り当ては変更しないこと
/// （未設定の旧データは 0 = None として読み込まれる）。
/// </summary>
public enum TodoRecurrenceUnit
{
    /// <summary>繰り返さない</summary>
    None = 0,
    Day = 1,
    Week = 2,
    Month = 3,
}

/// <summary>
/// ToDo の1件分。予定（ScheduleItem）とは別のデータで、時刻を持たない「やること」を表す。
///
/// 予定アイテムにしてしまうと必ず時間帯を決めなければならず、
/// 「いつやるか決めていないが忘れたくないこと」を置く場所が無くなる。
/// 期限日（DueDate）を持つものだけが日/週ビューの終日行にチップとして並ぶ。
/// </summary>
public class TodoItem : INotifyPropertyChanged
{
    /// <summary>識別子。記録との紐付けや並べ替えの安定化に使う</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value)) OnPropertyChanged(nameof(ToolTipText));
        }
    }

    private string _content = string.Empty;
    /// <summary>補足メモ</summary>
    public string Content
    {
        get => _content;
        set
        {
            if (SetProperty(ref _content, value))
            {
                OnPropertyChanged(nameof(HasContent));
                OnPropertyChanged(nameof(ToolTipText));
            }
        }
    }

    private DateTime? _dueDate;
    /// <summary>
    /// 期限日（日付部分のみ）。null は「期限なし」で、パネルには出るが日/週ビューには並ばない。
    /// </summary>
    public DateTime? DueDate
    {
        get => _dueDate;
        set
        {
            var normalized = value?.Date;
            if (SetProperty(ref _dueDate, normalized)) NotifyDueStateChanged();
        }
    }

    private DateTime? _remindAt;
    /// <summary>
    /// 通知する日時。null なら通知しない。
    ///
    /// 期限（DueDate）は日付しか持たないため「いつ思い出したいか」は別に持たせる。
    /// 期限の数日前に一度だけ知らせる、といった使い方ができる。
    /// </summary>
    public DateTime? RemindAt
    {
        get => _remindAt;
        set
        {
            if (SetProperty(ref _remindAt, value))
            {
                OnPropertyChanged(nameof(HasReminder));
                OnPropertyChanged(nameof(RemindDisplay));
                OnPropertyChanged(nameof(ToolTipText));
            }
        }
    }

    private TodoPriority _priority = TodoPriority.Normal;
    public TodoPriority Priority
    {
        get => _priority;
        set
        {
            if (SetProperty(ref _priority, value))
            {
                OnPropertyChanged(nameof(IsHighPriority));
                OnPropertyChanged(nameof(IsLowPriority));
                OnPropertyChanged(nameof(ToolTipText));
            }
        }
    }

    private bool _isCompleted;
    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (_isCompleted == value) return;

            _isCompleted = value;

            // 完了日時は変更通知より先に確定させる。
            // 購読側（繰り返しの次回分の生成）が、この値を基準日として読むため
            CompletedAt = value ? DateTime.Now : null;

            OnPropertyChanged();
            NotifyDueStateChanged();
        }
    }

    private DateTime? _completedAt;
    /// <summary>完了した日時（未完了なら null）</summary>
    public DateTime? CompletedAt
    {
        get => _completedAt;
        set => SetProperty(ref _completedAt, value);
    }

    private string? _categoryId;
    /// <summary>所属カテゴリのID（CategoryInfo.Id）。null なら ColorCode を使う</summary>
    public string? CategoryId
    {
        get => _categoryId;
        set => SetProperty(ref _categoryId, value);
    }

    private string _colorCode = Brushes.LightBlue.ToString();
    /// <summary>カテゴリ未解決時のフォールバック色。記録開始時の色にもそのまま使う</summary>
    public string ColorCode
    {
        get => _colorCode;
        set
        {
            if (string.IsNullOrEmpty(value)) return;
            if (SetProperty(ref _colorCode, value)) OnPropertyChanged(nameof(Brush));
        }
    }

    /// <summary>表示用ブラシ（ColorCode から生成）</summary>
    [JsonIgnore]
    public Brush Brush => CategoryInfo.CreateBrush(_colorCode);

    private long _recordedTicks;
    /// <summary>
    /// この ToDo で記録した時間の累計（Ticks）。
    /// TimeSpan のままだと JSON が "01:30:00" 形式になり、桁あふれ時の復旧が難しいため Ticks で持つ。
    /// </summary>
    public long RecordedTicks
    {
        get => _recordedTicks;
        set
        {
            if (SetProperty(ref _recordedTicks, value < 0 ? 0 : value))
            {
                OnPropertyChanged(nameof(RecordedDuration));
                OnPropertyChanged(nameof(HasRecorded));
                OnPropertyChanged(nameof(RecordedDisplay));
                OnPropertyChanged(nameof(ShowRecordedOnly));
                NotifyProgressChanged(); // 進捗は記録時間から計算している
            }
        }
    }

    /// <summary>記録済みの累計時間</summary>
    [JsonIgnore]
    public TimeSpan RecordedDuration => TimeSpan.FromTicks(_recordedTicks);

    private int _estimatedMinutes;
    /// <summary>
    /// 見積もり時間（分）。0 は未設定。
    /// 記録した時間（RecordedTicks）と並べて進捗として見せる。
    /// </summary>
    public int EstimatedMinutes
    {
        get => _estimatedMinutes;
        set
        {
            // 壊れた保存データで進捗バーが破綻しないよう、1週間ぶんを上限にする
            if (SetProperty(ref _estimatedMinutes, Math.Clamp(value, 0, 60 * 24 * 7)))
            {
                NotifyProgressChanged();
            }
        }
    }

    private int _sortOrder;
    /// <summary>手動並べ替えでの位置。小さいほど上に並ぶ（手動モード以外では使われない）</summary>
    public int SortOrder
    {
        get => _sortOrder;
        set => SetProperty(ref _sortOrder, value);
    }

    private TodoRecurrenceUnit _recurrence = TodoRecurrenceUnit.None;
    /// <summary>繰り返しの単位。None 以外なら、完了時に次回分の ToDo が自動で作られる</summary>
    public TodoRecurrenceUnit Recurrence
    {
        get => _recurrence;
        set
        {
            if (SetProperty(ref _recurrence, value)) NotifyRecurrenceChanged();
        }
    }

    private int _recurrenceInterval = 1;
    /// <summary>繰り返しの間隔（1 なら毎日／毎週／毎月、2 なら隔週／隔月）。旧データ対策で 1 未満は 1 として扱う</summary>
    public int RecurrenceInterval
    {
        get => _recurrenceInterval < 1 ? 1 : _recurrenceInterval;
        set
        {
            if (SetProperty(ref _recurrenceInterval, Math.Clamp(value, 1, 99))) NotifyRecurrenceChanged();
        }
    }

    private bool _recurrenceFromCompletion;
    /// <summary>
    /// true なら「完了した日」から次回の期限を数える（掃除・片付けのように、やった日が起点のもの）。
    /// false なら「期限日」から数えるため、遅れて完了しても曜日や日付がずれない。
    /// </summary>
    public bool RecurrenceFromCompletion
    {
        get => _recurrenceFromCompletion;
        set
        {
            if (SetProperty(ref _recurrenceFromCompletion, value)) NotifyRecurrenceChanged();
        }
    }

    /// <summary>作成日時。並べ替えの最終キーに使う（同着を安定させる）</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // ===== 表示用の派生プロパティ =====
    //
    // 期限に関する状態は「今日」に依存するため、日付をまたいだときは
    // MainViewModel が NotifyDueStateChanged を呼んで表示を更新する。

    [JsonIgnore]
    public bool HasContent => !string.IsNullOrWhiteSpace(Content);

    [JsonIgnore]
    public bool HasDueDate => DueDate.HasValue;

    [JsonIgnore]
    public bool HasRecorded => _recordedTicks > 0;

    [JsonIgnore]
    public bool HasReminder => RemindAt.HasValue;

    /// <summary>
    /// 一覧で記録時間だけを出すか。
    /// 見積もりがある場合は「実績 / 見積もり」と進捗バーで見せるため、こちらは出さない。
    /// </summary>
    [JsonIgnore]
    public bool ShowRecordedOnly => HasRecorded && !HasEstimate;

    /// <summary>一覧表示用：通知日時（例: "8/12 09:00"）</summary>
    [JsonIgnore]
    public string RemindDisplay => RemindAt.HasValue ? RemindAt.Value.ToString("M/d HH:mm") : string.Empty;

    [JsonIgnore]
    public bool IsHighPriority => Priority == TodoPriority.High;

    [JsonIgnore]
    public bool IsLowPriority => Priority == TodoPriority.Low;

    /// <summary>期限を過ぎている未完了の ToDo か</summary>
    [JsonIgnore]
    public bool IsOverdue => !IsCompleted && DueDate.HasValue && DueDate.Value.Date < DateTime.Today;

    /// <summary>期限が今日の未完了の ToDo か</summary>
    [JsonIgnore]
    public bool IsDueToday => !IsCompleted && DueDate.HasValue && DueDate.Value.Date == DateTime.Today;

    /// <summary>一覧表示用：期限（例: "今日" / "明日" / "3日超過" / "8/12 (水)"）</summary>
    [JsonIgnore]
    public string DueDisplay
    {
        get
        {
            if (!DueDate.HasValue) return string.Empty;

            var days = (DueDate.Value.Date - DateTime.Today).Days;
            return days switch
            {
                0 => "今日",
                1 => "明日",
                -1 => "昨日",
                < 0 => $"{-days}日超過",
                _ => DueDate.Value.ToString("M/d (ddd)"),
            };
        }
    }

    /// <summary>一覧表示用：記録済みの累計時間（例: "1:30"）</summary>
    [JsonIgnore]
    public string RecordedDisplay =>
        HasRecorded ? FormatDuration(RecordedDuration) : string.Empty;

    // ===== 見積もりと進捗 =====

    [JsonIgnore]
    public bool HasEstimate => EstimatedMinutes > 0;

    /// <summary>見積もり時間</summary>
    [JsonIgnore]
    public TimeSpan EstimatedDuration => TimeSpan.FromMinutes(EstimatedMinutes);

    /// <summary>一覧表示用：見積もり時間（例: "2:00"）</summary>
    [JsonIgnore]
    public string EstimateDisplay => HasEstimate ? FormatDuration(EstimatedDuration) : string.Empty;

    /// <summary>
    /// 進捗バー用の割合（0〜100）。
    /// 見積もりを超えても 100 で止める（バーは満杯・超過は色と数字で示す）。
    /// </summary>
    [JsonIgnore]
    public double ProgressPercent => HasEstimate
        ? Math.Clamp(RecordedDuration.TotalMinutes / EstimatedMinutes * 100.0, 0, 100)
        : 0;

    /// <summary>見積もりを超えて記録しているか</summary>
    [JsonIgnore]
    public bool IsOverEstimate => HasEstimate && RecordedDuration > EstimatedDuration;

    /// <summary>一覧表示用：実績と見積もり（例: "1:20 / 2:00"）</summary>
    [JsonIgnore]
    public string ProgressDisplay =>
        HasEstimate ? $"{FormatDuration(RecordedDuration)} / {EstimateDisplay}" : string.Empty;

    private void NotifyProgressChanged()
    {
        OnPropertyChanged(nameof(HasEstimate));
        OnPropertyChanged(nameof(ShowRecordedOnly));
        OnPropertyChanged(nameof(EstimatedDuration));
        OnPropertyChanged(nameof(EstimateDisplay));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(IsOverEstimate));
        OnPropertyChanged(nameof(ProgressDisplay));
        OnPropertyChanged(nameof(ToolTipText));
    }

    // ===== 繰り返し =====

    [JsonIgnore]
    public bool HasRecurrence => Recurrence != TodoRecurrenceUnit.None;

    /// <summary>一覧表示用：繰り返し（例: "毎週" / "隔週" / "3日ごと" / "毎月（完了日から）"）</summary>
    [JsonIgnore]
    public string RecurrenceDisplay
    {
        get
        {
            if (!HasRecurrence) return string.Empty;

            var unit = Recurrence switch
            {
                TodoRecurrenceUnit.Day => "日",
                TodoRecurrenceUnit.Month => "ヶ月",
                _ => "週",
            };

            var head = RecurrenceInterval switch
            {
                1 => Recurrence == TodoRecurrenceUnit.Day ? "毎日" : $"毎{unit}",
                2 => Recurrence == TodoRecurrenceUnit.Day ? "2日ごと" : $"隔{unit}",
                _ => $"{RecurrenceInterval}{unit}ごと",
            };

            return RecurrenceFromCompletion ? $"{head}（完了日から）" : head;
        }
    }

    private void NotifyRecurrenceChanged()
    {
        OnPropertyChanged(nameof(HasRecurrence));
        OnPropertyChanged(nameof(RecurrenceDisplay));
        OnPropertyChanged(nameof(ToolTipText));
    }

    /// <summary>
    /// 完了したときの次回分を作る。繰り返しが無ければ null。
    ///
    /// 期限日を基準にすると、遅れて完了しても曜日・日付がずれない。
    /// ただし何回分も溜まっていた場合は、次に来る回まで進めてから作る
    /// （3週間放置した毎週の ToDo が、完了した瞬間に過去の期限で復活しないようにする）。
    /// </summary>
    /// <param name="completedOn">完了した日時</param>
    public TodoItem? CreateNextOccurrence(DateTime completedOn)
    {
        if (!HasRecurrence) return null;

        var baseDate = (RecurrenceFromCompletion ? completedOn : DueDate ?? completedOn).Date;
        var next = AddRecurrenceInterval(baseDate);

        if (!RecurrenceFromCompletion)
        {
            // 間隔が 0 日になることは無いので必ず抜けるが、壊れたデータ対策に上限も置く
            var guard = 0;
            while (next <= completedOn.Date && guard++ < 500)
            {
                next = AddRecurrenceInterval(next);
            }
        }

        return new TodoItem
        {
            Title = Title,
            Content = Content,
            DueDate = next,
            RemindAt = ShiftReminder(next),
            Priority = Priority,
            CategoryId = CategoryId,
            ColorCode = ColorCode,
            EstimatedMinutes = EstimatedMinutes,
            Recurrence = Recurrence,
            RecurrenceInterval = RecurrenceInterval,
            RecurrenceFromCompletion = RecurrenceFromCompletion,
            SortOrder = SortOrder,
        };
    }

    private DateTime AddRecurrenceInterval(DateTime date) => Recurrence switch
    {
        TodoRecurrenceUnit.Day => date.AddDays(RecurrenceInterval),
        TodoRecurrenceUnit.Month => date.AddMonths(RecurrenceInterval),
        _ => date.AddDays(7 * RecurrenceInterval),
    };

    /// <summary>
    /// 次回分の通知日時。期限日との関係（「2日前の9:00」など）をそのまま引き継ぐ。
    /// 期限が無かった場合は時刻だけを引き継ぐ。
    /// </summary>
    private DateTime? ShiftReminder(DateTime nextDue)
    {
        if (RemindAt is not { } remindAt) return null;

        return DueDate.HasValue
            ? nextDue.Date + (remindAt - DueDate.Value.Date)
            : nextDue.Date + remindAt.TimeOfDay;
    }

    /// <summary>時間の表示（例: "1:05"）。24時間を超えても桁落ちしないよう総時間数で書く</summary>
    private static string FormatDuration(TimeSpan duration) =>
        $"{(int)duration.TotalHours}:{duration.Minutes:D2}";

    /// <summary>チップやリスト項目のホバー時に出す詳細テキスト</summary>
    [JsonIgnore]
    public string ToolTipText => BuildToolTipText();

    private string BuildToolTipText()
    {
        var lines = new System.Collections.Generic.List<string>
        {
            string.IsNullOrWhiteSpace(Title) ? "(無題)" : Title
        };

        if (DueDate.HasValue)
        {
            lines.Add(IsOverdue
                ? $"期限 {DueDate.Value:yyyy/MM/dd (ddd)}（{DueDisplay}）"
                : $"期限 {DueDate.Value:yyyy/MM/dd (ddd)}");
        }

        if (RemindAt.HasValue) lines.Add($"通知 {RemindAt.Value:yyyy/MM/dd (ddd) HH:mm}");

        if (Priority != TodoPriority.Normal)
        {
            lines.Add(Priority == TodoPriority.High ? "優先度: 高" : "優先度: 低");
        }

        if (HasRecurrence) lines.Add($"繰り返し: {RecurrenceDisplay}");

        if (HasEstimate)
        {
            lines.Add(IsOverEstimate
                ? $"実績 {ProgressDisplay}（見積もり超過）"
                : $"実績 {ProgressDisplay}");
        }
        else if (HasRecorded)
        {
            lines.Add($"記録済み {RecordedDisplay}");
        }

        if (HasContent)
        {
            var memo = Content.Replace("\r", "").Replace("\n", " ").Trim();
            if (memo.Length > 60) memo = string.Concat(memo.AsSpan(0, 60), "…");
            lines.Add("");
            lines.Add(memo);
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// 期限に依存する表示（超過・今日・残り日数）の再評価を促す。
    /// 日付をまたいだときと、期限・完了状態を変えたときに呼ぶ。
    /// </summary>
    public void NotifyDueStateChanged()
    {
        OnPropertyChanged(nameof(HasDueDate));
        OnPropertyChanged(nameof(IsOverdue));
        OnPropertyChanged(nameof(IsDueToday));
        OnPropertyChanged(nameof(DueDisplay));
        OnPropertyChanged(nameof(ToolTipText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
