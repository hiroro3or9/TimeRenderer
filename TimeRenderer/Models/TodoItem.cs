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
            if (SetProperty(ref _isCompleted, value))
            {
                CompletedAt = value ? DateTime.Now : null;
                NotifyDueStateChanged();
            }
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
                OnPropertyChanged(nameof(ToolTipText));
            }
        }
    }

    /// <summary>記録済みの累計時間</summary>
    [JsonIgnore]
    public TimeSpan RecordedDuration => TimeSpan.FromTicks(_recordedTicks);

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
        HasRecorded ? $"{(int)RecordedDuration.TotalHours}:{RecordedDuration.Minutes:D2}" : string.Empty;

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

        if (Priority != TodoPriority.Normal)
        {
            lines.Add(Priority == TodoPriority.High ? "優先度: 高" : "優先度: 低");
        }

        if (HasRecorded) lines.Add($"記録済み {RecordedDisplay}");

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
