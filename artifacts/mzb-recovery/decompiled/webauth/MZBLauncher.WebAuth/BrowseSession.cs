using Microsoft.Web.WebView2.Core;

namespace MZBLauncher.WebAuth;

internal sealed class BrowseSession
{
	public string? Proxy { get; init; }

	public string? ProxyUser { get; init; }

	public string? ProxyPass { get; init; }

	public string? Login { get; init; }

	public string? Password { get; init; }

	public string Lang { get; init; } = "fr";

	public string Label { get; init; } = "";

	public CoreWebView2Environment? Env { get; set; }

	public LocalSocksBridge? Bridge { get; set; }

	public BrowserSetup.Egress? Egress { get; set; }

	public string T(string fr, string en, string es)
	{
		string lang = Lang;
		if (!(lang == "en"))
		{
			if (lang == "es")
			{
				return es;
			}
			return fr;
		}
		return en;
	}
}
