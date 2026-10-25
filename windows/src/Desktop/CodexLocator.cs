using System.IO;
using System.Text.Json;
namespace ScreenshotAssistant;
internal static class CodexLocator
{
    internal static string Resolve(string configured,string? installationRoot=null)
    {
        // Keep explicit working configurations; only recover a removed executable.
        if(!string.IsNullOrWhiteSpace(configured)&&File.Exists(configured))return configured;
        var root=installationRoot??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
        if(Directory.Exists(root))
        {
            // Only inspect the known installation folder, never PATH or user credential folders.
            var candidates=new List<FileInfo>();
            foreach(var dir in Directory.EnumerateDirectories(root))
            {
                if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)continue;
                var file=new FileInfo(Path.Combine(dir,"codex.exe"));
                if(file.Exists&&(file.Attributes&FileAttributes.ReparsePoint)==0)candidates.Add(file);
            }
            var latest=candidates.OrderByDescending(f=>f.LastWriteTimeUtc).ThenBy(f=>f.FullName,StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if(latest!=null)return latest.FullName;
        }
        throw new FileNotFoundException("找不到 Codex App Server。请完成 Codex 安装或更新，然后重试；也可在 bridge-config.json 中填写有效的 Codex 路径。无需因此重新授权 ChatGPT。");
    }
    internal static int Check(string report)
    {
        var root=Path.Combine(Path.GetTempPath(),"codex-path-check-"+Guid.NewGuid());Directory.CreateDirectory(root);
        var results=new Dictionary<string,bool>();
        try
        {
            string Add(string version,int age){var d=Path.Combine(root,version);Directory.CreateDirectory(d);var p=Path.Combine(d,"codex.exe");File.WriteAllText(p,"test placeholder, never executed");File.SetLastWriteTimeUtc(p,DateTime.UtcNow.AddDays(age));return p;}
            var old=Add("old",-2);var current=Add("current",-1);
            results["keep_valid_custom_path"]=Resolve(old,root)==old;
            File.Delete(old);results["recover_removed_version"]=Resolve(old,root)==current;
            var next=Add("next",0);File.Delete(current);results["recover_second_update"]=Resolve(old,root)==next;
            results["empty_setting_discovers_install"]=Resolve("",root)==next;
            var unrelated=Path.Combine(root,"unrelated.exe");File.WriteAllText(unrelated,"unused");File.Delete(next);
            try{Resolve(old,root);results["missing_install_reports_error"]=false;}catch(FileNotFoundException){results["missing_install_reports_error"]=true;}
            var partial=Path.Combine(root,"partial");Directory.CreateDirectory(partial);File.WriteAllText(Path.Combine(partial,"codex.exe.tmp"),"unused");
            try{Resolve(old,root);results["ignore_incomplete_update"]=false;}catch(FileNotFoundException){results["ignore_incomplete_update"]=true;}
            File.WriteAllText(report,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));return results.Values.All(v=>v)?0:1;
        }
        finally{Directory.Delete(root,true);}
    }
}
