using TimeRenderer.Models;
using TimeRenderer.ViewModels;

namespace TimeRenderer.Services;

/// <summary>定期予定への操作（削除・編集・時間変更）を適用する範囲</summary>
public enum RoutineScope
{
    /// <summary>その日の1件のみ</summary>
    ThisDay,
    /// <summary>定期予定全体（テンプレート）</summary>
    WholeSeries
}

public interface IDialogService
{
    /// <summary>
    /// スケジュール編集ダイアログを開き、結果のアイテムを返します。
    /// キャンセルされた場合はnullを返します。
    /// </summary>
    /// <param name="initialItem">編集対象のアイテム。新規作成時はnull</param>
    /// <param name="categories">選択可能なカテゴリ一覧</param>
    /// <param name="titleSuggestions">タイトル入力欄のドロップダウン候補</param>
    /// <returns>追加または更新されたScheduleItem。キャンセルされた場合はnull</returns>
    ScheduleItem? ShowScheduleEditDialog(ScheduleItem? initialItem = null, IReadOnlyList<CategoryInfo>? categories = null, IReadOnlyList<string>? titleSuggestions = null);

    /// <summary>
    /// 定期予定（ルーティン）編集ダイアログを開き、結果のルーティンを返します。
    /// キャンセルされた場合はnullを返します。
    /// </summary>
    /// <param name="initialRoutine">編集対象のルーティン。新規作成時はnull</param>
    /// <param name="categories">選択可能なカテゴリ一覧</param>
    /// <param name="titleSuggestions">タイトル入力欄のドロップダウン候補</param>
    /// <returns>追加または更新されたRoutineScheduleItem。キャンセルされた場合はnull</returns>
    RoutineScheduleItem? ShowRoutineEditDialog(RoutineScheduleItem? initialRoutine = null, IReadOnlyList<CategoryInfo>? categories = null, IReadOnlyList<string>? titleSuggestions = null);

    /// <summary>
    /// ToDo の編集ダイアログを開き、結果の ToDo を返します。
    /// キャンセルされた場合は null を返します。
    /// </summary>
    /// <param name="initialTodo">編集対象の ToDo。新規作成時は null</param>
    /// <param name="categories">選択可能なカテゴリ一覧</param>
    /// <param name="titleSuggestions">タイトル入力欄のドロップダウン候補</param>
    /// <param name="estimateStats">見積もり欄に添える、過去の実績の傾向</param>
    /// <returns>追加または更新された TodoItem。キャンセルされた場合は null</returns>
    TodoItem? ShowTodoEditDialog(
        TodoItem? initialTodo = null,
        IReadOnlyList<CategoryInfo>? categories = null,
        IReadOnlyList<string>? titleSuggestions = null,
        TodoEstimateStats? estimateStats = null);

    /// <summary>
    /// 未完了の ToDo から1件選ぶダイアログを開きます。
    /// キャンセルされた場合、または選択肢が無い場合は null を返します。
    /// </summary>
    /// <param name="message">何を選ぶのかの説明</param>
    /// <param name="todos">選択肢（未完了のもの）</param>
    TodoItem? ShowTodoPickerDialog(string message, IReadOnlyList<TodoItem> todos);

    /// <summary>
    /// 記録開始ダイアログを開き、入力されたタイトルと選択されたタイマーオプションを返します。
    /// キャンセルされた場合はnullを返します。
    /// </summary>
    /// <param name="defaultTitle">デフォルト表示するタイトル</param>
    /// <param name="timerOptions">タイマーオプションのリスト</param>
    /// <param name="defaultOption">デフォルト選択されるタイマーオプション</param>
    /// <returns>入力されたタイトルと選択されたタイマーオプション。キャンセル時はnull</returns>
    (string Title, TimerOption SelectedOption)? ShowRecordingStartDialog(
        string defaultTitle,
        List<TimerOption> timerOptions,
        TimerOption defaultOption,
        IReadOnlyList<string>? titleSuggestions = null);
    
    /// <summary>
    /// 確認メッセージダイアログを表示し、Yes(true)またはNo(false)を返します。
    /// </summary>
    bool ShowConfirmationDialog(string message, string title);

    /// <summary>
    /// 定期予定への操作範囲（この日のみ／定期予定全体）を確認します。
    /// キャンセルされた場合は null を返します。
    /// </summary>
    RoutineScope? ShowRoutineScopeDialog(string message, string title);

    /// <summary>
    /// 通知メッセージダイアログ（OKボタンのみ）を表示します。
    /// </summary>
    void ShowMessage(string message, string title);

    /// <summary>
    /// 出勤・退勤の編集ダイアログを開き、確定した内容を返します。
    /// キャンセルされた場合は null を返します。
    /// </summary>
    /// <param name="date">対象の勤務日</param>
    /// <param name="start">既存の出勤時刻（新規追加の場合は null）</param>
    /// <param name="end">既存の退勤時刻（未退勤・新規追加の場合は null）</param>
    /// <param name="canDelete">既存の記録を編集する場合は true（削除ボタンを表示する）</param>
    WorkDayEditResult? ShowWorkDayEditDialog(DateTime date, DateTime? start, DateTime? end, bool canDelete);

    /// <summary>
    /// 記録中に検知した離席を提示し、記録から除外するかを確認します。
    /// </summary>
    /// <returns>離席時間を除外する場合は true</returns>
    bool ShowAwayReviewDialog(
        string recordTitle,
        DateTime recordStart,
        DateTime recordEnd,
        IReadOnlyList<AwayPeriod> awayPeriods);

    /// <summary>
    /// 予定アイテムの時間帯に使っていたアプリの内訳を表示します。
    /// </summary>
    void ShowAppUsageDialog(
        string itemTitle,
        DateTime rangeStart,
        DateTime rangeEnd,
        IReadOnlyList<AppUsageStat> stats);
}
