using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ScreenshotAssistant;
internal sealed record ApiOptions(bool Enabled=false,string BaseUrl="",string Model="",string ProtectedKey="");
internal static class ApiService
{
    internal static string Folder=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ScreenshotAssistant");
    static string ConfigFile=>Path.Combine(Folder,"api-settings.json");
    internal static ApiOptions Current {get;private set;}=Read();
    static ApiOptions Read(){try{return JsonSerializer.Deserialize<ApiOptions>(File.ReadAllText(ConfigFile))??new();}catch{return new();}}
    internal static string Protect(string key)=>Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key),null,DataProtectionScope.CurrentUser));
    internal static string Unprotect(string value)=>Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value),null,DataProtectionScope.CurrentUser));
    internal static Uri Endpoint(string baseUrl)
    {
        if(!Uri.TryCreate(baseUrl.Trim().TrimEnd('/')+"/",UriKind.Absolute,out var uri)||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment)||(uri.Scheme!="https"&&!(uri.Scheme=="http"&&uri.IsLoopback)))
            throw new InvalidOperationException("Base URL 必须为 HTTPS 地址（本机 localhost 可用 HTTP），不能包含密码、查询参数或片段。");
        if(uri.AbsolutePath.TrimEnd('/').EndsWith("/chat/completions"))throw new InvalidOperationException("请填写 Base URL，不要包含 /chat/completions。");
        return new Uri(uri,"chat/completions");
    }
    internal static void Save(bool enabled,string url,string model,string key,bool forget)
    {
        string normalized=url.Trim().TrimEnd('/');
        if(!string.IsNullOrWhiteSpace(normalized))_=Endpoint(normalized);
        string encrypted=forget?"":Current.ProtectedKey;
        if(!string.IsNullOrEmpty(key))encrypted=Protect(key.Trim());
        if(normalized!=Current.BaseUrl&&string.IsNullOrEmpty(key))encrypted=""; // Never send an old key to a new destination.
        if(enabled&&(string.IsNullOrWhiteSpace(model)||string.IsNullOrWhiteSpace(normalized)||string.IsNullOrEmpty(encrypted)))throw new InvalidOperationException("启用 API 时请填写 Base URL、支持图片的模型 ID 和 API Key。更换地址后须重新输入密钥。");
        var next=new ApiOptions(enabled,normalized,model.Trim(),encrypted);
        Directory.CreateDirectory(Folder);File.WriteAllText(ConfigFile+".tmp",JsonSerializer.Serialize(next));File.Move(ConfigFile+".tmp",ConfigFile,true);Current=next;
    }
    internal static async Task<(string Answer,string Usage)> Ask(ApiOptions options,string question,string png,CancellationToken cancel,HttpMessageHandler? testHandler=null)
    {
        var endpoint=Endpoint(options.BaseUrl);
        string key;
        try{key=Unprotect(options.ProtectedKey);}catch{throw new InvalidOperationException("API Key 无法解密，请在 API 设置中重新输入。不要复制他人的配置文件。");}
        using var client=new HttpClient(testHandler??new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(180)};
        using var request=new HttpRequestMessage(HttpMethod.Post,endpoint);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        var content=new List<object>{new{type="text",text=question}};
        if(!string.IsNullOrEmpty(png))content.Add(new{type="image_url",image_url=new{url="data:image/png;base64,"+png}});
        request.Content=new StringContent(JsonSerializer.Serialize(new{model=options.Model,stream=false,messages=new[]{new{role="user",content=content}}}),Encoding.UTF8,"application/json");
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancel);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException($"API 请求失败（HTTP {(int)response.StatusCode}）："+((int)response.StatusCode switch{401 or 403=>"请检查密钥、权限或免费额度。",429=>"达到服务限额或频率限制。",400 or 404 or 422=>"请检查 Base URL、模型 ID 及图片输入兼容性。",>=300 and <400=>"不跟随重定向，请填写最终服务地址。",_=>"服务暂不可用，请稍后重试。"}));
        await response.Content.LoadIntoBufferAsync(8_000_000,cancel);
        try
        {
            using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));var root=doc.RootElement;
            var choice=root.GetProperty("choices")[0];
            if(choice.TryGetProperty("finish_reason",out var finish)&&finish.GetString() is "length" or "content_filter")throw new InvalidOperationException("API 回答被截断或过滤，未作为完整回答展示。");
            string answer=choice.GetProperty("message").GetProperty("content").GetString()??"";
            if(string.IsNullOrWhiteSpace(answer))throw new InvalidOperationException("API 返回空回答，请检查模型兼容性。");
            string usage="API 用量未返回；费用以服务商账单为准";
            if(root.TryGetProperty("usage",out var u)&&u.TryGetProperty("total_tokens",out var total))usage=$"API {total} tokens；费用以服务商账单为准";
            return(answer,usage);
        }
        catch(Exception e) when(e is JsonException or KeyNotFoundException or IndexOutOfRangeException){throw new InvalidOperationException("服务响应不符合 Chat Completions 格式。");}
    }
}
