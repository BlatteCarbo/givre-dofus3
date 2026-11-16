using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace MZBLauncher.WebAuth;

internal static class BrowserSetup
{
	public sealed record Egress(string Ip, string? TimeZone);

	private static FileStream? _profileLock;

	private static string? s_uaOverride;

	public static async Task<(CoreWebView2Environment Env, LocalSocksBridge? Bridge)> CreateEnvironmentAsync(string? proxy, string? proxyUser, string? proxyPass)
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
			return (Env: await CoreWebView2Environment.CreateAsync(null, NewProfileDir(), coreWebView2EnvironmentOptions), Bridge: bridge);
		}
		catch
		{
			bridge?.Dispose();
			throw;
		}
	}

	private static string NewProfileDir()
	{
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MizanBot", "browser");
		PurgeProfiles(text);
		string text2 = Path.Combine(text, "s_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text2);
		_profileLock = new FileStream(Path.Combine(text2, "inuse.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
		try
		{
			Directory.Delete(Path.Combine(Path.GetTempPath(), "MizanWebAuth"), recursive: true);
		}
		catch
		{
		}
		return text2;
	}

	private static void PurgeProfiles(string root)
	{
		if (!Directory.Exists(root))
		{
			return;
		}
		string[] directories = Directory.GetDirectories(root);
		foreach (string text in directories)
		{
			string fileName = Path.GetFileName(text);
			if (!fileName.StartsWith("s_", StringComparison.Ordinal) && !fileName.StartsWith("a_", StringComparison.Ordinal))
			{
				continue;
			}
			try
			{
				string path = Path.Combine(text, "inuse.lock");
				if (File.Exists(path))
				{
					using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
					{
					}
				}
				Directory.Delete(text, recursive: true);
			}
			catch
			{
			}
		}
	}

	public static async Task ConfigureAsync(CoreWebView2 core, string? proxyUser, string? proxyPass)
	{
		await ApplyChromeIdentityAsync(core);
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

	private static async Task ApplyChromeIdentityAsync(CoreWebView2 core)
	{
		string text = core.Environment.BrowserVersionString.Split('.')[0];
		string ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/" + text + ".0.0.0 Safari/537.36";
		try
		{
			if (s_uaOverride == null)
			{
				string ua2 = ua;
				s_uaOverride = ChromeOverride(ua2, await ReadClientHintsAsync(core));
			}
			await core.CallDevToolsProtocolMethodAsync("Emulation.setUserAgentOverride", s_uaOverride);
		}
		catch
		{
			try
			{
				core.Settings.UserAgent = ua;
			}
			catch
			{
			}
		}
	}

	private static async Task<JsonElement> ReadClientHintsAsync(CoreWebView2 core)
	{
		string text = Path.Combine(Path.GetTempPath(), "MizanWebAuthProbe");
		Directory.CreateDirectory(text);
		File.WriteAllText(Path.Combine(text, "p.html"), "<!doctype html><title></title>");
		core.SetVirtualHostNameToFolderMapping("probe.mizan.example", text, CoreWebView2HostResourceAccessKind.Deny);
		TaskCompletionSource<bool> loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		core.NavigationCompleted += OnLoaded;
		JsonElement result;
		try
		{
			core.Navigate("https://probe.mizan.example/p.html");
			await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10L));
			using JsonDocument jsonDocument = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new Dictionary<string, object>
			{
				["expression"] = "navigator.userAgentData.getHighEntropyValues(['architecture','bitness','fullVersionList','model','platformVersion','wow64']).then(v => JSON.stringify(Object.assign({ brands: navigator.userAgentData.brands }, v)))",
				["awaitPromise"] = true,
				["returnByValue"] = true
			})));
			result = JsonDocument.Parse(jsonDocument.RootElement.GetProperty("result").GetProperty("value").GetString()).RootElement.Clone();
		}
		finally
		{
			core.NavigationCompleted -= OnLoaded;
			core.ClearVirtualHostNameToFolderMapping("probe.mizan.example");
			try
			{
				await core.CallDevToolsProtocolMethodAsync("Page.resetNavigationHistory", "{}");
			}
			catch
			{
			}
		}
		return result;
		void OnLoaded(object? sender, CoreWebView2NavigationCompletedEventArgs e)
		{
			loaded.TrySetResult(e.IsSuccess);
		}
	}

	private static string ChromeOverride(string ua, JsonElement h)
	{
		List<Dictionary<string, string>> list = Chrome(h.GetProperty("fullVersionList"));
		string value = list.FirstOrDefault((Dictionary<string, string> b) => b["brand"] == "Chromium")?["version"] ?? "";
		foreach (Dictionary<string, string> item in list)
		{
			if (item["brand"] == "Google Chrome")
			{
				item["version"] = value;
			}
		}
		JsonElement value2;
		return JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["userAgent"] = ua,
			["platform"] = "Win32",
			["userAgentMetadata"] = new Dictionary<string, object>
			{
				["brands"] = Chrome(h.GetProperty("brands")),
				["fullVersionList"] = list,
				["fullVersion"] = value,
				["platform"] = "Windows",
				["platformVersion"] = Str("platformVersion"),
				["architecture"] = Str("architecture"),
				["bitness"] = Str("bitness"),
				["model"] = Str("model"),
				["mobile"] = false,
				["wow64"] = h.TryGetProperty("wow64", out value2) && value2.ValueKind == JsonValueKind.True
			}
		});
		static List<Dictionary<string, string>> Chrome(JsonElement jsonElement)
		{
			return (from b in jsonElement.EnumerateArray()
				select (Brand: b.GetProperty("brand").GetString() ?? "", Version: b.GetProperty("version").GetString() ?? "") into b
				where b.Brand != "Microsoft Edge WebView2"
				select b).Select(((string Brand, string Version) b) =>
			{
				Dictionary<string, string> dictionary = new Dictionary<string, string>();
				string value3;
				if (!(b.Brand == "Microsoft Edge"))
				{
					(value3, _) = b;
				}
				else
				{
					value3 = "Google Chrome";
				}
				dictionary["brand"] = value3;
				dictionary["version"] = b.Version;
				return dictionary;
			}).ToList();
		}
		string Str(string name)
		{
			if (!h.TryGetProperty(name, out var value3))
			{
				return "";
			}
			return value3.GetString() ?? "";
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

	public static string PrefillScript(string? login, string? password)
	{
		return "(function(){function s(sel,v){if(!v)return;var e=document.querySelector(sel);if(e){e.focus();e.value=v;e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));}}s('#ankama-login,input[name=\"login\"],input[type=\"email\"],input[autocomplete=\"username\"]'," + JsonSerializer.Serialize(login ?? "") + ");s('#ankama-password,input[name=\"password\"],input[type=\"password\"],input[autocomplete=\"current-password\"]'," + JsonSerializer.Serialize(password ?? "") + ");})();";
	}
}
