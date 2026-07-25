using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using TimeRenderer.ViewModels;

namespace TimeRenderer.Views.Dialogs
{
    /// <summary>
    /// 予定アイテムの時間帯に使っていたアプリの内訳を表示するダイアログ。
    /// 表示するだけで、記録には一切手を加えない。
    /// </summary>
    public partial class AppUsageDialog : Window
    {
        public AppUsageDialog(
            string itemTitle,
            DateTime rangeStart,
            DateTime rangeEnd,
            IReadOnlyList<AppUsageStat> stats)
        {
            InitializeComponent();

            HeadlineText.Text = $"「{itemTitle}」の使用アプリ";

            var tracked = TimeSpan.FromTicks(stats.Sum(s => s.Duration.Ticks));
            SummaryText.Text =
                $"{rangeStart:M/d HH:mm} 〜 {rangeEnd:HH:mm} のうち、" +
                $"収集できた {Format(tracked)} の内訳（{stats.Count} アプリ）";

            UsageList.ItemsSource = stats;
        }

        private static string Format(TimeSpan span) =>
            span.TotalHours >= 1
                ? $"{(int)span.TotalHours}時間{span.Minutes}分"
                : $"{(int)span.TotalMinutes}分";

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
