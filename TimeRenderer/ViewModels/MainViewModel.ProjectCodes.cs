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

    public ICommand AddProjectCodeCommand { get; private set; } = null!;
    public ICommand DeleteProjectCodeCommand { get; private set; } = null!;

    private string? _defaultProjectCodeId;

    /// <summary>新しい予定・実績と、通常の記録開始で使用する既定のプロジェクトコード。</summary>
    public ProjectCodeInfo? DefaultProjectCode =>
        ResolveProjectCode(_defaultProjectCodeId) ?? ProjectCodes.FirstOrDefault();

    /// <summary>設定パネルの既定値コンボボックス用。</summary>
    public ProjectCodeInfo? SelectedDefaultProjectCode
    {
        get => DefaultProjectCode;
        set
        {
            if (value == null || value.Id == _defaultProjectCodeId) return;

            _defaultProjectCodeId = value.Id;
            NotifyDefaultProjectCodeChanged();
            SaveSettings();
        }
    }

    private void InitializeProjectCodeCommands()
    {
        ProjectCodes.CollectionChanged += (_, _) => NotifyDefaultProjectCodeChanged();

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

        DeleteProjectCodeCommand = new RelayCommand(
            param =>
            {
                if (param is not ProjectCodeInfo projectCode || ProjectCodes.Count <= 1) return;

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
                    _defaultProjectCodeId = ProjectCodes.FirstOrDefault()?.Id;
                }

                NotifyDefaultProjectCodeChanged();
                SaveSettings();
                UpdateStats();
            },
            param => param is ProjectCodeInfo && ProjectCodes.Count > 1);
    }

    private void LoadProjectCodes(List<ProjectCodeInfo>? loaded)
    {
        foreach (var old in ProjectCodes)
        {
            old.PropertyChanged -= OnProjectCodePropertyChanged;
        }

        ProjectCodes.Clear();

        var source = loaded is { Count: > 0 } ? loaded : ProjectCodeInfo.CreateDefaults();
        foreach (var projectCode in source)
        {
            if (string.IsNullOrEmpty(projectCode.Id)) projectCode.Id = Guid.NewGuid().ToString("N");
            AttachProjectCode(projectCode);
            ProjectCodes.Add(projectCode);
        }
    }

    private void LoadDefaultProjectCodeId(string? id)
    {
        _defaultProjectCodeId = ResolveProjectCode(id)?.Id ?? ProjectCodes.FirstOrDefault()?.Id;
        NotifyDefaultProjectCodeChanged();
    }

    private void AttachProjectCode(ProjectCodeInfo projectCode)
    {
        projectCode.PropertyChanged -= OnProjectCodePropertyChanged;
        projectCode.PropertyChanged += OnProjectCodePropertyChanged;
    }

    private void OnProjectCodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isLoadingData || e.PropertyName == nameof(ProjectCodeInfo.DisplayName)) return;

        SaveSettings();
        UpdateStats();
        NotifyDefaultProjectCodeChanged();
    }

    public ProjectCodeInfo? ResolveProjectCode(string? id) =>
        string.IsNullOrEmpty(id) ? null : ProjectCodes.FirstOrDefault(p => p.Id == id);

    private void NotifyDefaultProjectCodeChanged()
    {
        OnPropertyChanged(nameof(DefaultProjectCode));
        OnPropertyChanged(nameof(SelectedDefaultProjectCode));
    }
}
