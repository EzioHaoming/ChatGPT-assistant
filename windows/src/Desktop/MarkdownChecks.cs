using System.IO;
using System.Text.Json;
using System.Windows.Documents;

namespace ScreenshotAssistant;
internal static class MarkdownChecks
{
    public static int Run(string path)
    {
        var checks=new List<object>();int failures=0;
        void Check(string name,Func<bool> test){bool passed;try{passed=test();}catch{passed=false;}if(!passed)failures++;checks.Add(new{name,passed});}
        var view=new MarkdownAnswer{FontSize=18};
        foreach(var formula in new[]{@"\alpha=\frac{1}{2}",@"x\qquad y",@"\lambda=\frac{1}{8}\times 0.001"})
        {
            Check("formula "+formula,()=>{view.Text=@"\["+formula+@"\]";return view.FormulaCount==1&&view.FormulaErrors==0;});
        }
        string Visible()=>new TextRange(view.Document.ContentStart,view.Document.ContentEnd).Text;
        const string annotated=@"\min_{\theta\in\mathbb{R}^d} F(\theta) := \underbrace{\mathbb{E}_{\xi\sim\mathbb{P}}[f(\theta;\xi)]}_{f(\theta)}+\mathcal{R}(\theta)\tag{1}";
        Check("user expectation brace equation",()=>{view.Text=@"\["+annotated+@"\]";return view.FormulaCount==1&&view.FormulaErrors==0&&view.Text.Contains(annotated);});
        const string sized=@"\min_{\theta\in\mathbb{R}^d} F(\theta):=\mathbb{E}_{\xi\sim\mathbb{P}}\bigl[f(\theta;\xi)\bigr]+\mathcal{R}(\theta)\tag{1}";
        Check("user expectation sized bracket equation",()=>{view.Text=@"\["+sized+@"\]";return view.FormulaCount==1&&view.FormulaErrors==0&&view.Text.Contains(sized);});
        foreach(string size in new[]{"big","Big","bigg","Bigg"})
            Check(size+" paired brackets",()=>{view.Text=@"\[\"+size+@"l[\frac{x}{y}\"+size+@"r]\]";return view.FormulaCount==1&&view.FormulaErrors==0;});
        Check("sized brackets within brace",()=>{view.Text=@"\[\underbrace{\bigl[f(x)\bigr]}_{f(x)}\]";return view.FormulaCount==1&&view.FormulaErrors==0;});
        Check("sized separators",()=>{view.Text=@"\[\Big(x\Big)\bigm|y\]";return view.FormulaCount==1&&view.FormulaErrors==0;});
        Check("upper brace and nested labels",()=>{view.Text=@"\[a+\overbrace{x_{i}^{2}+y}^{\frac{a}{b}}=z\]";return view.FormulaCount==1&&view.FormulaErrors==0;});
        Check("nested braces",()=>{view.Text=@"\[\underbrace{a+\overbrace{x+y}^{z}}_{f(x)}\]";return view.FormulaCount==1&&view.FormulaErrors==0;});
        Check("brace streaming",()=>{view.Clear();foreach(char c in @"\["+annotated+@"\]"){view.AppendText(c.ToString());view.FlushPreview();}return view.FormulaCount==1&&view.FormulaErrors==0;});
        using(var png=AnnotatedMath.Render(annotated.Replace(@"\tag{1}",@"\qquad(1)"),36))
        using(var file=File.Create(path+".png"))png.CopyTo(file);
        Check("paper preconditioner",()=>{view.Text=@"\(M_t=\operatorname{diag}(\mathbf{s})^{-1}\)";return view.FormulaCount==1&&view.FormulaErrors==0;});
        Check("paper bold theta",()=>{view.Text=@"\(f_t(\boldsymbol{\theta})\)";return view.FormulaCount==1&&view.FormulaErrors==0;});
        Check("paper multiline equation",()=>{view.Text=PaperEquation;return view.FormulaCount==1&&view.FormulaErrors==0&&!Visible().Contains(@"\frac")&&!Visible().Contains("未闭合");});
        Check("paper streaming every character",()=>{view.Clear();foreach(char c in PaperEquation){view.AppendText(c.ToString());view.FlushPreview();if(Visible().Contains(@"\frac"))return false;}return view.FormulaCount==1&&view.FormulaErrors==0;});
        Check("headings emphasis paragraphs",()=>{view.Text="# Heading\n\n**bold** and *italic*\n\nNext paragraph";return view.Document.Blocks.Count==3&&!Visible().Contains("**")&&view.Document.Blocks.FirstBlock.FontSize>18;});
        Check("four math delimiters",()=>{view.Text=@"行内 \(x^2\) 和 $y_1$。"+"\n\n"+@"\[\frac{1}{2}\]"+"\n\n$$\n\\sum_{i=1}^{n}i\n$$";return view.FormulaCount==4&&view.FormulaErrors==0&&!Visible().Contains(@"\frac");});
        Check("code remains literal",()=>{view.Text="```tex\n\\(x^2\\) $x$\n```\n\n`\\frac{1}{2}`";return view.FormulaCount==0&&Visible().Contains(@"\(x^2\)")&&Visible().Contains(@"\frac{1}{2}");});
        Check("streaming incomplete then complete",()=>{view.Clear();view.AppendText(@"公式 \(\frac{1}");view.FlushPreview();bool pending=Visible().Contains("公式生成中")&&!Visible().Contains(@"\frac");view.AppendText(@"{2}\)");view.FlushPreview();return pending&&view.FormulaCount==1&&view.FormulaErrors==0;});
        Check("unsupported math fallback",()=>{view.Text=@"\(\nonexistentcommand{a}\)";return view.FormulaErrors==1&&Visible().Contains("公式暂不支持")&&!Visible().Contains("nonexistentcommand");});
        Check("source preserved for copying",()=>{string raw=@"**加粗** 与 \(x^2\)";view.Text=raw;return view.Text==raw;});
        Check("html and remote image inert",()=>{view.Text="<script>alert(1)</script>\n\n![remote](https://example.invalid/image.png)";return !Visible().Contains("alert(1)")&&Visible().Contains("图片");});
        Check("escaped dollars and currency",()=>{view.Text=@"\$5 and $10 and $20";return view.FormulaCount==0;});
        Check("lists and quote",()=>{view.Text="- first\n- second\n\n> quote";return view.Document.Blocks.FirstBlock is System.Windows.Documents.List&&Visible().Contains("quote");});
        view.Text=@"\["+sized+@"\]";
        view.Background=System.Windows.Media.Brushes.White;
        view.Measure(new System.Windows.Size(1000,180));view.Arrange(new System.Windows.Rect(0,0,1000,180));view.UpdateLayout();
        var preview=new System.Windows.Media.Imaging.RenderTargetBitmap(1000,180,96,96,System.Windows.Media.PixelFormats.Pbgra32);preview.Render(view);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(preview));
        using(var file=File.Create(path+".sized.png"))encoder.Save(file);
        File.WriteAllText(path,JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));return failures==0?0:1;
    }
    internal const string PaperEquation="""
\[
f_t^{sreg}(\boldsymbol{\theta})=
f_t(\boldsymbol{\theta})
+

\frac{\lambda'}{2\alpha}
\left\|\boldsymbol{\theta}\odot\sqrt{\mathbf{s}}\right\|_2^2,
\tag{2}
\]
""";
}
