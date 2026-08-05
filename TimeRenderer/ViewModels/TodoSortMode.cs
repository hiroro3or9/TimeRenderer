namespace TimeRenderer.ViewModels;

/// <summary>
/// ToDo パネルの並べ替え方。
/// 数値で保存されるため、既存の値の割り当ては変更しないこと。
/// </summary>
public enum TodoSortMode
{
    /// <summary>期限が近い順（期限なしは末尾）</summary>
    DueDate = 0,

    /// <summary>優先度が高い順</summary>
    Priority = 1,

    /// <summary>追加した順</summary>
    Created = 2,

    /// <summary>手動（ドラッグや Ctrl+↑↓ で決めた順）</summary>
    Manual = 3,
}

/// <summary>ToDo の並べ替えドロップダウン1項目</summary>
public record TodoSortOption(TodoSortMode Mode, string Label);
