using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ScreenshotAssistant;
internal static class AvatarChecks
{
    internal static int Run(string report)
    {
        var original=AvatarStore.Folder;var folder=Path.Combine(Path.GetTempPath(),"avatar-check-"+Guid.NewGuid());Directory.CreateDirectory(folder);AvatarStore.Folder=folder;
        var results=new Dictionary<string,bool>();
        try
        {
            var source=Path.Combine(folder,"sample.png");var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{20,40,80,255},4)));using(var stream=File.Create(source))encoder.Save(stream);
            var legacy=Path.Combine(folder,"legacy");Directory.CreateDirectory(legacy);File.Copy(source,Path.Combine(legacy,"avatar.png"));File.WriteAllText(Path.Combine(legacy,"settings.json"),"{}");
            AvatarStore.MigrateLegacy(new[]{Path.Combine(folder,"missing"),legacy});
            results["migrate_legacy_avatar_only"]=AvatarStore.Load()!=null&&!File.Exists(Path.Combine(folder,"settings.json"));
            AvatarStore.Reset();AvatarStore.MigrateLegacy(new[]{legacy});results["reset_does_not_reimport_legacy_avatar"]=AvatarStore.Load()==null;
            AvatarStore.Undo();AvatarStore.Reset();AvatarStore.Undo();
            File.Delete(Path.Combine(folder,"avatar.png"));
            AvatarStore.Set(source);AvatarStore.Undo();results["undo_first_image_returns_default"]=AvatarStore.Load()==null&&!AvatarStore.CanUndo;
            AvatarStore.Set(source);var before=File.ReadAllBytes(Path.Combine(folder,"avatar.png"));AvatarStore.Reset();results["reset_preserves_backup"]=AvatarStore.Load()==null&&AvatarStore.CanUndo;
            AvatarStore.Reset();AvatarStore.Undo();results["repeated_reset_preserves_original"]=File.ReadAllBytes(Path.Combine(folder,"avatar.png")).SequenceEqual(before);
            AvatarStore.Set(source);AvatarStore.Undo();results["undo_replacement"]=File.ReadAllBytes(Path.Combine(folder,"avatar.png")).SequenceEqual(before)&&!AvatarStore.CanUndo;
            AvatarStore.Reset();AvatarStore.Folder=original;AvatarStore.Folder=folder;results["undo_survives_reload"]=AvatarStore.CanUndo;AvatarStore.Undo();results["restored_image_decodes"]=AvatarStore.Load()!=null;
            _=new App();var assistant=new AssistantWindow();FloatingButton floating;
            using(var locked=new FileStream(Path.Combine(folder,"avatar.png"),FileMode.Open,FileAccess.Read,FileShare.None))
            {
                floating=new FloatingButton(assistant);
                results["locked_file_uses_temporary_default"]=!floating.HasCustomAvatar;
            }
            floating.RecoverAvatar();results["portrait_recovers_after_unlock"]=floating.HasCustomAvatar;
            using(var locked=new FileStream(Path.Combine(folder,"avatar.png"),FileMode.Open,FileAccess.Read,FileShare.None))
            {
                floating.ReloadAvatar();results["temporary_failure_keeps_displayed_portrait"]=floating.HasCustomAvatar;
            }
            var reopened=new FloatingButton(assistant);results["new_window_loads_saved_portrait"]=reopened.HasCustomAvatar;
            AvatarStore.Reset();floating.ReloadAvatar();results["intentional_reset_stays_default"]=!floating.HasCustomAvatar;
            floating.RecoverAvatar();results["recovery_does_not_undo_user_reset"]=!floating.HasCustomAvatar;
            AvatarStore.Undo();floating.RecoverAvatar();results["undo_restores_portrait"]=floating.HasCustomAvatar;
            floating.Close();reopened.Close();
            File.WriteAllText(report,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));return results.Values.All(v=>v)?0:1;
        }
        finally{AvatarStore.Folder=original;Directory.Delete(folder,true);}
    }
}
