using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenshotAssistant;
internal sealed class ApiSettingsWindow:Window
{
    internal ApiSettingsWindow()
    {
        Title="可选 API 设置";Width=520;Height=680;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        Background=new SolidColorBrush(Color.FromRgb(230,234,244));FontFamily=new FontFamily("Microsoft YaHei UI");Resources.MergedDictionaries.Add(Theme.Create());
        var panel=new StackPanel{Margin=new Thickness(24)};Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var enabled=new CheckBox{Content="使用自定义 API（关闭则使用 ChatGPT 授权）",IsChecked=ApiService.Current.Enabled,Margin=new Thickness(0,0,0,14)};panel.Children.Add(enabled);
        void Label(string text)=>panel.Children.Add(new TextBlock{Text=text,Margin=new Thickness(0,8,0,5)});
        var preset=new Button{Content="填入 Qwen / 百炼预设（北京）",HorizontalAlignment=HorizontalAlignment.Left};panel.Children.Add(preset);
        var presetHint=new TextBlock{Text="预设不含密钥；请从百炼控制台核对当前地域的地址、模型和免费额度。",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray};panel.Children.Add(presetHint);
        Label("Base URL（按服务商文档填写，通常以 /v1 结尾）");var url=new TextBox{Text=ApiService.Current.BaseUrl};panel.Children.Add(url);
        Label("模型 ID（必须支持图片 / image_url）");var model=new TextBox{Text=ApiService.Current.Model};panel.Children.Add(model);
        Label("API Key（留空保留；更换地址须重新输入）");var key=new PasswordBox{Padding=new Thickness(10)};panel.Children.Add(key);
        var forget=new CheckBox{Content="清除已保存的 API Key",Margin=new Thickness(0,12,0,8)};panel.Children.Add(forget);
        panel.Children.Add(new TextBlock{Text="兼容 OpenAI Chat Completions 图片接口，当前采用普通响应。截图和问题将发送至你填写的服务地址。API 可能收费，免费额度及停用规则由服务商管理。保存不会发起模型请求。",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray,Margin=new Thickness(0,12,0,8)});
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Firebrick};panel.Children.Add(error);
        preset.Click+=(_,_)=>{url.Text="https://{WorkspaceId}.cn-beijing.maas.aliyuncs.com/compatible-mode/v1";model.Text="qwen3-vl-plus";key.Clear();forget.IsChecked=true;presetHint.Text="请将 {WorkspaceId} 换成自己的业务空间 ID，或粘贴控制台提供的完整 Base URL。填写自己的 Key，再勾选启用。仅用免费额度请在百炼开启用完即停。";};
        var save=new Button{Content="保存设置",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,15,0,0)};panel.Children.Add(save);
        save.Click+=(_,_)=>{try{if(url.Text.Contains("{WorkspaceId}"))throw new InvalidOperationException("请填写实际业务空间 ID，或粘贴百炼控制台的完整 Base URL。");ApiService.Save(enabled.IsChecked==true,url.Text,model.Text,key.Password,forget.IsChecked==true);key.Clear();DialogResult=true;}catch(Exception e){error.Text=e is InvalidOperationException?e.Message:"无法保存设置，请检查本机文件权限。";}};
    }
}
