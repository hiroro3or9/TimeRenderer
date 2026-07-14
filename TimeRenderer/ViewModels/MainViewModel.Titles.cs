using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using TimeRenderer.Helpers;

namespace TimeRenderer.ViewModels;

/// <summary>
/// タイトル入力の候補（定型タイトル + 直近1か月に使われたタイトル）の管理。
/// </summary>
public partial class MainViewModel
{
    /// <summary>設定パネルでインライン編集できる定型タイトルの1件分</summary>
    public class TitleEntry : INotifyPropertyChanged
    {
        private string _text = string.Empty;
        public string Text
        {
            get => _text;
            set
            {
                if (_text == value) return;
                _text = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>ドロップダウンに常に表示する定型タイトル</summary>
    public ObservableCollection<TitleEntry> PinnedTitles { get; } = [];

    public ICommand AddPinnedTitleCommand { get; private set; } = null!;
    public ICommand DeletePinnedTitleCommand { get; private set; } = null!;

    private void InitializeTitleCommands()
    {
        AddPinnedTitleCommand = new RelayCommand(_ =>
        {
            var entry = new TitleEntry { Text = "新しい定型タイトル" };
            AttachPinnedTitle(entry);
            PinnedTitles.Add(entry);
            SaveSettings();
        });

        DeletePinnedTitleCommand = new RelayCommand(
            param =>
            {
                if (param is TitleEntry entry)
                {
                    entry.PropertyChanged -= OnPinnedTitleChanged;
                    PinnedTitles.Remove(entry);
                    SaveSettings();
                }
            },
            param => param is TitleEntry
        );
    }

    private void AttachPinnedTitle(TitleEntry entry)
    {
        entry.PropertyChanged -= OnPinnedTitleChanged;
        entry.PropertyChanged += OnPinnedTitleChanged;
    }

    private void OnPinnedTitleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isLoadingData) return;
        SaveSettings();
    }

    /// <summary>
    /// 設定から読み込んだ定型タイトルを反映する。
    /// null は「未設定（初回起動）」として既定値を入れ、空リストは「全削除済み」として尊重する。
    /// </summary>
    private void LoadPinnedTitles(List<string>? loaded)
    {
        foreach (var old in PinnedTitles)
        {
            old.PropertyChanged -= OnPinnedTitleChanged;
        }
        PinnedTitles.Clear();

        var source = loaded ?? ["打ち合わせ", "休憩"];
        foreach (var text in source)
        {
            var entry = new TitleEntry { Text = text };
            AttachPinnedTitle(entry);
            PinnedTitles.Add(entry);
        }
    }

    /// <summary>
    /// タイトル入力欄のドロップダウン候補を返す。
    /// 定型タイトル → 直近1か月のアイテムタイトル（新しい順・重複除外）の順。
    /// </summary>
    public IReadOnlyList<string> GetTitleSuggestions()
    {
        var result = new List<string>();
        var seen = new HashSet<string>();

        foreach (var entry in PinnedTitles)
        {
            var text = entry.Text?.Trim();
            if (!string.IsNullOrEmpty(text) && seen.Add(text))
            {
                result.Add(text);
            }
        }

        var cutoff = DateTime.Now.AddMonths(-1);
        var recent = ScheduleItems
            .Where(i => i.StartTime >= cutoff)
            .OrderByDescending(i => i.StartTime)
            .Select(i => i.Title.Trim())
            .Where(t => !string.IsNullOrEmpty(t));

        const int maxRecent = 30;
        int added = 0;
        foreach (var title in recent)
        {
            if (added >= maxRecent) break;
            if (seen.Add(title))
            {
                result.Add(title);
                added++;
            }
        }

        return result;
    }
}
