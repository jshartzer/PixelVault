using System;

namespace PixelVaultNative
{
    /// <summary>
    /// Persistence for <c>PixelVault.settings.ini</c>. Callers build <see cref="AppSettings"/> snapshots from UI state
    /// (see the <c>MainWindow.SettingsState</c> partial) and pass the on-disk path explicitly — do not read/write the ini file elsewhere.
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>Merge ini file over <paramref name="initialState"/> when the file exists; otherwise return a copy of <paramref name="initialState"/>.</summary>
        AppSettings LoadFromIni(
            string path,
            AppSettings initialState,
            string appRoot,
            Func<string> findFfmpegOnPath,
            Func<string> readSteamGridDbTokenFromEnvironment);

        void SaveToIni(string path, AppSettings state);
    }
}
