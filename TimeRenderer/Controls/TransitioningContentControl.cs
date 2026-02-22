using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

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

        private long _currentTransitionId = 0;

        private void StartTransition(object oldContent, object newContent)
        {
            if (_currentContentPresentationSite != null && _previousContentPresentationSite != null)
            {
                // 現在進行中の遷移IDを更新
                long transitionId = ++_currentTransitionId;

                _previousContentPresentationSite.Visibility = Visibility.Visible;
                _currentContentPresentationSite.Visibility = Visibility.Visible;

                _currentContentPresentationSite.Content = newContent;
                _previousContentPresentationSite.Content = oldContent;

                // Stop any running animations and clear properties
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

                // アニメーションのパラメータを方向に応じて設定
                bool isForward = TransitionDirection == TransitionDirection.Forward;
                double oldContentTargetX = isForward ? -width : width;
                double newContentStartX = isForward ? width : -width;

                var prevAnim = CreateAnimation(0, oldContentTargetX);
                previousTransform.BeginAnimation(TranslateTransform.XProperty, prevAnim);

                var currAnim = CreateAnimation(newContentStartX, 0);
                currentTransform.BeginAnimation(TranslateTransform.XProperty, currAnim);

                ClearPreviousContentAsync(transitionId);
            }
        }

        private void ClearPreviousContentAsync(long transitionId)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                if (_currentTransitionId == transitionId && _previousContentPresentationSite != null)
                {
                    _previousContentPresentationSite.Content = null;
                    _previousContentPresentationSite.Visibility = Visibility.Collapsed;
                }
            };
            timer.Start();
        }
    }
}
