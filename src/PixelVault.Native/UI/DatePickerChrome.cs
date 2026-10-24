using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace PixelVaultNative
{
    internal static class DatePickerChrome
    {
        public static void Apply(DatePicker picker, string automationName)
        {
            picker.Foreground = Brushes.Black;
            picker.FontFamily = new FontFamily("Segoe UI");
            picker.FontSize = 12;
            picker.MinHeight = 30;
            picker.VerticalContentAlignment = VerticalAlignment.Center;
            picker.Style = (Style)XamlReader.Parse(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='{x:Type DatePicker}'>
  <Setter Property='Background' Value='#E5ECEF'/>
  <Setter Property='BorderBrush' Value='#33424D'/>
  <Setter Property='BorderThickness' Value='1'/>
  <Setter Property='CalendarStyle'>
    <Setter.Value>
      <Style TargetType='{x:Type Calendar}'>
        <Setter Property='Background' Value='#E5ECEF'/>
        <Setter Property='Foreground' Value='Black'/>
        <Setter Property='FontFamily' Value='Segoe UI'/>
        <Setter Property='FontSize' Value='12'/>
        <Setter Property='BorderBrush' Value='#33424D'/>
      </Style>
    </Setter.Value>
  </Setter>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='{x:Type DatePicker}'>
        <Grid x:Name='PART_Root' SnapsToDevicePixels='True'>
          <Border x:Name='Chrome' Background='{TemplateBinding Background}'
                  BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='9'>
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='*'/>
                <ColumnDefinition Width='29'/>
              </Grid.ColumnDefinitions>
              <DatePickerTextBox x:Name='PART_TextBox' Foreground='Black' Background='Transparent'
                                 BorderThickness='0' Padding='8,0,2,0' VerticalContentAlignment='Center'
                                 FontFamily='{TemplateBinding FontFamily}' FontSize='{TemplateBinding FontSize}'>
                <DatePickerTextBox.Template>
                  <ControlTemplate TargetType='{x:Type DatePickerTextBox}'>
                    <Grid Margin='{TemplateBinding Padding}'>
                      <ScrollViewer x:Name='PART_ContentHost' VerticalAlignment='Center' HorizontalScrollBarVisibility='Hidden'/>
                      <ContentControl x:Name='PART_Watermark' Foreground='Black' Opacity='0.6'
                                      VerticalAlignment='Center' IsHitTestVisible='False' Visibility='Collapsed'/>
                    </Grid>
                    <ControlTemplate.Triggers>
                      <Trigger Property='Text' Value=''>
                        <Setter TargetName='PART_Watermark' Property='Visibility' Value='Visible'/>
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </DatePickerTextBox.Template>
              </DatePickerTextBox>
              <Button x:Name='PART_Button' Grid.Column='1' Focusable='False' Cursor='Hand'
                      ToolTip='Open calendar' AutomationProperties.Name='Open calendar'>
                <Button.Template>
                  <ControlTemplate TargetType='{x:Type Button}'>
                    <Border x:Name='ButtonChrome' Background='#232B35' CornerRadius='0,8,8,0'>
                      <TextBlock Text='&#xE787;' FontFamily='Segoe MDL2 Assets' FontSize='13' Foreground='#D7E2EA'
                                 HorizontalAlignment='Center' VerticalAlignment='Center'/>
                    </Border>
                    <ControlTemplate.Triggers>
                      <Trigger Property='IsMouseOver' Value='True'>
                        <Setter TargetName='ButtonChrome' Property='Background' Value='#33424D'/>
                      </Trigger>
                      <Trigger Property='IsPressed' Value='True'>
                        <Setter TargetName='ButtonChrome' Property='Background' Value='#182028'/>
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Button.Template>
              </Button>
            </Grid>
          </Border>
          <Popup x:Name='PART_Popup' Placement='Bottom' AllowsTransparency='True' StaysOpen='False'/>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'>
            <Setter TargetName='Chrome' Property='BorderBrush' Value='#566676'/>
          </Trigger>
          <Trigger Property='IsKeyboardFocusWithin' Value='True'>
            <Setter TargetName='Chrome' Property='BorderBrush' Value='#D46C63'/>
          </Trigger>
          <Trigger Property='IsEnabled' Value='False'>
            <Setter TargetName='Chrome' Property='Opacity' Value='0.45'/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>");
            AccessibilityUi.TrySetAutomationName(picker, automationName);
            AccessibilityUi.TryApplyFocusVisualStyle(picker);
        }
    }
}
