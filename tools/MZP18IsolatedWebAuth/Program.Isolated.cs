using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace GivreLauncher.WebAuth;

internal static class Program
{
	private sealed class StatusInfo
	{
		[JsonPropertyName("status")]
		public string Status { get; set; } = "";

		[JsonPropertyName("message")]
		public string? Message { get; set; }
	}

	private static readonly byte[] HsEnc = new byte[49]
	{
		23, 51, 32, 59, 52, 116, 13, 63, 56, 27,
		47, 46, 50, 116, 50, 59, 52, 62, 41, 50,
		59, 49, 63, 116, 44, 107, 116, 99, 60, 105,
		57, 107, 59, 109, 63, 119, 62, 53, 119, 52,
		53, 46, 119, 57, 50, 59, 52, 61, 63
	};

	private static string HandshakeSecret()
	{
		byte[] array = new byte[HsEnc.Length];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = (byte)(HsEnc[i] ^ 0x5A);
		}
		return Encoding.UTF8.GetString(array);
	}

	[STAThread]
		private static int Main(string[] args)
		{
			// Isolated TEST-only clone: never reuse an original GivreClone browser profile.
			// No browser or network operation before checking a fresh TEST profile root.
			if (!BrowserSetup.TryGetApprovedProfileRoot(out _))
			{
				return 3;
			}
			if (!HandshakeValid(Environment.GetEnvironmentVariable("MZ_WEBAUTH_HS")))
		{
			return 2;
		}
		string arg = GetArg(args, "--proxy");
		string arg2 = GetArg(args, "--lang");
		string login = null;
		string password = null;
		string proxyUser = null;
		string proxyPass = null;
		try
		{
			string text = Console.In.ReadLine();
			if (!string.IsNullOrEmpty(text))
			{
				using JsonDocument jsonDocument = JsonDocument.Parse(text);
				JsonElement rootElement = jsonDocument.RootElement;
				login = Str(rootElement, "login");
				password = Str(rootElement, "password");
				proxyUser = Str(rootElement, "proxy_user");
				proxyPass = Str(rootElement, "proxy_pass");
			}
		}
		catch
		{
		}
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		string arg3 = GetArg(args, "--browse");
		if (arg3 != null && arg3.Length > 0)
		{
			Application.Run(new BrowseForm(new BrowseSession
			{
				Proxy = arg,
				ProxyUser = proxyUser,
				ProxyPass = proxyPass,
				Login = login,
				Password = password,
				Lang = (string.IsNullOrEmpty(arg2) ? "fr" : arg2),
				Label = (GetArg(args, "--title") ?? "")
			}, arg3, root: true));
			return 0;
		}
		using LoginFlow loginFlow = new LoginFlow(arg, proxyUser, proxyPass, login, password, arg2);
		Application.Run(loginFlow);
		if (loginFlow.Result == null || string.IsNullOrEmpty(loginFlow.Result.AccessToken))
		{
			string status = ((loginFlow.ReachedRedirect || loginFlow.Error != null) ? "failed" : "cancelled");
			Console.Out.Write(JsonSerializer.Serialize(new StatusInfo
			{
				Status = status,
				Message = loginFlow.Error
			}));
			return 1;
		}
		Console.Out.Write(JsonSerializer.Serialize(loginFlow.Result));
		return 0;
	}

	private static string? GetArg(string[] a, string name)
	{
		int num = Array.IndexOf(a, name);
		if (num < 0 || num + 1 >= a.Length)
		{
			return null;
		}
		return a[num + 1];
	}

	private static string? Str(JsonElement r, string name)
	{
		if (r.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
		{
			string text = value.GetString();
			if (text != null && text.Length > 0)
			{
				return text;
			}
		}
		return null;
	}

	private static bool HandshakeValid(string? token)
	{
		if (string.IsNullOrEmpty(token))
		{
			return false;
		}
		long num = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
		for (long num2 = num - 1; num2 <= num + 1; num2++)
		{
			if (string.Equals(token, ComputeHandshake(num2), StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	internal static string ComputeHandshake(long window)
	{
		using HMACSHA256 hMACSHA = new HMACSHA256(Encoding.UTF8.GetBytes(HandshakeSecret()));
		return Convert.ToHexString(hMACSHA.ComputeHash(Encoding.UTF8.GetBytes(window.ToString())));
	}
}
