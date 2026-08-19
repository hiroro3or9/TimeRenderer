using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using TimeRenderer.Models;
using TimeRenderer.ViewModels;

namespace TimeRenderer.Views.Dialogs
{
    /// <summary>
    /// 未記録の帯を、その時間帯に残っていた手がかりから埋めるダイアログ。
    ///
    /// 上半分は証拠（何を使っていたか・何をコミットしたか）、
    /// 下半分は確定する内容（タイトル・カテゴリ・プロジェクトコード）。
    /// 推測は既定値として入れてあるので、合っていればそのまま Enter で確定できる。
    ///
    /// 証拠は片方だけのこともある（コミットは打ったが使用アプリの収集が止まっていた等）。
    /// 空の枠を出しても読むものが無いだけなので、無いほうは畳む。
    /// </summary>
    public partial class GapFillDialog : Window
    {
        /// <summary>確定した内容（キャンセル時は null）</summary>
        public GapFillResult? Result { get; private set; }

        public GapFillDialog(
            DateTime start,
            DateTime end,
            GapFillSuggestion suggestion,
            IReadOnlyList<CategoryInfo> categories,
            IReadOnlyList<ProjectCodeInfo> projectCodes)
        {
            InitializeComponent();

            // 高DPIや小さい画面でもウィンドウ下端が作業領域の外へ出ないようにする。
            MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 24);
            Height = Math.Min(Height, MaxHeight);

            var duration = end > start ? end - start : TimeSpan.Zero;
            HeadlineText.Text = $"{start:M月d日 (ddd)} {start:H:mm} - {end:H:mm}（{Format(duration)}）";

            var tracked = TimeSpan.FromTicks(suggestion.Stats.Sum(s => s.Duration.Ticks));
            SummaryText.Text = (suggestion.HasStats, suggestion.HasCommits) switch
            {
                (true, true) =>
                    $"この時間の記録がありません。収集できた {Format(tracked)} の内訳と、" +
                    "この時間に作ったコミットが手がかりです。",
                (true, false) =>
                    $"この時間の記録がありません。収集できた {Format(tracked)} の内訳は次の通りです。",
                _ =>
                    "この時間の記録がありません。この時間に作ったコミットが手がかりです。",
            };

            UsagePanel.Visibility = suggestion.HasStats ? Visibility.Visible : Visibility.Collapsed;
            UsageList.ItemsSource = suggestion.Stats;

            CommitPanel.Visibility = suggestion.HasCommits ? Visibility.Visible : Visibility.Collapsed;
            CommitList.ItemsSource = suggestion.Commits;
            CommitHeadText.Text = $"この時間のコミット {suggestion.Commits.Count} 件";

            TitleCombo.ItemsSource = suggestion.TitleSuggestions;
            TitleCombo.Text = suggestion.Title;

            GuessNoteText.Text = (suggestion.HasCommits, suggestion.TitleSuggestions.Count > 0) switch
            {
                (true, _) =>
                    "候補の先頭はこの時間のコミットです。その後ろに、過去に同じアプリを使っていた" +
                    "時間帯へ付けていたタイトルが並びます。",
                (false, true) =>
                    "候補は、過去に同じアプリを使っていた時間帯へ付けていたタイトルです。",
                _ =>
                    "このアプリでの記録がまだ無いため、アプリ名を入れてあります。",
            };

            CategoryCombo.ItemsSource = categories;
            CategoryCombo.SelectedItem = suggestion.Category is { } guessed
                ? categories.FirstOrDefault(c => c.Id == guessed.Id)
                : null;

            ProjectCodeCombo.ItemsSource = projectCodes;
            ProjectCodeCombo.SelectedItem = suggestion.ProjectCode is { } selectedProject
                ? projectCodes.FirstOrDefault(p => p.Id == selectedProject.Id)
                : projectCodes.FirstOrDefault();

            Loaded += (_, _) =>
            {
                TitleCombo.Focus();

                // 推測が当たっていればそのまま、外れていれば打ち直せるよう全選択しておく
                if (TitleCombo.Template.FindName("PART_EditableTextBox", TitleCombo)
                    is System.Windows.Controls.TextBox textBox)
                {
                    textBox.SelectAll();
                }
            };
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            var title = (TitleCombo.Text ?? string.Empty).Trim();
            if (title.Length == 0)
            {
                // タイトル無しの記録は一覧で見分けが付かなくなるので、ここで止める
                TitleCombo.Focus();
                return;
            }

            Result = new GapFillResult(
                title,
                CategoryCombo.SelectedItem as CategoryInfo,
                ProjectCodeCombo.SelectedItem as ProjectCodeInfo);
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = null;
            DialogResult = false;
        }

        private static string Format(TimeSpan span) =>
            span.TotalHours >= 1
                ? $"{(int)span.TotalHours}時間{span.Minutes}分"
                : $"{(int)span.TotalMinutes}分";
    }
}
