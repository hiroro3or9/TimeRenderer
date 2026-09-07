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
    /// 「（未設定）」を先頭に足した選択肢。
    /// コードを付けない選び方を、既定値・リポジトリの割り当てなど画面側からも取れるようにする。
    /// </summary>
    public IReadOnlyList<ProjectCodeInfo> SelectableProjectCodes =>
        [ProjectCodeInfo.Unassigned, .. ActiveProjectCodes];

    /// <summary>
    /// 期間割り当ての行で選べるコード。先頭の「（未設定）」はその期間を加算しない指定になる。
    /// 既に割り当てで使われている無効なコードも残す（選択肢に無いと
    /// ComboBox が選択を外し、触っただけで指定が消えてしまうため）。
    /// </summary>
    public IReadOnlyList<ProjectCodeInfo> AssignmentProjectCodeChoices =>
    [
        ProjectCodeInfo.Unassigned,
        .. ProjectCodes.Where(p =>
            p.IsActive ||
            UnrecordedTimeAssignments.Any(a => a.ProjectCodeId == p.Id))
    ];

    public ICommand AddProjectCodeCommand { get; private set; } = null!;
    public ICommand DeleteProjectCodeCommand { get; private set; } = null!;
    public ICommand ToggleProjectCodeActiveCommand { get; private set; } = null!;

    private string? _defaultProjectCodeId;
    private string? _unrecordedTimeProjectCodeId;
    private bool _isUnrecordedTimeProjectAggregationEnabled;

    /// <summary>
    /// 設定から読んだ割り当ての一時置き場。
    /// プロジェクトコードと手動スプリントが揃うまで反映できないため、
    /// binding では受け取るだけにして FinalizeAppSettingsApplication で取り込む。
    /// </summary>
    private List<UnrecordedTimeProjectAssignment>? _loadedUnrecordedTimeAssignments;

    /// <summary>先頭の有効なコード。既定値を決められないときの受け皿。</summary>
    private ProjectCodeInfo? FirstActiveProjectCode => ProjectCodes.FirstOrDefault(p => p.IsActive);

    /// <summary>
    /// 既定のプロジェクトコードとして「（未設定）」を選んでいるか。
    /// 保存値が空文字なら明示的な未設定、null なら未指定（先頭の有効コードへ倒す）。
    /// </summary>
    private bool IsDefaultProjectCodeUnassigned =>
        _defaultProjectCodeId == ProjectCodeInfo.UnassignedId;

    /// <summary>
    /// 新しい予定・実績と、通常の記録開始で使用する既定のプロジェクトコード。
    /// 「（未設定）」を選んでいるときは null を返し、ダイアログを開かない作成経路も
    /// コードを付けずに始める。
    /// </summary>
    public ProjectCodeInfo? DefaultProjectCode =>
        IsDefaultProjectCodeUnassigned
            ? null
            : ResolveProjectCode(_defaultProjectCodeId) is { IsActive: true } selected
                ? selected
                : FirstActiveProjectCode;

    /// <summary>設定パネルの既定値コンボボックス用。</summary>
    public ProjectCodeInfo? SelectedDefaultProjectCode
    {
        get => IsDefaultProjectCodeUnassigned ? ProjectCodeInfo.Unassigned : DefaultProjectCode;
        set
        {
            // 選択肢の入れ替えで一時的に null が入ることがあるため、選択解除は無視する
            if (value == null) return;

            var id = value.Id.Length == 0
                ? ProjectCodeInfo.UnassignedId
                : value.IsActive ? value.Id : null;
            if (id == null || id == _defaultProjectCodeId) return;

            _defaultProjectCodeId = id;
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
            : DefaultProjectCode ?? FirstActiveProjectCode;
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
    /// 指定日の未記録時間を加算するコード。機能が無効なら null。
    /// 割り当ては開始日の昇順に整えてあるので、その日以前で最後の行を採る。
    /// どの行にも掛からない（最初の行より前の）日は既定コードを使う。
    /// 行のコードを「（未設定）」にした期間は、加算先が無い＝加算しない。
    /// </summary>
    private ProjectCodeInfo? ResolveUnrecordedTimeProjectCode(DateTime date)
    {
        if (!IsUnrecordedTimeProjectAggregationEnabled) return null;

        if (UnrecordedTimeAssignmentHelper.Resolve(UnrecordedTimeAssignments, date) is { } assignment)
        {
            // 割り当てで明示された期間は、そのコードが無効化されていてもそのまま使う
            // （過去の集計先が勝手に別のコードへ移らないようにする）
            return ResolveProjectCode(assignment.ProjectCodeId);
        }

        return ResolveProjectCode(_unrecordedTimeProjectCodeId) is { IsActive: true } fallback
            ? fallback
            : null;
    }

    /// <summary>
    /// 期間割り当ての一覧（開始日の昇順に保つ）。
    /// 行を足す・日付を変える・コードを変えるたびに並べ替えて表示を作り直す。
    /// </summary>
    public ObservableCollection<UnrecordedTimeProjectAssignment> UnrecordedTimeAssignments { get; } = [];

    public ICommand AddUnrecordedTimeAssignmentCommand { get; private set; } = null!;
    public ICommand DeleteUnrecordedTimeAssignmentCommand { get; private set; } = null!;

    private void InitializeUnrecordedTimeAssignmentCommands()
    {
        AddUnrecordedTimeAssignmentCommand = new RelayCommand(_ =>
        {
            // 「今日から案件が変わる」が一番多いので今日を既定にし、
            // 未来の行が既にあるときだけその翌日へずらして重複を避ける
            var last = UnrecordedTimeAssignments.LastOrDefault();
            var start = last == null || last.StartDate < DateTime.Today
                ? DateTime.Today
                : last.StartDate.AddDays(1);

            var assignment = new UnrecordedTimeProjectAssignment
            {
                // 直前の行があればその指定（未設定も含めて）を引き継ぐ
                ProjectCodeId = last != null ? last.ProjectCodeId : DefaultProjectCode?.Id,
                StartDate = start
            };

            AttachUnrecordedTimeAssignment(assignment);
            UnrecordedTimeAssignments.Add(assignment);
            OnUnrecordedTimeAssignmentsChanged();
        });

        DeleteUnrecordedTimeAssignmentCommand = new RelayCommand(
            param =>
            {
                if (param is not UnrecordedTimeProjectAssignment assignment) return;

                assignment.PropertyChanged -= OnUnrecordedTimeAssignmentPropertyChanged;
                UnrecordedTimeAssignments.Remove(assignment);
                OnUnrecordedTimeAssignmentsChanged();
            },
            param => param is UnrecordedTimeProjectAssignment);
    }

    private void LoadUnrecordedTimeAssignments(
        List<UnrecordedTimeProjectAssignment>? loaded, IReadOnlyList<SprintInfo> manualSprints)
    {
        foreach (var old in UnrecordedTimeAssignments)
        {
            old.PropertyChanged -= OnUnrecordedTimeAssignmentPropertyChanged;
        }

        UnrecordedTimeAssignments.Clear();

        // null は旧形式。スプリント側に入れていた指定をここで一度だけ引き取る
        var source = loaded ?? MigrateSprintUnrecordedTimeProjectCodes(manualSprints);

        foreach (var assignment in UnrecordedTimeAssignmentHelper.Order(source))
        {
            AttachUnrecordedTimeAssignment(assignment);
            UnrecordedTimeAssignments.Add(assignment);
        }

        RefreshUnrecordedTimeAssignmentRanges();
        OnPropertyChanged(nameof(AssignmentProjectCodeChoices));
    }

    /// <summary>旧形式：スプリントに持たせていた加算先を割り当て行へ移す。</summary>
    private static List<UnrecordedTimeProjectAssignment> MigrateSprintUnrecordedTimeProjectCodes(
        IReadOnlyList<SprintInfo> manualSprints) =>
    [
        .. manualSprints
            .Where(s => s.IsManual && !string.IsNullOrEmpty(s.UnrecordedTimeProjectCodeId))
            .OrderBy(s => s.StartDate.Date)
            .Select(s => new UnrecordedTimeProjectAssignment
            {
                StartDate = s.StartDate.Date,
                ProjectCodeId = s.UnrecordedTimeProjectCodeId
            })
    ];

    private void AttachUnrecordedTimeAssignment(UnrecordedTimeProjectAssignment assignment)
    {
        assignment.PropertyChanged -= OnUnrecordedTimeAssignmentPropertyChanged;
        assignment.PropertyChanged += OnUnrecordedTimeAssignmentPropertyChanged;
    }

    private void OnUnrecordedTimeAssignmentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // RangeText はこちらが書き込む表示用なので、跳ね返って無限に呼ばれないよう弾く
        if (e.PropertyName == nameof(UnrecordedTimeProjectAssignment.RangeText)) return;

        OnUnrecordedTimeAssignmentsChanged();
    }

    private void OnUnrecordedTimeAssignmentsChanged()
    {
        if (_isLoadingData) return;

        SortUnrecordedTimeAssignments();
        RefreshUnrecordedTimeAssignmentRanges();
        OnPropertyChanged(nameof(AssignmentProjectCodeChoices));
        SaveSettings();
        UpdateStats();
    }

    /// <summary>開始日の昇順へ整える（解決も表示も並び順に依存するため）。</summary>
    private void SortUnrecordedTimeAssignments()
    {
        var sorted = UnrecordedTimeAssignmentHelper.Order(UnrecordedTimeAssignments);

        for (var i = 0; i < sorted.Count; i++)
        {
            var current = UnrecordedTimeAssignments.IndexOf(sorted[i]);
            if (current != i) UnrecordedTimeAssignments.Move(current, i);
        }
    }

    /// <summary>終了日は次の行の開始日から導出して、期間の文字列を作り直す。</summary>
    private void RefreshUnrecordedTimeAssignmentRanges()
    {
        for (var i = 0; i < UnrecordedTimeAssignments.Count; i++)
        {
            UnrecordedTimeAssignments[i].RangeText =
                UnrecordedTimeAssignmentHelper.BuildRangeText(UnrecordedTimeAssignments, i);
        }
    }

    /// <summary>削除されたコードを使っていた割り当て行を取り除く。</summary>
    private void ClearAssignmentProjectCodeReferences(string projectCodeId)
    {
        var affected = UnrecordedTimeAssignments.Where(a => a.ProjectCodeId == projectCodeId).ToList();
        if (affected.Count == 0) return;

        foreach (var assignment in affected)
        {
            assignment.PropertyChanged -= OnUnrecordedTimeAssignmentPropertyChanged;
            UnrecordedTimeAssignments.Remove(assignment);
        }

        RefreshUnrecordedTimeAssignmentRanges();
    }

    private void InitializeProjectCodeCommands()
    {
        InitializeUnrecordedTimeAssignmentCommands();

        ProjectCodes.CollectionChanged += (_, _) =>
        {
            NotifyProjectCodeChoicesChanged();
            OnPropertyChanged(nameof(IsDisplayFilterActive));
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
                    _defaultProjectCodeId = FirstActiveProjectCode?.Id;
                }

                ClearAssignmentProjectCodeReferences(projectCode.Id);
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
        // 空文字は「（未設定）」を選んだ状態。null（未指定）だけを先頭の有効コードへ倒す
        _defaultProjectCodeId = id == ProjectCodeInfo.UnassignedId
            ? ProjectCodeInfo.UnassignedId
            : ResolveProjectCode(id) is { IsActive: true } selected
                ? selected.Id
                : FirstActiveProjectCode?.Id;
        NotifyProjectCodeChoicesChanged();
    }

    private void LoadUnrecordedTimeProjectAggregation(bool isEnabled, string? projectCodeId)
    {
        // 加算先は集計の宛先そのものなので、既定が未設定でも具体的なコードを残す
        _unrecordedTimeProjectCodeId = ResolveProjectCode(projectCodeId) is { IsActive: true } selected
            ? selected.Id
            : (DefaultProjectCode ?? FirstActiveProjectCode)?.Id;
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

        // 表示フィルタはセッション内だけの状態なので、設定へ保存しない。
        if (e.PropertyName == nameof(ProjectCodeInfo.IsFilterEnabled))
        {
            if (_isLoadingData) return;

            OnPropertyChanged(nameof(IsDisplayFilterActive));
            RecalculateLayout();
            return;
        }

        if (e.PropertyName == nameof(ProjectCodeInfo.IsActive))
        {
            if (sender is ProjectCodeInfo { IsActive: false } projectCode &&
                _defaultProjectCodeId == projectCode.Id)
            {
                _defaultProjectCodeId = FirstActiveProjectCode?.Id;
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
        OnPropertyChanged(nameof(SelectableProjectCodes));
        OnPropertyChanged(nameof(AssignmentProjectCodeChoices));
        OnPropertyChanged(nameof(SelectedUnrecordedTimeProjectCode));
        NotifyDefaultProjectCodeChanged();
    }

    private void EnsureUnrecordedTimeProjectCode()
    {
        if (ResolveProjectCode(_unrecordedTimeProjectCodeId) is { IsActive: true }) return;

        _unrecordedTimeProjectCodeId = (DefaultProjectCode ?? FirstActiveProjectCode)?.Id;
        OnPropertyChanged(nameof(SelectedUnrecordedTimeProjectCode));
    }
}
