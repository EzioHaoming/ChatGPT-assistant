using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
namespace ScreenshotAssistant;
internal static class AvatarShell
{
    [DllImport("shell32.dll")] static extern void SHChangeNotify(uint eventId,uint flags,IntPtr item1,IntPtr item2);
    internal static string CreateIcon()
    {
        var image=AvatarStore.WindowIcon();var png=new PngBitmapEncoder();
        var scaled=new System.Windows.Media.Imaging.TransformedBitmap(image,new System.Windows.Media.ScaleTransform(256.0/image.PixelWidth,256.0/image.PixelHeight));
        png.Frames.Add(BitmapFrame.Create(scaled));using var data=new MemoryStream();png.Save(data);var bytes=data.ToArray();
        Directory.CreateDirectory(AvatarStore.Folder);
        var path=Path.Combine(AvatarStore.Folder,"shell-"+Convert.ToHexString(SHA256.HashData(bytes))[..16]+".ico");
        if(!File.Exists(path))
        {
            using var file=File.Create(path+".tmp");using var w=new BinaryWriter(file);
            w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)1);
            w.Write((byte)0);w.Write((byte)0);w.Write((byte)0);w.Write((byte)0);w.Write((ushort)1);w.Write((ushort)32);w.Write(bytes.Length);w.Write(22);w.Write(bytes);w.Flush();file.Close();File.Move(path+".tmp",path,true);
        }
        return path;
    }
    internal static int UpdateLinks(string folder,string exe,string icon)
    {
        if(!Directory.Exists(folder))return 0;
        dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;int count=0;
        try
        {
            foreach(var path in Directory.EnumerateFiles(folder,"*.lnk"))
            {
                dynamic? link=null;
                try
                {
                    link=shell.CreateShortcut(path);string target=link.TargetPath;
                    if(!string.Equals(Path.GetFullPath(target),Path.GetFullPath(exe),StringComparison.OrdinalIgnoreCase))continue;
                    link.IconLocation=icon+",0";link.Save();count++;
                    var ptr=Marshal.StringToCoTaskMemUni(path);try{SHChangeNotify(0x2000,0x1005,ptr,IntPtr.Zero);}finally{Marshal.FreeCoTaskMem(ptr);}
                }
                catch(Exception e){App.Trace("shortcut_icon_"+e.GetType().Name);}
                finally{if(link!=null)Marshal.FinalReleaseComObject(link);}
            }
        }
        finally{Marshal.FinalReleaseComObject(shell);}
        return count;
    }
    internal static void Sync()
    {
        try
        {
            if(AvatarStore.HasSavedAvatar&&AvatarStore.Load()==null)return;
            var icon=CreateIcon();var exe=Environment.ProcessPath!;
            UpdateLinks(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),exe,icon);
            UpdateLinks(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Microsoft","Internet Explorer","Quick Launch","User Pinned","TaskBar"),exe,icon);
            SHChangeNotify(0x8000000,0,IntPtr.Zero,IntPtr.Zero);
        }
        catch(Exception e){App.Trace("shell_icon_sync_"+e.GetType().Name);}
    }
}
