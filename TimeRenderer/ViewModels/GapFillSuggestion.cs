using System.Collections.Generic;

using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>
/// 未記録の帯を埋めるときに、アプリ使用記録から組み立てた下書き。
///
/// 「この時間、何をしていたか」を思い出す材料（<see cref="Stats"/>）と、
/// 過去に同じアプリを使っていた時間帯へ実際に付けていたタイトル・カテゴリ
/// （<see cref="TitleSuggestions"/> / <see cref="Category"/>）をまとめて渡す。
///
/// 推測は既定値を埋めるためだけに使い、確定はしない。
/// 外していたときに黙って間違った記録が残るのが、この機能で一番避けたい失敗のため。
/// </summary>
/// <param name="Stats">その時間帯のアプリ使用内訳（使用時間の長い順）</param>
/// <param name="Title">タイトル欄の初期値</param>
/// <param name="TitleSuggestions">タイトル欄のドロップダウン候補</param>
/// <param name="Category">カテゴリ欄の初期選択（推測できなければ null）</param>
/// <param name="ProjectCode">記録へ付けるプロジェクトコードの初期選択</param>
public sealed record GapFillSuggestion(
    IReadOnlyList<AppUsageStat> Stats,
    string Title,
    IReadOnlyList<string> TitleSuggestions,
    CategoryInfo? Category,
    ProjectCodeInfo? ProjectCode);

/// <summary>
/// 未記録の帯を埋めるダイアログの結果。
/// </summary>
/// <param name="Title">記録のタイトル</param>
/// <param name="Category">選ばれたカテゴリ（未選択なら null）</param>
/// <param name="ProjectCode">選ばれたプロジェクトコード</param>
public sealed record GapFillResult(
    string Title,
    CategoryInfo? Category,
    ProjectCodeInfo? ProjectCode);
