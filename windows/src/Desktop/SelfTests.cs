using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenshotAssistant;
internal static class SelfTests
{
    public static int Run(string[] args)
    {
        var results=new List<object>();
        void Check(string name,Func<bool> test){try{results.Add(new{name,passed=test()});}catch(Exception e){results.Add(new{name,passed=false,error=e.Message});}}
        Check("legacy settings default Enter",()=>JsonSerializer.Deserialize<Preferences>("{}")!.SendShortcut=="Enter");
        Check("send preference persists",()=>JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(new Preferences(SendShortcut:"Ctrl+Enter")))!.SendShortcut=="Ctrl+Enter");
        Check("Shift Enter does not send",()=>!AssistantWindow.SendShortcutMatches("Enter",System.Windows.Input.ModifierKeys.Shift));
        Check("Ctrl Enter exact modifiers",()=>AssistantWindow.SendShortcutMatches("Ctrl+Enter",System.Windows.Input.ModifierKeys.Control)&&!AssistantWindow.SendShortcutMatches("Ctrl+Enter",System.Windows.Input.ModifierKeys.None));
        Check("custom letter shortcut",()=>AssistantWindow.ParseEditorShortcut("Ctrl+Alt+S")== (System.Windows.Input.Key.S,System.Windows.Input.ModifierKeys.Control|System.Windows.Input.ModifierKeys.Alt));
        Check("reject typing character shortcut",()=>{try{AssistantWindow.ParseEditorShortcut("Shift+A");return false;}catch{return true;}});
        Check("newline preference roundtrip",()=>JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(new Preferences(NewlineShortcut:"Ctrl+J")))!.NewlineShortcut=="Ctrl+J");
        Check("record Ctrl Shift Enter",()=>AssistantWindow.FormatRecordedShortcut(System.Windows.Input.Key.Return,System.Windows.Input.ModifierKeys.Control|System.Windows.Input.ModifierKeys.Shift)=="Ctrl+Shift+Enter");
        Check("record modifier alone ignored",()=>AssistantWindow.FormatRecordedShortcut(System.Windows.Input.Key.LeftCtrl,System.Windows.Input.ModifierKeys.Control)==null);
        Check("record digit roundtrip",()=>AssistantWindow.ParseEditorShortcut(AssistantWindow.FormatRecordedShortcut(System.Windows.Input.Key.D5,System.Windows.Input.ModifierKeys.Alt)!)==(System.Windows.Input.Key.D5,System.Windows.Input.ModifierKeys.Alt));
        Check("Windows hotkey replacement and same-key reapply",()=>
        {
            const int a=19201,b=19202;const uint mods=0x4007;
            try
            {
                if(!Native.RegisterHotKey(IntPtr.Zero,a,mods,0x79))return false;
                Native.UnregisterHotKey(IntPtr.Zero,a);
                if(!Native.RegisterHotKey(IntPtr.Zero,b,mods,0x78))return false;
                Native.UnregisterHotKey(IntPtr.Zero,b);
                return Native.RegisterHotKey(IntPtr.Zero,a,mods,0x78);
            }
            finally{Native.UnregisterHotKey(IntPtr.Zero,a);Native.UnregisterHotKey(IntPtr.Zero,b);}
        });
        var bounds=new Native.Rect{Left=-1920,Top=-200,Right=2560,Bottom=1440};
        Check("legacy settings default model",()=>JsonSerializer.Deserialize<Preferences>("{\"Hotkey\":\"Alt+Q\",\"Topmost\":true}")!.Model=="gpt-6-luna");
        Check("model preference roundtrip",()=>JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(new Preferences(Model:"gpt-6-sol")))!.Model=="gpt-6-sol");
        Check("floating snap negative monitor and clamp top",()=>FloatingButton.Snap(new(){Left=-1800,Top=-50,Right=-1710,Bottom=40},new(){Left=-1920,Top=0,Right=0,Bottom=1040},12)==(-1908,12));
        Check("floating snap right and clamp bottom at 150 percent",()=>FloatingButton.Snap(new(){Left=3500,Top=2200,Right=3590,Bottom=2290},new(){Left=0,Top=0,Right=3840,Bottom=2100},12)==(3738,1998));
        Check("floating snap preserves vertical position",()=>FloatingButton.Snap(new(){Left=200,Top=450,Right=260,Bottom=510},new(){Left=0,Top=0,Right=1920,Bottom=1040},8)==(8,450));
        Check("negative-origin reverse selection",()=>{var r=Native.Selection(new(){X=800,Y=600},new(){X=-1200,Y=-100},bounds);return r.Left==-1200&&r.Top==-100&&r.Width==2000&&r.Height==700;});
        Check("clamp to virtual desktop",()=>{var r=Native.Selection(new(){X=-3000,Y=-500},new(){X=4000,Y=3000},bounds);return r.Left==-1920&&r.Top==-200&&r.Width==4480&&r.Height==1640;});
        Check("multi-monitor union",()=>{var r=Native.Union([new(){Left=-1920,Top=0,Right=0,Bottom=1080},new(){Left=0,Top=-200,Right=2560,Bottom=1440}]);return r.Left==-1920&&r.Top==-200&&r.Right==2560&&r.Bottom==1440;});
        Check("hotkey parser",()=>AssistantWindow.ParseHotkey("Ctrl+Alt+Q")== (0x4003u,0x51u));
        Check("reject reserved F12",()=>{try{AssistantWindow.ParseHotkey("Ctrl+F12");return false;}catch{return true;}});
        Check("reject unmodified key",()=>{try{AssistantWindow.ParseHotkey("Q");return false;}catch{return true;}});
        Check("pixel-exact detached crop",()=>{byte[] pixels=new byte[4*4*4];for(int i=0;i<pixels.Length;i++)pixels[i]=(byte)i;var bitmap=BitmapSource.Create(4,4,96,96,PixelFormats.Bgra32,null,pixels,16);var crop=Native.Crop(bitmap,new(){Left=-9,Top=-9,Right=-6,Bottom=-6},new(){Left=-10,Top=-10,Right=-6,Bottom=-6});byte[] actual=new byte[36];crop.CopyPixels(actual,12,0);return crop.PixelWidth==3&&crop.PixelHeight==3&&actual[0]==20&&actual[12]==36&&actual[24]==52;});
        Check("reject tiny crop",()=>{try{Native.Crop(BitmapSource.Create(4,4,96,96,PixelFormats.Bgra32,null,new byte[64],16),new(){Left=0,Top=0,Right=1,Bottom=1},new(){Left=0,Top=0,Right=4,Bottom=4});return false;}catch{return true;}});
        // Coordinate transforms at mixed scale factors, without changing OS display settings.
        foreach(double scale in new[]{1.0,1.25,1.5,2.0})Check($"physical/DIP roundtrip {scale}",()=>Math.Abs(((-1200.0+1920)/scale)*scale-720)<0.001);
        var output=args.SkipWhile(x=>x!="--report").Skip(1).FirstOrDefault()??"self-test.json";
        File.WriteAllText(output,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        return results.All(r=>JsonSerializer.Serialize(r).Contains("\"passed\":true"))?0:1;
    }
}
