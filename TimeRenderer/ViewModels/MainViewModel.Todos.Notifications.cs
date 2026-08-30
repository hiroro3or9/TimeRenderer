using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Media;

using TimeRenderer.Helpers;
using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>ToDoの個別通知、見逃し通知、朝のまとめと日付またぎ処理。</summary>
public partial class MainViewModel
{
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
    public static IReadOnlyList<int> TodoSnoozeOptions => Services.AppSettingsNormalizer.TodoSnoozeOptions;

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
        TodoQuickParser.DefaultRemindHour = _todoDefaultRemindHour;
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
            var state = TodoReminderHelper.Classify(
                todo, now, TodoReminderGrace, TodoMissedWindow);
            if (state == TodoReminderState.NotDue) continue;

            // 判定済みのものは、通知したかどうかに関わらず二度と見ない
            if (!_remindedTodoKeys.Add(TodoReminderHelper.BuildKey(todo))) continue;

            // 遅れすぎた通知は個別に出さず、まとめて1本で知らせる。
            // ただし古すぎるものは黙って捨てる。通知済みの記録はセッションを跨がないため、
            // これが無いと何ヶ月も前の通知が起動のたびに蒸し返される
            if (state != TodoReminderState.Due)
            {
                if (state == TodoReminderState.Missed) missed.Add(todo);
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
}
