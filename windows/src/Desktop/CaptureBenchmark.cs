using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace ScreenshotAssistant;
internal static class CaptureBenchmark
{
    public static int Run(string path)
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int exit=0;
        app.Startup+=async(_,_)=>
        {
            try
            {
                var samples=new List<double>();
                for(int i=0;i<5;i++)
                {
                    var timer=Stopwatch.StartNew();using var capture=await CaptureSession.CreateAsync();samples.Add(timer.Elapsed.TotalMilliseconds);
                }
                File.WriteAllText(path,JsonSerializer.Serialize(new{kind="real desktop capture + window construction; windows not shown; no image saved",milliseconds=samples,median=samples.Order().ElementAt(2)},new JsonSerializerOptions{WriteIndented=true}));
            }
            catch(Exception error){exit=1;File.WriteAllText(path,error.GetType().Name);}
            finally{app.Shutdown();}
        };
        app.Run();return exit;
    }
}
