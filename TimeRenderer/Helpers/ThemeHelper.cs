using System;
using System.Windows;
using System.Windows.Media;

namespace TimeRenderer.Helpers;

/// <summary>
/// Application.Current.Resources から指定キーのブラシを動的に取得するヘルパー
/// </summary>
public static class ThemeHelper
{
    public static System.Windows.Media.Brush GetBrush(string key, System.Windows.Media.Brush fallback)
    {
        if (System.Windows.Application.Current?.Resources[key] is System.Windows.Media.Brush b) return b;
        return fallback;
    }
}
