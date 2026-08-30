using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Media;
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
/// 通知日時（RemindAt）を設定した ToDo は、その時刻にバナー・通知音・トレイ通知で知らせる。
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
            // 右側のオーバーレイ同士が重ならないよう、ToDo を開くときは他のパネルを閉じる。
            if (value && _isSettingsPanelVisible)
            {
                _isSettingsPanelVisible = false;
                OnPropertyChanged(nameof(IsSettingsPanelVisible));
            }
            if (value && _isManagementPanelVisible)
            {
                _isManagementPanelVisible = false;
                OnPropertyChanged(nameof(IsManagementPanelVisible));
            }

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
        new(TodoSortMode.Manual, "手動"),
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

    private TodoItem? _selectedTodo;
    /// <summary>一覧で選択中の ToDo（キーボード操作の現在位置）</summary>
    public TodoItem? SelectedTodo
    {
        get => _selectedTodo;
        set => SetProperty(ref _selectedTodo, value);
    }

    private string _newTodoTitle = string.Empty;
    /// <summary>パネル上部の即時追加欄。Enter で1件追加して空に戻る</summary>
    public string NewTodoTitle
    {
        get => _newTodoTitle;
        set
        {
            if (SetProperty(ref _newTodoTitle, value)) UpdateQuickAddPreview();
        }
    }

    // ===== クイック追加の記法 =====
    //
    // 「思いついた瞬間に置ける」ことを保ったまま、期限や優先度まで一度に指定できるようにする。
    // 打っている最中に解釈結果を出すのが要点で、これが無いと
    // 「記号が効いたのか、ただの文字として残ったのか」が Enter を押すまで分からない。

    private bool _isTodoQuickSyntaxEnabled = true;
    /// <summary>クイック追加欄で @ ! # ~ * の記法を解釈するか</summary>
    public bool IsTodoQuickSyntaxEnabled
    {
        get => _isTodoQuickSyntaxEnabled;
        set
        {
            if (SetProperty(ref _isTodoQuickSyntaxEnabled, value))
            {
                UpdateQuickAddPreview();
                OnPropertyChanged(nameof(TodoQuickAddHint));
                SaveSettings();
            }
        }
    }

    /// <summary>入力欄の下に常時出すヒント（記法を切っているときは操作の説明を出す）</summary>
    public string TodoQuickAddHint =>
        IsTodoQuickSyntaxEnabled ? Helpers.TodoQuickParser.SyntaxHint : "やることを入力して Enter";

    private string? _todoQuickAddPreview;
    /// <summary>
    /// 入力中の解釈結果（例: "資料作成 ／ 期限 8/6(木)・優先度 高"）。
    /// 何も解釈されなかったときは null にして、ヒント表示へ戻す。
    /// </summary>
    public string? TodoQuickAddPreview
    {
        get => _todoQuickAddPreview;
        private set
        {
            if (SetProperty(ref _todoQuickAddPreview, value))
            {
                OnPropertyChanged(nameof(HasTodoQuickAddPreview));
            }
        }
    }

    public bool HasTodoQuickAddPreview => !string.IsNullOrEmpty(TodoQuickAddPreview);

    private void UpdateQuickAddPreview()
    {
        if (!IsTodoQuickSyntaxEnabled || string.IsNullOrWhiteSpace(_newTodoTitle))
        {
            TodoQuickAddPreview = null;
            return;
        }

        var parsed = Helpers.TodoQuickParser.Parse(_newTodoTitle, [.. Categories], LocalNow);
        if (!parsed.HasAttributes)
        {
            TodoQuickAddPreview = null;
            return;
        }

        // 記法を取り除いた結果、本文が消えてしまう入力は追加できない。
        // Enter を押してから何も起きない、を避けるためここで伝える
        var title = parsed.Title.Length == 0 ? "（タイトルが空です）" : parsed.Title;
        TodoQuickAddPreview = $"{title} ／ {string.Join("・", parsed.Summary)}";
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
        RebuildTodayWorkload(); // 「今日やる」の増減で見込みが変わる
    }

    // ===== コマンド =====

    public ICommand ToggleTodoPanelCommand { get; private set; } = null!;
    public ICommand AddQuickTodoCommand { get; private set; } = null!;
    public ICommand AddTodoCommand { get; private set; } = null!;
    public ICommand EditTodoCommand { get; private set; } = null!;
    public ICommand DeleteTodoCommand { get; private set; } = null!;
    public ICommand ToggleTodoCompletedCommand { get; private set; } = null!;
    public ICommand StartRecordingFromTodoCommand { get; private set; } = null!;
    public ICommand SetTodoDueTodayCommand { get; private set; } = null!;
    public ICommand SetTodoDueTomorrowCommand { get; private set; } = null!;
    public ICommand ClearTodoDueCommand { get; private set; } = null!;
    public ICommand TogglePlannedTodayCommand { get; private set; } = null!;
    public ICommand ClearCompletedTodosCommand { get; private set; } = null!;
    public ICommand StartRecordingFromTodoReminderCommand { get; private set; } = null!;
    public ICommand CompleteTodoReminderCommand { get; private set; } = null!;
    public ICommand SnoozeTodoReminderCommand { get; private set; } = null!;
    public ICommand DismissTodoReminderCommand { get; private set; } = null!;
    public ICommand MoveTodoUpCommand { get; private set; } = null!;
    public ICommand MoveTodoDownCommand { get; private set; } = null!;
    public ICommand FocusQuickAddTodoCommand { get; private set; } = null!;
    public ICommand DismissTodoDigestCommand { get; private set; } = null!;
    public ICommand DismissTodoMissedNoticeCommand { get; private set; } = null!;
    public ICommand ShowMissedTodosCommand { get; private set; } = null!;

    /// <summary>
    /// 即時追加欄へ入力を移す要求（Ctrl+T）。パネル側が購読してフォーカスを移す。
    /// VM からコントロールを直接触らないための経路。
    /// </summary>
    public event EventHandler? QuickAddTodoFocusRequested;

    private void InitializeTodoCommands()
    {
        Todos.CollectionChanged += OnTodosChanged;

        ToggleTodoPanelCommand = new RelayCommand(_ => IsTodoPanelVisible = !IsTodoPanelVisible);

        AddQuickTodoCommand = new RelayCommand(_ =>
        {
            var todo = BuildQuickTodo(NewTodoTitle);
            if (todo == null) return;

            AddTodo(todo);
            NewTodoTitle = string.Empty;
        });

        AddTodoCommand = new RelayCommand(_ =>
        {
            var result = _dialogService.ShowTodoEditDialog(null, [.. Categories], GetTitleSuggestions(), EstimateStats);
            if (result != null) AddTodo(result);
        });

        EditTodoCommand = new RelayCommand(
            param =>
            {
                if (param is not TodoItem todo) return;

                var edited = _dialogService.ShowTodoEditDialog(todo, [.. Categories], GetTitleSuggestions(), EstimateStats);
                if (edited == null) return;

                var before = TodoSnapshot.Capture(todo);

                // ダイアログは新しいインスタンスを返すため、既存の実体へ値を移す
                // （リストの並びと、記録中の紐付け先を保つ）
                _isUpdatingTodo = true;
                try
                {
                    todo.Title = edited.Title;
                    todo.Content = edited.Content;
                    // 相対指定は期限に連動して通知日時を書き換えるため、
                    // 先に解除して 期限 → 通知日時 → 相対指定 の順で入れる
                    todo.RemindOffsetDays = null;
                    todo.DueDate = edited.DueDate;
                    todo.RemindAt = edited.RemindAt;
                    todo.RemindOffsetDays = edited.RemindOffsetDays;
                    todo.Priority = edited.Priority;
                    todo.CategoryId = edited.CategoryId;
                    todo.ColorCode = edited.ColorCode;
                    todo.EstimatedMinutes = edited.EstimatedMinutes;
                    todo.Recurrence = edited.Recurrence;
                    todo.RecurrenceInterval = edited.RecurrenceInterval;
                    todo.RecurrenceDaysOfWeek = edited.RecurrenceDaysOfWeek;
                    todo.RecurrenceFromCompletion = edited.RecurrenceFromCompletion;
                    todo.Subtasks = edited.Subtasks;
                }
                finally
                {
                    _isUpdatingTodo = false;
                }

                RecordTodoModify(todo, before, "編集");
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
                    // 元の位置も履歴に残すため、取り除く前に記録する
                    RecordTodoRemove(todo);
                    Todos.Remove(todo);
                    if (ReferenceEquals(_recordingTodo, todo)) _recordingTodo = null;
                }
            },
            param => param is TodoItem
        );

        ToggleTodoCompletedCommand = new RelayCommand(
            param =>
            {
                if (param is TodoItem todo) SetTodoCompleted(todo, !todo.IsCompleted);
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
            param => SetTodoDue(param as TodoItem, LocalToday),
            param => param is TodoItem);

        SetTodoDueTomorrowCommand = new RelayCommand(
            param => SetTodoDue(param as TodoItem, LocalToday.AddDays(1)),
            param => param is TodoItem);

        ClearTodoDueCommand = new RelayCommand(
            param => SetTodoDue(param as TodoItem, null),
            param => param is TodoItem);

        TogglePlannedTodayCommand = new RelayCommand(
            param =>
            {
                if (param is not TodoItem todo) return;

                var before = TodoSnapshot.Capture(todo);
                var wasPlannedToday = todo.PlannedOn?.Date == LocalToday;
                todo.PlannedOn = wasPlannedToday ? null : LocalToday;
                RecordTodoModify(todo, before, wasPlannedToday ? "今日やるの取り消し" : "今日やる");
            },
            param => param is TodoItem);

        ClearCompletedTodosCommand = new RelayCommand(
            _ =>
            {
                var completed = Todos.Where(t => t.IsCompleted).ToList();
                if (completed.Count == 0) return;

                if (!_dialogService.ShowConfirmationDialog(
                    $"完了済みの ToDo {completed.Count} 件を削除しますか？\n（Ctrl+Z で元に戻せます）", "完了済みの削除")) return;

                // 1回の操作なので、まとめて1件の履歴にする。
                // 位置は取り除く直前に控える（先に全部数えると、削除で index がずれる）
                var edits = new List<IUndoableEdit>();
                foreach (var todo in completed)
                {
                    var index = Todos.IndexOf(todo);
                    if (index < 0) continue;

                    edits.Add(new RemoveTodoEdit(todo, index));
                    Todos.Remove(todo);
                    if (ReferenceEquals(_recordingTodo, todo)) _recordingTodo = null;
                }

                PushEdits(edits, $"完了済みの ToDo {edits.Count} 件の削除");
            },
            _ => HasCompletedTodos);

        StartRecordingFromTodoReminderCommand = new RelayCommand(
            param =>
            {
                if (param is not TodoItem todo) return;
                PendingTodoReminders.Remove(todo);
                StartRecordingFromTodo(todo);
            },
            param => param is TodoItem);

        CompleteTodoReminderCommand = new RelayCommand(
            param =>
            {
                if (param is TodoItem todo) SetTodoCompleted(todo, true); // 完了にすると通知一覧からも外れる
            },
            param => param is TodoItem);

        SnoozeTodoReminderCommand = new RelayCommand(
            param =>
            {
                if (param is not TodoItem todo) return;
                SnoozeTodoReminder(todo, TimeSpan.FromMinutes(TodoSnoozeMinutes));
            },
            param => param is TodoItem);

        DismissTodoReminderCommand = new RelayCommand(
            param =>
            {
                if (param is TodoItem todo) PendingTodoReminders.Remove(todo);
            },
            param => param is TodoItem);

        MoveTodoUpCommand = new RelayCommand(
            param => MoveTodoBy(param as TodoItem, -1),
            param => param is TodoItem);

        MoveTodoDownCommand = new RelayCommand(
            param => MoveTodoBy(param as TodoItem, 1),
            param => param is TodoItem);

        FocusQuickAddTodoCommand = new RelayCommand(_ =>
        {
            IsTodoPanelVisible = true;
            QuickAddTodoFocusRequested?.Invoke(this, EventArgs.Empty);
        });

        DismissTodoDigestCommand = new RelayCommand(_ => TodoDigestNotice = null);

        DismissTodoMissedNoticeCommand = new RelayCommand(_ => ClearMissedTodoReminders());

        ShowMissedTodosCommand = new RelayCommand(_ =>
        {
            // 見逃した1件目へ寄せる。件数だけ言われても、どれか分からなければ動けない
            var first = _missedTodoReminders.FirstOrDefault(t => !t.IsCompleted);

            IsTodoPanelVisible = true;
            if (first != null) SelectedTodo = first;

            ClearMissedTodoReminders();
        });
    }

    /// <summary>
    /// クイック追加欄の入力から ToDo を1件組み立てる。追加できないときは null。
    ///
    /// 記法で指定されなかった項目は、これまでどおり「未設定・先頭カテゴリ」で始める。
    /// 決まっていない段階でも置けることが、この入口の値だから。
    /// </summary>
    private TodoItem? BuildQuickTodo(string input)
    {
        if (!IsTodoQuickSyntaxEnabled)
        {
            var plain = input.Trim();
            if (plain.Length == 0) return null;

            var defaultCategory = Categories.FirstOrDefault();
            return new TodoItem
            {
                Title = plain,
                ColorCode = defaultCategory?.ColorCode ?? CategoryInfo.CreateBrush("LightBlue").ToString(),
                CategoryId = defaultCategory?.Id,
            };
        }

        var parsed = Helpers.TodoQuickParser.Parse(input, [.. Categories], LocalNow);
        if (parsed.Title.Length == 0) return null;

        var category = parsed.Category ?? Categories.FirstOrDefault();

        // 期限 → 通知 → 相対指定 の順で入れる。
        // 相対指定を先に入れると、期限を入れた時点で通知日時が計算し直されてしまう
        var todo = new TodoItem
        {
            Title = parsed.Title,
            ColorCode = category?.ColorCode ?? CategoryInfo.CreateBrush("LightBlue").ToString(),
            CategoryId = category?.Id,
            Priority = parsed.Priority ?? TodoPriority.Normal,
            EstimatedMinutes = parsed.EstimatedMinutes ?? 0,
            DueDate = parsed.DueDate,
            RemindAt = parsed.RemindAt,
            RemindOffsetDays = parsed.RemindOffsetDays,
        };

        return todo;
    }

    /// <summary>
    /// ToDo を一覧へ加える。手動並べ替え用の位置は末尾にする
    /// （追加したものが上に割り込むと、並べ直した意味が消える）。
    /// </summary>
    /// <param name="record">
    /// 取り消し履歴へ積むか。他の操作とまとめて1件として積む場合は false にして、
    /// 呼び出し側が組み立てる。
    /// </param>
    private void AddTodo(TodoItem todo, bool record = true)
    {
        todo.SortOrder = Todos.Count == 0 ? 0 : Todos.Max(t => t.SortOrder) + 1;
        Todos.Add(todo);
        if (record) RecordTodoAdd(todo);
    }

    /// <summary>現在の並び順を控える（並べ替えの取り消し用）</summary>
    private List<(TodoItem Todo, int Order)> CaptureTodoOrder() =>
        [.. Todos.Select(t => (Todo: t, Order: t.SortOrder))];

    /// <summary>控えておいた並び順との差分を履歴へ積む。変化がなければ何もしない</summary>
    private void RecordTodoReorder(List<(TodoItem Todo, int Order)> before)
    {
        var after = CaptureTodoOrder();
        if (before.Count == after.Count && before.SequenceEqual(after)) return;

        _undo.Push(new ReorderTodosEdit(before, after));
    }

    private void SetTodoDue(TodoItem? todo, DateTime? due)
    {
        if (todo == null || todo.DueDate == due) return;

        var before = TodoSnapshot.Capture(todo);
        todo.DueDate = due;
        RecordTodoModify(todo, before, "期限の変更");
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

                // 消えた ToDo の通知を残さない（バナーの「記録開始」が行き先を失う）
                PendingTodoReminders.Remove(todo);
                if (_missedTodoReminders.Remove(todo) && _missedTodoReminders.Count == 0)
                {
                    TodoMissedNotice = null;
                }
            }
        }

        if (_isLoadingTodos) return;
        if (IsApplyingUndo) return; // 取り消し・やり直しの適用中は AfterUndoRedo でまとめて実行

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
                           or nameof(TodoItem.HasReminder)
                           or nameof(TodoItem.RemindDisplay)
                           or nameof(TodoItem.IsOverdue)
                           or nameof(TodoItem.IsDueToday)
                           or nameof(TodoItem.IsPlannedToday)
                           or nameof(TodoItem.HasRecurrenceDays)
                           or nameof(TodoItem.IsHighPriority)
                           or nameof(TodoItem.IsLowPriority)
                           or nameof(TodoItem.HasEstimate)
                           or nameof(TodoItem.EstimatedDuration)
                           or nameof(TodoItem.EstimateDisplay)
                           or nameof(TodoItem.ProgressPercent)
                           or nameof(TodoItem.ProgressDisplay)
                           or nameof(TodoItem.IsOverEstimate)
                           or nameof(TodoItem.HasRecurrence)
                           or nameof(TodoItem.RecurrenceDisplay)
                           or nameof(TodoItem.HasSubtasks)
                           or nameof(TodoItem.SubtaskTotalCount)
                           or nameof(TodoItem.SubtaskDoneCount)
                           or nameof(TodoItem.AreAllSubtasksDone)
                           or nameof(TodoItem.SubtaskProgressText)
                           or nameof(TodoItem.SubtaskProgressPercent)
                           or nameof(TodoItem.IsExpanded)      // 見た目だけの状態
                           or nameof(TodoItem.NewSubtaskTitle) // 入力途中の文字
                           or nameof(TodoItem.ToolTipText)) return;

        // 完了になったものは、まだ出ているバナーを片付ける。
        // 取り消し・やり直しで完了へ戻った場合もここを通す
        if (e.PropertyName == nameof(TodoItem.IsCompleted) &&
            sender is TodoItem { IsCompleted: true } completed)
        {
            PendingTodoReminders.Remove(completed);
        }

        if (_isUpdatingTodo || _isLoadingTodos) return;
        if (IsApplyingUndo) return; // 取り消し・やり直しの適用中は AfterUndoRedo でまとめて実行

        OnTodoChanged();
    }

    /// <summary>
    /// 完了状態を切り替える。
    ///
    /// 繰り返す ToDo なら次回分の生成までを1回の取り消し単位にまとめる。
    /// 別々に積むと、完了を戻したのに次回分だけ残る中途半端な状態を作れてしまう。
    /// </summary>
    private void SetTodoCompleted(TodoItem todo, bool completed)
    {
        if (todo.IsCompleted == completed) return;

        var before = TodoSnapshot.Capture(todo);
        TodoItem? spawned;

        // 完了と次回分の生成で個別に再構築・保存が走らないよう、まとめて処理する
        _isUpdatingTodo = true;
        try
        {
            todo.IsCompleted = completed;
            spawned = completed ? SpawnNextOccurrence(todo) : null;
        }
        finally
        {
            _isUpdatingTodo = false;
        }

        if (completed) PendingTodoReminders.Remove(todo);

        var edits = new List<IUndoableEdit>();

        var after = TodoSnapshot.Capture(todo);
        if (!before.IsSameAs(after))
        {
            edits.Add(new ModifyTodoEdit(todo, before, after, completed ? "完了" : "完了の取り消し"));
        }
        if (spawned != null) edits.Add(new AddTodoEdit(spawned));

        PushEdits(edits, $"ToDo「{todo.Title}」の完了");
        OnTodoChanged();
    }

    /// <summary>
    /// 繰り返す ToDo を完了したとき、次回分を作って一覧へ加える。
    /// 完了した方は実績として残す（何をいつ済ませたかが消えないようにするため）。
    /// 履歴へは呼び出し側が完了とまとめて積むので、ここでは積まない。
    /// </summary>
    private TodoItem? SpawnNextOccurrence(TodoItem completed)
    {
        var next = completed.CreateNextOccurrence(completed.CompletedAt ?? LocalNow);
        if (next == null) return null;

        // 次回分は繰り返しを引き継ぐので、完了した方の繰り返しは解除する。
        // 残したままだと、完了を取り消して付け直すたびに次回分が増えていく
        completed.Recurrence = TodoRecurrenceUnit.None;

        AddTodo(next, record: false);
        ShowAutoStartNotice($"「{next.Title}」の次回分（期限 {next.DueDate:M/d}）を作成しました");
        return next;
    }

}
