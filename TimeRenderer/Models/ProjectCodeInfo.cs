using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TimeRenderer.Models;

/// <summary>
/// 記録を案件・プロジェクト単位で集計するためのプロジェクトコード。
/// アイテム側には Code ではなく Id を保存し、コード名を変更しても紐付けを維持する。
/// </summary>
public sealed class ProjectCodeInfo : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    private string _code = string.Empty;
    public string Code
    {
        get => _code;
        set
        {
            if (SetProperty(ref _code, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    private bool _isActive = true;
    /// <summary>新しい予定・実績へ割り当て可能か。無効でも過去データの参照は維持する。</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            var label = (Code, Name) switch
            {
                ({ Length: > 0 }, { Length: > 0 }) => $"{Code} - {Name}",
                ({ Length: > 0 }, _) => Code,
                (_, { Length: > 0 }) => Name,
                _ => "（コード未入力）"
            };
            return IsActive ? label : $"{label}（無効）";
        }
    }

    public static List<ProjectCodeInfo> CreateDefaults() =>
    [
        new() { Code = "GENERAL", Name = "共通" }
    ];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
