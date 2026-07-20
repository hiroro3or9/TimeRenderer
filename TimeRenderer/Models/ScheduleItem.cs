using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using System.Text.Json.Serialization;

namespace TimeRenderer.Models;

/// <summary>
/// スケジュールの1件分のデータを表すモデルクラス。
/// プロパティ変更通知によりUIへのリアルタイム反映をサポートする。
/// </summary>
public class ScheduleItem : INotifyPropertyChanged
{
    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value)) OnPropertyChanged(nameof(ToolTipText));
        }
    }

    private DateTime _startTime;
    public DateTime StartTime
    {
        get => _startTime;
        set
        {
            if (SetProperty(ref _startTime, value))
            {
                OnPropertyChanged(nameof(DurationHours));
                OnPropertyChanged(nameof(ToolTipText));
            }
        }
    }

    private DateTime _endTime;
    public DateTime EndTime
    {
        get => _endTime;
        set
        {
            if (SetProperty(ref _endTime, value))
            {
                OnPropertyChanged(nameof(DurationHours));
                OnPropertyChanged(nameof(ToolTipText));
            }
        }
    }

    private bool _isAllDay;
    public bool IsAllDay
    {
        get => _isAllDay;
        set
        {
            if (SetProperty(ref _isAllDay, value)) OnPropertyChanged(nameof(ToolTipText));
        }
    }

    private string _content = string.Empty;
    public string Content
    {
        get => _content;
        set
        {
            if (SetProperty(ref _content, value)) OnPropertyChanged(nameof(ToolTipText));
        }
    }

    private Brush _backgroundColor = Brushes.LightBlue;
    [JsonIgnore]
    public Brush BackgroundColor
    {
        get => _backgroundColor;
        set => SetProperty(ref _backgroundColor, value);
    }

    private string? _categoryId;
    /// <summary>
    /// 所属カテゴリのID（CategoryInfo.Id）。null は旧データ等の未設定を表し、
    /// その場合は色（ColorCode）からのフォールバック解決が行われる。
    /// </summary>
    public string? CategoryId
    {
        get => _categoryId;
        set => SetProperty(ref _categoryId, value);
    }

    private string? _routineId;
    /// <summary>
    /// このアイテムの生成元となった定期予定（RoutineScheduleItem.Id）。
    /// 手動で追加・記録されたアイテムは null。
    /// リマインダー通知・自動記録開始の対象判定や、定期予定の重複生成防止に使用する。
    /// </summary>
    public string? RoutineId
    {
        get => _routineId;
        set => SetProperty(ref _routineId, value);
    }

    private bool _remindAtStart;
    /// <summary>
    /// 手動登録した単発予定用：開始時刻になったらリマインダー通知を表示する。
    /// 定期予定から生成されたアイテム（RoutineId あり）はルーティン側の設定が優先される。
    /// </summary>
    public bool RemindAtStart
    {
        get => _remindAtStart;
        set => SetProperty(ref _remindAtStart, value);
    }

    private bool _autoStartRecording;
    /// <summary>
    /// 手動登録した単発予定用：開始時刻になったら確認なしで記録を自動開始する。
    /// RemindAtStart より優先される。
    /// </summary>
    public bool AutoStartRecording
    {
        get => _autoStartRecording;
        set => SetProperty(ref _autoStartRecording, value);
    }

    private bool _forceStartRecording;
    /// <summary>
    /// 自動開始時に既に記録中でも現在の記録を停止・保存して強制的に開始する。
    /// AutoStartRecording が true のときのみ有効。false なら記録中はリマインダー通知にフォールバックする。
    /// </summary>
    public bool ForceStartRecording
    {
        get => _forceStartRecording;
        set => SetProperty(ref _forceStartRecording, value);
    }

    public string ColorCode
    {
        get => _backgroundColor.ToString();
        set
        {
            if (!string.IsNullOrEmpty(value))
            {
                try
                {
                    BrushConverter converter = new();
                    if (converter.ConvertFromString(value) is Brush brush)
                    {
                        BackgroundColor = brush;
                    }
                }
                catch { } // 無効なカラーコードは無視
            }
        }
    }

    private bool _isSelected;
    /// <summary>
    /// 選択中か（キーボード操作の現在位置）。
    /// ビューごとに別々の選択を持つと、表示を切り替えたときに選択が消えて混乱するため、
    /// アイテム自身に持たせて日/週/月/タイムラインで共通にする。
    /// </summary>
    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    // 表示用プロパティ：列インデックス（終日イベントの縦積み位置に使用）
    private int _columnIndex;
    [JsonIgnore]
    public int ColumnIndex
    {
        get => _columnIndex;
        set => SetProperty(ref _columnIndex, value);
    }

    [JsonIgnore]
    public double DurationHours => (EndTime - StartTime).TotalHours;

    /// <summary>
    /// ホバー時に出す詳細テキスト。
    /// 短い予定はバーが数ピクセルしかなく、タイトルも時刻も潰れて読めないため、
    /// 日/週ビューとタイムラインの両方でこれを唯一の情報源として使う。
    /// </summary>
    [JsonIgnore]
    public string ToolTipText => BuildToolTipText();

    private string BuildToolTipText()
    {
        var duration = EndTime - StartTime;
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;

        var lines = new System.Collections.Generic.List<string>
        {
            string.IsNullOrWhiteSpace(Title) ? "(無題)" : Title
        };

        if (IsAllDay)
        {
            lines.Add(StartTime.Date == EndTime.Date || StartTime.Date == EndTime.Date.AddDays(-1)
                ? $"{StartTime:MM/dd (ddd)} 終日"
                : $"{StartTime:MM/dd} 〜 {EndTime.AddDays(-1):MM/dd} 終日");
        }
        else if (StartTime.Date == EndTime.Date)
        {
            lines.Add($"{StartTime:MM/dd (ddd) HH:mm} 〜 {EndTime:HH:mm}");
        }
        else
        {
            lines.Add($"{StartTime:MM/dd (ddd) HH:mm} 〜 {EndTime:MM/dd (ddd) HH:mm}");
        }

        if (!IsAllDay)
        {
            lines.Add(duration.TotalHours >= 1
                ? $"所要 {duration.TotalHours:0.#} 時間"
                : $"所要 {duration.TotalMinutes:0} 分");
        }

        if (!string.IsNullOrWhiteSpace(Content))
        {
            var memo = Content.Replace("\r", "").Replace("\n", " ").Trim();
            if (memo.Length > 60) memo = string.Concat(memo.AsSpan(0, 60), "…");
            lines.Add("");
            lines.Add(memo);
        }

        return string.Join("\n", lines);
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
