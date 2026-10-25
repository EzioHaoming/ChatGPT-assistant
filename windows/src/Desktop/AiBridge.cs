using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ScreenshotAssistant;

internal sealed record BridgeConfig(string Python, string Script, string Codex);
internal static class AiBridge
{
    public static async Task Run(string action, string question, string png, Action<string> delta, Action<string,string> done, CancellationToken cancel, string model="gpt-6-luna")
    {
        if(ApiService.Current.Enabled&&action=="ask")
        {
            var result=await ApiService.Ask(ApiService.Current,question,png,cancel);done(result.Answer,result.Usage);return;
        }
        if(action=="models"&&ApiService.Current.Enabled){done(JsonSerializer.Serialize(new[]{ApiService.Current.Model}),"");return;}
        var configPath=Path.Combine(AppContext.BaseDirectory,"bridge-config.json");
        if(!File.Exists(configPath))throw new InvalidOperationException("请先在设置中配置自定义 API。ChatGPT 授权模式需要单独安装并配置 OAuth 桥接工具。");
        var cfg=JsonSerializer.Deserialize<BridgeConfig>(await File.ReadAllTextAsync(configPath,cancel))!;
        if(!File.Exists(cfg.Python))throw new IOException("找不到桥接 Python，请检查 bridge-config.json 的 Python 路径。");
        if(!File.Exists(cfg.Script))throw new IOException("找不到桥接脚本，请检查 bridge-config.json 的 Script 路径。");
        cfg=cfg with{Codex=CodexLocator.Resolve(cfg.Codex)};
        var info=new ProcessStartInfo(cfg.Python){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8};
        info.ArgumentList.Add(cfg.Script);
        info.Environment.Remove("OPENAI_API_KEY");info.Environment.Remove("OPENAI_BASE_URL");
        using var proc=Process.Start(info)!;
        using var registration=cancel.Register(()=>{try{proc.Kill(true);}catch{}});
        // Drain stderr without logging potentially sensitive service content.
        var drain=Task.Run(async()=>{while(await proc.StandardError.ReadLineAsync() is not null){};});
        try
        {
            await proc.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{action,question,png,model,codex=cfg.Codex}));
            proc.StandardInput.Close();
            bool completed=false;
            while(await proc.StandardOutput.ReadLineAsync(cancel) is {} line)
            {
                using var doc=JsonDocument.Parse(line);var e=doc.RootElement;
                switch(e.GetProperty("type").GetString())
                {
                    case "delta":delta(e.GetProperty("text").GetString()??"");break;
                    case "error":throw new InvalidOperationException(e.GetProperty("message").GetString());
                    case "done":
                        string usage="tokens 未返回；套餐扣减请在用量页面核对";
                        if(e.TryGetProperty("usage",out var u)&&u.ValueKind==JsonValueKind.Object&&u.TryGetProperty("last",out var last)&&last.TryGetProperty("totalTokens",out var total))usage=$"本次 {total} tokens；不等于套餐扣减百分比";
                        done(e.GetProperty("answer").GetString()??"",usage);completed=true;break;
                }
            }
            await proc.WaitForExitAsync(cancel);await drain;
            if(!completed||proc.ExitCode!=0)throw new IOException("桥接进程异常结束，未得到完整回答。");
        }
        finally{try{if(!proc.HasExited)proc.Kill(true);}catch{}}
    }
}
