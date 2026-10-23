using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ScreenshotAssistant;
internal static class ApiChecks
{
    sealed class Handler:HttpMessageHandler
    {
        internal HttpStatusCode Status=HttpStatusCode.OK;
        internal string Body="{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"测试回答\"}}],\"usage\":{\"total_tokens\":12}}";
        internal string? Payload,Auth,Url;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel){cancel.ThrowIfCancellationRequested();Payload=await request.Content!.ReadAsStringAsync(cancel);Auth=request.Headers.Authorization?.ToString();Url=request.RequestUri!.ToString();return new(Status){Content=new StringContent(Body,Encoding.UTF8,"application/json")};}
    }
    internal static int Run(string path)
    {
        var results=new List<object>();
        void Check(string name,Func<bool> test){try{results.Add(new{name,passed=test()});}catch(Exception e){results.Add(new{name,passed=false,error=e.GetType().Name});}}
        string protectedKey=ApiService.Protect("test-only-key");var config=new ApiOptions(true,"https://example.invalid/v1","test-vision",protectedKey);
        Check("DPAPI roundtrip and no plaintext",()=>!protectedKey.Contains("test-only-key")&&ApiService.Unprotect(protectedKey)=="test-only-key");
        Check("endpoint preserves base path",()=>ApiService.Endpoint("https://example.invalid/compatible-mode/v1/").AbsolutePath=="/compatible-mode/v1/chat/completions");
        foreach(var url in new[]{"http://example.invalid/v1","https://user:pass@example.invalid/v1","https://example.invalid/v1?key=secret","https://example.invalid/v1/chat/completions"})Check("reject unsafe/malformed endpoint "+url.Split('?')[0],()=>{try{_=ApiService.Endpoint(url);return false;}catch(InvalidOperationException){return true;}});
        Check("allow local HTTP",()=>ApiService.Endpoint("http://127.0.0.1:1234/v1").IsLoopback);
        Check("PNG model auth and answer protocol",()=>{var h=new Handler();var r=ApiService.Ask(config,"translate","dGVzdA==",CancellationToken.None,h).GetAwaiter().GetResult();using var d=JsonDocument.Parse(h.Payload!);return r.Answer=="测试回答"&&r.Usage.Contains("12")&&h.Auth=="Bearer test-only-key"&&d.RootElement.GetProperty("model").GetString()=="test-vision"&&d.RootElement.GetProperty("messages")[0].GetProperty("content")[1].GetProperty("image_url").GetProperty("url").GetString()=="data:image/png;base64,dGVzdA==";});
        Check("text only has no image attachment",()=>{var h=new Handler();ApiService.Ask(config,"hello","",CancellationToken.None,h).GetAwaiter().GetResult();using var d=JsonDocument.Parse(h.Payload!);var c=d.RootElement.GetProperty("messages")[0].GetProperty("content");return c.GetArrayLength()==1&&c[0].GetProperty("type").GetString()=="text";});
        foreach(var code in new[]{HttpStatusCode.Unauthorized,HttpStatusCode.TooManyRequests,HttpStatusCode.Redirect})Check("safe HTTP error "+code,()=>{try{ApiService.Ask(config,"secret-question","image",CancellationToken.None,new Handler{Status=code,Body="private-server-error"}).GetAwaiter().GetResult();return false;}catch(InvalidOperationException e){return e.Message.Contains(((int)code).ToString())&&!e.Message.Contains("private-server-error");}});
        Check("reject empty answer",()=>{try{ApiService.Ask(config,"q","p",CancellationToken.None,new Handler{Body="{\"choices\":[{\"message\":{\"content\":\"\"}}]}"}).GetAwaiter().GetResult();return false;}catch(InvalidOperationException){return true;}});
        Check("reject malformed response",()=>{try{ApiService.Ask(config,"q","p",CancellationToken.None,new Handler{Body="{}"}).GetAwaiter().GetResult();return false;}catch(InvalidOperationException){return true;}});
        Check("cancellation",()=>{try{ApiService.Ask(config,"q","p",new CancellationToken(true),new Handler()).GetAwaiter().GetResult();return false;}catch(OperationCanceledException){return true;}});
        File.WriteAllText(path,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        return results.All(r=>JsonSerializer.Serialize(r).Contains("\"passed\":true"))?0:1;
    }
}
