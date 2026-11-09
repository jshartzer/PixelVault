using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using PixelVault.Tracking;

namespace PixelVaultNative;

internal sealed class CenturionRewardWindow : Window
{
    internal static BitmapImage Artwork(byte[] bytes)
    {
        if(bytes==null)return null;
        using var stream=new MemoryStream(bytes);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
        bitmap.StreamSource=stream;bitmap.DecodePixelWidth=320;bitmap.EndInit();bitmap.Freeze();return bitmap;
    }
    static Brush Ink(string hex)=>SystemParameters.HighContrast?SystemColors.WindowTextBrush:UiBrushHelper.FromHex(hex);
    internal static FrameworkElement Build(CenturionReward reward,bool animate)
    {
        var root=new Grid{Margin=new Thickness(14, reward.Badge?65:15,14,15)};
        var shell=new Border{CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),BorderBrush=Ink("#839EA9"),
            Background=SystemParameters.HighContrast?SystemColors.WindowBrush:new LinearGradientBrush(Color.FromRgb(24,44,54),Color.FromRgb(16,28,37),0),Padding=new Thickness(24)};
        root.Children.Add(shell);
        var body=new Grid();shell.Child=body;
        if(!reward.Badge){body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(86)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});}
        var art=new Image{Width=reward.Badge?124:86,Height=reward.Badge?124:86,Stretch=Stretch.Uniform,Source=Artwork(reward.Badge?
            CenturionRewards.BadgeArtwork(reward.Provider,reward.GameId):CenturyAchievementCatalog.Artwork(reward.Id,true))};
        var artHost=new Grid{Width=art.Width,Height=art.Height,HorizontalAlignment=HorizontalAlignment.Center};artHost.Children.Add(art);
        if(reward.Badge){artHost.VerticalAlignment=VerticalAlignment.Top;artHost.Margin=new Thickness(0,-55,0,0);root.Children.Add(artHost);}
        else{body.Children.Add(artHost);artHost.VerticalAlignment=VerticalAlignment.Center;}
        var copy=new StackPanel{Margin=reward.Badge?new Thickness(0,48,0,0):new Thickness(20,0,14,0)};
        if(!reward.Badge)Grid.SetColumn(copy,1);body.Children.Add(copy);
        TextBlock Text(string value,double size,string color,FontWeight weight)=>new(){Text=value,FontSize=size,Foreground=Ink(color),FontWeight=weight,
            TextWrapping=TextWrapping.Wrap,TextAlignment=reward.Badge?TextAlignment.Center:TextAlignment.Left,Margin=new Thickness(0,0,0,8)};
        copy.Children.Add(Text(reward.Badge?"CENTURION BADGE EARNED":"CENTURION ACHIEVEMENT EARNED",10,"#B6EDF5",FontWeights.SemiBold));
        copy.Children.Add(Text(reward.Title,reward.Badge?27:21,"#F0F5F7",FontWeights.SemiBold));
        copy.Children.Add(Text(reward.Description,12,"#C4D1D8",FontWeights.Normal));
        copy.Children.Add(Text(reward.GameTitle,10,"#A2B7C4",FontWeights.Normal));
        if(!reward.Badge){var points=Text("+"+reward.Points+" PV",10,"#B6EDF5",FontWeights.Normal);points.HorizontalAlignment=HorizontalAlignment.Center;points.VerticalAlignment=VerticalAlignment.Bottom;points.Margin=new Thickness(0,0,0,-20);artHost.Children.Add(points);}
        var close=new Button{Content="×",Width=32,Height=32,FontSize=20,Padding=new Thickness(0),Background=Brushes.Transparent,BorderThickness=new Thickness(0),
            Foreground=Ink("#A4B7C2"),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,4,4,0)};
        System.Windows.Automation.AutomationProperties.SetName(close,"Dismiss reward notification");close.Click+=(_,__)=>Window.GetWindow(close)?.Close();root.Children.Add(close);
        System.Windows.Automation.AutomationProperties.SetName(root,(reward.Badge?"Badge":"Achievement")+" earned: "+reward.Title+". "+reward.Description);
        if(animate&&!SystemParameters.HighContrast&&SystemParameters.ClientAreaAnimation)
        {
            root.Loaded+=(_,__)=>
            {
                var move=new TranslateTransform();root.RenderTransform=move;
                move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(28,0,TimeSpan.FromMilliseconds(620)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
                root.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(400)));
                art.RenderTransformOrigin=new Point(.5,.5);var scale=new ScaleTransform(1.18,1.18);art.RenderTransform=scale;
                foreach(var property in new[]{ScaleTransform.ScaleXProperty,ScaleTransform.ScaleYProperty})scale.BeginAnimation(property,new DoubleAnimation(1.18,1,TimeSpan.FromMilliseconds(850)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
                var pulse=new Polygon{Points=new PointCollection{new(62,0),new(116,31),new(116,93),new(62,124),new(8,93),new(8,31)},Stroke=Ink("#B6EDF5"),StrokeThickness=1,IsHitTestVisible=false,RenderTransformOrigin=new Point(.5,.5)};
                artHost.Children.Insert(0,pulse);var zoom=new ScaleTransform(1,1);pulse.RenderTransform=zoom;
                foreach(var property in new[]{ScaleTransform.ScaleXProperty,ScaleTransform.ScaleYProperty})zoom.BeginAnimation(property,new DoubleAnimation(1,1.7,TimeSpan.FromMilliseconds(1100)));
                pulse.BeginAnimation(OpacityProperty,new DoubleAnimation(.6,0,TimeSpan.FromMilliseconds(1100)));
                var sparks=new Canvas{IsHitTestVisible=false,ClipToBounds=false};artHost.Children.Insert(0,sparks);
                for(var i=0;i<16;i++)
                {
                    var angle=i*Math.PI/8;var spark=new Rectangle{Width=2,Height=6,Fill=Ink("#B6EDF5")};sparks.Children.Add(spark);
                    var center=art.Width/2;var duration=TimeSpan.FromMilliseconds(reward.Badge?1500:1050);
                    spark.BeginAnimation(Canvas.LeftProperty,new DoubleAnimation(center+Math.Cos(angle)*35,center+Math.Cos(angle)*105,duration));
                    spark.BeginAnimation(Canvas.TopProperty,new DoubleAnimation(center+Math.Sin(angle)*35,center+Math.Sin(angle)*105,duration));
                    spark.BeginAnimation(OpacityProperty,new DoubleAnimation(.9,0,duration));
                }
                var sheen=new Border{IsHitTestVisible=false,ClipToBounds=true,CornerRadius=new CornerRadius(12)};root.Children.Insert(1,sheen);
                var band=new Rectangle{Width=80,Height=400,Fill=new LinearGradientBrush(new GradientStopCollection{new(Colors.Transparent,0),new(Color.FromArgb(45,220,250,255),.5),new(Colors.Transparent,1)},0),HorizontalAlignment=HorizontalAlignment.Left,RenderTransform=new TranslateTransform(-100,0)};
                sheen.Child=band;((TranslateTransform)band.RenderTransform).BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(-100,root.ActualWidth+100,TimeSpan.FromMilliseconds(1100)){BeginTime=TimeSpan.FromMilliseconds(200)});
            };
        }
        return root;
    }
    internal CenturionRewardWindow(CenturionReward reward)
    {
        Title=reward.Badge?"Centurion badge earned":"Centurion achievement earned";Width=reward.Badge?460:550;
        SizeToContent=SizeToContent.Height;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;
        Topmost=true;AllowsTransparency=true;Background=Brushes.Transparent;Content=Build(reward,true);WindowStartupLocation=WindowStartupLocation.Manual;
        var remaining=TimeSpan.FromSeconds(reward.Badge?7:5);var last=DateTime.UtcNow;
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
        timer.Tick+=(_,__)=>{var now=DateTime.UtcNow;if(!IsMouseOver&&!IsKeyboardFocusWithin)remaining-=now-last;last=now;if(remaining<=TimeSpan.Zero)Close();};
        Loaded+=(_,__)=>{var work=SystemParameters.WorkArea;Left=Math.Max(work.Left,work.Right-ActualWidth-16);Top=Math.Max(work.Top,work.Bottom-ActualHeight-16);timer.Start();};
        Closed+=(_,__)=>timer.Stop();PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape)Close();};
    }
}
