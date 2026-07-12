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
        set => SetProperty(ref _title, value);
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
            }
        }
    }

    private bool _isAllDay;
    public bool IsAllDay
    {
        get => _isAllDay;
        set => SetProperty(ref _isAllDay, value);
    }

    private string _content = string.Empty;
    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }

    private Brush _backgroundColor = Brushes.LightBlue;
    [JsonIgnore]
    public Brush BackgroundColor
    {
        get => _backgroundColor;
        set => SetProperty(ref _backgroundColor, value);
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
