using System.Collections.Generic;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        bool ShowMetadataReviewWindow(List<ReviewItem> items)
        {
            return MetadataReviewWindow.Show(this, AppVersion, items, new MetadataReviewServices
            {
                CreateButton = Btn,
                PreviewBadge = PreviewBadgeBrush,
                LoadImageSource = LoadImageSource,
                GamePhotographyTag = GamePhotographyTag
            });
        }
    }
}
