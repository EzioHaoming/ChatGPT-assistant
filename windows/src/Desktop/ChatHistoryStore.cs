using System.IO;
using System.Text.Json;
namespace ScreenshotAssistant;
internal sealed record SavedTurn(string Question,string Answer);
internal sealed record SavedChat(string Id,string Title,DateTimeOffset Updated,string Transcript,List<SavedTurn> Turns,bool HasCustomTitle=false)
{
    public override string ToString()=>$"{Updated:MM-dd HH:mm}  {Title}";
}
internal sealed class ChatHistoryStore(string directory)
{
    string FileFor(string id){if(!Guid.TryParseExact(id,"N",out _))throw new ArgumentException("Invalid chat ID");return Path.Combine(directory,id+".json");}
    public List<SavedChat> Load()
    {
        if(!Directory.Exists(directory))return [];
        var chats=new List<SavedChat>();
        foreach(var file in Directory.EnumerateFiles(directory,"*.json"))
        {
            try{var item=JsonSerializer.Deserialize<SavedChat>(File.ReadAllText(file));if(item!=null&&Path.GetFileName(file)==item.Id+".json"){FileFor(item.Id);chats.Add(item);}}
            catch(JsonException){} // Keep damaged files on disk; never overwrite them on load.
        }
        return chats.OrderByDescending(x=>x.Updated).ToList();
    }
    public void Save(SavedChat chat)
    {
        var target=FileFor(chat.Id);Directory.CreateDirectory(directory);
        var temp=target+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(chat));File.Move(temp,target,true);
    }
    public void Delete(string id)=>File.Delete(FileFor(id));
}
