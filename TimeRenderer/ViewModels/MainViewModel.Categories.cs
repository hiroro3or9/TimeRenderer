using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

using TimeRenderer.Models;
using TimeRenderer.Helpers;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 作業カテゴリ（名前付きの色）の管理。
/// </summary>
public partial class MainViewModel
{
    public record PaletteColor(string Name, string Code)
    {
        public Brush Brush { get; } = CategoryInfo.CreateBrush(Code);
    }

    /// <summary>カテゴリに割り当て可能な色パレット</summary>
    public static List<PaletteColor> PaletteColors { get; } =
    [
        new("ライトブルー", Brushes.LightBlue.ToString()),
        new("ライトグリーン", Brushes.LightGreen.ToString()),
        new("ライトピンク", Brushes.LightPink.ToString()),
        new("ライトイエロー", Brushes.LightYellow.ToString()),
        new("ライトグレー", Brushes.LightGray.ToString()),
        new("ライトコーラル", Brushes.LightCoral.ToString()),
        new("ラベンダー", Brushes.Lavender.ToString()),
        new("ライトシアン", Brushes.LightCyan.ToString()),
        new("ダークオレンジ", Brushes.DarkOrange.ToString()),
        new("ライトサーモン", Brushes.LightSalmon.ToString()),
        new("カーキ", Brushes.Khaki.ToString()),
        new("プラム", Brushes.Plum.ToString()),
        new("パウダーブルー", Brushes.PowderBlue.ToString()),
        new("ミントクリーム", Brushes.Aquamarine.ToString()),
        new("ウィート", Brushes.Wheat.ToString()),
        new("シルバー", Brushes.Silver.ToString()),
    ];

    public ObservableCollection<CategoryInfo> Categories { get; } = [];

    public ICommand AddCategoryCommand { get; private set; } = null!;
    public ICommand DeleteCategoryCommand { get; private set; } = null!;

    private void InitializeCategoryCommands()
    {
        AddCategoryCommand = new RelayCommand(_ =>
        {
            // まだ使われていないパレット色を優先して割り当てる
            var used = Categories.Select(c => c.ColorCode).ToHashSet();
            var color = PaletteColors.FirstOrDefault(p => !used.Contains(p.Code)) ?? PaletteColors[0];
            var category = new CategoryInfo { Name = "新しいカテゴリ", ColorCode = color.Code };
            AttachCategory(category);
            Categories.Add(category);
            SaveSettings();
            UpdateStats();
        });

        DeleteCategoryCommand = new RelayCommand(
            param =>
            {
                if (param is CategoryInfo category && Categories.Count > 1)
                {
                    if (_dialogService.ShowConfirmationDialog(
                        $"カテゴリ「{category.Name}」を削除しますか？\n（この色を使っている既存の記録は残ります）", "削除確認"))
                    {
                        category.PropertyChanged -= OnCategoryPropertyChanged;
                        Categories.Remove(category);
                        SaveSettings();
                        UpdateStats();
                    }
                }
            },
            param => param is CategoryInfo && Categories.Count > 1
        );
    }

    private void AttachCategory(CategoryInfo category)
    {
        category.PropertyChanged -= OnCategoryPropertyChanged;
        category.PropertyChanged += OnCategoryPropertyChanged;
    }

    private void OnCategoryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isLoadingData) return;
        SaveSettings();
        UpdateStats();
    }

    /// <summary>設定から読み込んだカテゴリを反映する（空なら既定値）</summary>
    private void LoadCategories(List<CategoryInfo>? loaded)
    {
        foreach (var old in Categories)
        {
            old.PropertyChanged -= OnCategoryPropertyChanged;
        }
        Categories.Clear();

        var source = (loaded == null || loaded.Count == 0) ? CategoryInfo.CreateDefaults() : loaded;
        foreach (var category in source)
        {
            AttachCategory(category);
            Categories.Add(category);
        }
    }

    /// <summary>カラーコードからカテゴリ名を引く（未登録の色は「未分類」）</summary>
    public string GetCategoryName(string colorCode)
    {
        var category = Categories.FirstOrDefault(c => c.ColorCode == colorCode);
        return category?.Name ?? "未分類";
    }

    /// <summary>記録機能で使う既定カテゴリ（「記録」があればそれ、なければ先頭）</summary>
    public CategoryInfo? RecordingCategory =>
        Categories.FirstOrDefault(c => c.Name == "記録") ?? Categories.FirstOrDefault();
}
