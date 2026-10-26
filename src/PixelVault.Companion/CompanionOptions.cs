namespace PixelVault.Companion;

public sealed class CompanionOptions
{
    public string DataRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PixelVaultCompanion");
    public bool DemoEnabled { get; set; }
    public bool AllowHttp { get; set; }
    public string ScreenshotPilot { get; set; } = "";
}
