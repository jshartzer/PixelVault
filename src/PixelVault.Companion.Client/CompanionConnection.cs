namespace PixelVault.Companion.Client;

/// <summary>No redirects, persistent cookies, certificate bypass or local repository fallback.</summary>
public static class CompanionConnection
{
    public static Uri Server(string address,bool allowHttp=false)
    {
        if(!Uri.TryCreate(address.Trim(),UriKind.Absolute,out var uri) || uri.Scheme is not ("http" or "https")
            || uri.AbsolutePath!="/" || uri.Query.Length>0 || uri.Fragment.Length>0 || uri.UserInfo.Length>0)
            throw new ArgumentException("Enter the server address, such as https://companion.home, without a page path or sign-in details.");
        if(uri.Scheme=="http" && !uri.IsLoopback && !allowHttp)
            throw new ArgumentException("Use an HTTPS address, or select Allow HTTP on my home network.");
        return uri;
    }

    public static HttpClient CreateHttpClient(Uri server)=>new(new HttpClientHandler
    {
        AllowAutoRedirect=false,UseCookies=true,CookieContainer=new(),
    }) {BaseAddress=server,Timeout=TimeSpan.FromSeconds(15)};
}
