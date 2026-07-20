using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using TimeRenderer.Models;

namespace TimeRenderer.Views.Dialogs
{
    /// <summary>
    /// 記録中に検知した離席を提示し、記録から除くかどうかを選ばせるダイアログ。
    ///
    /// 「除く」を選んだ場合、離席をまたぐ記録は複数のアイテムに分割される。
    /// 単に合計時間を引くだけだと、いつ作業していたのかが失われるため。
    /// </summary>
    public partial class AwayReviewDialog : Window
    {
        /// <summary>離席時間を除外するか（キャンセル・そのまま記録なら false）</summary>
        public bool ShouldExclude { get; private set; }

        public AwayReviewDialog(
            string recordTitle,
            DateTime recordStart,
            DateTime recordEnd,
            IReadOnlyList<AwayPeriod> awayPeriods)
        {
            InitializeComponent();

            var total = recordEnd - recordStart;
            var awayTotal = TimeSpan.FromTicks(awayPeriods.Sum(p => p.Duration.Ticks));
            var effective = total - awayTotal;
            if (effective < TimeSpan.Zero) effective = TimeSpan.Zero;

            HeadlineText.Text = $"「{recordTitle}」の記録中に離席を検知しました";

            SummaryText.Text =
                $"記録全体 {recordStart:HH:mm} 〜 {recordEnd:HH:mm}（{Format(total)}）のうち、" +
                $"{awayPeriods.Count} 件・合計 {Format(awayTotal)} が離席です。";

            AwayList.ItemsSource = awayPeriods;

            EffectText.Text =
                $"「離席時間を除く」を選ぶと、実作業 {Format(effective)} 分のみが記録されます。" +
                (awayPeriods.Count > 1 || IsSplitting(recordStart, recordEnd, awayPeriods)
                    ? "\n離席をまたぐため、記録は複数のアイテムに分割されます。"
                    : "");
        }

        /// <summary>離席が記録の途中にある（＝分割が必要）か</summary>
        private static bool IsSplitting(DateTime start, DateTime end, IReadOnlyList<AwayPeriod> periods)
            => periods.Any(p => p.Start > start && p.End < end);

        private static string Format(TimeSpan span) =>
            span.TotalHours >= 1
                ? $"{(int)span.TotalHours}時間{span.Minutes}分"
                : $"{(int)span.TotalMinutes}分";

        private void ExcludeButton_Click(object sender, RoutedEventArgs e)
        {
            ShouldExclude = true;
            DialogResult = true;
        }

        private void KeepButton_Click(object sender, RoutedEventArgs e)
        {
            ShouldExclude = false;
            DialogResult = true;
        }
    }
}
