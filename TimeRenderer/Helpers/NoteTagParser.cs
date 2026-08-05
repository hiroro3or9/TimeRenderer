using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TimeRenderer.Helpers;

/// <summary>
/// ふりかえり本文から <c>#タグ</c> を拾う。
///
/// タグ用の入力欄を別に設けず本文に混ぜて書かせるのは、
/// 書く場所が退勤時の一言（<see cref="Models.WorkDayLog.Note"/>）しか無いため。
/// 欄が増えるほど書かなくなるので、思いついたまま打てる形に寄せている。
///
/// 本文はそのまま残す。タグを取り除いた文を保存すると、
/// 書いた文章と保存された文章が食い違って気持ちが悪い。
/// </summary>
public static partial class NoteTagParser
{
    /// <summary>
    /// タグとして拾う範囲。<c>#</c> の後ろの、空白・区切り記号以外が続くかぎり。
    /// 日本語をそのまま書けるよう、文字種では絞らず「終わりの記号」で切っている。
    /// </summary>
    [GeneratedRegex(@"#([^\s#、。，．,\.!?！？:：;；「」『』（）\(\)\[\]【】]+)")]
    private static partial Regex TagRegex();

    /// <summary>
    /// 本文に含まれるタグを、書かれた順で返す（重複は除く）。
    /// <c>#</c> は含めない。
    /// </summary>
    public static IReadOnlyList<string> Extract(string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return [];

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in TagRegex().Matches(note))
        {
            var tag = match.Groups[1].Value;
            if (tag.Length == 0) continue;
            if (seen.Add(tag)) result.Add(tag);
        }

        return result;
    }

    /// <summary>本文がそのタグを含むか（大文字小文字は区別しない）</summary>
    public static bool HasTag(string? note, string tag)
    {
        foreach (var found in Extract(note))
        {
            if (string.Equals(found, tag, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
