using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MZBLauncher.WebAuth;

public sealed class LoginFlow : Form
{
	private const string ClientId = "102";

	private readonly string? _proxy;

	private readonly string? _proxyUser;

	private readonly string? _proxyPass;

	private readonly string? _login;

	private readonly string? _password;

	private readonly string _lang;

	private readonly WebView2 _web = new WebView2
	{
		Dock = DockStyle.Fill
	};

	private readonly Label _status = new Label
	{
		Dock = DockStyle.Top,
		Height = 30,
		TextAlign = ContentAlignment.MiddleLeft,
		Padding = new Padding(10, 0, 10, 0),
		BackColor = Color.FromArgb(18, 22, 30),
		ForeColor = Color.Gainsboro,
		Text = "Initialisation du navigateur…"
	};

	private readonly string _verifier;

	private readonly string _challenge;

	private bool _done;

	private LocalSocksBridge? _bridge;

	public LoginResult? Result { get; private set; }

	public bool ReachedRedirect { get; private set; }

	public string? Error { get; private set; }

	private string T(string fr, string en, string es)
	{
		string lang = _lang;
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

	public LoginFlow(string? proxy, string? proxyUser, string? proxyPass, string? login, string? password, string? lang = null)
	{
		_proxy = proxy;
		_proxyUser = proxyUser;
		_proxyPass = proxyPass;
		_login = login;
		_password = password;
		_lang = (string.IsNullOrEmpty(lang) ? "fr" : lang);
		_verifier = GenerateCodeVerifier();
		_challenge = CreateCodeChallenge(_verifier);
		Text = T("Connexion Ankama", "Ankama sign-in", "Conexión Ankama") + " - MizanLauncher";
		try
		{
			Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		}
		catch
		{
		}
		_status.Text = T("Initialisation du navigateur…", "Starting the browser…", "Iniciando el navegador…");
		Width = 540;
		Height = 760;
		StartPosition = FormStartPosition.CenterScreen;
		Controls.Add(_web);
		Controls.Add(_status);
		Load += OnLoad;
		FormClosed += (object? _, FormClosedEventArgs _) =>
		{
			try
			{
				_bridge?.Dispose();
			}
			catch
			{
			}
		};
	}

	private async void OnLoad(object? sender, EventArgs e)
	{
		_ = 4;
		try
		{
			(CoreWebView2Environment, LocalSocksBridge) tuple = await BrowserSetup.CreateEnvironmentAsync(_proxy, _proxyUser, _proxyPass);
			CoreWebView2Environment item = tuple.Item1;
			LocalSocksBridge item2 = tuple.Item2;
			_bridge = item2;
			await _web.EnsureCoreWebView2Async(item);
			CoreWebView2 core = _web.CoreWebView2;
			await BrowserSetup.ConfigureAsync(core, _proxyUser, _proxyPass);
			core.NavigationStarting += OnNavigationStarting;
			core.NavigationCompleted += OnNavigationCompleted;
			CoreWebView2 core2 = core;
			await BrowserSetup.ApplyTimeZoneAsync(core2, (await CheckEgressIpAsync())?.TimeZone);
			string uri = $"https://auth.ankama.com/login/ankama?code_challenge={_challenge}&redirect_uri=zaap://login&client_id={"102"}&direct=true" + "&origin_tracker=https://www.ankama-launcher.com/launcher";
			core.Navigate(uri);
		}
		catch (Exception ex)
		{
			Error = ex.Message;
			_status.Text = T("Erreur navigateur : ", "Browser error: ", "Error del navegador: ") + ex.Message;
		}
	}

	private async Task<BrowserSetup.Egress?> CheckEgressIpAsync()
	{
		BrowserSetup.Egress egress = await BrowserSetup.ProbeEgressAsync(_proxy, _proxyUser, _proxyPass);
		if ((object)egress == null)
		{
			_status.Text = T("Vérification de l'IP échouée", "IP check failed", "Falló la verificación de IP") + "   ·   proxy : " + (_proxy ?? T("aucun", "none", "ninguno"));
			_status.ForeColor = Color.FromArgb(240, 200, 90);
			return null;
		}
		string value = (string.IsNullOrEmpty(_proxy) ? T("AUCUN proxy - IP RÉELLE exposée", "NO proxy - REAL IP exposed", "SIN proxy - IP REAL expuesta") : ("proxy " + _proxy));
		_status.Text = $"{T("IP de sortie", "Exit IP", "IP de salida")} : {egress.Ip}     ·     {value}";
		_status.ForeColor = (string.IsNullOrEmpty(_proxy) ? Color.FromArgb(240, 120, 90) : Color.FromArgb(90, 210, 140));
		return egress;
	}

	private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
	{
		if (string.IsNullOrEmpty(_login) && string.IsNullOrEmpty(_password))
		{
			return;
		}
		try
		{
			await _web.CoreWebView2.ExecuteScriptAsync(BrowserSetup.PrefillScript(_login, _password));
		}
		catch
		{
		}
	}

	private async void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
	{
		if (_done || !e.Uri.StartsWith("zaap://login", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		_done = true;
		ReachedRedirect = true;
		e.Cancel = true;
		_status.Text = T("Code reçu - échange du token…", "Code received - exchanging token…", "Código recibido - intercambiando el token…");
		string text = ExtractQuery(e.Uri, "code");
		if (string.IsNullOrEmpty(text))
		{
			Error = T("Code d'autorisation absent.", "Authorization code missing.", "Falta el código de autorización.");
			Close();
			return;
		}
		try
		{
			Result = await ExchangeTokenAsync(text);
		}
		catch (Exception ex)
		{
			Result = null;
			Error = ex.Message;
		}
		Close();
	}

	private async Task<LoginResult?> ExchangeTokenAsync(string code)
	{
		List<CoreWebView2Cookie> list = await _web.CoreWebView2.CookieManager.GetCookiesAsync("https://auth.ankama.com");
		CookieContainer cookieContainer = new CookieContainer();
		foreach (CoreWebView2Cookie item in list)
		{
			try
			{
				cookieContainer.Add(new Cookie(item.Name, item.Value, "/", "auth.ankama.com"));
			}
			catch
			{
			}
		}
		HttpClientHandler httpClientHandler = new HttpClientHandler
		{
			CookieContainer = cookieContainer,
			UseCookies = true,
			AutomaticDecompression = DecompressionMethods.All
		};
		if (!string.IsNullOrEmpty(_proxy))
		{
			WebProxy webProxy = new WebProxy(_proxy);
			if (!string.IsNullOrEmpty(_proxyUser))
			{
				webProxy.Credentials = new NetworkCredential(_proxyUser, _proxyPass ?? "");
			}
			httpClientHandler.Proxy = webProxy;
			httpClientHandler.UseProxy = true;
		}
		using HttpClient http = new HttpClient(httpClientHandler);
		http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Zaap 3.12.31");
		FormUrlEncodedContent content = new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "authorization_code",
			["code"] = code,
			["client_id"] = "102",
			["redirect_uri"] = "zaap://login",
			["code_verifier"] = _verifier
		});
		using HttpResponseMessage resp = await http.PostAsync("https://auth.ankama.com/token", content);
		using JsonDocument jsonDocument = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
		JsonElement rootElement = jsonDocument.RootElement;
		if (!rootElement.TryGetProperty("access_token", out var value) || string.IsNullOrEmpty(value.GetString()))
		{
			return null;
		}
		JsonElement value2;
		return new LoginResult
		{
			AccessToken = (value.GetString() ?? ""),
			RefreshToken = (rootElement.TryGetProperty("refresh_token", out value2) ? (value2.GetString() ?? "") : "")
		};
	}

	private static string ExtractQuery(string uri, string key)
	{
		string text = key + "=";
		int num = uri.IndexOf(text, StringComparison.Ordinal);
		if (num < 0)
		{
			return "";
		}
		string text2 = uri.Substring(num + text.Length);
		int num2 = text2.IndexOf('&');
		return Uri.UnescapeDataString((num2 >= 0) ? text2.Substring(0, num2) : text2);
	}

	private static string GenerateCodeVerifier()
	{
		return RandomNumberGenerator.GetString("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~", RandomNumberGenerator.GetInt32(43, 129));
	}

	private static string CreateCodeChallenge(string verifier)
	{
		return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(verifier))).Replace('+', '-').Replace('/', '_')
			.TrimEnd('=');
	}
}
