using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using TimeRenderer.Models;
using TimeRenderer.ViewModels;

namespace TimeRenderer.Views.Dialogs
{
    /// <summary>
    /// 退勤時のふりかえりダイアログ。
    ///
    /// 今日の実績を見せたうえで、片付かなかった ToDo を明日へ送るかどうかだけを聞く。
    /// 既定は「全部送る」にしてある。退勤時に一件ずつ選ばせるのは重く、
    /// そのまま Enter で確定できるほうが、この場面では正しく働くことが多いため。
    /// </summary>
    public partial class WorkEndReviewDialog : Window
    {
        /// <summary>明日へ送ることになった ToDo（閉じただけなら空）</summary>
        public IReadOnlyList<TodoItem> CarriedOver { get; private set; } = [];

        private readonly List<WorkEndCarryOver> _candidates;

        public WorkEndReviewDialog(
            DateTime date,
            DateTime start,
            DateTime end,
            TimeSpan recorded,
            int completedCount,
            IReadOnlyList<WorkEndCarryOver> candidates)
        {
            InitializeComponent();

            _candidates = [.. candidates];

            HeadlineText.Text = $"{date:M月d日 (ddd)} おつかれさまでした";

            var worked = end - start;
            WorkText.Text = Format(worked);
            WorkRangeText.Text = $"{start:H:mm} - {end:H:mm}";

            RecordedText.Text = recorded > TimeSpan.Zero ? Format(recorded) : "なし";
            RecordedRatioText.Text = worked > TimeSpan.Zero && recorded > TimeSpan.Zero
                ? $"勤務の {recorded.TotalMinutes / worked.TotalMinutes * 100:0}%"
                : string.Empty;

            CompletedText.Text = $"{completedCount} 件";

            if (_candidates.Count == 0)
            {
                ListPanel.Visibility = Visibility.Collapsed;
                ToggleAllButton.Visibility = Visibility.Collapsed;
                EmptyPanel.Visibility = Visibility.Visible;
                CarryOverHeadText.Text = "明日へ送るもの";
                EmptyText.Text = "今日やると決めたものは、すべて片付いています。";
            }
            else
            {
                CarryOverList.ItemsSource = _candidates;
                CarryOverHeadText.Text = $"片付かなかった ToDo が {_candidates.Count} 件あります";
            }

            UpdateButtons();
            Loaded += (_, _) => CarryOverButton.Focus();
        }

        private void CarryOverCheck_Changed(object sender, RoutedEventArgs e) => UpdateButtons();

        /// <summary>
        /// 選択の数に合わせてボタンの文言を変える。
        /// 「N 件を明日へ」と出しておかないと、何件送られるのか押すまで分からない。
        /// </summary>
        private void UpdateButtons()
        {
            // InitializeComponent 中の Checked では、まだ他の要素が作られていない
            if (CarryOverButton == null) return;

            var selected = _candidates.Count(c => c.IsSelected);

            // 候補が無い日は確定するものが無いので、ただ閉じるボタンにする。
            // 候補があるのに1件も選んでいない場合は「そのまま閉じる」と同じなので押させない
            if (_candidates.Count == 0)
            {
                CarryOverButton.Content = "閉じる";
                CarryOverButton.IsEnabled = true;
            }
            else
            {
                CarryOverButton.Content = selected > 0 ? $"{selected} 件を明日へ" : "明日へ送る";
                CarryOverButton.IsEnabled = selected > 0;
            }

            if (ToggleAllButton != null && _candidates.Count > 0)
            {
                ToggleAllButton.Content = selected == _candidates.Count ? "すべて外す" : "すべて選ぶ";
            }
        }

        private void ToggleAllButton_Click(object sender, RoutedEventArgs e)
        {
            var selectAll = _candidates.Count(c => c.IsSelected) < _candidates.Count;
            foreach (var candidate in _candidates)
            {
                candidate.IsSelected = selectAll;
            }

            // IsSelected は変更通知を持たないため、チェックの見た目は張り直して反映する
            CarryOverList.ItemsSource = null;
            CarryOverList.ItemsSource = _candidates;

            UpdateButtons();
        }

        private void CarryOverButton_Click(object sender, RoutedEventArgs e)
        {
            CarriedOver = [.. _candidates.Where(c => c.IsSelected).Select(c => c.Todo)];
            DialogResult = true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CarriedOver = [];
            DialogResult = false;
        }

        private static string Format(TimeSpan span) =>
            span.TotalHours >= 1
                ? $"{(int)span.TotalHours}時間{span.Minutes}分"
                : $"{(int)span.TotalMinutes}分";
    }
}
