using System;
using System.Collections.Generic;

using TimeRenderer.Models;

namespace TimeRenderer.ViewModels;

/// <summary>
/// ドラッグ中の吸着（マグネット）。
///
/// 刻み幅への丸めはビュー側が行い、こちらは「どこへ吸い付けるか」の候補だけを出す。
/// 候補を VM が持つのは、隣接する予定・勤務時間・現在時刻という
/// 別々の場所にあるデータをまとめて知っているのが VM だけのため。
/// </summary>
public partial class MainViewModel
{
    private bool _isMagnetSnapEnabled = true;

    /// <summary>
    /// ドラッグ中に隣接する予定の端・勤務時間・現在時刻へ吸い付けるか。
    ///
    /// 吸着は刻み幅より優先されるため、常に格子どおりに置きたい人のために切れるようにする。
    /// ドラッグ中に Alt を押している間も一時的に無効になる。
    /// </summary>
    public bool IsMagnetSnapEnabled
    {
        get => _isMagnetSnapEnabled;
        set
        {
            if (SetProperty(ref _isMagnetSnapEnabled, value))
            {
                SaveSettings();
            }
        }
    }

    /// <summary>
    /// 指定日の吸着先を集める。ドラッグ中のアイテム自身は候補から外す
    /// （自分の端に自分が吸い付いて動かなくなるのを防ぐ）。
    /// </summary>
    public IReadOnlyList<DateTime> GetSnapTargets(DateTime date, ScheduleItem? exclude)
    {
        // 日別インデックスは描画直前まで遅延されることがあるため、ここで確定させる
        FlushPendingLayout();

        var day = date.Date;
        var targets = new List<DateTime>();

        if (DailyScheduleItems.TryGetValue(day, out var items))
        {
            foreach (var item in items)
            {
                if (item.IsAllDay) continue;
                if (ReferenceEquals(item, exclude)) continue;

                // 日をまたぐアイテムは、その日に見えている側の端だけを候補にする
                if (item.StartTime.Date == day) targets.Add(item.StartTime);
                if (item.EndTime.Date == day) targets.Add(item.EndTime);
            }
        }

        // 出勤・退勤のライン（作業記録を勤務時間へ揃えたい場面が多い）
        if (FindLogByDate(day) is { } log)
        {
            targets.Add(log.StartTime);
            if (log.EndTime is { } workEnd) targets.Add(workEnd);
        }

        // 現在時刻（秒は落とす。1秒ずれた位置へ吸い付くと刻み幅の意味が無くなる）
        var now = DateTime.Now;
        if (now.Date == day)
        {
            targets.Add(new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0));
        }

        return targets;
    }
}
