using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TimeRenderer.Controls
{
    public enum TransitionDirection
    {
        Forward,
        Backward
    }

    [TemplatePart(Name = "PART_PaintArea", Type = typeof(System.Windows.Controls.Panel))]
    [TemplatePart(Name = "PART_PreviousContentPresentationSite", Type = typeof(ContentPresenter))]
    [TemplatePart(Name = "PART_CurrentContentPresentationSite", Type = typeof(ContentPresenter))]
    public class TransitioningContentControl : ContentControl
    {
        private ContentPresenter? _currentContentPresentationSite;
        private ContentPresenter? _previousContentPresentationSite;

        public static readonly DependencyProperty TransitionDirectionProperty =
            DependencyProperty.Register("TransitionDirection", typeof(TransitionDirection), typeof(TransitioningContentControl), new PropertyMetadata(TransitionDirection.Forward));

        public TransitionDirection TransitionDirection
        {
            get => (TransitionDirection)GetValue(TransitionDirectionProperty);
            set => SetValue(TransitionDirectionProperty, value);
        }

        static TransitioningContentControl()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(TransitioningContentControl), new FrameworkPropertyMetadata(typeof(TransitioningContentControl)));
        }

        public override void OnApplyTemplate()
        {
            _currentContentPresentationSite = GetTemplateChild("PART_CurrentContentPresentationSite") as ContentPresenter;
            _previousContentPresentationSite = GetTemplateChild("PART_PreviousContentPresentationSite") as ContentPresenter;

            _currentContentPresentationSite?.Content = Content;

            base.OnApplyTemplate();
        }

        protected override void OnContentChanged(object oldContent, object newContent)
        {
            base.OnContentChanged(oldContent, newContent);

            // 値が実質的に同じ場合、または初回描画(old=null)の場合はアニメーションをスキップ
            // WPF がテンプレートバインディングで自動更新するため、手動セットは不要
            // ※初回アニメーション中はCanvasのActualWidthが0になりアイテム位置の計算が狂うため
            if (Equals(oldContent, newContent) || oldContent == null)
                return;

            StartTransition(oldContent, newContent);
        }

        private void StartTransition(object oldContent, object newContent)
        {
            if (_currentContentPresentationSite != null && _previousContentPresentationSite != null)
            {
                _currentContentPresentationSite.Content = newContent;
                _previousContentPresentationSite.Content = oldContent;

                // Stop any running animations
                _currentContentPresentationSite.BeginAnimation(RenderTransformProperty, null);
                _previousContentPresentationSite.BeginAnimation(RenderTransformProperty, null);

                // Setup Transforms
                var currentTransform = new TranslateTransform();
                var previousTransform = new TranslateTransform();

                _currentContentPresentationSite.RenderTransform = currentTransform;
                _previousContentPresentationSite.RenderTransform = previousTransform;

                double width = ActualWidth;
                if (width <= 0) width = 500; // Fallback

                // Helper to create animation
                static DoubleAnimation CreateAnimation(double from, double to)
                {
                    return new DoubleAnimation
                    {
                        From = from,
                        To = to,
                        Duration = new Duration(TimeSpan.FromMilliseconds(300)),
                        EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                    };
                }

                if (TransitionDirection == TransitionDirection.Forward)
                {
                    // Forward: Old moves Left, New comes from Right
                    // Old: 0 -> -width
                    // New: width -> 0
                    var prevAnim = CreateAnimation(0, -width);
                    // アニメーション完了後に古いコンテンツをクリア（リサイズ時のはみ出し防止）
                    prevAnim.Completed += (s, e) => { _previousContentPresentationSite!.Content = null; };
                    previousTransform.BeginAnimation(TranslateTransform.XProperty, prevAnim);
                    currentTransform.BeginAnimation(TranslateTransform.XProperty, CreateAnimation(width, 0));
                }
                else
                {
                    // Backward: Old moves Right, New comes from Left
                    // Old: 0 -> width
                    // New: -width -> 0
                    var prevAnim = CreateAnimation(0, width);
                    // アニメーション完了後に古いコンテンツをクリア（リサイズ時のはみ出し防止）
                    prevAnim.Completed += (s, e) => { _previousContentPresentationSite!.Content = null; };
                    previousTransform.BeginAnimation(TranslateTransform.XProperty, prevAnim);
                    currentTransform.BeginAnimation(TranslateTransform.XProperty, CreateAnimation(-width, 0));
                }
            }
        }
    }
}
