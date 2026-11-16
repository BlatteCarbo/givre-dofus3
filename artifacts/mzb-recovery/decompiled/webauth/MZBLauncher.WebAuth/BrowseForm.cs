using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MZBLauncher.WebAuth;

internal sealed class BrowseForm : Form
{
	private static readonly (string Fr, string En, string Es, string Url)[] Shortcuts = new (string, string, string, string)[3]
	{
		("Compte Ankama", "Ankama account", "Cuenta Ankama", "https://account.ankama.com/"),
		("Boutique Ankama", "Ankama store", "Tienda Ankama", "https://store.ankama.com/"),
		("Site Dofus", "Dofus website", "Web de Dofus", "https://www.dofus.com/")
	};

	private static readonly Color Bar = Color.FromArgb(18, 22, 30);

	private static readonly Color Ok = Color.FromArgb(90, 210, 140);

	private static readonly Color Danger = Color.FromArgb(240, 120, 90);

	private static readonly Color Warn = Color.FromArgb(240, 200, 90);

	private readonly BrowseSession _s;

	private readonly string? _startUrl;

	private readonly bool _root;

	private readonly WebView2 _web = new WebView2
	{
		Dock = DockStyle.Fill
	};

	private readonly TextBox _address = new TextBox
	{
		Dock = DockStyle.Fill,
		BorderStyle = BorderStyle.FixedSingle
	};

	private readonly Button _back = NavButton("‹");

	private readonly Button _forward = NavButton("›");

	private readonly Button _reload = NavButton("⟳");

	private readonly Button _shortcuts = new Button
	{
		Dock = DockStyle.Fill,
		FlatStyle = FlatStyle.Flat,
		ForeColor = Color.Gainsboro,
		AutoSize = true
	};

	private readonly Label _status = new Label
	{
		Dock = DockStyle.Top,
		Height = 26,
		TextAlign = ContentAlignment.MiddleLeft,
		Padding = new Padding(10, 0, 10, 0),
		BackColor = Bar,
		ForeColor = Color.Gainsboro
	};

	private readonly TaskCompletionSource<CoreWebView2> _ready = new TaskCompletionSource<CoreWebView2>(TaskCreationOptions.RunContinuationsAsynchronously);

	private string? _notice;

	private string? _pending;

	private bool _signInTried;

	private const string AccountHost = "account.ankama.com";

	private const string AuthHost = "auth.ankama.com";

	private const string SubmitScript = "(function(){var p=document.querySelector('input[type=\"password\"]');if(!p||!p.value)return false;var f=p.form;var b=(f?f.querySelector('button[type=\"submit\"],input[type=\"submit\"]'):null)||document.querySelector('button[type=\"submit\"],input[type=\"submit\"]');if(!b||b.disabled)return false;b.click();return true;})()";

	private string WebLang
	{
		get
		{
			string lang = _s.Lang;
			if ((!(lang == "en") && !(lang == "es")) || 1 == 0)
			{
				return "fr";
			}
			return _s.Lang;
		}
	}

	private string AccountHome => "https://account.ankama.com/" + WebLang;

	public BrowseForm(BrowseSession session, string? startUrl, bool root)
	{
		_s = session;
		_startUrl = startUrl;
		_root = root;
		Text = Caption(null);
		try
		{
			Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		}
		catch
		{
		}
		Width = 1180;
		Height = 860;
		StartPosition = (root ? FormStartPosition.CenterScreen : FormStartPosition.WindowsDefaultLocation);
		_status.Text = _s.T("Ouverture du navigateur du compte…", "Opening the account browser…", "Abriendo el navegador de la cuenta…");
		_shortcuts.Text = _s.T("Raccourcis ▾", "Shortcuts ▾", "Accesos ▾");
		ContextMenuStrip menu = new ContextMenuStrip();
		(string, string, string, string)[] shortcuts = Shortcuts;
		for (int i = 0; i < shortcuts.Length; i++)
		{
			(string, string, string, string) tuple = shortcuts[i];
			string url = tuple.Item4;
			menu.Items.Add(_s.T(tuple.Item1, tuple.Item2, tuple.Item3), null, (object? _, EventArgs _) =>
			{
				_pending = null;
				Go(url);
			});
		}
		_shortcuts.Click += (object? _, EventArgs _) =>
		{
			menu.Show(_shortcuts, new Point(0, _shortcuts.Height));
		};
		_back.Click += (object? _, EventArgs _) =>
		{
			CoreWebView2 coreWebView = _web.CoreWebView2;
			if (coreWebView != null && coreWebView.CanGoBack)
			{
				_web.CoreWebView2.GoBack();
			}
		};
		_forward.Click += (object? _, EventArgs _) =>
		{
			CoreWebView2 coreWebView = _web.CoreWebView2;
			if (coreWebView != null && coreWebView.CanGoForward)
			{
				_web.CoreWebView2.GoForward();
			}
		};
		_reload.Click += (object? _, EventArgs _) =>
		{
			_web.CoreWebView2?.Reload();
		};
		_address.KeyDown += (object? _, KeyEventArgs e) =>
		{
			if (e.KeyCode == Keys.Return)
			{
				e.SuppressKeyPress = true;
				_pending = null;
				Go(_address.Text);
			}
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 38,
			ColumnCount = 5,
			BackColor = Bar,
			Padding = new Padding(6, 5, 6, 5)
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(_back, 0, 0);
		tableLayoutPanel.Controls.Add(_forward, 1, 0);
		tableLayoutPanel.Controls.Add(_reload, 2, 0);
		tableLayoutPanel.Controls.Add(_address, 3, 0);
		tableLayoutPanel.Controls.Add(_shortcuts, 4, 0);
		Controls.Add(_web);
		Controls.Add(_status);
		Controls.Add(tableLayoutPanel);
		Load += OnLoad;
		if (!root)
		{
			return;
		}
		FormClosed += (object? _, FormClosedEventArgs _) =>
		{
			try
			{
				_s.Bridge?.Dispose();
			}
			catch
			{
			}
		};
	}

	private static Button NavButton(string glyph)
	{
		return new Button
		{
			Text = glyph,
			Dock = DockStyle.Fill,
			FlatStyle = FlatStyle.Flat,
			ForeColor = Color.Gainsboro,
			Enabled = false,
			Font = new Font(FontFamily.GenericSansSerif, 11f),
			Margin = new Padding(0, 0, 4, 0)
		};
	}

	private async void OnLoad(object? sender, EventArgs e)
	{
		_ = 4;
		try
		{
			if (_s.Env == null)
			{
				Task<BrowserSetup.Egress?> probe = BrowserSetup.ProbeEgressAsync(_s.Proxy, _s.ProxyUser, _s.ProxyPass);
				var (env, bridge) = await BrowserSetup.CreateEnvironmentAsync(_s.Proxy, _s.ProxyUser, _s.ProxyPass);
				_s.Env = env;
				_s.Bridge = bridge;
				BrowseSession s = _s;
				s.Egress = await probe;
			}
			await _web.EnsureCoreWebView2Async(_s.Env);
			CoreWebView2 core = _web.CoreWebView2;
			await BrowserSetup.ConfigureAsync(core, _s.ProxyUser, _s.ProxyPass);
			await BrowserSetup.ApplyTimeZoneAsync(core, _s.Egress?.TimeZone);
			Wire(core);
			_reload.Enabled = true;
			ShowStatus();
			_ready.TrySetResult(core);
			Start();
		}
		catch (Exception ex)
		{
			_ready.TrySetException(ex);
			_status.Text = _s.T("Erreur navigateur : ", "Browser error: ", "Error del navegador: ") + ex.Message;
			_status.ForeColor = Danger;
		}
	}

	private void Wire(CoreWebView2 core)
	{
		core.SourceChanged += (object? _, CoreWebView2SourceChangedEventArgs _) =>
		{
			_address.Text = core.Source;
			_notice = null;
			ShowStatus();
			Text = Caption(core.DocumentTitle);
		};
		core.DocumentTitleChanged += (object? _, object _) =>
		{
			Text = Caption(core.DocumentTitle);
		};
		core.HistoryChanged += (object? _, object _) =>
		{
			_back.Enabled = core.CanGoBack;
			_forward.Enabled = core.CanGoForward;
		};
		core.WindowCloseRequested += (object? _, object _) =>
		{
			Close();
		};
		core.NavigationStarting += (object? _, CoreWebView2NavigationStartingEventArgs ev) =>
		{
			Uri result;
			if (!IsWebScheme(ev.Uri))
			{
				ev.Cancel = true;
				Notice(_s.T("Lien vers une application bloqué", "Link to an app blocked", "Enlace a una aplicación bloqueado") + " (" + SchemeOf(ev.Uri) + ")");
			}
			else if (Uri.TryCreate(ev.Uri, UriKind.Absolute, out result) && result.Host.Equals("auth.ankama.com", StringComparison.OrdinalIgnoreCase) && result.AbsolutePath.TrimEnd('/').Equals("/login", StringComparison.OrdinalIgnoreCase))
			{
				ev.Cancel = true;
				string form = result.GetLeftPart(UriPartial.Authority) + "/login/ankama" + result.Query;
				BeginInvoke(() =>
				{
					core.Navigate(form);
				});
			}
		};
		core.LaunchingExternalUriScheme += (object? _, CoreWebView2LaunchingExternalUriSchemeEventArgs ev) =>
		{
			ev.Cancel = true;
			Notice(_s.T("Lien vers une application bloqué", "Link to an app blocked", "Enlace a una aplicación bloqueado") + " (" + SchemeOf(ev.Uri) + ")");
		};
		core.DownloadStarting += (object? _, CoreWebView2DownloadStartingEventArgs ev) =>
		{
			ev.Cancel = true;
			Notice(_s.T("Téléchargement bloqué", "Download blocked", "Descarga bloqueada"));
		};
		core.NewWindowRequested += async (object? _, CoreWebView2NewWindowRequestedEventArgs ev) =>
		{
			CoreWebView2Deferral deferral = ev.GetDeferral();
			try
			{
				BrowseForm browseForm = new BrowseForm(_s, null, root: false);
				browseForm.Show();
				ev.NewWindow = await browseForm._ready.Task;
				ev.Handled = true;
			}
			catch
			{
				ev.Handled = true;
				core.Navigate(ev.Uri);
			}
			finally
			{
				deferral.Complete();
			}
		};
		core.NavigationCompleted += async (object? _, CoreWebView2NavigationCompletedEventArgs ev) =>
		{
			if (ev.IsSuccess && Uri.TryCreate(core.Source, UriKind.Absolute, out Uri result))
			{
				if (result.Host.Equals("auth.ankama.com", StringComparison.OrdinalIgnoreCase))
				{
					if (!string.IsNullOrEmpty(_s.Login))
					{
						try
						{
							await core.ExecuteScriptAsync(BrowserSetup.PrefillScript(_s.Login, _s.Password));
							if (!_signInTried && !string.IsNullOrEmpty(_s.Password))
							{
								_signInTried = true;
								await SignInAsync(core);
							}
						}
						catch
						{
						}
					}
				}
				else
				{
					string pending = _pending;
					if (pending != null && result.Host.Equals("account.ankama.com", StringComparison.OrdinalIgnoreCase) && !result.AbsolutePath.Contains("authorized", StringComparison.OrdinalIgnoreCase) && !result.AbsolutePath.Contains("sso-redirect", StringComparison.OrdinalIgnoreCase))
					{
						_pending = null;
						Go(pending);
					}
				}
			}
		};
	}

	private async Task SignInAsync(CoreWebView2 core)
	{
		for (int i = 0; i < 5; i++)
		{
			await Task.Delay(400);
			string text;
			try
			{
				text = await core.ExecuteScriptAsync("(function(){var p=document.querySelector('input[type=\"password\"]');if(!p||!p.value)return false;var f=p.form;var b=(f?f.querySelector('button[type=\"submit\"],input[type=\"submit\"]'):null)||document.querySelector('button[type=\"submit\"],input[type=\"submit\"]');if(!b||b.disabled)return false;b.click();return true;})()");
			}
			catch
			{
				break;
			}
			if (text == "true")
			{
				Notice(_s.T("connexion envoyée…", "signing in…", "conexión enviada…"));
				break;
			}
		}
	}

	private void Start()
	{
		if (_startUrl != null)
		{
			if ((Uri.TryCreate(_startUrl, UriKind.Absolute, out Uri result) && result.Host.Equals("account.ankama.com", StringComparison.OrdinalIgnoreCase)) || string.IsNullOrEmpty(_s.Login))
			{
				Go(_startUrl);
				return;
			}
			_pending = _startUrl;
			Notice(_s.T("connexion au compte…", "signing in…", "conectando a la cuenta…"));
			Go(AccountHome);
		}
	}

	private void Go(string raw)
	{
		string text = raw.Trim();
		if (text.Length == 0)
		{
			return;
		}
		CoreWebView2 coreWebView = _web.CoreWebView2;
		if (coreWebView != null)
		{
			if (!text.Contains("://", StringComparison.Ordinal))
			{
				text = "https://" + text;
			}
			if (!Uri.TryCreate(text, UriKind.Absolute, out Uri result) || !IsWebScheme(result.AbsoluteUri))
			{
				Notice(_s.T("Adresse invalide", "Invalid address", "Dirección no válida"));
			}
			else
			{
				coreWebView.Navigate(result.AbsoluteUri);
			}
		}
	}

	private static bool IsWebScheme(string uri)
	{
		bool flag = Uri.TryCreate(uri, UriKind.Absolute, out Uri result);
		if (flag)
		{
			bool flag2;
			switch (result.Scheme)
			{
			case "http":
			case "https":
			case "about":
			case "data":
			case "blob":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = flag2;
		}
		return flag;
	}

	private static string SchemeOf(string uri)
	{
		int num = uri.IndexOf(':');
		if (num <= 0)
		{
			return uri;
		}
		return uri.Substring(0, num);
	}

	private string Caption(string? title)
	{
		string text;
		if (_s.Label.Length > 0)
		{
			text = _s.Label;
		}
		else
		{
			text = ((!string.IsNullOrEmpty(_s.Login)) ? _s.Login : _s.T("Navigateur du compte", "Account browser", "Navegador de la cuenta"));
		}
		if (!string.IsNullOrWhiteSpace(title))
		{
			return text + " - " + title + " - MizanBot";
		}
		return text + " - MizanBot";
	}

	private void Notice(string text)
	{
		_notice = text;
		ShowStatus();
	}

	private void ShowStatus()
	{
		string value = ((Uri.TryCreate(_web.CoreWebView2?.Source, UriKind.Absolute, out Uri result) && result.Host.Length > 0) ? result.Host : "-");
		string value2 = (string.IsNullOrEmpty(_s.Proxy) ? _s.T("AUCUN proxy - IP RÉELLE exposée", "NO proxy - REAL IP exposed", "SIN proxy - IP REAL expuesta") : ("proxy " + _s.Proxy));
		BrowserSetup.Egress egress = _s.Egress;
		string value3 = (((object)egress != null) ? (_s.T("IP de sortie", "Exit IP", "IP de salida") + " : " + egress.Ip) : _s.T("IP de sortie non vérifiée", "Exit IP not checked", "IP de salida no verificada"));
		string text = _s.Egress?.TimeZone;
		string value4 = ((text != null) ? ("     ·     " + _s.T("fuseau", "time zone", "zona horaria") + " " + text) : "");
		string value5 = ((_notice == null) ? "" : ("     ·     " + _notice));
		_status.Text = $"{_s.T("Site", "Site", "Sitio")} : {value}     ·     {value3}     ·     {value2}{value4}{value5}";
		Label status = _status;
		Color foreColor;
		if (string.IsNullOrEmpty(_s.Proxy))
		{
			foreColor = Danger;
		}
		else
		{
			foreColor = ((_notice != null || (object)_s.Egress == null) ? Warn : Ok);
		}
		status.ForeColor = foreColor;
	}
}
