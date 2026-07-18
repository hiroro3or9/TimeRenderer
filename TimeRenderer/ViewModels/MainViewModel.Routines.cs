using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Media;
using System.Windows.Input;
using System.Windows.Threading;

using TimeRenderer.Models;
using TimeRenderer.Helpers;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 定期予定（ルーティン）の管理。
/// 「記録開始を忘れる」対策として、毎週決まった曜日・時刻の予定を自動生成し、
/// 開始時刻にはリマインダー通知（または設定に応じた自動記録開始）を行う。
/// </summary>
public partial class MainViewModel
{
    private List<RoutineScheduleItem> _routines = [];
    public List<RoutineScheduleItem> Routines
    {
        get => _routines;
        set
        {
            if (SetProperty(ref _routines, value))
            {
                SaveSettings();
                EnsureRoutineOccurrences(CurrentDate);
            }
        }
    }

    /// <summary>開始時刻に達し、まだユーザーの操作を待っているリマインダー対象のアイテム</summary>
    public ObservableCollection<ScheduleItem> PendingReminders { get; } = [];

    /// <summary>同一セッション内で既にリマインダー判定（通知 or 自動開始）を行ったアイテム</summary>
    private readonly HashSet<ScheduleItem> _remindedRoutineItems = [];

    private DateTime? _lastRoutineGenerationDate;

    private string? _autoStartNotice;
    /// <summary>自動記録開始が行われたことを一時的に知らせるメッセージ（数秒で自動的に消える）</summary>
    public string? AutoStartNotice
    {
        get => _autoStartNotice;
        set
        {
            if (SetProperty(ref _autoStartNotice, value))
            {
                OnPropertyChanged(nameof(HasAutoStartNotice));
            }
        }
    }

    public bool HasAutoStartNotice => !string.IsNullOrEmpty(AutoStartNotice);

    private DispatcherTimer? _autoStartNoticeTimer;

    public ICommand AddRoutineCommand { get; private set; } = null!;
    public ICommand EditRoutineCommand { get; private set; } = null!;
    public ICommand DeleteRoutineCommand { get; private set; } = null!;
    public ICommand StartReminderCommand { get; private set; } = null!;
    public ICommand DismissReminderCommand { get; private set; } = null!;

    private void InitializeRoutineCommands()
    {
        AddRoutineCommand = new RelayCommand(_ =>
        {
            var result = _dialogService.ShowRoutineEditDialog(null, [.. Categories], GetTitleSuggestions());
            if (result != null)
            {
                var list = new List<RoutineScheduleItem>(Routines) { result };
                Routines = list;
            }
        });

        EditRoutineCommand = new RelayCommand(
            param =>
            {
                if (param is RoutineScheduleItem routine)
                {
                    var result = _dialogService.ShowRoutineEditDialog(routine, [.. Categories], GetTitleSuggestions());
                    if (result != null)
                    {
                        var list = new List<RoutineScheduleItem>(Routines);
                        var index = list.FindIndex(r => r.Id == routine.Id);
                        if (index >= 0)
                        {
                            list[index] = result;
                        }
                        Routines = list;
                    }
                }
            },
            param => param is RoutineScheduleItem
        );

        DeleteRoutineCommand = new RelayCommand(
            param =>
            {
                if (param is RoutineScheduleItem routine)
                {
                    if (_dialogService.ShowConfirmationDialog(
                        $"定期予定「{routine.Title}」を削除しますか？\n（すでに生成済みの予定アイテムは残ります）", "削除確認"))
                    {
                        var list = new List<RoutineScheduleItem>(Routines);
                        list.RemoveAll(r => r.Id == routine.Id);
                        Routines = list;
                    }
                }
            },
            param => param is RoutineScheduleItem
        );

        StartReminderCommand = new RelayCommand(
            param =>
            {
                if (param is ScheduleItem item)
                {
                    PendingReminders.Remove(item);
                    // 予定アイテム自体を実績として使う（停止時に別アイテムを作らない）
                    StartRecordingFromItem(item, consumeItem: true);
                }
            },
            param => param is ScheduleItem
        );

        DismissReminderCommand = new RelayCommand(
            param =>
            {
                if (param is ScheduleItem item)
                {
                    PendingReminders.Remove(item);
                }
            },
            param => param is ScheduleItem
        );
    }

    /// <summary>
    /// 有効な定期予定について、指定日を中心とした一定期間（過去7日～先60日）に
    /// 未生成のスケジュールアイテムがあれば生成する。同一ルーティン・同一日の重複生成は行わない。
    /// ユーザーが生成済みの予定を削除した場合、その日は再生成されない（RoutineId+日付で判定するため）。
    /// </summary>
    private void EnsureRoutineOccurrences(DateTime aroundDate)
    {
        if (Routines.Count == 0) return;

        var rangeStart = aroundDate.Date.AddDays(-7);
        var rangeEnd = aroundDate.Date.AddDays(60);

        var existingKeys = ScheduleItems
            .Where(i => i.RoutineId != null)
            .Select(i => (i.RoutineId, i.StartTime.Date))
            .ToHashSet();

        var toAdd = new List<ScheduleItem>();
        foreach (var routine in Routines)
        {
            if (!routine.IsEnabled || routine.DaysOfWeek.Count == 0) continue;
            if (routine.EndTime <= routine.StartTime) continue;

            var categoryColor = routine.CategoryId != null
                ? Categories.FirstOrDefault(c => c.Id == routine.CategoryId)?.ColorCode
                : null;

            for (var date = rangeStart; date <= rangeEnd; date = date.AddDays(1))
            {
                if (!routine.DaysOfWeek.Contains(date.DayOfWeek)) continue;
                if (existingKeys.Contains((routine.Id, date))) continue;

                toAdd.Add(new ScheduleItem
                {
                    Title = routine.Title,
                    StartTime = date.Add(routine.StartTime),
                    EndTime = date.Add(routine.EndTime),
                    ColorCode = categoryColor ?? routine.ColorCode,
                    CategoryId = routine.CategoryId,
                    RoutineId = routine.Id
                });
            }
        }

        if (toAdd.Count == 0) return;

        // ロード中と同様に、1件ずつの再計算・保存を避けて最後にまとめて実行する
        var wasLoading = _isLoadingData;
        _isLoadingData = true;
        try
        {
            foreach (var item in toAdd)
            {
                ScheduleItems.Add(item);
            }
        }
        finally
        {
            _isLoadingData = wasLoading;
        }
        RecalculateLayout();
        SaveData();
    }

    /// <summary>
    /// 開始時刻に達した予定を判定し、設定に応じて自動記録開始またはリマインダー通知
    /// （バナー表示＋通知音）を行う。対象は以下の2種類：
    /// ・定期予定から生成されたアイテム（RoutineId あり）→ ルーティン側の IsAutoStart 設定に従う
    /// ・手動登録の単発予定 → アイテム自身の RemindAtStart / AutoStartRecording フラグに従う
    /// 毎tick呼ばれる想定だが、判定済みのアイテムはセッション中は再判定しない。
    /// </summary>
    private void CheckReminders(DateTime now)
    {
        // 定期予定生成のローリングウィンドウを1日1回更新する（アプリを開きっぱなしにしていても先の予定が生成され続ける）
        if (_lastRoutineGenerationDate != now.Date)
        {
            _lastRoutineGenerationDate = now.Date;
            EnsureRoutineOccurrences(now.Date);
        }

        foreach (var item in ScheduleItems)
        {
            if (item.IsAllDay) continue;
            if (_remindedRoutineItems.Contains(item)) continue;
            if (item.StartTime.Date != now.Date) continue;
            if (now < item.StartTime) continue;

            // 対象判定：定期予定由来か、通知/自動開始フラグ付きの単発予定のみ
            bool autoStart;
            bool forceStart;
            if (item.RoutineId != null)
            {
                var routine = Routines.FirstOrDefault(r => r.Id == item.RoutineId);
                autoStart = routine?.IsAutoStart == true;
                forceStart = routine?.IsForceStart == true;
            }
            else if (item.AutoStartRecording || item.RemindAtStart)
            {
                autoStart = item.AutoStartRecording;
                forceStart = item.ForceStartRecording;
            }
            else
            {
                continue; // フラグなしの通常アイテム（記録済みログ等）は対象外
            }

            _remindedRoutineItems.Add(item);

            // アプリ起動直後などで開始からかなり時間が経っている場合は通知しない（古いリマインダーの氾濫防止）
            if (now - item.StartTime > TimeSpan.FromMinutes(15)) continue;

            // 強制開始が有効なら記録中でも開始する（StartRecordingFromItem が現在の記録を停止・保存する）
            if (autoStart && (forceStart || !IsRecording))
            {
                // 予定アイテム自体を実績として使う（停止時に別アイテムを作らない）
                StartRecordingFromItem(item, consumeItem: true);
                ShowAutoStartNotice($"「{item.Title}」の記録を自動開始しました");
            }
            else if (!PendingReminders.Contains(item))
            {
                PendingReminders.Add(item);
                SystemSounds.Asterisk.Play();
            }
        }
    }

    private void ShowAutoStartNotice(string message)
    {
        AutoStartNotice = message;

        _autoStartNoticeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _autoStartNoticeTimer.Stop();
        _autoStartNoticeTimer.Tick -= AutoStartNoticeTimer_Tick;
        _autoStartNoticeTimer.Tick += AutoStartNoticeTimer_Tick;
        _autoStartNoticeTimer.Start();
    }

    private void AutoStartNoticeTimer_Tick(object? sender, EventArgs e)
    {
        _autoStartNoticeTimer?.Stop();
        AutoStartNotice = null;
    }
}
