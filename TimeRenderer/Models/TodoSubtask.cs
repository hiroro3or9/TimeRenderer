using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TimeRenderer.Models;

/// <summary>
/// ToDo を分解した1手順。
///
/// 意図的に「タイトルと完了」だけにしている。
/// 期限・見積もり・記録・通知はすべて親（<see cref="TodoItem"/>）が持つため、
/// 一覧・終日行のチップ・検索・まとめ通知といった既存の処理は
/// サブタスクの存在を意識しなくてよい。
/// </summary>
public sealed class TodoSubtask : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    private bool _isCompleted;
    public bool IsCompleted
    {
        get => _isCompleted;
        set => SetProperty(ref _isCompleted, value);
    }

    /// <summary>
    /// 同じ内容の別インスタンスを作る。
    /// 取り消し履歴は「変更前の状態」を持つ必要があるが、
    /// 参照をそのまま持つと中身を書き換えたときに履歴側も一緒に変わってしまう。
    /// </summary>
    public TodoSubtask Clone() => new()
    {
        Id = Id,
        Title = Title,
        IsCompleted = IsCompleted,
    };

    /// <summary>取り消しの判定用（Id・タイトル・完了がすべて同じか）</summary>
    public bool IsSameAs(TodoSubtask other) =>
        Id == other.Id && Title == other.Title && IsCompleted == other.IsCompleted;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
