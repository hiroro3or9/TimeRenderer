using System;

namespace TimeRenderer.Models;

/// <summary>
/// リポジトリから読んだコミット1件。
///
/// 保存はしない。必要になったときにその場で読み直す。
/// 写しを持つと、リベースや取り消しで消えたコミットが
/// TimeRenderer の中にだけ残り続けることになる。
/// </summary>
/// <param name="RepositoryId">読み出し元のリポジトリ（プロジェクトコードの推測に使う）</param>
/// <param name="RepositoryName">読み出し元の表示名</param>
/// <param name="CommittedAt">作成日時（author date。リベースで動かないほう）</param>
/// <param name="ShortHash">短縮ハッシュ</param>
/// <param name="Subject">コミットメッセージの1行目</param>
public sealed record GitCommit(
    string RepositoryId,
    string RepositoryName,
    DateTime CommittedAt,
    string ShortHash,
    string Subject)
{
    public string TimeText => CommittedAt.ToString("H:mm");

    /// <summary>一覧の右側に出す出所（リポジトリ名と短縮ハッシュ）</summary>
    public string OriginText => $"{RepositoryName} · {ShortHash}";
}
