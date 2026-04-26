using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace VideoResizer
{
    public partial class MainWindow : Window
    {
        Queue<string> queue = new Queue<string>();
        bool running = false;

        Process currentProcess;
        Storyboard shimmer;

        string resolution = "1920x1080";

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }

        void Add(string file)
        {
            queue.Enqueue(file);
            QueueItems.Items.Add(System.IO.Path.GetFileName(file));

            try
            {
                ResultPreview.Stop();

                ResultPreview.Source = new Uri(file);
                ResultPreview.LoadedBehavior = MediaState.Manual;
                ResultPreview.UnloadedBehavior = MediaState.Manual;

                ResultPreview.Position = TimeSpan.FromMilliseconds(1);
                ResultPreview.Play();
                ResultPreview.Pause(); // кадр превью
            }
            catch { }
        }

        private void DropArea_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            if (dlg.ShowDialog() == true)
                Add(dlg.FileName);
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
                Add(files[0]);
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;
        }

        void ShowToast(string text)
        {
            var toast = new Border
            {
                Width = 260,
                Height = 50,
                Background = Brushes.Black,
                CornerRadius = new CornerRadius(10),
                Opacity = 0.9,
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            Canvas.SetRight(toast, 20);
            Canvas.SetBottom(toast, 20 + ToastHost.Children.Count * 60);

            ToastHost.Children.Add(toast);

            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(2);

            timer.Tick += (s, e) =>
            {
                timer.Stop();
                ToastHost.Children.Remove(toast);
            };

            timer.Start();
        }

        private void ToggleQueue(object sender, RoutedEventArgs e)
        {
            QueueItems.Visibility =
                QueueItems.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        async void StartQueue(object sender, RoutedEventArgs e)
        {
            if (running || queue.Count == 0) return;

            running = true;

            while (queue.Count > 0 && running)
            {
                string file = queue.Dequeue();
                QueueItems.Items.RemoveAt(0);

                await System.Threading.Tasks.Task.Run(() => Process(file));
            }

            running = false;

            Dispatcher.Invoke(() =>
            {
                ShowToast("ALL TASKS COMPLETED");
            });
        }

        void Cancel(object sender, RoutedEventArgs e)
        {
            running = false;

            try
            {
                if (currentProcess != null && !currentProcess.HasExited)
                {
                    currentProcess.Kill();
                    currentProcess.WaitForExit();
                }
            }
            catch { }

            Dispatcher.Invoke(() =>
            {
                Progress.Value = 0;
            });
        }

        void Process(string file)
        {
            string ffmpeg = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");

            if (!File.Exists(ffmpeg))
            {
                Dispatcher.Invoke(() => ShowToast("FFMPEG NOT FOUND"));
                return;
            }

            string output = file + "_out.mp4";
            var r = resolution.Split('x');

            Dispatcher.Invoke(() =>
            {
                Progress.Value = 0;
                StartShimmer();
            });

            currentProcess = new Process();

            currentProcess.StartInfo.FileName = ffmpeg;
            currentProcess.StartInfo.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;

            currentProcess.StartInfo.UseShellExecute = false;
            currentProcess.StartInfo.CreateNoWindow = true;

            currentProcess.StartInfo.RedirectStandardError = true;
            currentProcess.StartInfo.RedirectStandardOutput = true;

            currentProcess.StartInfo.Arguments =
                $"-y -hide_banner -loglevel info " +
                $"-i \"{file}\" " +
                $"-vf scale={r[0]}:{r[1]} " +
                $"-an -c:v libx264 -preset ultrafast \"{output}\"";

            currentProcess.Start();
            currentProcess.BeginErrorReadLine();
            currentProcess.BeginOutputReadLine();

            currentProcess.WaitForExit();

            Dispatcher.Invoke(() => StopShimmer());

            if (!File.Exists(output))
            {
                Dispatcher.Invoke(() =>
                {
                    ShowToast("FFMPEG FAILED");
                    Progress.Value = 0;
                });
                return;
            }

            try
            {
                File.Delete(file);
                File.Move(output, file);

                Dispatcher.Invoke(() =>
                {
                    Progress.Value = 100;
                    ShowToast("DONE");
                });
            }
            catch
            {
                Dispatcher.Invoke(() =>
                {
                    ShowToast("FILE ERROR");
                });
            }
        }

        void StartShimmer()
        {
            var rect = (Rectangle)Progress.Template.FindName("Shimmer", Progress);
            var transform = (TranslateTransform)Progress.Template.FindName("ShimmerTransform", Progress);

            if (rect == null || transform == null) return;

            rect.Opacity = 1;

            shimmer = new Storyboard
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            var anim = new DoubleAnimation
            {
                From = -300,
                To = 500,
                Duration = TimeSpan.FromSeconds(1.1)
            };

            Storyboard.SetTarget(anim, transform);
            Storyboard.SetTargetProperty(anim, new PropertyPath(TranslateTransform.XProperty));

            shimmer.Children.Add(anim);
            shimmer.Begin();
        }

        void StopShimmer()
        {
            try
            {
                var rect = (Rectangle)Progress.Template.FindName("Shimmer", Progress);
                if (rect != null) rect.Opacity = 0;

                shimmer?.Stop();
            }
            catch { }
        }

        void OpenResolution(object sender, RoutedEventArgs e)
        {
            ResolutionOverlay.Visibility = Visibility.Visible;
        }

        void ApplyResolution(object sender, RoutedEventArgs e)
        {
            resolution = $"{WidthInput.Text}x{HeightInput.Text}";
            ResolutionOverlay.Visibility = Visibility.Collapsed;
        }

        void Close_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (currentProcess != null && !currentProcess.HasExited)
                    currentProcess.Kill();
            }
            catch { }

            Application.Current.Shutdown();
        }
    }
}