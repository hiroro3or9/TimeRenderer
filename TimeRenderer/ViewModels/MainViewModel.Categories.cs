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
        // 追加・削除・読み込み直しのたびに解決用の索引を作り直す
        Categories.CollectionChanged += (_, _) => InvalidateCategoryLookup();

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
            OnPropertyChanged(nameof(IsColorFilterActive));
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
                        OnPropertyChanged(nameof(IsColorFilterActive));
                        RecalculateLayout(); // 内部で UpdateStats も実行される
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
        // 色が変わるとアイテムとの紐付け（色フォールバック）も変わるため索引を捨てる
        if (e.PropertyName == nameof(CategoryInfo.ColorCode)) InvalidateCategoryLookup();

        if (_isLoadingData) return;

        // フィルタ表示状態はセッション内のみの状態のため保存せず、表示だけ更新する
        if (e.PropertyName == nameof(CategoryInfo.IsFilterEnabled))
        {
            OnPropertyChanged(nameof(IsColorFilterActive));
            RecalculateLayout();
            return;
        }

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

    /// <summary>
    /// アイテムの所属カテゴリを解決する。
    /// CategoryId（一意ID）を優先し、未設定の旧データは色コードでフォールバックする。
    /// </summary>
    public CategoryInfo? ResolveCategory(ScheduleItem item)
    {
        var (byId, byColor) = GetCategoryLookup();

        if (!string.IsNullOrEmpty(item.CategoryId) &&
            byId.TryGetValue(item.CategoryId, out var category))
        {
            return category;
        }

        return byColor.TryGetValue(item.ColorCode, out var byColorMatch) ? byColorMatch : null;
    }

    // ===== カテゴリ解決の索引 =====
    //
    // ResolveCategory はレイアウト・統計・タイムラインの各ループの内側から
    // アイテム1件につき何度も呼ばれる。以前は毎回 Categories を LINQ で
    // 2回走査していたため、件数×カテゴリ数のコストが乗っていた。
    // 索引を作って辞書引きにし、カテゴリが変化したときだけ作り直す。

    private Dictionary<string, CategoryInfo>? _categoryById;
    private Dictionary<string, CategoryInfo>? _categoryByColor;

    private (Dictionary<string, CategoryInfo> ById, Dictionary<string, CategoryInfo> ByColor) GetCategoryLookup()
    {
        if (_categoryById != null && _categoryByColor != null)
        {
            return (_categoryById, _categoryByColor);
        }

        var byId = new Dictionary<string, CategoryInfo>(Categories.Count);
        var byColor = new Dictionary<string, CategoryInfo>(Categories.Count);

        foreach (var category in Categories)
        {
            if (!string.IsNullOrEmpty(category.Id)) byId[category.Id] = category;
            // 同じ色のカテゴリが複数あるときは、先勝ち（FirstOrDefault と同じ挙動）
            if (!string.IsNullOrEmpty(category.ColorCode)) byColor.TryAdd(category.ColorCode, category);
        }

        _categoryById = byId;
        _categoryByColor = byColor;
        return (byId, byColor);
    }

    /// <summary>カテゴリの追加・削除・ID/色の変更時に索引を捨てる</summary>
    private void InvalidateCategoryLookup()
    {
        _categoryById = null;
        _categoryByColor = null;
    }

    /// <summary>
    /// 色フィルタでこのアイテムを表示するか判定する。
    /// どのカテゴリにも紐づかないアイテム（未分類）は常に表示する。
    /// </summary>
    public bool IsItemVisible(ScheduleItem item)
    {
        var category = ResolveCategory(item);
        return category == null || category.IsFilterEnabled;
    }

    /// <summary>記録機能で使う既定カテゴリ（「記録」があればそれ、なければ先頭）</summary>
    public CategoryInfo? RecordingCategory =>
        Categories.FirstOrDefault(c => c.Name == "記録") ?? Categories.FirstOrDefault();
}
