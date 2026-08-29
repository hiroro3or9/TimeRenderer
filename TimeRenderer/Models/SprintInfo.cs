using System;

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
    /// 旧形式：この期間の未記録時間を加算するプロジェクトコードの Id。
    /// 加算先は AppSettings.UnrecordedTimeProjectAssignments へ移したため、
    /// 保存済みの設定を一度だけ取り込むためだけに残している。新規に書き込むことはない。
    /// </summary>
    public string? UnrecordedTimeProjectCodeId { get; set; }
}
