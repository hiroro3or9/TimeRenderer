using System.ComponentModel;
using System.Text.Json.Serialization;

using TimeRenderer.Infrastructure;

namespace TimeRenderer.Models;

/// <summary>
/// 記録を案件・プロジェクト単位で集計するためのプロジェクトコード。
/// アイテム側には Code ではなく Id を保存し、コード名を変更しても紐付けを維持する。
/// </summary>
public sealed class ProjectCodeInfo : ObservableObject
{
    /// <summary>「（未設定）」の選択肢が持つ Id。マスターのコードは必ず非空なので衝突しない。</summary>
    public const string UnassignedId = "";

    /// <summary>プロジェクトコードを持たない状態の表示名。</summary>
    public const string UnassignedLabel = "（未設定）";

    /// <summary>
    /// コンボボックスの先頭へ並べる「（未設定）」の選択肢。
    /// マスター（<see cref="AppSettings.ProjectCodes"/>）には入れず、保存時は null へ戻す。
    /// </summary>
    public static ProjectCodeInfo Unassigned { get; } = new()
    {
        Id = UnassignedId,
        Name = UnassignedLabel
    };

    /// <summary>選択結果を保存値へ直す。「（未設定）」と未選択はどちらも null。</summary>
    public static string? ToStoredId(ProjectCodeInfo? selected) => Normalize(selected)?.Id;

    /// <summary>「（未設定）」の選択肢を null に均す。</summary>
    public static ProjectCodeInfo? Normalize(ProjectCodeInfo? selected) =>
        selected is { Id.Length: > 0 } ? selected : null;

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

    private bool _isFilterEnabled = true;
    /// <summary>表示フィルタでこのプロジェクトコードを表示するか（セッション内のみの状態・非永続）。</summary>
    [JsonIgnore]
    public bool IsFilterEnabled
    {
        get => _isFilterEnabled;
        set => SetProperty(ref _isFilterEnabled, value);
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

}
