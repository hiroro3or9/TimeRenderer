using System;
using System.Text.Json.Serialization;

namespace TimeRenderer.Models;

/// <summary>
/// スプリント情報を管理するモデルクラスです。
/// </summary>
public class SprintInfo
{
    /// <summary>識別子</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>スプリントの名称 (例: "Sprint 1")</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>開始日 (日付部分のみ)</summary>
    public DateTime StartDate { get; set; }

    /// <summary>終了日 (日付部分のみ)</summary>
    public DateTime EndDate { get; set; }

    /// <summary>ユーザーにより明示的に手動登録されたスプリントかどうか</summary>
    public bool IsManual { get; set; }

    /// <summary>
    /// この期間の未記録時間を加算するプロジェクトコードの Id。
    /// null・空なら、それより前で最後に指定されたスプリントの値を引き継ぐ。
    /// </summary>
    public string? UnrecordedTimeProjectCodeId { get; set; }

    /// <summary>
    /// スプリント一覧に出す加算先の表示文字列。
    /// SprintInfo は変更通知を持たないため、一覧を作り直す側 (MainViewModel) が入れ直す。
    /// </summary>
    [JsonIgnore]
    public string UnrecordedTimeProjectCodeLabel { get; set; } = string.Empty;
}
