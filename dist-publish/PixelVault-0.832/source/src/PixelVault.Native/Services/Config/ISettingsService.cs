using System;

namespace PixelVaultNative
{
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
