using System;
using System.Globalization;
using System.Windows;

using TimeRenderer.Models;

namespace TimeRenderer.Views.Dialogs
{
    /// <summary>
    /// 出勤・退勤の時刻をあとから編集するダイアログ。
    /// 押し忘れた日の新規追加と、記録済みの日の削除も同じ画面から行える。
    /// </summary>
    public partial class WorkDayEditDialog : Window
    {
        /// <summary>時刻の入力揺れを吸収する（"9:5" や "0905" も受ける）</summary>
        private static readonly string[] TimeFormats = ["H:m", "H:mm", "HH:mm", "Hmm", "HHmm"];

        public DateTime? SelectedDate { get; set; }
        public string StartText { get; set; } = string.Empty;
        public string EndText { get; set; } = string.Empty;

        /// <summary>確定した内容（キャンセル時は null）</summary>
        public WorkDayEditResult? Result { get; private set; }

        /// <param name="date">対象の勤務日</param>
        /// <param name="start">既存の出勤時刻（新規追加時は null）</param>
        /// <param name="end">既存の退勤時刻</param>
        /// <param name="canDelete">既存の記録を編集している場合は true（削除ボタンを出す）</param>
        public WorkDayEditDialog(DateTime date, DateTime? start, DateTime? end, bool canDelete)
        {
            InitializeComponent();

            SelectedDate = date.Date;
            StartText = start?.ToString("H:mm", CultureInfo.InvariantCulture) ?? "9:00";
            EndText = end?.ToString("H:mm", CultureInfo.InvariantCulture) ?? string.Empty;

            DeleteButton.Visibility = canDelete ? Visibility.Visible : Visibility.Collapsed;
            Title = canDelete ? "勤務時間の編集" : "勤務時間の追加";

            DataContext = this;

            Loaded += (_, _) =>
            {
                StartTextBox.Focus();
                StartTextBox.SelectAll();
            };
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedDate is not DateTime date)
            {
                ShowError("勤務日を選んでください。");
                return;
            }
            date = date.Date;

            if (!TryParseTime(StartText, out var startOfDay))
            {
                ShowError("出勤時刻を 9:05 の形式で入力してください。");
                return;
            }

            var start = date + startOfDay;
            DateTime? end = null;

            if (!string.IsNullOrWhiteSpace(EndText))
            {
                if (!TryParseTime(EndText, out var endOfDay))
                {
                    ShowError("退勤時刻を 18:30 の形式で入力してください。");
                    return;
                }

                // 深夜勤務（22:00→2:00）は翌日として扱う。
                // 同時刻の場合も「0分の勤務」より翌日と解釈するほうが自然
                var candidate = date + endOfDay;
                end = candidate > start ? candidate : candidate.AddDays(1);

                if (end.Value - start > TimeSpan.FromHours(24))
                {
                    ShowError("勤務時間が24時間を超えています。時刻を確認してください。");
                    return;
                }
            }
            else if (date != DateTime.Today)
            {
                // 過去日を未退勤のままにすると、日付またぎの自動締めで
                // 意図しない時刻が入ってしまう
                ShowError("過去の日は退勤時刻が必要です。");
                return;
            }

            Result = new WorkDayEditResult(IsDeleted: false, date, start, end);
            DialogResult = true;
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var date = (SelectedDate ?? DateTime.Today).Date;
            Result = new WorkDayEditResult(IsDeleted: true, date, date, null);
            DialogResult = true;
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        private static bool TryParseTime(string text, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            var normalized = (text ?? string.Empty).Trim().Replace('：', ':');
            if (normalized.Length == 0) return false;

            if (!DateTime.TryParseExact(normalized, TimeFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
            {
                return false;
            }

            time = parsed.TimeOfDay;
            return true;
        }
    }
}
