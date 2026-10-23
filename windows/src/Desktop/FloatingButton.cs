using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ellipse=System.Windows.Shapes.Ellipse;

namespace ScreenshotAssistant;

internal sealed class FloatingButton:Window
{
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo {public int Size;public Native.Rect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Native.Rect rect);
    readonly AssistantWindow assistant;
    Point down;bool dragged,pressed;
    DateTime nextAvatarRetry;
    internal bool HasCustomAvatar {get;private set;}
    static string PositionFile=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ScreenshotAssistant","floating-position.json");
    public FloatingButton(AssistantWindow window)
    {
        assistant=window;Title="ChatGPT assistant";Width=60;Height=60;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;ShowActivated=false;
        ReloadAvatar();
        ShowInTaskbar=App.FloatingTestMode; // Expose a targetable test surface to UI automation only.
        ToolTip="单击展开/收起 · 拖动贴边 · 右键截图或提问";
        var menu=new ContextMenu();menu.Resources.MergedDictionaries.Add(Theme.Create());ContextMenu=menu;
        var toggle=new MenuItem{Header="展开 / 收起"};toggle.Click+=(_,_)=>assistant.TogglePanel();menu.Items.Add(toggle);
        var ask=new MenuItem{Header="提问"};ask.Click+=(_,_)=>assistant.ShowQuestion();menu.Items.Add(ask);
        var shot=new MenuItem{Header="框选截图"};shot.Click+=async(_,_)=>{menu.IsOpen=false;await Dispatcher.InvokeAsync(async()=>await assistant.Capture());};menu.Items.Add(shot);
        menu.Items.Add(new Separator());var exit=new MenuItem{Header="退出"};exit.Click+=(_,_)=>assistant.ExitApplication();menu.Items.Add(exit);
        MouseLeftButtonDown+=(_,e)=>{pressed=true;dragged=false;down=e.GetPosition(this);CaptureMouse();e.Handled=true;};
        MouseMove+=(_,e)=>
        {
            if(!pressed||e.LeftButton!=MouseButtonState.Pressed)return;
            var p=e.GetPosition(this);if(!dragged&&Math.Abs(p.X-down.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(p.Y-down.Y)<SystemParameters.MinimumVerticalDragDistance)return;
            dragged=true;pressed=false;ReleaseMouseCapture();
            Left+=p.X-down.X;Top+=p.Y-down.Y;
            try{DragMove();}catch(InvalidOperationException){}finally{SnapToEdge();}
        };
        MouseLeftButtonUp+=(_,e)=>{bool click=pressed&&!dragged;pressed=false;ReleaseMouseCapture();if(click)assistant.TogglePanel();e.Handled=true;};
        Loaded+=(_,_)=>RestorePosition();
        SourceInitialized+=(_,_)=>HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook((IntPtr h,int m,IntPtr w,IntPtr l,ref bool handled)=>{if(m==0x007E)Dispatcher.BeginInvoke(SnapToEdge);return IntPtr.Zero;});
    }
    internal void ReloadAvatar()
    {
        var portrait=AvatarStore.Load();
        // Keep an already displayed portrait if its file is temporarily unreadable.
        if(portrait==null&&AvatarStore.HasSavedAvatar&&HasCustomAvatar)return;
        HasCustomAvatar=portrait!=null;
        Icon=portrait??BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico"));
        Content=portrait!=null?new Ellipse{Margin=new Thickness(3),Stroke=Brushes.White,StrokeThickness=2,Fill=new ImageBrush(portrait){Stretch=Stretch.UniformToFill}}:
            new Border{Margin=new Thickness(3),CornerRadius=new CornerRadius(27),Background=new SolidColorBrush(Color.FromRgb(88,101,190)),BorderBrush=Brushes.White,BorderThickness=new Thickness(2),Child=new TextBlock{Text="自",FontSize=25,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
    }
    internal void EnsureVisible()
    {
        if(!HasCustomAvatar&&DateTime.UtcNow>=nextAvatarRetry)
        {
            nextAvatarRetry=DateTime.UtcNow.AddSeconds(5);
            RecoverAvatar();
        }
        if(pressed||dragged&&Mouse.LeftButton==MouseButtonState.Pressed)return;
        if(!IsVisible){Show();App.Trace("floating_visibility_recovered");}
        if(WindowState!=WindowState.Normal)WindowState=WindowState.Normal;
        var handle=new WindowInteropHelper(this).Handle;
        if(GetWindowRect(handle,out var rect)&&!Native.Monitors().Any(m=>rect.Left>=m.Left&&rect.Top>=m.Top&&rect.Right<=m.Right&&rect.Bottom<=m.Bottom))SnapToEdge();
        Native.SetWindowPos(handle,new IntPtr(-1),0,0,0,0,0x0013); // TOPMOST, no move/size/activation.
    }
    internal void RecoverAvatar()
    {
        if(HasCustomAvatar||!AvatarStore.HasSavedAvatar)return;
        ReloadAvatar();
        if(HasCustomAvatar){assistant.Icon=Icon;App.Trace("avatar_load_recovered");}
    }
    internal void RestoreToPrimary()
    {
        Show();WindowState=WindowState.Normal;
        Left=SystemParameters.WorkArea.Right-Width-16;
        Top=SystemParameters.WorkArea.Top+SystemParameters.WorkArea.Height*.45;
        SnapToEdge();EnsureVisible();
    }
    void RestorePosition()
    {
        try{var p=JsonSerializer.Deserialize<int[]>(File.ReadAllText(PositionFile));if(p is {Length:2})Native.SetWindowPos(new WindowInteropHelper(this).Handle,IntPtr.Zero,p[0],p[1],0,0,0x0015);}
        catch{Left=SystemParameters.WorkArea.Right-Width-12;Top=SystemParameters.WorkArea.Top+SystemParameters.WorkArea.Height*.45;}
        SnapToEdge();
    }
    internal static (int X,int Y) Snap(Native.Rect window,Native.Rect work,int gap)
    {
        int x=(window.Left+window.Width/2)<(work.Left+work.Width/2)?work.Left+gap:work.Right-window.Width-gap;
        int y=Math.Clamp(window.Top,work.Top+gap,Math.Max(work.Top+gap,work.Bottom-window.Height-gap));return(x,y);
    }
    internal void SnapToEdge()
    {
        var handle=new WindowInteropHelper(this).Handle;var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetWindowRect(handle,out var rect)||!GetMonitorInfo(MonitorFromWindow(handle,2),ref info))return;
        var p=Snap(rect,info.Work,(int)Math.Ceiling(Native.GetDpiForWindow(handle)/96.0*8));Native.SetWindowPos(handle,IntPtr.Zero,p.X,p.Y,0,0,0x0015);
        try{Directory.CreateDirectory(Path.GetDirectoryName(PositionFile)!);File.WriteAllText(PositionFile,JsonSerializer.Serialize(new[]{p.X,p.Y}));}catch{}
    }
}
