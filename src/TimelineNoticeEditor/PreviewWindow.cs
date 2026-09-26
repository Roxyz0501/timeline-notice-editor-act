using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace TimelineNoticeEditor
{
    internal sealed class PreviewWindow : Window
    {
        private readonly Image picture = new Image { Stretch = Stretch.Fill };
        private readonly Border border;
        private bool shown, locked;
        internal event Action<double, double> Moved;
        internal PreviewWindow()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowActivated = false;
            Opacity = 0; Width = 1; Height = 1;
            border = new Border { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Child = picture };
            Content = border;
            MouseLeftButtonDown += (s, e) => { if (locked) return; DragMove(); Moved?.Invoke(Left, Top); };
        }
        internal void Present(BitmapSource bitmap, double x, double y, bool visible, bool fixedPosition)
        {
            if (!shown) { Show(); shown = true; }
            if (!ReferenceEquals(picture.Source, bitmap)) picture.Source = bitmap;
            if (bitmap != null)
            {
                if (Width != bitmap.Width) Width = bitmap.Width; if (Height != bitmap.Height) Height = bitmap.Height;
                if (Left != x) Left = x; if (Top != y) Top = y;
            }
            SetLocked(fixedPosition);
            var opacity = visible && bitmap != null ? 1d : 0d;
            if (Opacity != opacity) Opacity = opacity;
        }
        internal void SetLocked(bool value)
        {
            if (locked == value) return; locked = value;
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            var style = GetWindowLong(handle, -20);
            SetWindowLong(handle, -20, locked ? style | 0x20 : style & ~0x20);
        }
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr h, int index, int value);
    }
}
