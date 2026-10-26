using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace PixelVaultNative
{
    /// <summary>Shared tracker dialog chrome. System high-contrast controls remain native.</summary>
    static class TrackerFormStyle
    {
        internal static void Apply(Window window)
        {
            window.FontFamily=new System.Windows.Media.FontFamily("Segoe UI");window.FontSize=15;
            if(SystemParameters.HighContrast)return;
            window.Foreground=UiBrushHelper.FromHex("#D9E6ED");
            window.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Resources));
        }
        internal static void ApplyScoped(FrameworkElement host)
        {
            if (!SystemParameters.HighContrast)
                host.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Resources));
        }
        const string Resources="""
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
 <Style TargetType="DockPanel"><Setter Property="Background" Value="#11181D"/></Style>
 <Style TargetType="ContextMenu"><Setter Property="Background" Value="#17252D"/><Setter Property="Foreground" Value="#D9E6ED"/><Setter Property="BorderBrush" Value="#415561"/><Setter Property="FontSize" Value="15"/></Style>
 <Style TargetType="MenuItem"><Setter Property="Background" Value="#17252D"/><Setter Property="Foreground" Value="#D9E6ED"/><Setter Property="Padding" Value="10,7"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="MenuItem"><Grid><Border x:Name="Frame" Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}" CornerRadius="3"><DockPanel><TextBlock x:Name="Arrow" Text="›" DockPanel.Dock="Right" Visibility="Collapsed" Margin="16,0,0,0"/><ContentPresenter ContentSource="Header" RecognizesAccessKey="True"/></DockPanel></Border><Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsSubmenuOpen}" Placement="Right" AllowsTransparency="True" Focusable="False"><Border Background="#17252D" BorderBrush="#415561" BorderThickness="1" Padding="3"><StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/></Border></Popup></Grid><ControlTemplate.Triggers><Trigger Property="HasItems" Value="True"><Setter TargetName="Arrow" Property="Visibility" Value="Visible"/></Trigger><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Frame" Property="Background" Value="#304B59"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
 </Style>
 <SolidColorBrush x:Key="TrackerSurface" Color="#151F25"/>
 <SolidColorBrush x:Key="TrackerText" Color="#D9E6ED"/>
 <SolidColorBrush x:Key="TrackerLine" Color="#344B58"/>
 <Style TargetType="ComboBoxItem">
  <Setter Property="Foreground" Value="{StaticResource TrackerText}"/><Setter Property="Background" Value="{StaticResource TrackerSurface}"/><Setter Property="Padding" Value="10,7"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem"><Border x:Name="Frame" Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Frame" Property="Background" Value="#304B59"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="Frame" Property="Background" Value="#28515C"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="ComboBox">
  <Setter Property="Foreground" Value="{StaticResource TrackerText}"/><Setter Property="Background" Value="{StaticResource TrackerSurface}"/><Setter Property="BorderBrush" Value="{StaticResource TrackerLine}"/><Setter Property="MinHeight" Value="34"/><Setter Property="FontSize" Value="15"/>
  <Setter Property="ScrollViewer.CanContentScroll" Value="True"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
   <Grid><Border x:Name="Frame" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="5"/>
    <ContentPresenter Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" Margin="10,5,28,5" VerticalAlignment="Center" IsHitTestVisible="False"/>
    <ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}" Background="Transparent" BorderThickness="0">
     <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="Transparent"><TextBlock Text="⌄" Foreground="#A4BDC9" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,10,0"/></Border></ControlTemplate></ToggleButton.Template>
    </ToggleButton>
    <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False">
     <Border Background="{StaticResource TrackerSurface}" BorderBrush="{StaticResource TrackerLine}" BorderThickness="1" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}" MaxHeight="320"><ScrollViewer><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer></Border>
    </Popup>
   </Grid><ControlTemplate.Triggers><Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="#8CD1DF"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="CheckBox">
  <Setter Property="Foreground" Value="{StaticResource TrackerText}"/><Setter Property="FontSize" Value="15"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox"><Grid Background="Transparent"><Grid.ColumnDefinitions><ColumnDefinition Width="24"/><ColumnDefinition/></Grid.ColumnDefinitions><Border x:Name="Box" Width="18" Height="18" CornerRadius="3" BorderThickness="1" BorderBrush="#7597A8" Background="#151F25" VerticalAlignment="Center"><TextBlock x:Name="Tick" Text="✓" Foreground="#DFF5EE" Visibility="Collapsed" FontSize="14" HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ContentPresenter Grid.Column="1" Margin="5,3,0,3" VerticalAlignment="Center" RecognizesAccessKey="True"/></Grid><ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="Box" Property="Background" Value="#215444"/><Setter TargetName="Tick" Property="Visibility" Value="Visible"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Box" Property="BorderBrush" Value="#F1D395"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType="DataGrid">
  <Setter Property="Background" Value="{StaticResource TrackerSurface}"/><Setter Property="Foreground" Value="{StaticResource TrackerText}"/><Setter Property="RowBackground" Value="#17252D"/><Setter Property="AlternatingRowBackground" Value="#1B2B34"/><Setter Property="BorderBrush" Value="{StaticResource TrackerLine}"/><Setter Property="GridLinesVisibility" Value="Horizontal"/><Setter Property="HorizontalGridLinesBrush" Value="#2B3F4A"/><Setter Property="FontSize" Value="14"/><Setter Property="RowHeaderWidth" Value="0"/><Setter Property="MinRowHeight" Value="32"/>
 </Style>
 <Style TargetType="DataGridColumnHeader"><Setter Property="Background" Value="#233943"/><Setter Property="Foreground" Value="{StaticResource TrackerText}"/><Setter Property="Padding" Value="8,7"/><Setter Property="FontWeight" Value="SemiBold"/><Setter Property="BorderThickness" Value="0,0,0,1"/><Setter Property="BorderBrush" Value="#42606E"/></Style>
 <Style TargetType="DataGridCell"><Setter Property="Padding" Value="8,4"/><Setter Property="BorderThickness" Value="0"/><Setter Property="Foreground" Value="{StaticResource TrackerText}"/><Style.Triggers><Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="#28515C"/></Trigger></Style.Triggers></Style>
 <Style TargetType="ListBox"><Setter Property="Background" Value="{StaticResource TrackerSurface}"/><Setter Property="Foreground" Value="{StaticResource TrackerText}"/><Setter Property="BorderBrush" Value="{StaticResource TrackerLine}"/><Setter Property="FontSize" Value="15"/></Style>
 <Style TargetType="Expander"><Setter Property="Foreground" Value="{StaticResource TrackerText}"/><Setter Property="FontSize" Value="15"/>
  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Expander"><DockPanel><ToggleButton DockPanel.Dock="Top" Content="{TemplateBinding Header}" IsChecked="{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}" HorizontalContentAlignment="Left" Foreground="{TemplateBinding Foreground}"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="#1B2C35" CornerRadius="4" Padding="8,7"><StackPanel Orientation="Horizontal"><TextBlock x:Name="Arrow" Text="›" Margin="0,0,8,0" Foreground="#8CD1DF"/><ContentPresenter/></StackPanel></Border><ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="Arrow" Property="Text" Value="⌄"/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template></ToggleButton><ContentPresenter x:Name="Body" ContentSource="Content" Visibility="Collapsed" Margin="0,6,0,0"/></DockPanel><ControlTemplate.Triggers><Trigger Property="IsExpanded" Value="True"><Setter TargetName="Body" Property="Visibility" Value="Visible"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key="TrackerThumb" TargetType="Thumb"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Thumb"><Border Background="#4C6978" CornerRadius="5" Margin="2"/></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType="ScrollBar"><Setter Property="Width" Value="12"/><Setter Property="Background" Value="#101A21"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar"><Grid Background="{TemplateBinding Background}"><Track x:Name="PART_Track" IsDirectionReversed="True"><Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0" Focusable="False"/></Track.DecreaseRepeatButton><Track.Thumb><Thumb Style="{StaticResource TrackerThumb}"/></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0" Focusable="False"/></Track.IncreaseRepeatButton></Track></Grid><ControlTemplate.Triggers><Trigger Property="Orientation" Value="Horizontal"><Setter Property="Width" Value="Auto"/><Setter Property="Height" Value="12"/><Setter TargetName="PART_Track" Property="IsDirectionReversed" Value="False"/><Setter TargetName="PART_Track" Property="Orientation" Value="Horizontal"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
</ResourceDictionary>
""";
    }
}
