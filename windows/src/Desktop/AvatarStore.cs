using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ScreenshotAssistant;
internal static class AvatarStore
{
    // AppData is virtualized when launched by a packaged app. Keep user-selected
    // artwork outside AppData so Explorer and development launches share it.
    internal static string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".chatgpt-assistant");
    static string MigrationMarker=>Path.Combine(Folder,"avatar-storage-v2");
    internal static void MigrateLegacy(IEnumerable<string> sourceFolders)
    {
        if(File.Exists(MigrationMarker))return;
        Directory.CreateDirectory(Folder);
        if(!File.Exists(FilePath))
        {
            foreach(var sourceFolder in sourceFolders)
            {
                var source=Path.Combine(sourceFolder,"avatar.png");
                if(!File.Exists(source))continue;
                try
                {
                    // Validate the source and copy artwork only, never account data.
                    _=Read(source);
                    File.Copy(source,FilePath+".tmp",true);File.Move(FilePath+".tmp",FilePath,true);
                    break;
                }
                catch(IOException){throw;} // Retry migration next launch if temporarily locked.
            }
        }
        File.WriteAllText(MigrationMarker,"2");
    }
    internal static void Initialize()
    {
        var local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        MigrateLegacy(new[]{Path.Combine(local,"ScreenshotAssistant"),
            Path.Combine(local,"Packages","OpenAI.Codex_2p2nqsd0c76g0","LocalCache","Local","ScreenshotAssistant")});
    }
    static string FilePath=>Path.Combine(Folder,"avatar.png");
    static string UndoPath=>Path.Combine(Folder,"avatar-undo.bin");
    internal static bool CanUndo=>File.Exists(UndoPath);
    internal static bool HasSavedAvatar=>File.Exists(FilePath);
    static void Backup()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllBytes(UndoPath+".tmp",File.Exists(FilePath)?File.ReadAllBytes(FilePath):[]);
        File.Move(UndoPath+".tmp",UndoPath,true);
    }
    internal static void Undo()
    {
        if(!CanUndo)return;
        var bytes=File.ReadAllBytes(UndoPath);
        if(bytes.Length==0){if(File.Exists(FilePath))File.Delete(FilePath);}
        else{File.WriteAllBytes(FilePath+".tmp",bytes);File.Move(FilePath+".tmp",FilePath,true);}
        File.Delete(UndoPath);
    }
    internal static BitmapSource Read(string path)
    {
        if(new FileInfo(path).Length>20_000_000)throw new InvalidOperationException();
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=1024;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();
        if(bitmap.PixelHeight>10000)throw new InvalidOperationException();
        int side=Math.Min(bitmap.PixelWidth,bitmap.PixelHeight);
        var crop=new CroppedBitmap(bitmap,new Int32Rect((bitmap.PixelWidth-side)/2,(bitmap.PixelHeight-side)/2,side,side));crop.Freeze();return crop;
    }
    internal static BitmapSource WindowIcon()=>Load()??BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico"));
    internal static BitmapSource? Load(){try{if(!File.Exists(FilePath)){App.Trace("avatar_missing_path="+FilePath);return null;}var image=Read(FilePath);App.Trace("avatar_loaded_path="+FilePath);return image;}catch(Exception e){App.Trace("avatar_load_failed_"+e.GetType().Name);return null;}}
    internal static void Set(string path)
    {
        var bitmap=Read(path);Directory.CreateDirectory(Folder);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var file=File.Create(FilePath+".tmp"))encoder.Save(file);Backup();File.Move(FilePath+".tmp",FilePath,true);
    }
    internal static void Reset(){Directory.CreateDirectory(Folder);File.WriteAllText(MigrationMarker,"2");if(File.Exists(FilePath)){Backup();File.Delete(FilePath);}}
}
