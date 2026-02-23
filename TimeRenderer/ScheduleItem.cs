using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using System.Text.Json.Serialization;

namespace TimeRenderer;

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
        set { if (_title != value) { _title = value; OnPropertyChanged(); } }
    }

    private DateTime _startTime;
    public DateTime StartTime
    {
        get => _startTime;
        set { if (_startTime != value) { _startTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationHours)); } }
    }

    private DateTime _endTime;
    public DateTime EndTime
    {
        get => _endTime;
        set { if (_endTime != value) { _endTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationHours)); } }
    }

    private bool _isAllDay;
    public bool IsAllDay
    {
        get => _isAllDay;
        set { if (_isAllDay != value) { _isAllDay = value; OnPropertyChanged(); } }
    }

    private string _content = string.Empty;
    public string Content
    {
        get => _content;
        set { if (_content != value) { _content = value; OnPropertyChanged(); } }
    }

    private Brush _backgroundColor = Brushes.LightBlue;
    [JsonIgnore]
    public Brush BackgroundColor
    {
        get => _backgroundColor;
        set { if (_backgroundColor != value) { _backgroundColor = value; OnPropertyChanged(); } }
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

    // 表示用プロパティ：列インデックス
    private int _columnIndex;
    [JsonIgnore]
    public int ColumnIndex
    {
        get => _columnIndex;
        set { if (_columnIndex != value) { _columnIndex = value; OnPropertyChanged(); } }
    }

    // 表示用プロパティ：最大列インデックス（グループ内の総列数 - 1）
    private int _maxColumnIndex;
    [JsonIgnore]
    public int MaxColumnIndex
    {
        get => _maxColumnIndex;
        set { if (_maxColumnIndex != value) { _maxColumnIndex = value; OnPropertyChanged(); } }
    }

    public double DurationHours => (EndTime - StartTime).TotalHours;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
