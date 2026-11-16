using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace GivreLauncher.WebAuth;

internal static class BrowserSetup
{
	public sealed record Egress(string Ip, string? TimeZone);

	public const string ChromeUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

	public static async Task<(CoreWebView2Environment Env, LocalSocksBridge? Bridge)> CreateEnvironmentAsync(string? proxy, string? proxyUser, string? proxyPass, string? login)
	{
		CoreWebView2EnvironmentOptions coreWebView2EnvironmentOptions = new CoreWebView2EnvironmentOptions();
		string text = proxy;
		LocalSocksBridge bridge = null;
		if (!string.IsNullOrEmpty(proxy) && proxy.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(proxyUser))
		{
			try
			{
				string text2 = proxy.Substring("socks5://".Length);
				int num = text2.LastIndexOf(':');
				if (num > 0 && int.TryParse(text2.Substring(num + 1), out var result))
				{
					bridge = new LocalSocksBridge(text2.Substring(0, num), result, proxyUser, proxyPass ?? "");
					text = $"socks5://127.0.0.1:{bridge.Start()}";
				}
			}
			catch
			{
			}
		}
		List<string> list = new List<string> { "--disable-quic", "--force-webrtc-ip-handling-policy=disable_non_proxied_udp" };
		if (!string.IsNullOrEmpty(text))
		{
			list.Add("--proxy-server=" + text);
		}
		coreWebView2EnvironmentOptions.AdditionalBrowserArguments = string.Join(' ', list);
		try
		{
			return (Env: await CoreWebView2Environment.CreateAsync(null, ProfileDir(login), coreWebView2EnvironmentOptions), Bridge: bridge);
		}
		catch
		{
			bridge?.Dispose();
			throw;
		}
	}

		public static string ProfileDir(string? login)
		{
			if (!TryGetApprovedProfileRoot(out var root))
				throw new UnauthorizedAccessException("Isolated TEST browser directory not authorized.");
			var leaf = string.IsNullOrEmpty(login)
				? "session_" + Guid.NewGuid().ToString("N")
				: "login_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(login))).ToLowerInvariant()[..16];
			var path = Path.Combine(root, leaf);
			if (Directory.Exists(path) || File.Exists(path))
				throw new IOException("TEST browser profile already exists; no sharing or reuse.");
			return path;
		}

		public static bool TryGetApprovedProfileRoot(out string approved)
		{
			approved = string.Empty;
			if (!OperatingSystem.IsWindows()) return false;
			try
			{
				var supplied = Environment.GetEnvironmentVariable("MZ_TEST_PROFILE_ROOT");
				if (string.IsNullOrWhiteSpace(supplied) || !Path.IsPathFullyQualified(supplied)) return false;
				var leaf = Path.GetFileName(supplied.TrimEnd(Path.DirectorySeparatorChar));
				if (!Guid.TryParseExact(leaf, "N", out _)) return false;
				var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
				var replica = Path.Combine(local, "GivreReplica");
				var runs = Path.Combine(replica, "TestRuns");
				var run = Path.Combine(runs, "givre-app-offline");
				var browser = Path.Combine(run, "webauth");
				var expected = Path.GetFullPath(Path.Combine(browser, leaf));
				if (!string.Equals(supplied, expected, StringComparison.OrdinalIgnoreCase)) return false;
				foreach (var path in new[] { local, replica, runs, run, browser, expected })
				{
					var attributes = File.GetAttributes(path);
					if (!attributes.HasFlag(FileAttributes.Directory) ||
					    attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
				}
				approved = expected;
				return true;
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
			{
				return false;
			}
		}

	public static void Configure(CoreWebView2 core, string? proxyUser, string? proxyPass)
	{
		try
		{
			core.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
		}
		catch
		{
		}
		try
		{
			core.Settings.IsPasswordAutosaveEnabled = false;
			core.Settings.IsGeneralAutofillEnabled = false;
		}
		catch
		{
		}
		if (!string.IsNullOrEmpty(proxyUser))
		{
			core.BasicAuthenticationRequested += (object? _, CoreWebView2BasicAuthenticationRequestedEventArgs ev) =>
			{
				ev.Response.UserName = proxyUser;
				ev.Response.Password = proxyPass ?? "";
			};
		}
	}

	public static async Task<Egress?> ProbeEgressAsync(string? proxy, string? proxyUser, string? proxyPass)
	{
		HttpClientHandler httpClientHandler = new HttpClientHandler();
		if (!string.IsNullOrEmpty(proxy))
		{
			WebProxy webProxy = new WebProxy(proxy);
			if (!string.IsNullOrEmpty(proxyUser))
			{
				webProxy.Credentials = new NetworkCredential(proxyUser, proxyPass ?? "");
			}
			httpClientHandler.Proxy = webProxy;
			httpClientHandler.UseProxy = true;
		}
		using HttpClient http = new HttpClient(httpClientHandler)
		{
			Timeout = TimeSpan.FromSeconds(8L)
		};
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(await http.GetStringAsync("https://ipwho.is/"));
			JsonElement rootElement = jsonDocument.RootElement;
			string text;
			object obj;
			if (rootElement.TryGetProperty("ip", out var value))
			{
				text = value.GetString();
				if (text != null && text.Length > 0)
				{
					if (rootElement.TryGetProperty("timezone", out var value2) && value2.ValueKind == JsonValueKind.Object && value2.TryGetProperty("id", out var value3))
					{
						string text2 = value3.GetString();
						if (text2 != null && text2.Length > 0)
						{
							obj = text2;
							goto IL_017c;
						}
					}
					obj = null;
					goto IL_017c;
				}
			}
			goto end_IL_0106;
			IL_017c:
			string timeZone = (string)obj;
			return new Egress(text, timeZone);
			end_IL_0106:;
		}
		catch
		{
		}
		try
		{
			return new Egress((await http.GetStringAsync("https://api.ipify.org")).Trim(), null);
		}
		catch
		{
			return null;
		}
	}

	public static async Task ApplyTimeZoneAsync(CoreWebView2 core, string? timeZone)
	{
		if (string.IsNullOrEmpty(timeZone))
		{
			return;
		}
		try
		{
			await core.CallDevToolsProtocolMethodAsync("Emulation.setTimezoneOverride", JsonSerializer.Serialize(new Dictionary<string, string> { ["timezoneId"] = timeZone }));
		}
		catch
		{
		}
	}

	public static async Task PersistAnkamaSessionAsync(CoreWebView2 core, int days = 30)
	{
		try
		{
			foreach (CoreWebView2Cookie item in await core.CookieManager.GetCookiesAsync(""))
			{
				if (item.IsSession && item.Domain.TrimStart('.').EndsWith("ankama.com", StringComparison.OrdinalIgnoreCase))
				{
					item.Expires = DateTime.UtcNow.AddDays(days);
					core.CookieManager.AddOrUpdateCookie(item);
				}
			}
		}
		catch
		{
		}
	}

	public static string PrefillScript(string? login, string? password)
	{
		return "(function(){function s(sel,v){if(!v)return;var e=document.querySelector(sel);if(e){e.focus();e.value=v;e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));}}s('#ankama-login,input[name=\"login\"],input[type=\"email\"],input[autocomplete=\"username\"]'," + JsonSerializer.Serialize(login ?? "") + ");s('#ankama-password,input[name=\"password\"],input[type=\"password\"],input[autocomplete=\"current-password\"]'," + JsonSerializer.Serialize(password ?? "") + ");})();";
	}
}
