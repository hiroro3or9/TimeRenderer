using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>ToDoの保存・読込、完了済みアーカイブ、見積もり実績キャッシュ。</summary>
public partial class MainViewModel
{
    // チェックの付け外しや即時追加が続く間ずっと書き込むのを避けるため、
    // 予定データと同じくデバウンスしてまとめて1回にする。
    private DispatcherTimer? _todoSaveTimer;
    private bool _hasPendingTodoSave;
    private bool _isLoadingTodos;
    private static readonly TimeSpan TodoSaveDebounceInterval = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan TodoSaveRetryInterval = TimeSpan.FromSeconds(30);

    private bool _isTodoLoadFailed;
    /// <summary>ToDoを読み込めず、破損データを守るため保存を停止しているか。</summary>
    public bool IsTodoLoadFailed
    {
        get => _isTodoLoadFailed;
        private set => SetProperty(ref _isTodoLoadFailed, value);
    }

    // 完了済みを現役の一覧に残し続けると todos.json が延々と膨らむ。
    // 保持日数を過ぎたものは別ファイルへ移し、見積もりの実績集計にだけ使う。
    public static IReadOnlyList<int> TodoArchiveRetentionOptions { get; } = [30, 60, 90, 180, 365];

    private int _todoArchiveRetentionDays = 90;
    /// <summary>完了済みを一覧に残す日数。この日数を過ぎたものはアーカイブへ移す</summary>
    public int TodoArchiveRetentionDays
    {
        get => _todoArchiveRetentionDays;
        set
        {
            if (SetProperty(ref _todoArchiveRetentionDays, Math.Clamp(value, 7, 3650))) SaveSettings();
        }
    }

    /// <summary>アーカイブ済みの ToDo。表示や編集はせず、実績の集計にだけ使う</summary>
    private List<TodoItem> _archivedTodos = [];

    private TodoEstimateStats? _estimateStats;

    /// <summary>
    /// 見積もりに対する実績の傾向（現役の完了済み＋アーカイブが材料）。
    /// 編集ダイアログを開くときにしか使わないので、必要になってから作る。
    /// </summary>
    private TodoEstimateStats EstimateStats =>
        _estimateStats ??= TodoEstimateStats.Build(Todos.Concat(_archivedTodos));

    /// <summary>完了や記録時間が動いたら、次に開くときに作り直す</summary>
    private void InvalidateEstimateStats() => _estimateStats = null;

    /// <summary>
    /// 保持日数を過ぎた完了済みをアーカイブへ移す。起動時に1回だけ行う。
    /// 移す対象が無ければファイルには触らない。
    /// </summary>
    private void ArchiveOldTodos()
    {
        if (IsTodoLoadFailed) return;

        var targets = TodoArchiveHelper.GetArchiveTargets(
            Todos, LocalToday, TodoArchiveRetentionDays);

        if (targets.Count == 0) return;

        _isLoadingTodos = true;
        try
        {
            foreach (var todo in targets)
            {
                todo.PropertyChanged -= OnTodoPropertyChanged;
                Todos.Remove(todo);
                _archivedTodos.Add(todo);
            }
        }
        finally
        {
            _isLoadingTodos = false;
        }

        Services.FilePersistenceService.SaveTodoArchive(_archivedTodos);
        Services.FilePersistenceService.SaveTodos(Todos);
    }

    private void ScheduleTodoSave()
    {
        if (!_isInitialized || _isLoadingTodos || IsTodoLoadFailed) return;

        _hasPendingTodoSave = true;
        var timer = EnsureTodoSaveTimer();

        timer.Stop();
        timer.Interval = TodoSaveDebounceInterval;
        timer.Start();
    }

    private DispatcherTimer EnsureTodoSaveTimer()
    {
        if (_todoSaveTimer != null) return _todoSaveTimer;

        _todoSaveTimer = new DispatcherTimer { Interval = TodoSaveDebounceInterval };
        _todoSaveTimer.Tick += (_, _) => FlushTodoSave();
        return _todoSaveTimer;
    }

    /// <summary>保留中の ToDo 保存を即時実行する（アプリ終了時などに呼ぶ）</summary>
    public void FlushTodoSave()
    {
        _todoSaveTimer?.Stop();
        if (!_hasPendingTodoSave || IsTodoLoadFailed) return;

        if (Services.FilePersistenceService.SaveTodos(Todos))
        {
            _hasPendingTodoSave = false;
            return;
        }

        var timer = EnsureTodoSaveTimer();
        timer.Interval = TodoSaveRetryInterval;
        timer.Start();
    }

    private void LoadTodos()
    {
        var result = Services.FilePersistenceService.LoadTodos();

        _isLoadingTodos = true;
        try
        {
            foreach (var old in Todos)
            {
                old.PropertyChanged -= OnTodoPropertyChanged;
            }
            // 読み込みでインスタンスが入れ替わるため、通知の状態も作り直す
            PendingTodoReminders.Clear();
            _remindedTodoKeys.Clear();
            ClearMissedTodoReminders();
            Todos.Clear();
            foreach (var todo in result.Items)
            {
                // 変更の購読は OnTodosChanged がまとめて行う
                Todos.Add(todo);
            }
        }
        finally
        {
            _isLoadingTodos = false;
        }

        ApplyTodoLoadStatus(result);

        _archivedTodos = Services.FilePersistenceService.LoadTodoArchive();
        ArchiveOldTodos();
        InvalidateEstimateStats();

        _lastTodoDueRefreshDate = LocalToday;
        RebuildVisibleTodos();
        NotifyTodoCountsChanged();

        // 起動直後は他に再計算のきっかけが無いため、ここで終日行のチップを作る
        RecalculateLayout();
    }

    private void ApplyTodoLoadStatus(Services.FilePersistenceService.TodoLoadResult result)
    {
        switch (result.Status)
        {
            case Services.LoadStatus.RecoveredFromBackup:
                IsTodoLoadFailed = false;
                AppendDataNotice(result.Message);
                break;

            case Services.LoadStatus.Failed:
                IsTodoLoadFailed = true;
                AppendDataNotice(
                    (result.Message ?? "ToDoデータを読み込めませんでした。") +
                    "\nデータを保護するため、このセッションではToDoの保存を停止しています。" +
                    "\nデータフォルダのバックアップ（.bak）を確認してください。");
                break;

            default:
                IsTodoLoadFailed = false;
                break;
        }
    }

    private void AppendDataNotice(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        DataNotice = string.IsNullOrWhiteSpace(DataNotice)
            ? message
            : DataNotice + "\n\n" + message;
    }
}
