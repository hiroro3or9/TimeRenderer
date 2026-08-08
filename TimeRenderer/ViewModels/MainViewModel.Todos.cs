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

        var parsed = Helpers.TodoQuickParser.Parse(_newTodoTitle, [.. Categories], DateTime.Now);
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
            param => SetTodoDue(param as TodoItem, DateTime.Today),
            param => param is TodoItem);

        SetTodoDueTomorrowCommand = new RelayCommand(
            param => SetTodoDue(param as TodoItem, DateTime.Today.AddDays(1)),
            param => param is TodoItem);

        ClearTodoDueCommand = new RelayCommand(
            param => SetTodoDue(param as TodoItem, null),
            param => param is TodoItem);

        TogglePlannedTodayCommand = new RelayCommand(
            param =>
            {
                if (param is not TodoItem todo) return;

                var before = TodoSnapshot.Capture(todo);
                todo.PlannedOn = todo.IsPlannedToday ? null : DateTime.Today;
                RecordTodoModify(todo, before, todo.IsPlannedToday ? "今日やる" : "今日やるの取り消し");
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

        var parsed = Helpers.TodoQuickParser.Parse(input, [.. Categories], DateTime.Now);
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

    // ===== サブタスク =====
    //
    // 親と子の変更は「親1件の内容変更」として1つの取り消し単位にまとめる。
    // 子を1つチェックするたびに履歴が積み上がると、Ctrl+Z が使いものにならない。

    /// <summary>サブタスクの一覧を開閉する</summary>
    public static void ToggleTodoExpanded(TodoItem todo) => todo.IsExpanded = !todo.IsExpanded;

    /// <summary>その場の入力欄からサブタスクを1件足す</summary>
    public void AddSubtask(TodoItem parent)
    {
        var title = parent.NewSubtaskTitle.Trim();
        if (title.Length == 0) return;

        ApplyToSubtasks(parent, "サブタスクの追加", () =>
        {
            parent.Subtasks.Add(new TodoSubtask { Title = title });
            parent.NotifySubtasksChanged();
        });

        parent.NewSubtaskTitle = string.Empty;
        parent.IsExpanded = true; // 続けて足せるように開いたままにする
    }

    /// <summary>サブタスクを削除する</summary>
    public void RemoveSubtask(TodoItem parent, TodoSubtask subtask)
    {
        ApplyToSubtasks(parent, "サブタスクの削除", () =>
        {
            parent.Subtasks.Remove(subtask);
            parent.NotifySubtasksChanged();
        });
    }

    /// <summary>
    /// サブタスクの完了を切り替える。
    /// これで全部が済んだら親も完了にする（繰り返しなら次回分の生成まで含めて1件の取り消し単位）。
    /// </summary>
    public void ToggleSubtask(TodoItem parent, TodoSubtask subtask)
    {
        ApplyToSubtasks(parent, "サブタスクの完了", () =>
        {
            subtask.IsCompleted = !subtask.IsCompleted;
            parent.NotifySubtasksChanged();
        });
    }

    /// <summary>
    /// サブタスクへの変更を、親1件の内容変更として履歴へ積む。
    /// 変更の結果すべて済んだ場合は、親の完了と次回分の生成もこの1件に含める。
    /// </summary>
    private void ApplyToSubtasks(TodoItem parent, string label, Action change)
    {
        var before = TodoSnapshot.Capture(parent);
        TodoItem? spawned = null;

        // 途中の状態で一覧の作り直しや保存が走らないよう、まとめて処理する
        _isUpdatingTodo = true;
        try
        {
            change();

            if (!parent.IsCompleted && parent.AreAllSubtasksDone)
            {
                parent.IsCompleted = true;
                spawned = SpawnNextOccurrence(parent);
            }
        }
        finally
        {
            _isUpdatingTodo = false;
        }

        if (parent.IsCompleted) PendingTodoReminders.Remove(parent);

        var edits = new List<IUndoableEdit>();

        var after = TodoSnapshot.Capture(parent);
        if (!before.IsSameAs(after))
        {
            edits.Add(new ModifyTodoEdit(parent, before, after, label));
        }
        if (spawned != null) edits.Add(new AddTodoEdit(spawned));

        PushEdits(edits, $"ToDo「{parent.Title}」の{label}");
        OnTodoChanged();
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
        var next = completed.CreateNextOccurrence(completed.CompletedAt ?? DateTime.Now);
        if (next == null) return null;

        // 次回分は繰り返しを引き継ぐので、完了した方の繰り返しは解除する。
        // 残したままだと、完了を取り消して付け直すたびに次回分が増えていく
        completed.Recurrence = TodoRecurrenceUnit.None;

        AddTodo(next, record: false);
        ShowAutoStartNotice($"「{next.Title}」の次回分（期限 {next.DueDate:M/d}）を作成しました");
        return next;
    }

    /// <summary>ToDo が増減・変化したときの共通処理（表示の作り直しと保存）</summary>
    private void OnTodoChanged()
    {
        RebuildVisibleTodos();
        NotifyTodoCountsChanged();
        InvalidateEstimateStats(); // 完了・記録時間が動くと見積もりの傾向も変わる
        RecalculateLayout();       // 終日行のチップも作り直す
        ScheduleTodoSave();
    }

    /// <summary>
    /// 表示用の一覧を作り直す。
    /// 完了済みは常に末尾へ送り、その中では選択中の並び順に従う。
    /// </summary>
    private void RebuildVisibleTodos()
    {
        IEnumerable<TodoItem> source = ShowCompletedTodos ? Todos : Todos.Where(t => !t.IsCompleted);

        // 「今日やる」は並び順に関わらず先頭へ寄せる。
        // 今日ぶんを選び出したのに探し回るのでは、印を付けた意味が無い
        var ordered = CurrentTodoSortMode switch
        {
            // 期限なしを末尾に送るため、期限の有無を先に見る
            TodoSortMode.DueDate => source
                .OrderBy(t => t.IsCompleted)
                .ThenByDescending(t => t.IsPlannedToday)
                .ThenBy(t => t.DueDate.HasValue ? 0 : 1)
                .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
                .ThenByDescending(t => t.Priority)
                .ThenBy(t => t.CreatedAt),

            TodoSortMode.Priority => source
                .OrderBy(t => t.IsCompleted)
                .ThenByDescending(t => t.IsPlannedToday)
                .ThenByDescending(t => t.Priority)
                .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
                .ThenBy(t => t.CreatedAt),

            TodoSortMode.Manual => source
                .OrderBy(t => t.IsCompleted)
                .ThenByDescending(t => t.IsPlannedToday)
                .ThenBy(t => t.SortOrder)
                .ThenBy(t => t.CreatedAt),

            _ => source
                .OrderBy(t => t.IsCompleted)
                .ThenByDescending(t => t.IsPlannedToday)
                .ThenBy(t => t.CreatedAt),
        };

        VisibleTodos = [.. ordered];
    }

    // ===== 手動並べ替え =====

    /// <summary>
    /// 手動モードでなければ切り替える。
    /// 切り替えた瞬間に並びが崩れないよう、今見えている順をそのまま初期値にする。
    /// </summary>
    private void EnsureManualSort()
    {
        if (CurrentTodoSortMode == TodoSortMode.Manual) return;

        ApplyManualOrder(VisibleTodos);
        CurrentTodoSortMode = TodoSortMode.Manual;
    }

    /// <summary>
    /// 並び順を SortOrder へ書き戻す。
    /// 一覧に出ていない ToDo（完了済み・絞り込みで隠れているもの）は、
    /// 番号がぶつからないよう後ろへ続けて振る。
    /// </summary>
    private void ApplyManualOrder(IReadOnlyList<TodoItem> ordered)
    {
        var placed = new HashSet<TodoItem>(ordered);

        // 1件ごとの再構築・保存を避けるため、書き戻しの間は通知の処理を止める
        _isUpdatingTodo = true;
        try
        {
            var index = 0;
            foreach (var todo in ordered) todo.SortOrder = index++;
            foreach (var todo in Todos)
            {
                if (!placed.Contains(todo)) todo.SortOrder = index++;
            }
        }
        finally
        {
            _isUpdatingTodo = false;
        }
    }

    /// <summary>
    /// 手動並べ替え：moved を target の位置へ差し込む（ドラッグ＆ドロップ用）。
    /// 並べ替えは手動モードでしか意味がないため、必要なら自動で切り替える。
    /// </summary>
    public void MoveTodoTo(TodoItem moved, TodoItem target)
    {
        if (ReferenceEquals(moved, target)) return;

        // 手動モードへの切り替えで並び順が動くため、その前に控える
        var before = CaptureTodoOrder();
        EnsureManualSort();

        var list = VisibleTodos.ToList();
        var from = list.IndexOf(moved);
        var to = list.IndexOf(target);
        if (from < 0 || to < 0) return;

        list.RemoveAt(from);
        list.Insert(to, moved);

        ApplyManualOrder(list);
        RecordTodoReorder(before);
        OnTodoChanged();
    }

    /// <summary>手動並べ替え：1つ上／下へ移動する（Ctrl+↑↓ とコンテキストメニュー用）</summary>
    private void MoveTodoBy(TodoItem? todo, int delta)
    {
        if (todo == null) return;

        var before = CaptureTodoOrder();
        EnsureManualSort();

        var list = VisibleTodos.ToList();
        var from = list.IndexOf(todo);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= list.Count) return;

        list.RemoveAt(from);
        list.Insert(to, todo);

        ApplyManualOrder(list);
        RecordTodoReorder(before);
        OnTodoChanged();

        SelectedTodo = todo; // 移動しても選択が外れないようにする
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
        DateTime rangeStart, DateTime rangeEnd, Dictionary<DateTime, int> allDayRowCounts)
    {
        // 終日行を描くのは日/週ビューだけ。他のモードでは作っても誰も見ない
        if (!IsDayOrWeekMode || Todos.Count == 0)
        {
            if (TodoChips.Count > 0) TodoChips = [];
            return 0;
        }

        var targets = Todos
            .Where(t => !t.IsCompleted && t.DueDate is { } due && due >= rangeStart && due < rangeEnd)
            .Where(IsTodoVisible) // 色フィルタはビュー上の表示にだけ効かせる（パネルは全件のまま）
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
        _recordingProjectCodeId = DefaultProjectCode?.Id;
        _recordingSourceItem = null;
        _recordingTodo = todo;

        ClearAwayState(); // 前回の記録で拾った離席を持ち越さない
        IsRecording = true;
        RecordingStartTime = DateTime.Now;
        RecordingDuration = TimeSpan.Zero;
        IsCountdownMode = false;
        CountdownRemaining = null;
    }

    // ===== 予定への割り当て（タイムブロッキング） =====

    /// <summary>見積もりを持たない ToDo を置いたときの既定の長さ</summary>
    private static readonly TimeSpan DefaultBlockDuration = TimeSpan.FromHours(1);

    /// <summary>
    /// ToDo を指定時刻に置いて、作業時間を先に押さえる。
    /// 長さは見積もり時間、無ければ1時間。予定には TodoId が入るので、
    /// この予定から記録を回せば実績が ToDo へ戻ってくる。
    /// </summary>
    /// <param name="todo">置く ToDo</param>
    /// <param name="start">開始時刻</param>
    public void BlockTimeForTodo(TodoItem todo, DateTime start)
    {
        var duration = todo.HasEstimate ? todo.EstimatedDuration : DefaultBlockDuration;

        var item = CreateItemForTodo(todo, start, start.Add(duration));
        ScheduleItems.Add(item);
        RecordAdd(item);

        ShowAutoStartNotice($"「{todo.Title}」を {start:M/d HH:mm} に {FormatBlockDuration(duration)} で置きました");
    }

    /// <summary>
    /// 記録漏れの帯を ToDo で埋める。
    /// こちらは過去に実際にやっていた時間なので、予定を作ると同時に ToDo へも積算する
    /// （これから押さえる時間ブロックとは扱いが違う）。
    /// </summary>
    public void FillGapWithTodo(TodoItem todo, DateTime start, DateTime end)
    {
        if (end <= start) return;

        var item = CreateItemForTodo(todo, start, end);
        item.Kind = ScheduleItemKind.Recorded;
        item.Content = $"記録時間: {(int)(end - start).TotalHours}:{(end - start).Minutes:D2}";

        var before = TodoSnapshot.Capture(todo);

        _isUpdatingTodo = true;
        try
        {
            ScheduleItems.Add(item);
            todo.RecordedTicks += (end - start).Ticks;
        }
        finally
        {
            _isUpdatingTodo = false;
        }

        // 予定の追加と ToDo への積算で1回の操作なので、まとめて1件の履歴にする
        var edits = new List<IUndoableEdit> { new AddItemEdit(item) };
        var after = TodoSnapshot.Capture(todo);
        if (!before.IsSameAs(after))
        {
            edits.Add(new ModifyTodoEdit(todo, before, after, "実績の追加"));
        }
        PushEdits(edits, $"「{todo.Title}」で記録漏れを埋める");

        OnTodoChanged();
    }

    /// <summary>
    /// 記録漏れの帯を埋める操作の入口。
    /// 未完了の ToDo から選ばせ、選ばれたらその帯の時間幅で実績を作る。
    /// </summary>
    public void FillGapFromTodoPicker(UnrecordedGap gap)
    {
        var candidates = Todos.Where(t => !t.IsCompleted).ToList();
        if (candidates.Count == 0)
        {
            _dialogService.ShowMessage("未完了の ToDo がありません。", "ToDo から埋める");
            return;
        }

        var message = $"{gap.StartTime:M/d (ddd) HH:mm} 〜 {gap.EndTime:HH:mm} の記録として残す ToDo を選んでください。";
        var todo = _dialogService.ShowTodoPickerDialog(message, candidates);
        if (todo == null) return;

        FillGapWithTodo(todo, gap.StartTime, gap.EndTime);
    }

    /// <summary>ToDo のタイトル・色・紐づけを引き継いだ予定アイテムを作る</summary>
    private ScheduleItem CreateItemForTodo(TodoItem todo, DateTime start, DateTime end) => new()
    {
        Kind = ScheduleItemKind.Planned,
        Title = todo.Title,
        StartTime = start,
        EndTime = end,
        ColorCode = todo.ColorCode,
        CategoryId = todo.CategoryId ?? ResolveCategory(todo.CategoryId, todo.ColorCode)?.Id,
        ProjectCodeId = DefaultProjectCode?.Id,
        TodoId = todo.Id,
    };

    private static string FormatBlockDuration(TimeSpan duration) =>
        duration.TotalHours >= 1 ? $"{duration.TotalHours:0.#} 時間" : $"{duration.TotalMinutes:0} 分";

    /// <summary>
    /// ID から ToDo を引く。見つからなければ null（削除済みの ToDo を指したまま残った予定など）。
    /// </summary>
    private TodoItem? FindTodoById(string? id) =>
        string.IsNullOrEmpty(id) ? null : Todos.FirstOrDefault(t => t.Id == id);

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

    // ===== 通知 =====

    /// <summary>通知日時に達し、まだユーザーの操作を待っている ToDo</summary>
    public ObservableCollection<TodoItem> PendingTodoReminders { get; } = [];

    /// <summary>
    /// 通知済みの ToDo。「Id｜通知日時」をキーにする。
    /// 通知日時を変えれば別のキーになるため、設定し直せば同じ ToDo でも再び通知される
    /// （スヌーズはこの性質をそのまま使っている）。
    /// </summary>
    private readonly HashSet<string> _remindedTodoKeys = [];

    /// <summary>
    /// 通知時刻からこれ以上遅れた通知は、個別のバナーにはしない。
    /// アプリを何日も起動していなかった場合に、過ぎた通知がまとめて溢れるのを防ぐ。
    /// 代わりに「見逃した通知」として1本にまとめて知らせる。
    /// </summary>
    private static readonly TimeSpan TodoReminderGrace = TimeSpan.FromMinutes(15);

    /// <summary>
    /// 「見逃した通知」として拾う範囲。これより古い通知は黙って捨てる。
    /// 期限を過ぎたこと自体は一覧の色とまとめ通知で分かるので、
    /// 何ヶ月も前の通知時刻まで蒸し返す必要はない。
    /// </summary>
    private static readonly TimeSpan TodoMissedWindow = TimeSpan.FromDays(3);

    /// <summary>「あとで」で先送りできる時間の選択肢（分）</summary>
    public static IReadOnlyList<int> TodoSnoozeOptions { get; } = [5, 10, 15, 30, 60, 120];

    private int _todoSnoozeMinutes = 10;
    /// <summary>バナーの「あとで」を押したときに先送りする分数</summary>
    public int TodoSnoozeMinutes
    {
        get => _todoSnoozeMinutes;
        set
        {
            if (SetProperty(ref _todoSnoozeMinutes, Math.Clamp(value, 1, 24 * 60)))
            {
                OnPropertyChanged(nameof(TodoSnoozeLabel));
                SaveSettings();
            }
        }
    }

    /// <summary>バナーの「あとで」ボタンの表示（例: "10分後"）</summary>
    public string TodoSnoozeLabel => FormatSnoozeLabel(TodoSnoozeMinutes);

    public static string FormatSnoozeLabel(int minutes) =>
        minutes % 60 == 0 && minutes >= 60 ? $"{minutes / 60}時間後" : $"{minutes}分後";

    private bool _isTodoReminderSoundEnabled = true;
    /// <summary>通知を出すときに音を鳴らすか</summary>
    public bool IsTodoReminderSoundEnabled
    {
        get => _isTodoReminderSoundEnabled;
        set
        {
            if (SetProperty(ref _isTodoReminderSoundEnabled, value)) SaveSettings();
        }
    }

    public static IReadOnlyList<int> TodoDefaultRemindHourOptions { get; } = [.. Enumerable.Range(0, 24)];

    private int _todoDefaultRemindHour = 9;
    /// <summary>
    /// 通知時刻を省略したときに使う時。
    /// クイック追加の「*前日」や、編集ダイアログで通知を入れた直後の初期値になる。
    /// </summary>
    public int TodoDefaultRemindHour
    {
        get => _todoDefaultRemindHour;
        set
        {
            if (SetProperty(ref _todoDefaultRemindHour, Math.Clamp(value, 0, 23)))
            {
                ApplyDefaultRemindHour();
                SaveSettings();
            }
        }
    }

    /// <summary>
    /// 既定の通知時刻を、参照する側（モデルとパーサー）へ配る。
    /// どちらも VM を知らない場所で時刻を必要とするため、静的な既定値として持たせている。
    /// </summary>
    private void ApplyDefaultRemindHour()
    {
        TodoItem.DefaultRemindHour = _todoDefaultRemindHour;
        Helpers.TodoQuickParser.DefaultRemindHour = _todoDefaultRemindHour;
    }

    /// <summary>
    /// 通知日時に達した ToDo をバナーへ積み、通知音を鳴らす。
    /// アプリが非アクティブなら、MainWindow 側がこの追加を拾ってトレイ通知も出す。
    /// </summary>
    private void CheckTodoReminders(DateTime now)
    {
        var missed = new List<TodoItem>();

        foreach (var todo in Todos)
        {
            if (todo.IsCompleted) continue;
            if (todo.RemindAt is not { } remindAt) continue;
            if (now < remindAt) continue;

            // 判定済みのものは、通知したかどうかに関わらず二度と見ない
            if (!_remindedTodoKeys.Add(BuildTodoReminderKey(todo))) continue;

            // 遅れすぎた通知は個別に出さず、まとめて1本で知らせる。
            // ただし古すぎるものは黙って捨てる。通知済みの記録はセッションを跨がないため、
            // これが無いと何ヶ月も前の通知が起動のたびに蒸し返される
            if (now - remindAt > TodoReminderGrace)
            {
                if (now - remindAt <= TodoMissedWindow) missed.Add(todo);
                continue;
            }

            if (!PendingTodoReminders.Contains(todo))
            {
                PendingTodoReminders.Add(todo);
                if (IsTodoReminderSoundEnabled) SystemSounds.Asterisk.Play();
            }
        }

        if (missed.Count > 0) AddMissedTodoReminders(missed);
    }

    private static string BuildTodoReminderKey(TodoItem todo) => $"{todo.Id}|{todo.RemindAt:O}";

    /// <summary>
    /// 通知を先送りする。
    /// 通知日時を動かすと通知済みのキー（Id｜通知日時）も変わるため、その時刻に再び通知される。
    /// 相対指定は「期限の前日9時」という約束のことなので、先送りした時点で外す。
    /// </summary>
    public void SnoozeTodoReminder(TodoItem todo, TimeSpan delay)
    {
        PendingTodoReminders.Remove(todo);
        todo.RemindOffsetDays = null;
        todo.RemindAt = DateTime.Now.Add(delay);
    }

    /// <summary>翌日の既定の通知時刻まで先送りする（「今日はもう見ない」ためのもの）</summary>
    public void SnoozeTodoReminderUntilTomorrow(TodoItem todo)
    {
        PendingTodoReminders.Remove(todo);
        todo.RemindOffsetDays = null;
        todo.RemindAt = DateTime.Today.AddDays(1).AddHours(TodoDefaultRemindHour);
    }

    // ===== 見逃した通知 =====
    //
    // アプリを閉じている間に過ぎた通知は、個別のバナーで出すと起動直後に画面が埋まる。
    // かといって黙って消すと、通知を付けた意味が無くなる。まとめて1本だけ出す。

    private readonly List<TodoItem> _missedTodoReminders = [];

    private string? _todoMissedNotice;
    /// <summary>見逃した通知の本文（無いときは null）</summary>
    public string? TodoMissedNotice
    {
        get => _todoMissedNotice;
        private set
        {
            if (SetProperty(ref _todoMissedNotice, value))
            {
                OnPropertyChanged(nameof(HasTodoMissedNotice));
            }
        }
    }

    public bool HasTodoMissedNotice => !string.IsNullOrEmpty(TodoMissedNotice);

    private void AddMissedTodoReminders(IEnumerable<TodoItem> todos)
    {
        foreach (var todo in todos)
        {
            if (!_missedTodoReminders.Contains(todo)) _missedTodoReminders.Add(todo);
        }

        if (_missedTodoReminders.Count == 0) return;

        var head = _missedTodoReminders[0].Title;
        TodoMissedNotice = _missedTodoReminders.Count == 1
            ? $"通知時刻を過ぎた ToDo があります：{head}"
            : $"通知時刻を過ぎた ToDo が {_missedTodoReminders.Count} 件あります（{head} ほか）";
    }

    private void ClearMissedTodoReminders()
    {
        _missedTodoReminders.Clear();
        TodoMissedNotice = null;
    }

    /// <summary>
    /// ビューに出す ToDo（未完了・期限あり・色フィルタを通過）を期限日ごとにまとめる。
    /// 月・スプリントビューのセルが日付で引くために使う。
    /// </summary>
    private Dictionary<DateTime, List<TodoItem>> GetVisibleTodosByDueDate()
    {
        var result = new Dictionary<DateTime, List<TodoItem>>();
        if (Todos.Count == 0) return result;

        foreach (var todo in Todos)
        {
            if (todo.IsCompleted) continue;
            if (todo.DueDate is not { } due) continue;
            if (!IsTodoVisible(todo)) continue;

            if (!result.TryGetValue(due.Date, out var list))
            {
                list = [];
                result[due.Date] = list;
            }
            list.Add(todo);
        }

        foreach (var key in result.Keys.ToList())
        {
            // 終日行のチップと同じ並び（優先度が高い順、次に追加順）にそろえる
            result[key] = [.. result[key].OrderByDescending(t => t.Priority).ThenBy(t => t.CreatedAt)];
        }

        return result;
    }

    // ===== 朝のまとめ通知 =====
    //
    // 個別の通知日時を付け忘れていても、その日に片付けるべき量が朝に一度は目に入るようにする。

    private bool _isTodoDigestEnabled = true;
    /// <summary>1日1回、期限が今日・期限超過の件数をまとめて知らせるか</summary>
    public bool IsTodoDigestEnabled
    {
        get => _isTodoDigestEnabled;
        set
        {
            if (SetProperty(ref _isTodoDigestEnabled, value)) SaveSettings();
        }
    }

    public static IReadOnlyList<int> TodoDigestHourOptions { get; } = [.. Enumerable.Range(0, 24)];

    private int _todoDigestHour = 9;
    /// <summary>まとめ通知を出す時刻（時）</summary>
    public int TodoDigestHour
    {
        get => _todoDigestHour;
        set
        {
            if (SetProperty(ref _todoDigestHour, Math.Clamp(value, 0, 23))) SaveSettings();
        }
    }

    /// <summary>まとめ通知を最後に出した日。1日に何度も出さないため設定へ保存する</summary>
    private DateTime _lastTodoDigestDate;

    private const string TodoDigestDateFormat = "yyyy-MM-dd";

    /// <summary>設定へ書き出す形（未通知なら null）。カルチャに依存しない形式で持つ</summary>
    private string? FormatTodoDigestDate() =>
        _lastTodoDigestDate == default
            ? null
            : _lastTodoDigestDate.ToString(TodoDigestDateFormat, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>設定から読み戻す。壊れていれば「未通知」として扱う</summary>
    private void ParseTodoDigestDate(string? value)
    {
        _lastTodoDigestDate = DateTime.TryParseExact(
            value, TodoDigestDateFormat, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed
            : default;
    }

    private string? _todoDigestNotice;
    /// <summary>まとめ通知の本文（出していないときは null）</summary>
    public string? TodoDigestNotice
    {
        get => _todoDigestNotice;
        private set
        {
            if (SetProperty(ref _todoDigestNotice, value))
            {
                OnPropertyChanged(nameof(HasTodoDigest));
            }
        }
    }

    public bool HasTodoDigest => !string.IsNullOrEmpty(TodoDigestNotice);

    /// <summary>
    /// 設定した時刻を過ぎていれば、その日の1回目のまとめ通知を出す。
    /// 出す時刻より遅く起動した日でも、その日ぶんはまだ出していないので1回出す
    /// （朝に見られなかった日ほど、いま何件あるかを知りたい）。
    /// </summary>
    private void CheckTodoDigest(DateTime now)
    {
        if (!IsTodoDigestEnabled) return;
        if (_lastTodoDigestDate.Date == now.Date) return;
        if (now.Hour < TodoDigestHour) return;

        _lastTodoDigestDate = now.Date;
        SaveSettings();

        var dueToday = Todos.Count(t => t.IsDueToday);
        var overdue = TodoOverdueCount;
        if (dueToday == 0 && overdue == 0) return;

        var parts = new List<string>();
        if (dueToday > 0) parts.Add($"今日が期限の ToDo が {dueToday} 件");
        if (overdue > 0) parts.Add($"期限を過ぎた ToDo が {overdue} 件");

        TodoDigestNotice = string.Join("、", parts) + " あります。";
    }

    // ===== 日付またぎ =====

    private DateTime _lastTodoDueRefreshDate = DateTime.MinValue;

    /// <summary>
    /// 毎tickの処理：通知の判定と、日付が変わったときの表示の作り直し。
    /// 期限に依存する表示（超過・今日・残り日数）は、起動しっぱなしで日をまたぐと
    /// 昨日の「今日」がそのまま残ってしまうため作り直す。
    /// </summary>
    private void UpdateTodoTick(DateTime now)
    {
        CheckTodoReminders(now);
        CheckTodoDigest(now);

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

    // ===== 完了済みのアーカイブ =====
    //
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
        var cutoff = DateTime.Today.AddDays(-TodoArchiveRetentionDays);

        var targets = Todos
            .Where(t => t.IsCompleted && t.CompletedAt is { } done && done.Date < cutoff)
            .ToList();

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
            // 読み込みでインスタンスが入れ替わるため、通知の状態も作り直す
            PendingTodoReminders.Clear();
            _remindedTodoKeys.Clear();
            ClearMissedTodoReminders();
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

        _archivedTodos = Services.FilePersistenceService.LoadTodoArchive();
        ArchiveOldTodos();
        InvalidateEstimateStats();

        _lastTodoDueRefreshDate = DateTime.Today;
        RebuildVisibleTodos();
        NotifyTodoCountsChanged();

        // 起動直後は他に再計算のきっかけが無いため、ここで終日行のチップを作る
        RecalculateLayout();
    }
}
