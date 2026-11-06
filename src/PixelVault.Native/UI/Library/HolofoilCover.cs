using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using PixelVault.Tracking;

namespace PixelVaultNative
{
    /// <summary>Foil on the artwork, perspective on the complete cover, unchanged layout and input.</summary>
    internal sealed class HolofoilCover : Grid
    {
        readonly FrameworkElement artwork, scene;
        readonly Effect originalEffect;
        readonly Border inputLayer;
        readonly Viewport3D projection;
        readonly VisualBrush texture;
        readonly GeometryModel3D plane;
        readonly PerspectiveCamera camera;
        readonly QuaternionRotation3D rotation = new QuaternionRotation3D(Quaternion.Identity);
        readonly CenturyPrismaticEffect foil = new CenturyPrismaticEffect { ColorRetention = 1 };
        bool completed;
        int poseGeneration;
        internal bool IsProjecting => projection.Visibility == Visibility.Visible;
        internal Visual ReflectionVisual => artwork;

        internal HolofoilCover(FrameworkElement scene, FrameworkElement artwork)
        {
            this.scene = scene; this.artwork = artwork; originalEffect = artwork.Effect;
            Width = scene.Width; Height = scene.Height; Margin = scene.Margin; scene.Margin = new Thickness(0);
            Background = Brushes.Transparent; ClipToBounds = false;
            inputLayer = new Border { Child = scene, Background = Brushes.Transparent };
            Children.Add(inputLayer);
            texture = new VisualBrush(scene) { AutoLayoutContent = false, ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
            plane = new GeometryModel3D { Material = new DiffuseMaterial(texture), Transform = new RotateTransform3D(rotation) };
            camera = new PerspectiveCamera { FieldOfView = 35, UpDirection = new Vector3D(0, 1, 0) };
            projection = new Viewport3D { Camera = camera, IsHitTestVisible = false, Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var models = new Model3DGroup(); models.Children.Add(new AmbientLight(Colors.White)); models.Children.Add(plane);
            projection.Children.Add(new ModelVisual3D { Content = models }); Children.Add(projection);
            MouseMove += (_, e) =>
            {
                var p = e.GetPosition(this);
                UpdatePointer(new Point(p.X / Math.Max(1, ActualWidth), p.Y / Math.Max(1, ActualHeight)));
            };
            MouseLeave += (_, __) => Rest(true);
            PreviewMouseLeftButtonDown += (_, __) =>
            {
                if (CanMove) foil.BeginAnimation(CenturyPrismaticEffect.SweepProperty,
                    new DoubleAnimation(-.5, 1.9, TimeSpan.FromMilliseconds(1350)) { FillBehavior = FillBehavior.Stop });
            };
            SizeChanged += (_, __) => { Rest(false); UpdateGeometry(); };
            IsVisibleChanged += (_, __) => { if (!IsVisible) Rest(false); };
            Loaded += (_, __) => { SystemParameters.StaticPropertyChanged += SettingsChanged; UpdateAppearance(); UpdateGeometry(); };
            Unloaded += (_, __) => { SystemParameters.StaticPropertyChanged -= SettingsChanged; Rest(false); };
        }

        internal void SetTier(AchievementCompletionTier tier)
        {
            completed = tier == AchievementCompletionTier.BaseAndDlcAndPv;
            UpdateAppearance();
        }
        bool CanMove => completed && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
        void SettingsChanged(object sender, PropertyChangedEventArgs e) => UpdateAppearance();
        void UpdateAppearance()
        {
            Rest(false);
            artwork.Effect = completed && !SystemParameters.HighContrast ? foil : originalEffect;
        }
        void UpdateGeometry()
        {
            var w = ActualWidth; var h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            var bleed = Math.Max(24, Math.Max(w, h) * .14);
            var halfW = w / 2 + bleed; var halfH = h / 2 + bleed;
            projection.Width = w + 2 * bleed; projection.Height = h + 2 * bleed;
            projection.Margin = new Thickness(-bleed);
            texture.Viewbox = new Rect(-bleed, -bleed, projection.Width, projection.Height);
            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection { new Point3D(-halfW,-halfH,0), new Point3D(halfW,-halfH,0), new Point3D(halfW,halfH,0), new Point3D(-halfW,halfH,0) },
                TextureCoordinates = new PointCollection { new Point(0,1), new Point(1,1), new Point(1,0), new Point(0,0) },
                TriangleIndices = new Int32Collection { 0,1,2, 0,2,3 }
            };
            mesh.Freeze(); plane.Geometry = mesh;
            var distance = halfW / Math.Tan(camera.FieldOfView * Math.PI / 360);
            camera.Position = new Point3D(0, 0, distance); camera.LookDirection = new Vector3D(0, 0, -distance);
        }

        // Normalized input keeps the light and tilt consistent at every cover size.
        internal void UpdatePointer(Point position)
        {
            if (!CanMove || ActualWidth <= 0 || ActualHeight <= 0) return;
            var x = Math.Clamp(position.X, 0, 1); var y = Math.Clamp(position.Y, 0, 1);
            poseGeneration++; rotation.BeginAnimation(QuaternionRotation3D.QuaternionProperty, null);
            rotation.Quaternion = new Quaternion(new Vector3D(0, 1, 0), (x - .5) * 16)
                * new Quaternion(new Vector3D(1, 0, 0), (y - .5) * 16);
            foil.BeginAnimation(CenturyPrismaticEffect.LightProperty, null);
            foil.Light = new Point(.18 + x * .64, .18 + y * .64);
            projection.Visibility = Visibility.Visible;
            // The original scene stays hit-testable; its visual brush ignores this ancestor opacity.
            inputLayer.Opacity = 0;
        }
        void Rest(bool ease)
        {
            var generation = ++poseGeneration;
            foil.BeginAnimation(CenturyPrismaticEffect.SweepProperty, null);
            foil.BeginAnimation(CenturyPrismaticEffect.LightProperty, null); foil.Light = new Point(.4, .35);
            rotation.BeginAnimation(QuaternionRotation3D.QuaternionProperty, null);
            if (ease && CanMove && IsProjecting)
            {
                var settle = new QuaternionAnimation(rotation.Quaternion, Quaternion.Identity, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop };
                settle.Completed += (_, __) => { if (poseGeneration == generation) Rest(false); };
                rotation.BeginAnimation(QuaternionRotation3D.QuaternionProperty, settle);
            }
            else
            {
                rotation.Quaternion = Quaternion.Identity;
                inputLayer.Opacity = 1; projection.Visibility = Visibility.Collapsed;
            }
        }
    }
}
