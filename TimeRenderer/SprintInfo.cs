using System;

namespace TimeRenderer;

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
}
