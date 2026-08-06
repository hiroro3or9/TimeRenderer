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

    public ICommand AddProjectCodeCommand { get; private set; } = null!;
    public ICommand DeleteProjectCodeCommand { get; private set; } = null!;
    public ICommand ToggleProjectCodeActiveCommand { get; private set; } = null!;

    private string? _defaultProjectCodeId;

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

                if (ScheduleItems.Any(item => item.ProjectCodeId == projectCode.Id))
                {
                    _dialogService.ShowMessage(
                        $"プロジェクトコード「{projectCode.DisplayName}」は予定または実績で使用中のため削除できません。\n" +
                        "対象アイテムを別のプロジェクトコードへ変更してから削除してください。",
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
        NotifyDefaultProjectCodeChanged();
    }
}
