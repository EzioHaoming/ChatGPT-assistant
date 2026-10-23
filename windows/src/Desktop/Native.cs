using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ScreenshotAssistant;

internal static class Native
{
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
    internal static void WaitForComposition(){DwmFlush();}
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; public int Width=>Right-Left; public int Height=>Bottom-Top; }
    internal delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);
    [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd,int id);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc,int width,int height);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest,int x,int y,int w,int h,IntPtr src,int sx,int sy,uint rop);

    public static List<Rect> Monitors()
    {
        var list=new List<Rect>();
        if(!EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,(IntPtr m,IntPtr dc,ref Rect r,IntPtr d)=>{list.Add(r);return true;},IntPtr.Zero) || list.Count==0)
            throw new InvalidOperationException("无法枚举显示器。");
        return list;
    }
    public static Rect Union(IReadOnlyList<Rect> monitors)=>new(){Left=monitors.Min(r=>r.Left),Top=monitors.Min(r=>r.Top),Right=monitors.Max(r=>r.Right),Bottom=monitors.Max(r=>r.Bottom)};
    public static Rect Selection(Point start,Point end,Rect bounds)=>new(){Left=Math.Clamp(Math.Min(start.X,end.X),bounds.Left,bounds.Right),Top=Math.Clamp(Math.Min(start.Y,end.Y),bounds.Top,bounds.Bottom),Right=Math.Clamp(Math.Max(start.X,end.X),bounds.Left,bounds.Right),Bottom=Math.Clamp(Math.Max(start.Y,end.Y),bounds.Top,bounds.Bottom)};
    public static BitmapSource Capture(Rect r)
    {
        if(r.Width<1 || r.Height<1 || (long)r.Width*r.Height>100_000_000) throw new InvalidOperationException("桌面尺寸过大（上限一亿像素），无法安全截图。");
        var src=GetDC(IntPtr.Zero); var dc=CreateCompatibleDC(src); var bmp=CreateCompatibleBitmap(src,r.Width,r.Height);
        if(src==IntPtr.Zero || dc==IntPtr.Zero || bmp==IntPtr.Zero){if(bmp!=IntPtr.Zero)DeleteObject(bmp);if(dc!=IntPtr.Zero)DeleteDC(dc);if(src!=IntPtr.Zero)ReleaseDC(IntPtr.Zero,src);throw new InvalidOperationException("无法创建截图缓冲区。");}
        var previous=SelectObject(dc,bmp);
        try
        {
            if(!BitBlt(dc,0,0,r.Width,r.Height,src,r.Left,r.Top,0x40CC0020))throw new InvalidOperationException("屏幕捕获失败。");
            var result=Imaging.CreateBitmapSourceFromHBitmap(bmp,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());result.Freeze();return result;
        }
        finally{SelectObject(dc,previous);DeleteObject(bmp);DeleteDC(dc);ReleaseDC(IntPtr.Zero,src);}
    }
    public static BitmapSource Crop(BitmapSource source,Rect region,Rect desktop)
    {
        if(region.Width<3||region.Height<3)throw new InvalidOperationException("选区过小，请至少选择 3 × 3 像素。");
        var crop=new CroppedBitmap(source,new Int32Rect(region.Left-desktop.Left,region.Top-desktop.Top,region.Width,region.Height));
        // Detach the crop: retaining CroppedBitmap would retain the entire desktop.
        int stride=(crop.PixelWidth*crop.Format.BitsPerPixel+7)/8;
        var bytes=new byte[checked(stride*crop.PixelHeight)];crop.CopyPixels(bytes,stride,0);
        var copy=BitmapSource.Create(crop.PixelWidth,crop.PixelHeight,96,96,crop.Format,crop.Palette,bytes,stride);copy.Freeze();return copy;
    }
}
