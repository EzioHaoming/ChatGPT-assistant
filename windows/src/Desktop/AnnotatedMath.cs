using System.IO;
using System.Text.RegularExpressions;
using CSharpMath.SkiaSharp;
using SkiaSharp;

namespace ScreenshotAssistant;

// Compose brace annotations around native math runs without discarding their labels.
internal static class AnnotatedMath
{
    sealed record Box(float Width,float Ascent,float Descent,Action<SKCanvas,float,float> Draw);
    public static Stream Render(string latex,float size)
    {
        var box=Parse(latex,size,0);
        int width=(int)Math.Ceiling(box.Width+8),height=(int)Math.Ceiling(box.Ascent+box.Descent+8);
        if(width>16000||height>8000||(long)width*height>32000000)throw new InvalidOperationException("Formula too large");
        using var surface=SKSurface.Create(new SKImageInfo(Math.Max(1,width),Math.Max(1,height)));
        surface.Canvas.Clear(SKColors.Transparent);box.Draw(surface.Canvas,4,4+box.Ascent);
        using var image=surface.Snapshot();using var data=image.Encode(SKEncodedImageFormat.Png,100);
        return new MemoryStream(data.ToArray());
    }
    static Box Native(string text,float size)
    {
        var painter=new MathPainter{LaTeX=text,FontSize=size};
        if(painter.ErrorMessage is not null)throw new FormatException(painter.ErrorMessage);
        painter.Measure();var display=painter.Display;
        if(display is null)throw new FormatException("Missing formula display");
        return new(display.Width,display.Ascent,display.Descent,(canvas,x,y)=>painter.Draw(canvas,x,y));
    }
    static Box Parse(string text,float size,int depth)
    {
        if(depth>16)throw new FormatException("Annotation nesting too deep");
        var parts=new List<Box>();int start=0,level=0;
        for(int i=0;i<text.Length;)
        {
            if(text[i]=='\\')
            {
                var match=Regex.Match(text[i..],@"^\\(underbrace|overbrace)\b");
                if(match.Success&&level==0)
                {
                    if(i>start)parts.Add(Native(text[start..i],size));
                    bool over=match.Groups[1].Value=="overbrace";i+=match.Length;
                    string body=Group(text,ref i);Skip(text,ref i);string label="";
                    if(i<text.Length&&text[i]==(over?'^':'_')){i++;label=Group(text,ref i);}
                    parts.Add(Brace(Parse(body,size,depth+1),Parse(label,size*.72f,depth+1),over,size));start=i;continue;
                }
                // Skip escaped braces and control words when tracking group depth.
                i++;if(i<text.Length&&char.IsLetter(text[i]))while(i<text.Length&&char.IsLetter(text[i]))i++;else if(i<text.Length)i++;
                continue;
            }
            if(text[i]=='{')level++;else if(text[i]=='}')level--;i++;
        }
        if(start<text.Length)parts.Add(Native(text[start..],size));
        if(parts.Count==0)return new(0,0,0,(_,_,_)=>{});
        return new(parts.Sum(p=>p.Width),parts.Max(p=>p.Ascent),parts.Max(p=>p.Descent),(canvas,x,y)=>{foreach(var p in parts){p.Draw(canvas,x,y);x+=p.Width;}});
    }
    static void Skip(string s,ref int i){while(i<s.Length&&char.IsWhiteSpace(s[i]))i++;}
    static string Group(string s,ref int i)
    {
        Skip(s,ref i);if(i>=s.Length||s[i]!='{')throw new FormatException("Expected annotation group");
        int start=++i,depth=1;
        while(i<s.Length)
        {
            if(s[i]=='\\'){i+=Math.Min(2,s.Length-i);continue;}
            if(s[i]=='{')depth++;else if(s[i]=='}'&&--depth==0){string value=s[start..i];i++;return value;}i++;
        }
        throw new FormatException("Unclosed annotation group");
    }
    static Box Brace(Box body,Box label,bool over,float size)
    {
        float width=Math.Max(body.Width,label.Width),gap=size*.12f,h=size*.22f;
        float extra=gap+h+(label.Width>0?gap+label.Ascent+label.Descent:0);
        return new(width,body.Ascent+(over?extra:0),body.Descent+(over?0:extra),(canvas,x,y)=>
        {
            body.Draw(canvas,x+(width-body.Width)/2,y);
            float left=x+(width-body.Width)/2,w=body.Width,edge=y+(over?-body.Ascent-gap:body.Descent+gap),sign=over?-1:1;
            using var path=new SKPath();
            path.MoveTo(left,edge);path.CubicTo(left,edge+sign*h*.5f,left+w*.06f,edge+sign*h*.5f,left+w*.12f,edge+sign*h*.5f);
            path.LineTo(left+w*.42f,edge+sign*h*.5f);path.CubicTo(left+w*.48f,edge+sign*h*.5f,left+w*.5f,edge+sign*h*.5f,left+w*.5f,edge+sign*h);
            path.CubicTo(left+w*.5f,edge+sign*h*.5f,left+w*.52f,edge+sign*h*.5f,left+w*.58f,edge+sign*h*.5f);
            path.LineTo(left+w*.88f,edge+sign*h*.5f);path.CubicTo(left+w*.94f,edge+sign*h*.5f,left+w,edge+sign*h*.5f,left+w,edge);
            using var pen=new SKPaint{Color=SKColors.Black,Style=SKPaintStyle.Stroke,StrokeWidth=Math.Max(1,size*.035f),IsAntialias=true};canvas.DrawPath(path,pen);
            if(label.Width>0)label.Draw(canvas,x+(width-label.Width)/2,over?edge-h-gap-label.Descent:edge+h+gap+label.Ascent);
        });
    }
}
