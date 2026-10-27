using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace ScreenshotAssistant;
internal static class RichMathChecks
{
    public static int Run(string path)
    {
        var app=new App();var view=new RichMarkdownAnswer();
        var window=new Window{Width=1000,Height=850,WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,Content=view,ShowInTaskbar=false};
        int exit=1;var checks=new List<object>();int failures=0;
        void Check(string name,bool passed){if(!passed)failures++;checks.Add(new{name,passed});}
        async Task<JsonElement> Render(string text,bool pending=false)
        {
            using var json=JsonDocument.Parse(await view.Inspect($"renderAnswer({JsonSerializer.Serialize(text)},18,{(pending?"true":"false")})"));return json.RootElement.Clone();
        }
        window.Loaded+=async(_,_)=>
        {
            try
            {
                await view.Ready.WaitAsync(TimeSpan.FromSeconds(30));await Task.Delay(250);
                foreach(var (name,tex) in MathCorpus.Cases)
                {
                    var result=await Render("\\[\n"+tex+"\n\\]");Check(name,result.GetProperty("count").GetInt32()==1&&result.GetProperty("errors").GetInt32()==0);
                }
                var mixed=await Render("# 标题\n\n**粗体**，行内 \\(x^2\\) 和 $y_1$。\n\n$$\\frac12$$\n\n- 第一项\n- 第二项");
                Check("Markdown and three math delimiters",mixed.GetProperty("count").GetInt32()==3&&await view.Inspect("!!document.querySelector('h1')&&!!document.querySelector('strong')&&!!document.querySelector('ul')")=="true");
                await Render("```tex\n\\[\\frac12\\]\n```\n\n`$x$`");Check("code remains literal",await view.Inspect("document.querySelectorAll('.katex').length===0 && !!document.querySelector('pre code')")=="true");
                await Render("\\$5 and $10 and $20");Check("currency literal",await view.Inspect("document.querySelectorAll('.katex').length===0")=="true");
                var numeric=await Render("数值 $2x+1$ 和 $42$。");Check("numeric inline formulas",numeric.GetProperty("count").GetInt32()==2);
                var environment=await Render(@"\begin{equation}x^2+y^2=z^2\end{equation}");Check("standalone equation environment",environment.GetProperty("count").GetInt32()==1&&environment.GetProperty("errors").GetInt32()==0);
                bool streamed=true;string formula="\\["+MathCorpus.Cases[5].Tex+"\\]";
                for(int i=1;i<=formula.Length;i+=7){var r=await Render(formula[..i],true);if(r.GetProperty("errors").GetInt32()!=0)streamed=false;}
                var complete=await Render(formula);Check("streaming cases",streamed&&complete.GetProperty("count").GetInt32()==1);
                await Render(@"\[\frac{1}",true);Check("incomplete streaming",await view.Inspect("document.querySelector('.math-pending').textContent.includes('生成中')")=="true");
                var bad=await Render(@"\[\nonexistentcommand{x}\]");Check("unsupported graceful error",bad.GetProperty("errors").GetInt32()==1);
                await Render("<img src='https://example.invalid/a' onerror='window.pwned=1'><script>window.pwned=1</script>\n\n[link](https://example.invalid)\n\n\\[\\href{javascript:alert(1)}{x}\\]");
                Check("untrusted HTML and math inert",await view.Inspect("!window.pwned&&!document.querySelector('#answer img,#answer script,#answer [onerror],#answer [href]')")=="true");
                await Render(@"\[\def\a{\a}\a\]");Check("macro expansion bounded",await view.Inspect("!!document.querySelector('.math-error')")=="true");
                await Render(@"\[\newcommand{\privatecmd}{x}\privatecmd\]");var isolated=await Render(@"\[\privatecmd\]");Check("macros isolated between answers",isolated.GetProperty("errors").GetInt32()==1);
                // Visual sheets exercise real fonts, transparent WPF hosting and long horizontal scrolling.
                for(int sheet=0;sheet<3;sheet++)
                {
                    var samples=sheet==0?MathCorpus.Cases.Take(6):sheet==1?MathCorpus.Cases.Skip(17).Take(9):MathCorpus.Cases.Skip(26).Take(10);
                    await Render(string.Join("\n\n",samples.Select(c=>"### "+c.Name+"\n\n\\["+c.Tex+"\\]")));
                    await view.Inspect("document.fonts.ready.then(()=>true)");
                    await view.Inspect("document.body.style.zoom='0.75'");await Task.Delay(200);
                    using var file=File.Create(path+$".{sheet}.png");await view.Capture(file);
                }
                Check("wide formulas scroll inside answer",await view.Inspect("Array.from(document.querySelectorAll('.katex-display')).every(x=>getComputedStyle(x).overflowX==='auto')")=="true");
                await view.Inspect("document.body.style.zoom='1'");
                view.Text="### 实际流式回答\n\n";await Task.Delay(200);
                view.AppendText(@"\[\begin{cases}x&x>0\\0&x\le0\end{cases}\]");await Task.Delay(350);
                Check("production AppendText",view.FormulaCount==1&&view.FormulaErrors==0);
                string original=view.Text;view.FontSize=24;view.RefreshTypography();await Task.Delay(250);
                Check("font change and copy source",view.Text==original&&await view.Inspect("getComputedStyle(document.documentElement).fontSize==='24px'")=="true");
                window.Width=480;await Task.Delay(200);
                Check("narrow window",await view.Inspect("document.documentElement.scrollWidth<=window.innerWidth")=="true");
                var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(view);
                var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var f=File.Create(path+".wpf.png"))encoder.Save(f);
                window.Hide();window.Show();await Task.Delay(200);Check("hide and restore",await view.Inspect("!!document.querySelector('.katex')")=="true");
                view.Clear();await Task.Delay(250);Check("clear response",view.Text==""&&await view.Inspect("document.getElementById('answer').textContent===''")=="true");
                exit=failures==0?0:1;
            }
            catch(Exception e){checks.Add(new{error=e.ToString()});}
            finally{File.WriteAllText(path,JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));window.Close();}
        };
        app.Run(window);return exit;
    }
}
