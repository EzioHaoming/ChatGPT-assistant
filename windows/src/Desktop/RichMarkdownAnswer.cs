using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ScreenshotAssistant;

internal sealed class RichMarkdownAnswer : UserControl
{
    readonly WebView2CompositionControl browser=new();
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(160)};
    readonly TaskCompletionSource ready=new();
    string source="";bool streaming,initializing,rendering,dirty,closed,home;
    public int FormulaCount{get;private set;}
    public int FormulaErrors{get;private set;}
    public Task Ready=>ready.Task;
    public string Text{get=>source;set{source=value??"";streaming=false;Queue();}}
    public RichMarkdownAnswer()
    {
        Content=browser;browser.AllowExternalDrop=false;
        Loaded+=async(_,_)=>await Initialize();
        timer.Tick+=async(_,_)=>{timer.Stop();await RenderAsync();};
        Unloaded+=(_,_)=>timer.Stop();
    }
    async Task Initialize()
    {
        if(initializing)return;initializing=true;
        try
        {
            string assets=Path.Combine(AppContext.BaseDirectory,"Renderer");
            if(!File.Exists(Path.Combine(assets,"index.html")))throw new FileNotFoundException("缺少 Renderer 目录，请完整解压程序包。");
            var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ChatGPT-assistant","Renderer"));
            var options=env.CreateCoreWebView2ControllerOptions();options.IsInPrivateModeEnabled=true;
            await browser.EnsureCoreWebView2Async(env,options);
            var core=browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled=false;core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.AreBrowserAcceleratorKeysEnabled=false;
            core.Settings.IsStatusBarEnabled=false;core.Settings.IsZoomControlEnabled=false;core.Settings.AreDefaultScriptDialogsEnabled=false;
            core.Settings.IsPasswordAutosaveEnabled=false;core.Settings.IsGeneralAutofillEnabled=false;
            core.SetVirtualHostNameToFolderMapping("assistant.invalid",assets,CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting+=(_,e)=>{if(e.Uri!="https://assistant.invalid/index.html")e.Cancel=true;};
            core.NewWindowRequested+=(_,e)=>e.Handled=true;
            core.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;
            core.DownloadStarting+=(_,e)=>e.Cancel=true;
            core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
            core.WebResourceRequested+=(_,e)=>{if(!Uri.TryCreate(e.Request.Uri,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="assistant.invalid")e.Response=env.CreateWebResourceResponse(new MemoryStream(),403,"Blocked","");};
            core.WebMessageReceived+=async(_,e)=>{if(e.TryGetWebMessageAsString()=="ready"){ready.TrySetResult();await RenderAsync();}};
            core.NavigationCompleted+=(_,e)=>{if(!e.IsSuccess)ready.TrySetException(new InvalidOperationException("本地公式排版页面加载失败。"));};
            core.Navigate("https://assistant.invalid/index.html");
            var window=Window.GetWindow(this);if(window!=null)window.Closed+=(_,_)=>{closed=true;timer.Stop();browser.Dispose();};
        }
        catch(Exception e)
        {
            ready.TrySetException(e);
            Content=new TextBlock{Text="公式排版组件无法启动，请检查 WebView2 Runtime 和程序的 Renderer 目录。回答仍可通过“复制回答”获取。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(16)};
            App.Trace("renderer_init_failed "+e.GetType().Name);
        }
    }
    void Queue(){dirty=true;if(!timer.IsEnabled&&IsLoaded)timer.Start();}
    public void AppendText(string value){source+=value;streaming=true;Queue();}
    public void Clear()=>Text="";
    public void RefreshTypography()=>Queue();
    public void ScrollToHome(){home=true;Queue();}
    internal async Task RenderAsync()
    {
        if(closed||!ready.Task.IsCompletedSuccessfully)return;
        if(rendering){dirty=true;return;}rendering=true;
        try
        {
            do
            {
                dirty=false;
                string result=await browser.ExecuteScriptAsync($"renderAnswer({JsonSerializer.Serialize(source)},{FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)},{(streaming?"true":"false")})");
                using var json=JsonDocument.Parse(result);
                FormulaCount=json.RootElement.GetProperty("count").GetInt32();FormulaErrors=json.RootElement.GetProperty("errors").GetInt32();
                if(home){home=false;await browser.ExecuteScriptAsync("window.scrollTo(0,0)");}
            }while(dirty&&!closed);
        }
        catch(Exception e){App.Trace("renderer_failed "+e.GetType().Name);}
        finally{rendering=false;}
    }
    internal Task<string> Inspect(string script)=>browser.ExecuteScriptAsync(script);
    internal Task Capture(Stream stream)=>browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
}
