using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>プロジェクトコードのマスターと、記録開始時の既定値を管理する。</summary>
public partial class MainViewModel
{
    public ObservableCollection<ProjectCodeInfo> ProjectCodes { get; } = [];

    /// <summary>新しい予定・実績で選択できるプロジェクトコード。</summary>
    public IReadOnlyList<ProjectCodeInfo> ActiveProjectCodes => [.. ProjectCodes.Where(p => p.IsActive)];

    /// <summary>
    /// スプリントの加算先コンボボックスで「指定しない」を表す番兵。
    /// Id を空にしてあるので <see cref="ResolveProjectCode"/> は null を返し、引き継ぎ扱いになる。
    /// 参照一致で選択状態を復元するため、使い回せる単一インスタンスにしている。
    /// </summary>
    public static readonly ProjectCodeInfo InheritedProjectCode =
        new() { Id = string.Empty, Name = "（前のスプリントから引き継ぐ）" };

    /// <summary>
    /// スプリント編集フォームの加算先コンボボックス用。
    /// 編集中のスプリントが無効なコードを指している場合はそれも残す
    /// （選択肢に無いと ComboBox が選択を外し、保存で指定が消えてしまうため）。
    /// </summary>
    public IReadOnlyList<ProjectCodeInfo> SprintProjectCodeChoices =>
        [InheritedProjectCode, .. GetSelectableProjectCodes(EditingSprint?.UnrecordedTimeProjectCodeId)];

    public ICommand AddProjectCodeCommand { get; private set; } = null!;
    public ICommand DeleteProjectCodeCommand { get; private set; } = null!;
    public ICommand ToggleProjectCodeActiveCommand { get; private set; } = null!;

    private string? _defaultProjectCodeId;
    private string? _unrecordedTimeProjectCodeId;
    private bool _isUnrecordedTimeProjectAggregationEnabled;

    /// <summary>新しい予定・実績と、通常の記録開始で使用する既定のプロジェクトコード。</summary>
    public ProjectCodeInfo? DefaultProjectCode =>
        ResolveProjectCode(_defaultProjectCodeId) is { IsActive: true } selected
            ? selected
            : ProjectCodes.FirstOrDefault(p => p.IsActive);

    /// <summary>設定パネルの既定値コンボボックス用。</summary>
    public ProjectCodeInfo? SelectedDefaultProjectCode
    {
        get => DefaultProjectCode;
        set
        {
            if (value is not { IsActive: true } || value.Id == _defaultProjectCodeId) return;

            _defaultProjectCodeId = value.Id;
            NotifyDefaultProjectCodeChanged();
            SaveSettings();
        }
    }

    /// <summary>勤務時間内の未記録時間を、選択したコードの統計へ加算するか。</summary>
    public bool IsUnrecordedTimeProjectAggregationEnabled
    {
        get => _isUnrecordedTimeProjectAggregationEnabled;
        set
        {
            if (!SetProperty(ref _isUnrecordedTimeProjectAggregationEnabled, value)) return;

            EnsureUnrecordedTimeProjectCode();
            SaveSettings();
            UpdateStats();
        }
    }

    /// <summary>
    /// 未記録時間の加算先を選ぶコンボボックス用。
    /// スプリント側の指定が優先で、これはどのスプリントからも引き継げないときのフォールバック。
    /// </summary>
    public ProjectCodeInfo? SelectedUnrecordedTimeProjectCode
    {
        get => ResolveProjectCode(_unrecordedTimeProjectCodeId) is { IsActive: true } selected
            ? selected
            : DefaultProjectCode;
        set
        {
            if (value is not { IsActive: true } || value.Id == _unrecordedTimeProjectCodeId) return;

            _unrecordedTimeProjectCodeId = value.Id;
            OnPropertyChanged();
            SaveSettings();
            UpdateStats();
        }
    }

    /// <summary>
    /// スプリント側で指定された加算先を、直近の過去へ遡って探す。
    /// 指定されたコードが無効化・削除されている場合はさらに前のスプリントへ遡る
    /// （全体設定へ落とすより、スプリントの連なりの中で解決したほうが意図に近いため）。
    /// </summary>
    /// <param name="orderedSources">
    /// <see cref="SprintHelper.GetUnrecordedTimeProjectCodeSources"/> で並べ替え済みのスプリント一覧
    /// </param>
    /// <param name="date">基準日</param>
    private ProjectCodeInfo? FindSprintUnrecordedTimeProjectCode(
        IReadOnlyList<SprintInfo> orderedSources, DateTime date)
    {
        var target = date.Date;

        for (var i = orderedSources.Count - 1; i >= 0; i--)
        {
            if (orderedSources[i].StartDate.Date > target) continue;

            if (ResolveProjectCode(orderedSources[i].UnrecordedTimeProjectCodeId) is { IsActive: true } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>指定日の未記録時間を加算するコード。機能が無効なら null。</summary>
    private ProjectCodeInfo? ResolveUnrecordedTimeProjectCode(
        IReadOnlyList<SprintInfo> orderedSources, DateTime date)
    {
        if (!IsUnrecordedTimeProjectAggregationEnabled) return null;

        if (FindSprintUnrecordedTimeProjectCode(orderedSources, date) is { } fromSprint) return fromSprint;

        return ResolveProjectCode(_unrecordedTimeProjectCodeId) is { IsActive: true } fallback
            ? fallback
            : null;
    }

    /// <summary>
    /// スプリント一覧に出す加算先の表示を作り直す。
    /// <see cref="SprintInfo"/> は変更通知を持たないので、一覧の差し替えとセットで呼ぶ。
    /// </summary>
    private void RefreshSprintProjectCodeLabels(IReadOnlyList<SprintInfo> sprints)
    {
        var sources = SprintHelper.GetUnrecordedTimeProjectCodeSources(sprints);

        foreach (var sprint in sprints)
        {
            var effective = FindSprintUnrecordedTimeProjectCode(sources, sprint.StartDate);
            if (effective == null)
            {
                sprint.UnrecordedTimeProjectCodeLabel = "全体設定に従う";
                continue;
            }

            // 自分の指定がそのまま使われるときだけ「引き継ぎ」を付けない。
            // 指定したコードを無効化した場合は前から引き継ぐので、表示もそちらに合わせる
            var isOwn = sprint.UnrecordedTimeProjectCodeId == effective.Id;

            sprint.UnrecordedTimeProjectCodeLabel = isOwn
                ? effective.DisplayName
                : $"引き継ぎ：{effective.DisplayName}";
        }
    }

    /// <summary>削除されたコードを指していたスプリントの指定を解除する。</summary>
    private void ClearSprintProjectCodeReferences(string projectCodeId)
    {
        var affected = ManualSprints.Where(s => s.UnrecordedTimeProjectCodeId == projectCodeId).ToList();
        if (affected.Count == 0) return;

        foreach (var sprint in affected)
        {
            sprint.UnrecordedTimeProjectCodeId = null;
        }

        ManualSprints = [.. ManualSprints];
    }

    private void InitializeProjectCodeCommands()
    {
        ProjectCodes.CollectionChanged += (_, _) =>
        {
            NotifyProjectCodeChoicesChanged();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        };

        AddProjectCodeCommand = new RelayCommand(_ =>
        {
            var usedCodes = ProjectCodes.Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var number = 1;
            while (usedCodes.Contains($"PROJECT-{number}")) number++;

            var projectCode = new ProjectCodeInfo
            {
                Code = $"PROJECT-{number}",
                Name = "新しいプロジェクト"
            };

            AttachProjectCode(projectCode);
            ProjectCodes.Add(projectCode);
            SaveSettings();
        });

        ToggleProjectCodeActiveCommand = new RelayCommand(
            param =>
            {
                if (param is not ProjectCodeInfo projectCode) return;

                projectCode.IsActive = !projectCode.IsActive;
            },
            param => param is ProjectCodeInfo projectCode &&
                     (!projectCode.IsActive || ProjectCodes.Count(p => p.IsActive) > 1));

        DeleteProjectCodeCommand = new RelayCommand(
            param =>
            {
                if (param is not ProjectCodeInfo projectCode ||
                    ProjectCodes.Count <= 1 ||
                    (projectCode.IsActive && ProjectCodes.Count(p => p.IsActive) <= 1)) return;

                if (ScheduleItems.Any(item => item.ProjectCodeId == projectCode.Id) ||
                    Routines.Any(routine => routine.ProjectCodeId == projectCode.Id))
                {
                    _dialogService.ShowMessage(
                        $"プロジェクトコード「{projectCode.DisplayName}」は予定・実績・定期予定で使用中のため削除できません。\n" +
                        "対象アイテムや定期予定を別のプロジェクトコードへ変更してから削除してください。",
                        "プロジェクトコードの削除");
                    return;
                }

                if (!_dialogService.ShowConfirmationDialog(
                    $"プロジェクトコード「{projectCode.DisplayName}」を削除しますか？",
                    "削除確認")) return;

                projectCode.PropertyChanged -= OnProjectCodePropertyChanged;
                ProjectCodes.Remove(projectCode);

                if (_defaultProjectCodeId == projectCode.Id)
                {
                    _defaultProjectCodeId = ProjectCodes.FirstOrDefault(p => p.IsActive)?.Id;
                }

                ClearSprintProjectCodeReferences(projectCode.Id);
                EnsureUnrecordedTimeProjectCode();

                NotifyDefaultProjectCodeChanged();
                SaveSettings();
                UpdateStats();
            },
            param => param is ProjectCodeInfo projectCode &&
                     ProjectCodes.Count > 1 &&
                     (!projectCode.IsActive || ProjectCodes.Count(p => p.IsActive) > 1));
    }

    private void LoadProjectCodes(List<ProjectCodeInfo>? loaded)
    {
        foreach (var old in ProjectCodes)
        {
            old.PropertyChanged -= OnProjectCodePropertyChanged;
        }

        ProjectCodes.Clear();

        var source = loaded is { Count: > 0 } ? loaded : ProjectCodeInfo.CreateDefaults();
        if (source.All(p => !p.IsActive)) source[0].IsActive = true;
        foreach (var projectCode in source)
        {
            if (string.IsNullOrEmpty(projectCode.Id)) projectCode.Id = Guid.NewGuid().ToString("N");
            AttachProjectCode(projectCode);
            ProjectCodes.Add(projectCode);
        }
    }

    private void LoadDefaultProjectCodeId(string? id)
    {
        _defaultProjectCodeId = ResolveProjectCode(id) is { IsActive: true } selected
            ? selected.Id
            : ProjectCodes.FirstOrDefault(p => p.IsActive)?.Id;
        NotifyProjectCodeChoicesChanged();
    }

    private void LoadUnrecordedTimeProjectAggregation(bool isEnabled, string? projectCodeId)
    {
        _unrecordedTimeProjectCodeId = ResolveProjectCode(projectCodeId) is { IsActive: true } selected
            ? selected.Id
            : DefaultProjectCode?.Id;
        _isUnrecordedTimeProjectAggregationEnabled = isEnabled && _unrecordedTimeProjectCodeId != null;
        OnPropertyChanged(nameof(IsUnrecordedTimeProjectAggregationEnabled));
        OnPropertyChanged(nameof(SelectedUnrecordedTimeProjectCode));
    }

    private void AttachProjectCode(ProjectCodeInfo projectCode)
    {
        projectCode.PropertyChanged -= OnProjectCodePropertyChanged;
        projectCode.PropertyChanged += OnProjectCodePropertyChanged;
    }

    private void OnProjectCodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectCodeInfo.DisplayName)) return;

        if (e.PropertyName == nameof(ProjectCodeInfo.IsActive))
        {
            if (sender is ProjectCodeInfo { IsActive: false } projectCode &&
                _defaultProjectCodeId == projectCode.Id)
            {
                _defaultProjectCodeId = ProjectCodes.FirstOrDefault(p => p.IsActive)?.Id;
            }
            EnsureUnrecordedTimeProjectCode();
            NotifyProjectCodeChoicesChanged();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        if (_isLoadingData) return;

        SaveSettings();
        UpdateStats();
        NotifyProjectCodeChoicesChanged();
    }

    public ProjectCodeInfo? ResolveProjectCode(string? id) =>
        string.IsNullOrEmpty(id) ? null : ProjectCodes.FirstOrDefault(p => p.Id == id);

    /// <summary>新規選択肢に、編集中の無効コードだけを加えて過去の紐づけを維持する。</summary>
    private IReadOnlyList<ProjectCodeInfo> GetSelectableProjectCodes(string? currentProjectCodeId = null) =>
        [.. ProjectCodes.Where(p => p.IsActive || p.Id == currentProjectCodeId)];

    private void NotifyDefaultProjectCodeChanged()
    {
        OnPropertyChanged(nameof(DefaultProjectCode));
        OnPropertyChanged(nameof(SelectedDefaultProjectCode));
    }

    private void NotifyProjectCodeChoicesChanged()
    {
        OnPropertyChanged(nameof(ActiveProjectCodes));
        OnPropertyChanged(nameof(SprintProjectCodeChoices));
        OnPropertyChanged(nameof(SelectedUnrecordedTimeProjectCode));
        NotifyDefaultProjectCodeChanged();

        // コード名の変更・無効化はスプリント一覧の表示にも効く
        RefreshSprintProjectCodeLabels(ManualSprints);
        OnPropertyChanged(nameof(ManualSprints));
    }

    private void EnsureUnrecordedTimeProjectCode()
    {
        if (ResolveProjectCode(_unrecordedTimeProjectCodeId) is { IsActive: true }) return;

        _unrecordedTimeProjectCodeId = DefaultProjectCode?.Id;
        OnPropertyChanged(nameof(SelectedUnrecordedTimeProjectCode));
    }
}
