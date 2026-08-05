using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;

using TimeRenderer.Models;
using TimeRenderer.Helpers;

namespace TimeRenderer.ViewModels;

/// <summary>
/// ToDo（やることリスト）の管理。
///
/// 予定アイテムと違い時刻を持たないため、「いつやるか決めていないが忘れたくないこと」を置ける。
/// 期限日を持つものだけが日/週ビューの終日行にチップとして並ぶ（TodoChips）。
/// ToDo から記録を開始でき、停止時にその時間が ToDo へ積算される。
/// </summary>
public partial class MainViewModel
{
    /// <summary>ToDo の全件（完了済みを含む）。表示用の絞り込み・並べ替えは VisibleTodos で行う</summary>
    public ObservableCollection<TodoItem> Todos { get; } = [];

    private IReadOnlyList<TodoItem> _visibleTodos = [];
    /// <summary>パネルに表示する ToDo（絞り込み・並べ替え済み）</summary>
    public IReadOnlyList<TodoItem> VisibleTodos
    {
        get => _visibleTodos;
        private set => SetProperty(ref _visibleTodos, value);
    }

    private IReadOnlyList<TodoChip> _todoChips = [];
    /// <summary>日/週ビューの終日行に並べる、期限付きの未完了 ToDo</summary>
    public IReadOnlyList<TodoChip> TodoChips
    {
        get => _todoChips;
        private set => SetProperty(ref _todoChips, value);
    }

    // ===== パネルの状態 =====

    private bool _isTodoPanelVisible;
    public bool IsTodoPanelVisible
    {
        get => _isTodoPanelVisible;
        set
        {
            if (SetProperty(ref _isTodoPanelVisible, value)) SaveSettings();
        }
    }

    private bool _showCompletedTodos;
    /// <summary>完了済みの ToDo も一覧に出すか</summary>
    public bool ShowCompletedTodos
    {
        get => _showCompletedTodos;
        set
        {
            if (SetProperty(ref _showCompletedTodos, value))
            {
                RebuildVisibleTodos();
                SaveSettings();
            }
        }
    }

    public IReadOnlyList<TodoSortOption> TodoSortOptions { get; } =
    [
        new(TodoSortMode.DueDate, "期限順"),
        new(TodoSortMode.Priority, "優先度順"),
        new(TodoSortMode.Created, "追加順"),
    ];

    private TodoSortMode _todoSortMode = TodoSortMode.DueDate;
    public TodoSortMode CurrentTodoSortMode
    {
        get => _todoSortMode;
        set
        {
            if (SetProperty(ref _todoSortMode, value))
            {
                OnPropertyChanged(nameof(SelectedTodoSortOption));
                RebuildVisibleTodos();
                SaveSettings();
            }
        }
    }

    public TodoSortOption SelectedTodoSortOption
    {
        get => TodoSortOptions.FirstOrDefault(o => o.Mode == CurrentTodoSortMode) ?? TodoSortOptions[0];
        set
        {
            if (value != null) CurrentTodoSortMode = value.Mode;
        }
    }

    private string _newTodoTitle = string.Empty;
    /// <summary>パネル上部の即時追加欄。Enter で1件追加して空に戻る</summary>
    public string NewTodoTitle
    {
        get => _newTodoTitle;
        set => SetProperty(ref _newTodoTitle, value);
    }

    // ===== 件数の表示 =====

    public int TodoActiveCount => Todos.Count(t => !t.IsCompleted);
    public int TodoOverdueCount => Todos.Count(t => t.IsOverdue);
    public int TodoCompletedCount => Todos.Count(t => t.IsCompleted);
    public bool HasCompletedTodos => TodoCompletedCount > 0;

    /// <summary>ヘッダーの要約（例: "未完了 5 件・期限超過 2 件"）</summary>
    public string TodoSummaryText
    {
        get
        {
            var overdue = TodoOverdueCount;
            return overdue > 0
                ? $"未完了 {TodoActiveCount} 件・期限超過 {overdue} 件"
                : $"未完了 {TodoActiveCount} 件";
        }
    }

    /// <summary>トグルボタンのバッジ表示（期限超過があるときだけ点を出す）</summary>
    public bool HasOverdueTodos => TodoOverdueCount > 0;

    private void NotifyTodoCountsChanged()
    {
        OnPropertyChanged(nameof(TodoActiveCount));
        OnPropertyChanged(nameof(TodoOverdueCount));
        OnPropertyChanged(nameof(TodoCompletedCount));
        OnPropertyChanged(nameof(HasCompletedTodos));
        OnPropertyChanged(nameof(TodoSummaryText));
        OnPropertyChanged(nameof(HasOverdueTodos));
    }

    // ===== コマンド =====

    public ICommand ToggleTodoPanelCommand { get; private set; } = null!;
    public ICommand AddQuickTodoCommand { get; private set; } = null!;
    public ICommand AddTodoCommand { get; private set; } = null!;
    public ICommand EditTodoCommand { get; private set; } = null!;
    public ICommand DeleteTodoCommand { get; private set; } = null!;
    public ICommand StartRecordingFromTodoCommand { get; private set; } = null!;
    public ICommand SetTodoDueTodayCommand { get; private set; } = null!;
    public ICommand SetTodoDueTomorrowCommand { get; private set; } = null!;
    public ICommand ClearTodoDueCommand { get; private set; } = null!;
    public ICommand ClearCompletedTodosCommand { get; private set; } = null!;

    private void InitializeTodoCommands()
    {
        Todos.CollectionChanged += OnTodosChanged;

        ToggleTodoPanelCommand = new RelayCommand(_ => IsTodoPanelVisible = !IsTodoPanelVisible);

        AddQuickTodoCommand = new RelayCommand(_ =>
        {
            var title = NewTodoTitle.Trim();
            if (title.Length == 0) return;

            // 即時追加は「思いついた瞬間に置ける」ことが値なので、期限・優先度は後から付ける。
            // カテゴリは予定の新規作成と同じく先頭カテゴリを既定にする
            var category = Categories.FirstOrDefault();
            Todos.Add(new TodoItem
            {
                Title = title,
                ColorCode = category?.ColorCode ?? CategoryInfo.CreateBrush("LightBlue").ToString(),
                CategoryId = category?.Id,
            });

            NewTodoTitle = string.Empty;
        });

        AddTodoCommand = new RelayCommand(_ =>
        {
            var result = _dialogService.ShowTodoEditDialog(null, [.. Categories], GetTitleSuggestions());
            if (result != null) Todos.Add(result);
        });

        EditTodoCommand = new RelayCommand(
            param =>
            {
                if (param is not TodoItem todo) return;

                var edited = _dialogService.ShowTodoEditDialog(todo, [.. Categories], GetTitleSuggestions());
                if (edited == null) return;

                // ダイアログは新しいインスタンスを返すため、既存の実体へ値を移す
                // （リストの並びと、記録中の紐付け先を保つ）
                _isUpdatingTodo = true;
                try
                {
                    todo.Title = edited.Title;
                    todo.Content = edited.Content;
                    todo.DueDate = edited.DueDate;
                    todo.Priority = edited.Priority;
                    todo.CategoryId = edited.CategoryId;
                    todo.ColorCode = edited.ColorCode;
                }
                finally
                {
                    _isUpdatingTodo = false;
                }

                OnTodoChanged();
            },
            param => param is TodoItem
        );

        DeleteTodoCommand = new RelayCommand(
            param =>
            {
                if (param is not TodoItem todo) return;

                if (_dialogService.ShowConfirmationDialog($"ToDo「{todo.Title}」を削除しますか？", "削除確認"))
                {
                    Todos.Remove(todo);
                    if (ReferenceEquals(_recordingTodo, todo)) _recordingTodo = null;
                }
            },
            param => param is TodoItem
        );

        StartRecordingFromTodoCommand = new RelayCommand(
            param =>
            {
                if (param is TodoItem todo) StartRecordingFromTodo(todo);
            },
            param => param is TodoItem
        );

        SetTodoDueTodayCommand = new RelayCommand(
            param => SetTodoDue(param as TodoItem, DateTime.Today),
            param => param is TodoItem);

        SetTodoDueTomorrowCommand = new RelayCommand(
            param => SetTodoDue(param as TodoItem, DateTime.Today.AddDays(1)),
            param => param is TodoItem);

        ClearTodoDueCommand = new RelayCommand(
            param => SetTodoDue(param as TodoItem, null),
            param => param is TodoItem);

        ClearCompletedTodosCommand = new RelayCommand(
            _ =>
            {
                var completed = Todos.Where(t => t.IsCompleted).ToList();
                if (completed.Count == 0) return;

                if (!_dialogService.ShowConfirmationDialog(
                    $"完了済みの ToDo {completed.Count} 件を削除しますか？", "完了済みの削除")) return;

                foreach (var todo in completed)
                {
                    Todos.Remove(todo);
                    if (ReferenceEquals(_recordingTodo, todo)) _recordingTodo = null;
                }
            },
            _ => HasCompletedTodos);
    }

    private void SetTodoDue(TodoItem? todo, DateTime? due)
    {
        if (todo == null) return;
        todo.DueDate = due;
    }

    // ===== 変更の監視 =====

    /// <summary>編集ダイアログの結果を1件へ書き戻す間、都度の再構築・保存を止める</summary>
    private bool _isUpdatingTodo;

    private void OnTodosChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (TodoItem todo in e.NewItems)
            {
                todo.PropertyChanged -= OnTodoPropertyChanged;
                todo.PropertyChanged += OnTodoPropertyChanged;
            }
        }
        if (e.OldItems != null)
        {
            foreach (TodoItem todo in e.OldItems)
            {
                todo.PropertyChanged -= OnTodoPropertyChanged;
            }
        }

        if (_isLoadingTodos) return;
        OnTodoChanged();
    }

    private void OnTodoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 派生の表示用プロパティは元のプロパティ側で既に処理されるため二重に走らせない
        if (e.PropertyName is nameof(TodoItem.Brush)
                           or nameof(TodoItem.CompletedAt) // IsCompleted の変更に伴う派生通知
                           or nameof(TodoItem.DueDisplay)
                           or nameof(TodoItem.RecordedDisplay)
                           or nameof(TodoItem.RecordedDuration)
                           or nameof(TodoItem.HasRecorded)
                           or nameof(TodoItem.HasContent)
                           or nameof(TodoItem.HasDueDate)
                           or nameof(TodoItem.IsOverdue)
                           or nameof(TodoItem.IsDueToday)
                           or nameof(TodoItem.IsHighPriority)
                           or nameof(TodoItem.IsLowPriority)
                           or nameof(TodoItem.ToolTipText)) return;

        if (_isUpdatingTodo || _isLoadingTodos) return;

        OnTodoChanged();
    }

    /// <summary>ToDo が増減・変化したときの共通処理（表示の作り直しと保存）</summary>
    private void OnTodoChanged()
    {
        RebuildVisibleTodos();
        NotifyTodoCountsChanged();
        RecalculateLayout(); // 終日行のチップも作り直す
        ScheduleTodoSave();
    }

    /// <summary>
    /// 表示用の一覧を作り直す。
    /// 完了済みは常に末尾へ送り、その中では選択中の並び順に従う。
    /// </summary>
    private void RebuildVisibleTodos()
    {
        IEnumerable<TodoItem> source = ShowCompletedTodos ? Todos : Todos.Where(t => !t.IsCompleted);

        var ordered = CurrentTodoSortMode switch
        {
            // 期限なしを末尾に送るため、期限の有無を先に見る
            TodoSortMode.DueDate => source
                .OrderBy(t => t.IsCompleted)
                .ThenBy(t => t.DueDate.HasValue ? 0 : 1)
                .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
                .ThenByDescending(t => t.Priority)
                .ThenBy(t => t.CreatedAt),

            TodoSortMode.Priority => source
                .OrderBy(t => t.IsCompleted)
                .ThenByDescending(t => t.Priority)
                .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
                .ThenBy(t => t.CreatedAt),

            _ => source
                .OrderBy(t => t.IsCompleted)
                .ThenBy(t => t.CreatedAt),
        };

        VisibleTodos = [.. ordered];
    }

    /// <summary>
    /// 終日行に並べる ToDo チップを作り直し、必要な段数を返す。
    /// 期限付きの未完了 ToDo だけを対象にし、終日イベントの段の続きに積む。
    /// </summary>
    /// <param name="rangeStart">表示範囲の開始日</param>
    /// <param name="rangeEnd">表示範囲の終了日（この日は含まない）</param>
    /// <param name="allDayRowCounts">日付ごとの終日イベントの段数</param>
    /// <returns>チップまで含めた必要段数（チップが無ければ 0）</returns>
    private int RebuildTodoChips(
        DateTime rangeStart, DateTime rangeEnd, IReadOnlyDictionary<DateTime, int> allDayRowCounts)
    {
        // 終日行を描くのは日/週ビューだけ。他のモードでは作っても誰も見ない
        if (!IsDayOrWeekMode || Todos.Count == 0)
        {
            if (TodoChips.Count > 0) TodoChips = [];
            return 0;
        }

        var targets = Todos
            .Where(t => !t.IsCompleted && t.DueDate is { } due && due >= rangeStart && due < rangeEnd)
            .GroupBy(t => t.DueDate!.Value.Date);

        var chips = new List<TodoChip>();
        int maxRows = 0;

        foreach (var group in targets)
        {
            var baseRow = allDayRowCounts.TryGetValue(group.Key, out var count) ? count : 0;

            var index = 0;
            foreach (var todo in group.OrderByDescending(t => t.Priority).ThenBy(t => t.CreatedAt))
            {
                chips.Add(new TodoChip(todo, group.Key, baseRow + index));
                index++;
            }

            if (baseRow + index > maxRows) maxRows = baseRow + index;
        }

        TodoChips = chips;
        return maxRows;
    }

    // ===== 記録との連動 =====

    /// <summary>
    /// 記録中の ToDo。停止時にその記録時間をこの ToDo へ積算する。
    /// 予定アイテムからの記録（_recordingSourceItem）とは併存しない。
    /// </summary>
    private TodoItem? _recordingTodo;

    /// <summary>
    /// ToDo のタイトル・色で記録を開始する。記録中だった場合は現在の記録を保存してから始める。
    /// 停止時には実績として通常の記録アイテムが作られ、あわせて ToDo に時間が積算される。
    /// </summary>
    private void StartRecordingFromTodo(TodoItem todo)
    {
        if (IsRecording)
        {
            ToggleRecording(); // 現在の記録を停止・保存（この中で _recordingTodo も精算される）
        }

        RecordingTitle = todo.Title;
        _recordingColorCode = todo.ColorCode;
        _recordingCategoryId = todo.CategoryId ?? ResolveCategory(todo.CategoryId, todo.ColorCode)?.Id;
        _recordingSourceItem = null;
        _recordingTodo = todo;

        ClearAwayState(); // 前回の記録で拾った離席を持ち越さない
        IsRecording = true;
        RecordingStartTime = DateTime.Now;
        RecordingDuration = TimeSpan.Zero;
        IsCountdownMode = false;
        CountdownRemaining = null;
    }

    /// <summary>
    /// 記録停止時に、記録していた ToDo へ実績時間を積算する。
    /// 離席を除外した場合は残った区間の合計だけが積まれる。
    /// </summary>
    private void AccumulateTodoRecording(TodoItem? todo, List<(DateTime Start, DateTime End)> segments)
    {
        if (todo == null || segments.Count == 0) return;
        if (!Todos.Contains(todo)) return; // 記録中に削除された

        var ticks = segments.Sum(s => (s.End - s.Start).Ticks);
        if (ticks <= 0) return;

        // 変更通知から OnTodoChanged が走り、一覧の作り直しと保存はそこで行われる
        todo.RecordedTicks += ticks;
    }

    // ===== 日付またぎ =====

    private DateTime _lastTodoDueRefreshDate = DateTime.MinValue;

    /// <summary>
    /// 日付が変わったら、期限に依存する表示（超過・今日・残り日数）を作り直す。
    /// 起動しっぱなしのまま日をまたぐと、昨日の「今日」がそのまま残ってしまう。
    /// </summary>
    private void UpdateTodoTick(DateTime now)
    {
        if (_lastTodoDueRefreshDate == now.Date) return;
        _lastTodoDueRefreshDate = now.Date;

        foreach (var todo in Todos)
        {
            todo.NotifyDueStateChanged();
        }

        RebuildVisibleTodos();
        NotifyTodoCountsChanged();
        RecalculateLayout();
    }

    // ===== 保存と読込 =====
    //
    // チェックの付け外しや即時追加が続く間ずっと書き込むのを避けるため、
    // 予定データと同じくデバウンスしてまとめて1回にする。

    private DispatcherTimer? _todoSaveTimer;
    private bool _hasPendingTodoSave;
    private bool _isLoadingTodos;

    private void ScheduleTodoSave()
    {
        if (!_isInitialized || _isLoadingTodos) return;

        _hasPendingTodoSave = true;

        if (_todoSaveTimer == null)
        {
            _todoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _todoSaveTimer.Tick += (_, _) => FlushTodoSave();
        }

        _todoSaveTimer.Stop();
        _todoSaveTimer.Start();
    }

    /// <summary>保留中の ToDo 保存を即時実行する（アプリ終了時などに呼ぶ）</summary>
    public void FlushTodoSave()
    {
        _todoSaveTimer?.Stop();
        if (!_hasPendingTodoSave) return;

        _hasPendingTodoSave = false;
        Services.FilePersistenceService.SaveTodos(Todos);
    }

    private void LoadTodos()
    {
        var loaded = Services.FilePersistenceService.LoadTodos();

        _isLoadingTodos = true;
        try
        {
            foreach (var old in Todos)
            {
                old.PropertyChanged -= OnTodoPropertyChanged;
            }
            Todos.Clear();
            foreach (var todo in loaded)
            {
                // 変更の購読は OnTodosChanged がまとめて行う
                Todos.Add(todo);
            }
        }
        finally
        {
            _isLoadingTodos = false;
        }

        _lastTodoDueRefreshDate = DateTime.Today;
        RebuildVisibleTodos();
        NotifyTodoCountsChanged();

        // 起動直後は他に再計算のきっかけが無いため、ここで終日行のチップを作る
        RecalculateLayout();
    }
}
