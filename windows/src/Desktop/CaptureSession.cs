using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Globalization;
using System.Diagnostics;
using System.Windows.Controls;

namespace ScreenshotAssistant;

internal sealed class CaptureSession : IDisposable
{
    readonly List<Overlay> windows=[];
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(16)};
    readonly TaskCompletionSource<BitmapSource?> completion=new();
    BitmapSource? desktopImage;
    readonly Native.Rect bounds;
    Native.Point? start;
    Native.Rect selection;
    bool finished;
    public static async Task<CaptureSession> CreateAsync()
    {
        var watch=Stopwatch.StartNew();var monitors=Native.Monitors();var bounds=Native.Union(monitors);
        var pixels=await Task.Run(()=>{Native.WaitForComposition();return Native.Capture(bounds);});
        App.Trace($"capture_pixels_ms {watch.ElapsedMilliseconds}");
        return new CaptureSession(monitors,bounds,pixels);
    }
    CaptureSession(List<Native.Rect> monitors,Native.Rect desktop,BitmapSource pixels)
    {
        bounds=desktop;desktopImage=pixels;
        foreach(var monitor in monitors)
        {
            var image=new CroppedBitmap(desktopImage,new Int32Rect(monitor.Left-bounds.Left,monitor.Top-bounds.Top,monitor.Width,monitor.Height));image.Freeze();
            windows.Add(new Overlay(this,monitor,image));
        }
        timer.Tick+=(_,_)=>UpdateSelection();
    }
    public Task<BitmapSource?> Run()
    {
        foreach(var window in windows){window.Show();App.Trace("overlay_shown");}
        Native.GetCursorPos(out var p);
        var active=windows.FirstOrDefault(w=>p.X>=w.Bounds.Left&&p.X<w.Bounds.Right&&p.Y>=w.Bounds.Top&&p.Y<w.Bounds.Bottom)??windows[0];
        active.Activate();active.Focus();timer.Start();return completion.Task;
    }
    void UpdateSelection()
    {
        if(start is null||finished)return;
        Native.GetCursorPos(out var p);var next=Native.Selection(start.Value,p,bounds);
        if(next.Left==selection.Left&&next.Top==selection.Top&&next.Right==selection.Right&&next.Bottom==selection.Bottom)return;
        selection=next;
        foreach(var w in windows)w.Surface.InvalidateVisual();
    }
    public void Begin(Overlay window)
    {
        Native.GetCursorPos(out var p);start=p;window.CaptureMouse();UpdateSelection();
    }
    public void Complete()
    {
        if(finished)return;
        UpdateSelection();
        if(start is null||selection.Width<3||selection.Height<3){Cancel();return;}
        try{var image=Native.Crop(desktopImage!,selection,bounds);Finish();completion.TrySetResult(image);}
        catch(Exception ex){Finish();completion.TrySetException(ex);}
    }
    public void Cancel(){if(finished)return;Finish();completion.TrySetResult(null);}
    void Finish(){finished=true;timer.Stop();Mouse.Capture(null);foreach(var w in windows){w.Image=null;w.Close();}desktopImage=null;}
    public void Dispose(){Cancel();windows.Clear();}

    internal sealed class Overlay : Window
    {
        readonly CaptureSession session;
        public FrameworkElement Surface {get;}
        public Native.Rect Bounds {get;}
        public BitmapSource? Image;
        public Overlay(CaptureSession owner,Native.Rect bounds,BitmapSource image)
        {
            session=owner;Bounds=bounds;Image=image;
            Title="截图选区 — Esc 取消";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
            Topmost=true;ShowInTaskbar=true;Background=Brushes.Black;Cursor=Cursors.Cross;FocusVisualStyle=null;
            var layers=new Grid();layers.Children.Add(new System.Windows.Controls.Image{Source=image,Stretch=Stretch.Fill});
            Surface=new RenderSurface(this);layers.Children.Add(Surface);Content=layers;
            SourceInitialized+=(_,_)=>Native.SetWindowPos(new WindowInteropHelper(this).Handle,new IntPtr(-1),bounds.Left,bounds.Top,bounds.Width,bounds.Height,0x0040);
            MouseLeftButtonDown+=(_,e)=>{session.Begin(this);e.Handled=true;};
            MouseLeftButtonUp+=(_,e)=>{session.Complete();e.Handled=true;};
            MouseMove+=(_,_)=>session.UpdateSelection();
            MouseRightButtonDown+=(_,_)=>session.Cancel();
            KeyDown+=(_,e)=>{if(e.Key==Key.Escape)session.Cancel();};
        }
        sealed class RenderSurface(Overlay owner):FrameworkElement
        {
            protected override void OnRender(DrawingContext dc){base.OnRender(dc);owner.DrawScene(dc,ActualWidth,ActualHeight);}
        }
        void DrawScene(DrawingContext dc,double width,double height)
        {
            var full=new Rect(0,0,width,height);
            var shade=new SolidColorBrush(Color.FromArgb(125,0,0,0));
            var s=session.selection;
            double sx=width/Bounds.Width,sy=height/Bounds.Height;
            var local=new Rect((s.Left-Bounds.Left)*sx,(s.Top-Bounds.Top)*sy,Math.Max(0,s.Width)*sx,Math.Max(0,s.Height)*sy);
            var cut=Rect.Intersect(full,local);
            if(cut.IsEmpty||cut.Width==0||cut.Height==0)dc.DrawRectangle(shade,null,full);
            else
            {
                dc.DrawRectangle(shade,null,new Rect(0,0,width,cut.Top));
                dc.DrawRectangle(shade,null,new Rect(0,cut.Bottom,width,height-cut.Bottom));
                dc.DrawRectangle(shade,null,new Rect(0,cut.Top,cut.Left,cut.Height));
                dc.DrawRectangle(shade,null,new Rect(cut.Right,cut.Top,width-cut.Right,cut.Height));
            }
            if(session.start!=null){dc.DrawRectangle(null,new Pen(Brushes.DeepSkyBlue,2),local);}
            string label=session.start==null?"拖动框选 · Esc / 右键取消":$"{s.Width} × {s.Height} px · 松开完成 · Esc 取消";
            var text=new FormattedText(label,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),16,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(22,30,45)),null,new Rect(24,24,text.Width+32,44),10,10);
            dc.DrawText(text,new Point(40,34));
        }
    }
}
