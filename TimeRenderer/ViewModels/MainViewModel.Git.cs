using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

using TimeRenderer.Helpers;
using TimeRenderer.Models;
using TimeRenderer.Services;

namespace TimeRenderer.ViewModels;

/// <summary>
/// コミット履歴を作業の手がかりに使うための設定と、その読み出し口。
///
/// 使用アプリの記録は「Visual Studio を42分」までは言えるが、何をしていたかは言わない。
/// コミットメッセージはそこを埋める材料で、開発の記録に関してはアプリ名より圧倒的に強い。
///
/// 使いどころは2つ:
/// - 未記録の帯を埋めるとき（<see cref="MainViewModel.FillGapFromAppUsage"/>）
/// - 退勤時のふりかえりを書くとき（<see cref="MainViewModel.ShowWorkEndReview"/>）
///
/// どちらも<b>ユーザーが操作した直後</b>にしか読まない。
/// git の起動は数百ミリ秒かかることがあり、定期的に走らせると
/// 何もしていないのに画面が引っかかる原因になる。
///
/// 読み出しは常にこの入口（<see cref="GetCommitsBetween"/>）を通す。
/// 機能のオン／オフとリポジトリの絞り込みを1か所にまとめておかないと、
/// 「設定で切ったのにどこかで git が動いている」が起きる。
/// </summary>
public partial class MainViewModel
{
    private readonly GitCommitReader _gitCommitReader = new();

    /// <summary>登録済みのリポジトリ（無効なものも残す）</summary>
    public ObservableCollection<GitRepositoryInfo> GitRepositories { get; } = [];

    /// <summary>実際に読みに行くリポジトリ</summary>
    public IReadOnlyList<GitRepositoryInfo> ActiveGitRepositories =>
        [.. GitRepositories.Where(r => r.IsEnabled && r.Path.Length > 0)];

    /// <summary>1件でも登録されているか（説明文の出し分けに使う）</summary>
    public bool HasGitRepositories => GitRepositories.Count > 0;

    public ICommand AddGitRepositoryCommand { get; private set; } = null!;
    public ICommand DeleteGitRepositoryCommand { get; private set; } = null!;

    private bool _isGitCommitLookupEnabled = true;

    /// <summary>
    /// コミット履歴を手がかりに使うか。
    /// 既定は有効だが、リポジトリを1件も登録していなければ何も起きない。
    /// </summary>
    public bool IsGitCommitLookupEnabled
    {
        get => _isGitCommitLookupEnabled;
        set
        {
            if (SetProperty(ref _isGitCommitLookupEnabled, value))
            {
                SaveSettings();
            }
        }
    }

    /// <summary>
    /// 設定画面に出す状態の一文。
    /// リポジトリを登録した後で初めて git の有無を確かめる
    /// （登録していない人に「git がありません」と言っても意味が無い）。
    /// </summary>
    public string GitStatusText
    {
        get
        {
            if (GitRepositories.Count == 0) return "リポジトリはまだ登録されていません。";
            if (!_gitCommitReader.IsGitAvailable)
            {
                return "git コマンドが見つかりません。git を PATH の通った場所へ入れると使えるようになります。";
            }

            var active = ActiveGitRepositories.Count;
            return active == GitRepositories.Count
                ? $"{active} 件のリポジトリを見に行きます。"
                : $"{GitRepositories.Count} 件中 {active} 件を見に行きます。";
        }
    }

    private void InitializeGitCommands()
    {
        GitRepositories.CollectionChanged += (_, _) => NotifyGitRepositoriesChanged();

        AddGitRepositoryCommand = new RelayCommand(_ =>
        {
            var path = _dialogService.ShowFolderPicker("コミット履歴を見に行くリポジトリのフォルダーを選びます");
            if (string.IsNullOrWhiteSpace(path)) return;

            if (GitRepositories.Any(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                _dialogService.ShowMessage("このリポジトリは既に登録されています。", "リポジトリの追加");
                return;
            }

            // .git が無いフォルダーでも止めはしない。サブフォルダーを選んでしまった、
            // これから clone する、といった場合に登録し直させるほうが手間になる
            if (!GitCommitReader.LooksLikeRepository(path)
                && !_dialogService.ShowConfirmationDialog(
                    $"選んだフォルダーに .git が見つかりません。\n{path}\n\nこのまま登録しますか？",
                    "リポジトリの追加"))
            {
                return;
            }

            var repository = new GitRepositoryInfo
            {
                Path = path,
                ProjectCodeId = DefaultProjectCode?.Id,
            };

            AttachGitRepository(repository);
            GitRepositories.Add(repository);
            SaveSettings();
        });

        DeleteGitRepositoryCommand = new RelayCommand(param =>
        {
            if (param is not GitRepositoryInfo repository) return;

            // 消しても記録は残る（コミットは元から保存していない）ので、確認は出さない
            repository.PropertyChanged -= OnGitRepositoryPropertyChanged;
            GitRepositories.Remove(repository);
            SaveSettings();
        });
    }

    private void LoadGitRepositories(List<GitRepositoryInfo>? loaded)
    {
        foreach (var old in GitRepositories)
        {
            old.PropertyChanged -= OnGitRepositoryPropertyChanged;
        }

        GitRepositories.Clear();

        if (loaded == null) return;

        foreach (var repository in loaded)
        {
            if (string.IsNullOrWhiteSpace(repository.Path)) continue;
            if (string.IsNullOrEmpty(repository.Id)) repository.Id = Guid.NewGuid().ToString("N");

            AttachGitRepository(repository);
            GitRepositories.Add(repository);
        }
    }

    private void AttachGitRepository(GitRepositoryInfo repository)
    {
        repository.PropertyChanged -= OnGitRepositoryPropertyChanged;
        repository.PropertyChanged += OnGitRepositoryPropertyChanged;
    }

    private void OnGitRepositoryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GitRepositoryInfo.DisplayName) or nameof(GitRepositoryInfo.FolderName))
        {
            return;
        }

        if (e.PropertyName == nameof(GitRepositoryInfo.IsEnabled))
        {
            NotifyGitRepositoriesChanged();
        }

        if (_isLoadingData) return;

        SaveSettings();
    }

    private void NotifyGitRepositoriesChanged()
    {
        OnPropertyChanged(nameof(ActiveGitRepositories));
        OnPropertyChanged(nameof(HasGitRepositories));
        OnPropertyChanged(nameof(GitStatusText));
    }

    /// <summary>
    /// 指定した期間のコミットを新しい順に返す。
    /// 機能が無効・リポジトリが未登録・git が無い、のいずれでも空を返す。
    /// </summary>
    public IReadOnlyList<GitCommit> GetCommitsBetween(DateTime from, DateTime to)
    {
        if (!IsGitCommitLookupEnabled) return [];

        var repositories = ActiveGitRepositories;
        if (repositories.Count == 0) return [];

        return _gitCommitReader.Read(repositories, from, to);
    }

    /// <summary>
    /// コミット群が1つのリポジトリだけから来ていて、そこにプロジェクトコードが
    /// 紐づいている場合に、それを返す。
    ///
    /// 複数のリポジトリにまたがっている時間帯では推測しない。
    /// どちらの案件か決められないのに片方を既定値に入れると、
    /// そのまま確定されて誤った集計が残る。
    /// </summary>
    private ProjectCodeInfo? GuessProjectCodeFromCommits(IReadOnlyList<GitCommit> commits)
    {
        if (commits.Count == 0) return null;

        var repositoryId = commits[0].RepositoryId;
        foreach (var commit in commits)
        {
            if (commit.RepositoryId != repositoryId) return null;
        }

        var repository = GitRepositories.FirstOrDefault(r => r.Id == repositoryId);
        if (repository?.ProjectCodeId is not { Length: > 0 } projectCodeId) return null;

        return ResolveProjectCode(projectCodeId) is { IsActive: true } projectCode ? projectCode : null;
    }
}
