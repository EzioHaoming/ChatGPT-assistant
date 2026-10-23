using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using CSharpMath.SkiaSharp;
using Block = System.Windows.Documents.Block;
using Inline = System.Windows.Documents.Inline;

namespace ScreenshotAssistant;

// Build WPF objects from a Markdown AST; never interpret model text as XAML or HTML.
internal sealed class MarkdownAnswer : RichTextBox
{
    readonly MarkdownPipeline pipeline;
    readonly ProtectedMathParser protectedMath=new();
    readonly Dictionary<string,BitmapSource> formulaCache=new();
    readonly DispatcherTimer renderTimer=new(){Interval=TimeSpan.FromMilliseconds(160)};
    string source="";
    bool streaming;
    public int FormulaCount {get;private set;}
    public int FormulaErrors {get;private set;}
    public string Text {get=>source;set{source=value??"";streaming=false;renderTimer.Stop();Render();}}
    public MarkdownAnswer()
    {
        IsReadOnly=true;IsDocumentEnabled=true;VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
        var builder=new MarkdownPipelineBuilder();builder.InlineParsers.Insert(0,new LatexParser());builder.InlineParsers.Insert(0,protectedMath);pipeline=builder.Build();
        renderTimer.Tick+=(_,_)=>{renderTimer.Stop();Render();};
        Unloaded+=(_,_)=>renderTimer.Stop();
    }
    public new void AppendText(string value){source+=value;streaming=true;if(!renderTimer.IsEnabled)renderTimer.Start();}
    public void Clear(){Text="";}
    public void RefreshTypography(){Render();}
    public void FlushPreview(){renderTimer.Stop();Render();}
    void Render()
    {
        double offset=VerticalOffset;
        FormulaCount=FormulaErrors=0;
        var doc=new FlowDocument{FontFamily=FontFamily,FontSize=FontSize,PagePadding=new Thickness(0),LineHeight=FontSize*1.65,ColumnWidth=100000};
        try
        {
            // Bound re-parsing and rendering work for unusually large model responses.
            var input=source.Length>100000?source[..100000]+"\n\n（显示长度已达上限，复制回答可获得完整原文。）":source;
            foreach(var block in Markdown.Parse(protectedMath.Protect(input),pipeline))doc.Blocks.Add(RenderBlock(block));
        }
        catch
        {
            doc.Blocks.Clear();doc.Blocks.Add(new Paragraph(new Run("此段内容暂时无法排版，可复制回答查看原文。")));
        }
        Document=doc;ScrollToVerticalOffset(offset);
    }
    Block RenderBlock(Markdig.Syntax.Block block)
    {
        switch(block)
        {
            case HeadingBlock heading:
                var h=Paragraph(heading.Inline);h.FontWeight=FontWeights.SemiBold;h.FontSize=FontSize*(heading.Level==1?1.45:heading.Level==2?1.25:1.1);h.Margin=new Thickness(0,16,0,10);return h;
            case ParagraphBlock paragraph:return Paragraph(paragraph.Inline);
            case CodeBlock code:
                return new Paragraph(new Run(code.Lines.ToString())){FontFamily=new FontFamily("Consolas"),FontSize=FontSize*.85,Background=new SolidColorBrush(Color.FromRgb(242,244,248)),Padding=new Thickness(12),Margin=new Thickness(0,8,0,14),LineHeight=FontSize*1.4};
            case ListBlock list:
                var result=new System.Windows.Documents.List{MarkerStyle=list.IsOrdered?TextMarkerStyle.Decimal:TextMarkerStyle.Disc,Margin=new Thickness(0,6,0,12),Padding=new Thickness(24,0,0,0)};
                if(list.IsOrdered&&int.TryParse(list.OrderedStart,out int start)&&start>0)result.StartIndex=start;
                foreach(var item in list.OfType<ListItemBlock>()){var li=new ListItem();foreach(var child in item)li.Blocks.Add(RenderBlock(child));result.ListItems.Add(li);}return result;
            case QuoteBlock quote:
                var section=new Section{BorderBrush=Brushes.LightSteelBlue,BorderThickness=new Thickness(3,0,0,0),Padding=new Thickness(14,0,0,0),Foreground=Brushes.DimGray,Margin=new Thickness(0,8,0,14)};
                foreach(var child in quote)section.Blocks.Add(RenderBlock(child));return section;
            case ThematicBreakBlock:return new Paragraph{BorderBrush=Brushes.LightGray,BorderThickness=new Thickness(0,0,0,1),Margin=new Thickness(0,12,0,12)};
            case HtmlBlock:return new Paragraph(new Run("（HTML 内容未执行）"));
            case ContainerBlock container:
                var group=new Section();foreach(var child in container)group.Blocks.Add(RenderBlock(child));return group;
            default:return new Paragraph();
        }
    }
    Paragraph Paragraph(ContainerInline? content)
    {
        var result=new Paragraph{Margin=new Thickness(0,0,0,14)};
        if(content!=null)
        {
            if(content.FirstChild is LatexInline {Display:true} math&&math.NextSibling==null)result.TextAlignment=TextAlignment.Center;
            foreach(var item in content)result.Inlines.Add(RenderInline(item));
        }
        return result;
    }
    Inline RenderInline(Markdig.Syntax.Inlines.Inline item)
    {
        switch(item)
        {
            case LatexInline math:
                if(!math.Complete)return new Run(streaming?"〔公式生成中…〕":"〔公式未闭合，可复制原文检查〕"){Foreground=Brushes.SlateGray};
                try
                {
                    if(math.Source.Length>4000)throw new InvalidOperationException();
                    var normalized=Regex.Replace(math.Source,@"\\tag\{([^{}]*)\}",m=>@"\qquad("+m.Groups[1].Value+")");
                    normalized=Regex.Replace(normalized,@"\\(?:boldsymbol|bm)\b",@"\mathbf");
                    normalized=Regex.Replace(normalized,@"\\[A-Za-z]+(?=[^A-Za-z\s]|$)","$0 ");
                    string cacheKey=$"{FontSize}:{math.Display}:{normalized}";
                    if(!formulaCache.TryGetValue(cacheKey,out var bitmap))
                    {
                        var painter=new MathPainter{LaTeX=normalized,FontSize=(float)(FontSize*2)};
                        if(painter.ErrorMessage is not null)throw new InvalidOperationException(painter.ErrorMessage);
                        using var png=painter.DrawAsStream()??throw new InvalidOperationException("Formula image unavailable");
                        var decoded=new BitmapImage();decoded.BeginInit();decoded.CacheOption=BitmapCacheOption.OnLoad;decoded.StreamSource=png;decoded.EndInit();decoded.Freeze();bitmap=decoded;
                        if(formulaCache.Count>=256)formulaCache.Clear();formulaCache[cacheKey]=bitmap;
                    }
                    FormulaCount++;
                    var image=new Image{Source=bitmap,Width=bitmap.PixelWidth/2.0,Height=bitmap.PixelHeight/2.0,Stretch=Stretch.Uniform,Margin=new Thickness(3,math.Display?8:0,3,math.Display?8:0),ToolTip="数学公式（复制回答可保留原始 LaTeX）"};
                    System.Windows.Automation.AutomationProperties.SetName(image,"数学公式");
                    return new InlineUIContainer(image){BaselineAlignment=BaselineAlignment.Center};
                }
                catch{FormulaErrors++;return new Run("〔公式暂不支持，可复制原文〕"){Foreground=Brushes.DarkOrange};}
            case LiteralInline literal:return new Run(literal.Content.ToString());
            case CodeInline code:return new Run(code.Content){FontFamily=new FontFamily("Consolas"),Background=Brushes.WhiteSmoke,FontSize=FontSize*.9};
            case LineBreakInline:return new LineBreak();
            case EmphasisInline emphasis:
                Span styled=emphasis.DelimiterCount>=2?new Bold():new Italic();foreach(var child in emphasis)styled.Inlines.Add(RenderInline(child));return styled;
            case LinkInline link:
                // No navigation or image downloads from generated content.
                var label=new Span();if(link.IsImage)label.Inlines.Add(new Run("〔图片："));foreach(var child in link)label.Inlines.Add(RenderInline(child));if(link.IsImage)label.Inlines.Add(new Run("〕"));return label;
            case AutolinkInline autoLink:return new Run(autoLink.Url);
            case HtmlInline:return new Run("");
            case ContainerInline container:
                var span=new Span();foreach(var child in container)span.Inlines.Add(RenderInline(child));return span;
            default:return new Run("");
        }
    }
}

// Protect display math BEFORE Markdown's block parser sees +, _, blank lines,
// or equation tags. An inline parser alone cannot prevent block splitting.
internal sealed class ProtectedMathParser:InlineParser
{
    readonly List<(string Text,bool Complete)> formulas=[];
    public ProtectedMathParser(){OpeningCharacters=['\uE000'];}
    public string Protect(string input)
    {
        formulas.Clear();var output=new StringBuilder();int i=0;
        while(i<input.Length)
        {
            // Skip code spans and fenced code, including an unfinished fence.
            if(input[i]=='`'||(input[i]=='~'&&(i==0||input[i-1]=='\n')))
            {
                char mark=input[i];int count=1;while(i+count<input.Length&&input[i+count]==mark)count++;
                if(mark=='`'||count>=3)
                {
                    string delimiter=new(mark,count);int end=input.IndexOf(delimiter,i+count,StringComparison.Ordinal);
                    int length=end<0?input.Length-i:end+count-i;output.Append(input,i,length);i+=length;continue;
                }
            }
            bool bracket=input.AsSpan(i).StartsWith(@"\[");bool dollars=input.AsSpan(i).StartsWith("$$");
            if((bracket||dollars)&&(i==0||input[i-1]!='\\'))
            {
                string close=bracket?@"\]":"$$";int end=input.IndexOf(close,i+2,StringComparison.Ordinal);bool complete=end>=0;
                if(!complete)end=input.Length;
                int id=formulas.Count;formulas.Add((input.Substring(i+2,end-i-2),complete));
                output.Append('\uE000').Append(id).Append('\uE001');i=complete?end+2:end;continue;
            }
            output.Append(input[i++]);
        }
        return output.ToString();
    }
    public override bool Match(InlineProcessor processor,ref StringSlice slice)
    {
        int end=slice.Text.IndexOf('\uE001',slice.Start+1);
        if(end<0||end>slice.End||!int.TryParse(slice.Text.AsSpan(slice.Start+1,end-slice.Start-1),out int id)||id<0||id>=formulas.Count)return false;
        var item=formulas[id];processor.Inline=new LatexInline(item.Text,true,item.Complete);slice.Start=end+1;return true;
    }
}

internal sealed class LatexInline(string source,bool display,bool complete):LeafInline
{
    public string Source {get;}=source;
    public bool Display {get;}=display;
    public bool Complete {get;}=complete;
}

// Runs before Markdown backslash escaping. Code spans/fences are consumed by
// Markdig as a unit, so their LaTeX-like content is never transformed.
internal sealed class LatexParser:InlineParser
{
    public LatexParser(){OpeningCharacters=['\\','$'];}
    public override bool Match(InlineProcessor processor,ref StringSlice slice)
    {
        int start=slice.Start;string input=slice.Text;
        bool slash=slice.CurrentChar=='\\';
        if(slash&&slice.PeekChar()!='('&&slice.PeekChar()!='[')return false;
        bool display=slash?slice.PeekChar()=='[':slice.PeekChar()=='$';
        int size=slash||display?2:1;
        if(!slash&&!display&&(char.IsWhiteSpace(slice.PeekChar())||slice.PeekChar()=='\0'))return false;
        string close=slash?(display?@"\]":@"\)"):(display?"$$":"$");
        int end=input.IndexOf(close,start+size,StringComparison.Ordinal);
        while(end>=0&&end>0&&input[end-1]=='\\')end=input.IndexOf(close,end+close.Length,StringComparison.Ordinal);
        bool complete=end>=0&&end+close.Length-1<=slice.End;
        if(!complete&&!slash&&!display&&char.IsDigit(slice.PeekChar()))return false;
        // Avoid treating prices such as "$5 and $10" as a formula.
        if(!slash&&!display&&complete&&end>start+1&&char.IsWhiteSpace(input[end-1]))return false;
        if(!complete)end=slice.End+1;
        processor.Inline=new LatexInline(input.Substring(start+size,Math.Max(0,end-start-size)),display,complete);
        slice.Start=complete?end+close.Length:slice.End+1;return true;
    }
}
