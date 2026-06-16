namespace TimeRenderer.Services;

public interface IDialogService
{
    /// <summary>
    /// スケジュール編集ダイアログを開き、結果のアイテムを返します。
    /// キャンセルされた場合はnullを返します。
    /// </summary>
    /// <param name="initialItem">編集対象のアイテム。新規作成時はnull</param>
    /// <returns>追加または更新されたScheduleItem。キャンセルされた場合はnull</returns>
    ScheduleItem? ShowScheduleEditDialog(ScheduleItem? initialItem = null);

    /// <summary>
    /// 記録開始ダイアログを開き、入力されたタイトルと選択されたタイマーオプションを返します。
    /// キャンセルされた場合はnullを返します。
    /// </summary>
    /// <param name="defaultTitle">デフォルト表示するタイトル</param>
    /// <param name="timerOptions">タイマーオプションのリスト</param>
    /// <param name="defaultOption">デフォルト選択されるタイマーオプション</param>
    /// <returns>入力されたタイトルと選択されたタイマーオプション。キャンセル時はnull</returns>
    (string Title, MainViewModel.TimerOption SelectedOption)? ShowRecordingStartDialog(
        string defaultTitle,
        List<MainViewModel.TimerOption> timerOptions,
        MainViewModel.TimerOption defaultOption);
    
    /// <summary>
    /// 確認メッセージダイアログを表示し、Yes(true)またはNo(false)を返します。
    /// </summary>
    bool ShowConfirmationDialog(string message, string title);
}
