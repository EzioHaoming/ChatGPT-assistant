using System.Windows;
using System.Windows.Markup;

namespace ScreenshotAssistant;
internal static class Theme
{
    internal static ResourceDictionary Create() => (ResourceDictionary)XamlReader.Parse("""
    <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
      <Style TargetType="TextBox">
        <Setter Property="Padding" Value="12,9"/><Setter Property="Background" Value="White"/>
        <Setter Property="BorderBrush" Value="#BCC6DA"/><Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox">
          <Border x:Name="box" CornerRadius="15" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
            <ScrollViewer x:Name="PART_ContentHost" Margin="2"/>
          </Border>
          <ControlTemplate.Triggers><Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="box" Property="BorderBrush" Value="#6577DB"/></Trigger></ControlTemplate.Triggers>
        </ControlTemplate></Setter.Value></Setter>
      </Style>
      <Style TargetType="ToggleButton">
        <Setter Property="Background" Value="#F6F7FB"/><Setter Property="Foreground" Value="#68748A"/><Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ToggleButton">
          <Border x:Name="pin" CornerRadius="12" Background="{TemplateBinding Background}" BorderBrush="Transparent" BorderThickness="1"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True"><Setter Property="Background" Value="#CCD4F0"/><Setter Property="Foreground" Value="#384984"/></Trigger>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="pin" Property="BorderBrush" Value="#8796C4"/></Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="pin" Property="BorderBrush" Value="#5865BE"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate></Setter.Value></Setter>
      </Style>
      <Style TargetType="ContextMenu">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="FontFamily" Value="Microsoft YaHei UI"/>
        <Setter Property="FontSize" Value="13"/>
        <Setter Property="MinWidth" Value="180"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ContextMenu">
          <Border Background="#E6EAF4" BorderBrush="#BCC6DA" BorderThickness="1" CornerRadius="12" Padding="6">
            <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/>
          </Border>
        </ControlTemplate></Setter.Value></Setter>
      </Style>
      <Style TargetType="MenuItem">
        <Setter Property="Foreground" Value="#354257"/>
        <Setter Property="Padding" Value="14,9"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="MenuItem">
          <Border x:Name="item" Background="Transparent" CornerRadius="7" Padding="{TemplateBinding Padding}">
            <ContentPresenter ContentSource="Header" RecognizesAccessKey="True"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="item" Property="Background" Value="#D7DDF2"/><Setter Property="Foreground" Value="#525FA8"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate></Setter.Value></Setter>
      </Style>
      <Style TargetType="Separator">
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Separator"><Border Height="1" Background="#E6E9F1" Margin="10,5"/></ControlTemplate></Setter.Value></Setter>
      </Style>
      <Style TargetType="Button">
        <Setter Property="Padding" Value="14,8"/>
        <Setter Property="Margin" Value="0,0,8,0"/>
        <Setter Property="Background" Value="White"/>
        <Setter Property="Foreground" Value="#354257"/>
        <Setter Property="BorderBrush" Value="#BCC6DA"/>
        <Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
          <Setter.Value>
            <ControlTemplate TargetType="Button">
              <Border x:Name="surface" CornerRadius="14" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" RecognizesAccessKey="True"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Opacity" Value="0.82"/></Trigger>
                <Trigger Property="IsPressed" Value="True"><Setter TargetName="surface" Property="Opacity" Value="0.65"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="surface" Property="BorderBrush" Value="#6577DB"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.42"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Style>
    </ResourceDictionary>
    """);
}


