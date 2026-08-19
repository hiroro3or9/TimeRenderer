using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

using TimeRenderer.Models;

namespace TimeRenderer.Services;

/// <summary>
/// ローカルリポジトリのコミット履歴を読む。
///
/// 使用アプリの記録は「Visual Studio を42分」までは言えるが、
/// <b>何をしていたか</b>は言わない。コミットメッセージはそこを埋める。
/// 未記録の帯を埋めるときも、退勤時にその日を思い出すときも、材料はこれが一番強い。
///
/// 方針:
/// - <b>読むだけ</b>。<c>git log</c> と <c>git config --get</c> しか実行しない。
///   作業ツリーにも索引にも触れないので、手元の git 操作と衝突しない
/// - <b>自分のコミットだけ</b>。<c>--all</c> で全ブランチを見るため、
///   絞らないと fetch してきた他人のコミットが混ざる
/// - <b>マージコミットは除く</b>。「Merge branch ...」は作業内容を何も語らない
/// - <b>止まらない</b>。git が無い・パスが消えた・応答しない、のいずれでも
///   空の結果を返して黙る。手がかりが1つ減るだけで、記録そのものは付けられる
/// - <b>保存しない</b>。呼ばれるたびに読み直す。写しを持つと、
///   リベースで消えたコミットが TimeRenderer の中にだけ残る
///
/// UI スレッドから同期で呼ばれる前提で、リポジトリ1つあたりと全体の両方に
/// 時間の上限を設けている。数秒とはいえ画面が固まるので、
/// 呼ぶのは「ユーザーが操作した直後」（穴埋め・退勤）に限ること。
/// </summary>
public sealed class GitCommitReader
{
    /// <summary>1リポジトリあたりの待ち時間の上限</summary>
    private static readonly TimeSpan PerRepositoryTimeout = TimeSpan.FromSeconds(3);

    /// <summary>全リポジトリ合計の待ち時間の上限。超えたら残りは諦める</summary>
    private static readonly TimeSpan TotalBudget = TimeSpan.FromSeconds(8);

    /// <summary>1リポジトリから取り出す最大件数（多すぎると読む気が失せる）</summary>
    private const int MaxCommitsPerRepository = 100;

    /// <summary>
    /// 問い合わせの前後に足す余裕。
    /// <c>--since</c>／<c>--until</c> はコミット日時で絞るが、こちらが見たいのは作成日時。
    /// リベースするとこの2つはずれるため、広めに取り出してから作成日時で絞り直す。
    /// </summary>
    private static readonly TimeSpan QueryPadding = TimeSpan.FromDays(1);

    /// <summary>各項目の区切り。コミットメッセージに出てこない制御文字を使う</summary>
    private const char FieldSeparator = '\u001f';

    /// <summary>git が使えるか。初回の確認結果を覚えておく（毎回起動すると重い）</summary>
    private bool? _isGitAvailable;

    /// <summary>リポジトリごとの user.email。セッション中は変わらないものとして扱う</summary>
    private readonly Dictionary<string, string> _authorCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git コマンドが使えるか。使えない環境では設定画面でその旨を出す。
    /// </summary>
    public bool IsGitAvailable => _isGitAvailable ??= CheckGitAvailable();

    /// <summary>
    /// 指定した期間に作られたコミットを、新しい順に返す。
    /// 読めなかったリポジトリは黙って飛ばす。
    /// </summary>
    /// <param name="repositories">対象のリポジトリ（無効なものは呼び出し側で除いておく）</param>
    /// <param name="from">開始（この時刻を含む）</param>
    /// <param name="to">終了（この時刻を含まない）</param>
    public IReadOnlyList<GitCommit> Read(
        IReadOnlyList<GitRepositoryInfo> repositories, DateTime from, DateTime to)
    {
        if (repositories.Count == 0 || to <= from) return [];
        if (!IsGitAvailable) return [];

        var result = new List<GitCommit>();
        var watch = Stopwatch.StartNew();

        foreach (var repository in repositories)
        {
            // 使い切ったら残りは諦める。全部そろわなくても、出せるものは出したほうがよい
            if (watch.Elapsed >= TotalBudget) break;

            if (string.IsNullOrWhiteSpace(repository.Path)) continue;
            if (!System.IO.Directory.Exists(repository.Path)) continue;

            result.AddRange(ReadRepository(repository, from, to));
        }

        result.Sort((a, b) => b.CommittedAt.CompareTo(a.CommittedAt));
        return result;
    }

    /// <summary>
    /// パスがリポジトリとして扱えるか。リポジトリの追加時に確認する。
    /// ワークツリーやサブモジュールでは .git がファイルになるため、種類は問わない。
    /// </summary>
    public static bool LooksLikeRepository(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            if (!System.IO.Directory.Exists(path)) return false;

            var gitPath = System.IO.Path.Combine(path, ".git");
            return System.IO.Directory.Exists(gitPath) || System.IO.File.Exists(gitPath);
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private List<GitCommit> ReadRepository(GitRepositoryInfo repository, DateTime from, DateTime to)
    {
        var commits = new List<GitCommit>();

        var arguments = new List<string>
        {
            "-C", repository.Path,
            "log",
            "--all",          // ブランチを切り替えていても、その日の作業を取りこぼさない
            "--no-merges",
            $"-n{MaxCommitsPerRepository}",
            $"--since={FormatForGit(from - QueryPadding)}",
            $"--until={FormatForGit(to + QueryPadding)}",
            "--pretty=tformat:%h%x1f%aI%x1f%s",
        };

        // 自分のコミットだけに絞る。取れなければ絞らない（個人のリポジトリなら実害は無い）
        var author = GetAuthorEmail(repository.Path);
        if (author.Length > 0) arguments.Add($"--author={author}");

        if (!TryRunGit(arguments, PerRepositoryTimeout, out var output)) return commits;

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0) continue;

            var parts = trimmed.Split(FieldSeparator);
            if (parts.Length < 3) continue;

            if (!DateTimeOffset.TryParse(
                    parts[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var committedAt))
            {
                continue;
            }

            // git は元のタイムゾーン付きで返すので、こちらの時刻に直してから期間を判定する
            var localTime = committedAt.ToLocalTime().DateTime;
            if (localTime < from || localTime >= to) continue;

            var subject = parts[2].Trim();
            if (subject.Length == 0) continue;

            commits.Add(new GitCommit(
                repository.Id, repository.DisplayName, localTime, parts[0].Trim(), subject));
        }

        return commits;
    }

    private string GetAuthorEmail(string path)
    {
        if (_authorCache.TryGetValue(path, out var cached)) return cached;

        var email = TryRunGit(
            ["-C", path, "config", "--get", "user.email"], PerRepositoryTimeout, out var output)
            ? output.Trim()
            : string.Empty;

        _authorCache[path] = email;
        return email;
    }

    private static bool CheckGitAvailable()
    {
        return TryRunGit(["--version"], TimeSpan.FromSeconds(3), out var output)
               && output.Contains("git", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// git を1回動かして標準出力を受け取る。
    /// 失敗・タイムアウト・git 不在のいずれでも false を返し、例外は外へ出さない。
    /// </summary>
    private static bool TryRunGit(IReadOnlyList<string> arguments, TimeSpan timeout, out string output)
    {
        output = string.Empty;

        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null) return false;

            // 標準エラーは読み捨てるが、読まないままだとバッファが詰まって
            // 標準出力の読み取りごと止まる。非同期で流し続ける
            process.ErrorDataReceived += static (_, _) => { };
            process.BeginErrorReadLine();

            output = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* 既に終わっていた */ }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            // git が入っていない環境が主。手がかりが1つ減るだけなので、記録には影響させない
            Debug.WriteLine($"git command failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>git が確実に解釈できる形（ローカル時刻の ISO 8601）に整える</summary>
    private static string FormatForGit(DateTime value) =>
        value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
}
