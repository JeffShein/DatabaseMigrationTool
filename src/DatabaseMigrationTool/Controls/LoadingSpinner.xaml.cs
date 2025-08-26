using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.ComponentModel;

namespace DatabaseMigrationTool.Controls
{
    public partial class LoadingSpinner : UserControl
    {
        private bool _isAnimating;
        private Storyboard? _spinnerStoryboard;

        public LoadingSpinner()
        {
            InitializeComponent();
            Loaded += LoadingSpinner_Loaded;
            Unloaded += LoadingSpinner_Unloaded;
        }

        public static readonly DependencyProperty IsSpinningProperty = DependencyProperty.Register(
            "IsSpinning", typeof(bool), typeof(LoadingSpinner),
            new PropertyMetadata(false, OnIsSpinningChanged));

        public bool IsSpinning
        {
            get { return (bool)GetValue(IsSpinningProperty); }
            set { SetValue(IsSpinningProperty, value); }
        }

        private static void OnIsSpinningChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var spinner = (LoadingSpinner)d;
            bool isSpinning = (bool)e.NewValue;

            if (isSpinning)
                spinner.StartSpinning();
            else
                spinner.StopSpinning();
        }

        private void LoadingSpinner_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeStoryboard();
            
            // Add a rendering event handler to ensure smooth animations
            CompositionTarget.Rendering += CompositionTarget_Rendering;
            
            if (IsSpinning && !_isAnimating)
            {
                StartSpinning();
                StartRenderTimer();
            }
        }
        
        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            // This event handler helps keep the UI thread rendering smoothly
            if (_isAnimating && SpinnerRotate != null)
            {
                // Ensure render priority
                if (!_renderTimer.IsEnabled)
                {
                    StartRenderTimer();
                }
                
                // Force a layout update
                InvalidateVisual();
            }
        }
        
        private DispatcherTimer _renderTimer = new DispatcherTimer(DispatcherPriority.Render);
        
        private void StartRenderTimer()
        {
            _renderTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60fps
            _renderTimer.Tick += (s, e) => {
                if (_isAnimating)
                {
                    InvalidateVisual();
                }
                else
                {
                    _renderTimer.Stop();
                }
            };
            _renderTimer.Start();
        }

        private void LoadingSpinner_Unloaded(object sender, RoutedEventArgs e)
        {
            StopSpinning();
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            _renderTimer.Stop();
        }

        private void InitializeStoryboard()
        {
            if (_spinnerStoryboard != null)
                return;

            _spinnerStoryboard = new Storyboard();
            var animation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(1.0),
                RepeatBehavior = RepeatBehavior.Forever,
                FillBehavior = FillBehavior.HoldEnd,
                AccelerationRatio = 0.1,
                DecelerationRatio = 0.1
            };
            
            // Ensure animation gets top priority
            Timeline.SetDesiredFrameRate(animation, 60); // Request 60fps
            
            Storyboard.SetTarget(animation, SpinnerRotate);
            Storyboard.SetTargetProperty(animation, new PropertyPath("Angle"));
            _spinnerStoryboard.Children.Add(animation);
            
            // Make sure storyboard has highest priority
            _spinnerStoryboard.SetValue(Storyboard.SpeedRatioProperty, 1.0);
        }

        public void StartSpinning()
        {
            if (_isAnimating)
                return;

            // Stop any existing animation
            _spinnerStoryboard?.Stop();
            
            // Make sure the storyboard is initialized
            if (_spinnerStoryboard == null)
                InitializeStoryboard();
                
            // Begin animation in a dispatcher with high priority
            Dispatcher.BeginInvoke(new Action(() => {
                _spinnerStoryboard?.Begin();
                InvalidateVisual(); // Force a visual refresh
            }), System.Windows.Threading.DispatcherPriority.Render);
            
            _isAnimating = true;
        }

        public void StopSpinning()
        {
            if (!_isAnimating)
                return;

            // Use high-priority dispatcher to stop the animation
            Dispatcher.BeginInvoke(new Action(() => {
                _spinnerStoryboard?.Stop();
            }), DispatcherPriority.Render);
            
            _isAnimating = false;
            _renderTimer.Stop();
        }
    }
}