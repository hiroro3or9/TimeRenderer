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
    /// テキスト入力ダイアログを開き、入力されたテキストを返します。
    /// キャンセルされた場合はnullを返します。
    /// </summary>
    /// <returns>入力テキスト。キャンセルされたり空の場合はnull</returns>
    string? ShowTextInputDialog();
    
    /// <summary>
    /// 確認メッセージダイアログを表示し、Yes(true)またはNo(false)を返します。
    /// </summary>
    bool ShowConfirmationDialog(string message, string title);
}
