using System;
using System.IO;
using System.Text.Json;
using PixelVault.Companion.Client;

namespace PixelVaultNative
{
    // This remembers a destination only. Passwords and session cookies remain in memory.
    sealed record CompanionDesktopSettings(string Server="http://127.0.0.1:8787",bool AllowHttp=false)
    {
        internal static string DefaultRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PixelVault","Companion");
        internal static CompanionDesktopSettings Load(string root)
        {
            var path=Path.Combine(root,"companion-connection.json");
            if(!File.Exists(path))return new();
            if(new FileInfo(path).Length>8192)throw new InvalidDataException("The saved Companion address is invalid.");
            var settings=JsonSerializer.Deserialize<CompanionDesktopSettings>(File.ReadAllText(path))??new();
            CompanionConnection.Server(settings.Server,settings.AllowHttp);return settings;
        }
        internal void Save(string root)
        {
            var server=CompanionConnection.Server(Server,AllowHttp);
            Directory.CreateDirectory(root);
            var path=Path.Combine(root,"companion-connection.json");var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temp,JsonSerializer.Serialize(this with{Server=server.AbsoluteUri}));File.Move(temp,path,true);}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
