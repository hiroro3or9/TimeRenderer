using System;
using System.Globalization;
using System.Windows.Data;

using TimeRenderer.Models;

namespace TimeRenderer.Converters
{
    /// <summary>
    /// プロジェクトコード選択コンボの SelectedValue 用。
    /// モデル側の null（未設定）と、選択肢に並べる「（未設定）」の Id（空文字）を相互に変換する。
    ///
    /// SelectedValue に null を渡すと ComboBox は「選択なし」になり、
    /// 「（未設定）」の行が選ばれた状態にならないため空文字へ寄せている。
    /// </summary>
    public sealed class ProjectCodeIdConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value as string ?? ProjectCodeInfo.UnassignedId;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is string { Length: > 0 } id ? id : null;
    }
}
