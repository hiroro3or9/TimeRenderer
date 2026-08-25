using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// WinForms にも同名の型があるため、WPF 側を明示する
using Control = System.Windows.Controls.Control;
using TextBox = System.Windows.Controls.TextBox;
using Panel = System.Windows.Controls.Panel;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using Keyboard = System.Windows.Input.Keyboard;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using KeyboardFocusChangedEventArgs = System.Windows.Input.KeyboardFocusChangedEventArgs;

using TimeRenderer.Models;

namespace TimeRenderer.Views
{
    /// <summary>
    /// 日/週ビューの、バー上でのタイトル直接入力。
    ///
    /// 枠を引いた直後にそのまま名前を打てるようにするための入力欄を、
    /// 描画面へ実行時に足す。描画面は DataTemplate の中にあり x:Name で参照できないため、
    /// XAML 側に <see cref="ScheduleSurfaceTag"/> を Tag として付けて見つける。
    ///
    /// 確定・取り消しの意味づけ:
    /// - Enter / 入力欄から外れる: 確定
    /// - Ctrl+Enter: 確定したうえで編集ダイアログを開く（カテゴリやメモを続けて設定する）
    /// - Esc: 取り消し（新規なら作りかけのアイテムごと取り除く）
    /// </summary>
    public partial class DayWeekView
    {
        /// <summary>描画面（DataTemplate 内の Grid）を見分けるための目印</summary>
        internal const string ScheduleSurfaceTag = "ScheduleSurface";

        /// <summary>入力欄の高さ。15分の予定でも文字が潰れない高さに固定する</summary>
        private const double InlineEditorHeight = 26.0;

        private TextBox? _inlineEditor;
        private ScheduleItem? _inlineItem;
        private bool _inlineIsNew;
        private ItemSnapshot? _inlineBefore;

        /// <summary>閉じる処理の再入防止（入力欄を外すと LostKeyboardFocus が走るため）</summary>
        private bool _inlineClosing;

        /// <summary>確定後に編集ダイアログまで開くか（Ctrl+Enter）</summary>
        private bool _inlineOpenDialog;

        /// <summary>インライン入力中か（背景ドラッグ等で確定させたいときの判定用）</summary>
        private bool IsInlineEditing => _inlineEditor != null;

        // ===== 開始 =====

        /// <summary>指定の時間帯に空のアイテムを作り、その場でタイトルを打てるようにする</summary>
        private void StartInlineCreate(Grid surface, DateTime start, DateTime end)
        {
            CloseInlineEditor(commit: true);

            var item = ViewModel.BeginInlineCreate(start, end);

            _inlineIsNew = true;
            _inlineBefore = null;

            if (!OpenInlineEditor(surface, item, string.Empty))
            {
                // 位置を決められない場合は空のアイテムを残さず、従来どおりダイアログへ回す
                ViewModel.CancelInlineCreate(item);
                _inlineIsNew = false;
                if (ViewModel.AddScheduleItemInRangeCommand.CanExecute((start, end)))
                {
                    ViewModel.AddScheduleItemInRangeCommand.Execute((start, end));
                }
            }
        }

        /// <summary>既存アイテムのタイトルをその場で書き換える（F2）</summary>
        private void StartInlineRename(ScheduleItem item)
        {
            // 終日アイテムは時間グリッド上に位置を持たない。
            // 定期予定の仮想アイテムは「この日のみ／全体」の確認が要る。どちらもダイアログへ回す
            if (item.IsAllDay || item.IsVirtual)
            {
                ExecuteEdit(item);
                return;
            }

            var surface = FindScheduleSurface();
            if (surface == null)
            {
                ExecuteEdit(item);
                return;
            }

            CloseInlineEditor(commit: true);

            _inlineIsNew = false;
            _inlineBefore = ViewModel.BeginInlineRename(item);

            if (!OpenInlineEditor(surface, item, item.Title))
            {
                _inlineBefore = null;
                ExecuteEdit(item);
            }
        }

        private void ExecuteEdit(ScheduleItem item)
        {
            if (ViewModel.EditCommand.CanExecute(item)) ViewModel.EditCommand.Execute(item);
        }

        // ===== 入力欄 =====

        /// <summary>入力欄を作って描画面へ載せる。位置を決められなければ false</summary>
        private bool OpenInlineEditor(Grid surface, ScheduleItem item, string text)
        {
            if (!TryGetItemBounds(surface, item, out double left, out double top, out double width))
            {
                return false;
            }

            var editor = new TextBox
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(left + 2, top, 0, 0),
                Width = Math.Max(60, width - 4),
                Height = InlineEditorHeight,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(6, 2, 6, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(2),
                MaxLength = 200
            };

            editor.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
            editor.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            editor.SetResourceReference(Control.BorderBrushProperty, "PrimaryBrush");

            editor.KeyDown += InlineEditor_KeyDown;
            editor.LostKeyboardFocus += InlineEditor_LostKeyboardFocus;

            surface.Children.Add(editor);

            _inlineEditor = editor;
            _inlineItem = item;

            EnsureInlineEditorVisible(top);

            // レイアウトが確定してからでないとフォーカスが入らない
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (!ReferenceEquals(_inlineEditor, editor)) return;
                    editor.Focus();
                    Keyboard.Focus(editor);
                    editor.SelectAll();
                }),
                System.Windows.Threading.DispatcherPriority.Input);

            return true;
        }

        /// <summary>アイテムの描画位置（描画面の座標）を求める</summary>
        private bool TryGetItemBounds(Grid surface, ScheduleItem item, out double left, out double top, out double width)
        {
            left = 0;
            top = 0;
            width = 0;

            var days = ViewModel.VisibleDays;
            if (days.Count == 0 || surface.ActualWidth <= 0) return false;

            int column = -1;
            for (int i = 0; i < days.Count; i++)
            {
                if (days[i].Date == item.StartTime.Date)
                {
                    column = i;
                    break;
                }
            }
            if (column < 0) return false;

            width = surface.ActualWidth / days.Count;
            left = column * width;

            top = (item.StartTime.TimeOfDay.TotalHours - ViewModel.DisplayStartHour) * PixelsPerHour;
            top = Math.Clamp(top, 0, Math.Max(0, ViewModel.ScheduleGridHeight - InlineEditorHeight));

            return true;
        }

        /// <summary>入力欄が表示範囲の外なら、見える位置までスクロールする</summary>
        private void EnsureInlineEditorVisible(double top)
        {
            if (MainScrollViewer == null || MainScrollViewer.ViewportHeight <= 0) return;

            double viewTop = MainScrollViewer.VerticalOffset;
            double viewBottom = viewTop + MainScrollViewer.ViewportHeight;

            if (top >= viewTop && top + InlineEditorHeight <= viewBottom) return;

            MainScrollViewer.ScrollToVerticalOffset(
                Math.Max(0, top - (MainScrollViewer.ViewportHeight / 3.0)));
        }

        private void InlineEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _inlineOpenDialog = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                CloseInlineEditor(commit: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseInlineEditor(commit: false);
                e.Handled = true;
            }
        }

        private void InlineEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
            => CloseInlineEditor(commit: true);

        // ===== 終了 =====

        /// <summary>
        /// 入力欄を閉じる。commit=true なら入力内容を反映し、false なら取り消す。
        /// 入力欄を外すと LostKeyboardFocus が走るため、フラグで再入を防ぐ。
        /// </summary>
        private void CloseInlineEditor(bool commit)
        {
            if (_inlineClosing) return;

            var editor = _inlineEditor;
            var item = _inlineItem;
            if (editor == null || item == null) return;

            _inlineClosing = true;
            try
            {
                string text = editor.Text;
                bool isNew = _inlineIsNew;
                var before = _inlineBefore;
                bool openDialog = _inlineOpenDialog;

                editor.KeyDown -= InlineEditor_KeyDown;
                editor.LostKeyboardFocus -= InlineEditor_LostKeyboardFocus;
                (editor.Parent as Panel)?.Children.Remove(editor);

                _inlineEditor = null;
                _inlineItem = null;
                _inlineIsNew = false;
                _inlineBefore = null;
                _inlineOpenDialog = false;

                bool itemRemains = true;

                if (commit)
                {
                    if (isNew)
                    {
                        itemRemains = ViewModel.CommitInlineCreate(item, text);
                    }
                    else if (before != null)
                    {
                        ViewModel.CommitInlineRename(item, before, text);
                    }
                }
                else if (isNew)
                {
                    ViewModel.CancelInlineCreate(item);
                    itemRemains = false;
                }

                // キーボード操作の起点を描画面へ戻す
                MainScrollViewer?.Focus();

                if (openDialog && itemRemains) ExecuteEdit(item);
            }
            finally
            {
                _inlineClosing = false;
            }
        }

        // ===== 描画面の探索 =====

        /// <summary>表示中の描画面を探す（日付を送るたびに作り直されるため都度探す）</summary>
        private Grid? FindScheduleSurface() => FindSurfaceBelow(MainScrollViewer);

        private static Grid? FindSurfaceBelow(DependencyObject? node)
        {
            if (node == null) return null;
            if (node is Grid grid && (grid.Tag as string) == ScheduleSurfaceTag) return grid;

            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                if (FindSurfaceBelow(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
            }
            return null;
        }

        /// <summary>与えられた要素から親をたどって描画面を探す</summary>
        private static Grid? FindSurfaceAbove(DependencyObject? node)
        {
            var current = node;
            while (current != null)
            {
                if (current is Grid grid && (grid.Tag as string) == ScheduleSurfaceTag) return grid;
                current = GetParentSafe(current);
            }
            return null;
        }
    }
}
