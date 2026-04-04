using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    /// <summary>One row in the photography gallery (bind in XAML / Blend).</summary>
    public sealed class PhotographyGalleryEntry : INotifyPropertyChanged
    {
        public string FullPath { get; set; }
        public string Title { get; set; }
        public string Caption { get; set; }

        bool _starred;
        public bool Starred
        {
            get => _starred;
            set
            {
                if (_starred == value) return;
                _starred = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StarGlyph));
            }
        }

        /// <summary>Filled vs outline star for bindable UI.</summary>
        public string StarGlyph => Starred ? "\u2605" : "\u2606";

        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>Callbacks supplied by <see cref="MainWindow"/> so the gallery window can stay mostly declarative in XAML.</summary>
    public sealed class PhotographyGalleryHost
    {
        public string LibraryRoot { get; init; }
        public string AppVersion { get; init; }
        public string GamePhotographyTag { get; init; }
        public Func<bool, List<string>> LoadTaggedImagePaths { get; init; }
        public Func<IEnumerable<string>, List<PhotographyGalleryEntry>> BuildEntries { get; init; }
        public Action<PhotographyGalleryEntry> TogglePhotoStarred { get; init; }
        public Action PrepareExifOnBackgroundThread { get; init; }
        public Action<string> SetAppStatus { get; init; }
        public Action<string, Exception> LogError { get; init; }
        public Action OpenLibraryFolder { get; init; }
        public Action<string> OpenImageWithShell { get; init; }
        public Action<Image, string, int, Action<BitmapImage>> QueueImageLoad { get; init; }
    }
}
