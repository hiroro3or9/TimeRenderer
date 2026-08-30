using System;
using System.Linq;

using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>ToDo と作業記録の連動。</summary>
public partial class MainViewModel
{
    /// <summary>
    /// 記録中の ToDo。停止時にその記録時間をこの ToDo へ積算する。
    /// 予定アイテムからの記録（_recordingSourceItem）とは併存しない。
    /// </summary>
    private TodoItem? _recordingTodo;

    /// <summary>
    /// ToDo のタイトル・色で記録を開始する。記録中だった場合は現在の記録を保存してから始める。
    /// 停止時には実績として通常の記録アイテムが作られ、あわせて ToDo に時間が積算される。
    /// </summary>
    private void StartRecordingFromTodo(TodoItem todo)
    {
        if (IsRecording)
        {
            ToggleRecording(); // 現在の記録を停止・保存（この中で _recordingTodo も精算される）
        }

        RecordingTitle = todo.Title;
        _recordingColorCode = todo.ColorCode;
        _recordingCategoryId = todo.CategoryId ?? ResolveCategory(todo.CategoryId, todo.ColorCode)?.Id;
        _recordingProjectCodeId = DefaultProjectCode?.Id;
        _recordingSourceItem = null;
        _recordingTodo = todo;

        BeginRecording(useSelectedTimer: false);
    }

    /// <summary>
    /// ID から ToDo を引く。見つからなければ null（削除済みの ToDo を指したまま残った予定など）。
    /// </summary>
    private TodoItem? FindTodoById(string? id) =>
        string.IsNullOrEmpty(id) ? null : Todos.FirstOrDefault(t => t.Id == id);
}
