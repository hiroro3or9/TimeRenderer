using System;
using System.Text.Json.Serialization;

using TimeRenderer.Infrastructure;

namespace TimeRenderer.Models;

/// <summary>
/// 未記録時間の加算先を期間で切り替えるための1行。
///
/// 終了日は持たず「この日からこのコード」だけを並べる。期間の終わりは次の行の開始日から
/// 導出するので、隙間や重複が原理的に起きない（案件の切り替わりは日付が1つ決まれば足りる）。
/// 最初の行より前は <see cref="AppSettings.UnrecordedTimeProjectCodeId"/> を使う。
/// </summary>
public sealed class UnrecordedTimeProjectAssignment : ObservableObject
{
    private DateTime _startDate = DateTime.Today;

    /// <summary>この加算先を使い始める日（日付部分のみ）</summary>
    public DateTime StartDate
    {
        get => _startDate;
        set => SetProperty(ref _startDate, value.Date);
    }

    private string? _projectCodeId;

    /// <summary>加算先のプロジェクトコード Id</summary>
    public string? ProjectCodeId
    {
        get => _projectCodeId;
        set => SetProperty(ref _projectCodeId, value);
    }

    private string _rangeText = string.Empty;

    /// <summary>
    /// 一覧に出す「2026/01/05 〜 2026/02/01」の文字列。
    /// 終了日が次の行に依存するため、一覧を並べ替える側 (MainViewModel) が入れ直す。
    /// </summary>
    [JsonIgnore]
    public string RangeText
    {
        get => _rangeText;
        set => SetProperty(ref _rangeText, value);
    }
}
