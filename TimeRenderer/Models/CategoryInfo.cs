using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace TimeRenderer.Models;

/// <summary>
/// 作業カテゴリ（名前付きの色）。ScheduleItem とは ColorCode で紐づく。
/// </summary>
public class CategoryInfo : INotifyPropertyChanged
{
    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private string _colorCode = Brushes.LightBlue.ToString();
    public string ColorCode
    {
        get => _colorCode;
        set
        {
            if (SetProperty(ref _colorCode, value))
            {
                OnPropertyChanged(nameof(Brush));
            }
        }
    }

    /// <summary>表示用ブラシ（ColorCode から生成）</summary>
    [JsonIgnore]
    public Brush Brush => CreateBrush(_colorCode);

    private bool _isFilterEnabled = true;
    /// <summary>
    /// 色フィルタでこのカテゴリを表示するか（セッション内のみの状態・非永続）。
    /// </summary>
    [JsonIgnore]
    public bool IsFilterEnabled
    {
        get => _isFilterEnabled;
        set => SetProperty(ref _isFilterEnabled, value);
    }

    public static Brush CreateBrush(string colorCode)
    {
        if (!string.IsNullOrEmpty(colorCode))
        {
            try
            {
                BrushConverter converter = new();
                if (converter.ConvertFromString(colorCode) is Brush brush)
                {
                    if (brush.CanFreeze) brush.Freeze();
                    return brush;
                }
            }
            catch { /* 無効なカラーコードはデフォルトへフォールバック */ }
        }
        return Brushes.LightBlue;
    }

    /// <summary>既定のカテゴリ一覧（従来の色パレットを引き継ぐ）</summary>
    public static List<CategoryInfo> CreateDefaults() =>
    [
        new() { Name = "作業", ColorCode = Brushes.LightBlue.ToString() },
        new() { Name = "会議", ColorCode = Brushes.LightGreen.ToString() },
        new() { Name = "休憩", ColorCode = Brushes.LightPink.ToString() },
        new() { Name = "学習", ColorCode = Brushes.LightYellow.ToString() },
        new() { Name = "雑務", ColorCode = Brushes.LightGray.ToString() },
        new() { Name = "重要", ColorCode = Brushes.LightCoral.ToString() },
        new() { Name = "予定", ColorCode = Brushes.Lavender.ToString() },
        new() { Name = "その他", ColorCode = Brushes.LightCyan.ToString() },
        new() { Name = "記録", ColorCode = Brushes.DarkOrange.ToString() },
    ];

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
