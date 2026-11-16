using System.Text.Json.Serialization;

namespace MZBLauncher.WebAuth;

public sealed class LoginResult
{
	[JsonPropertyName("access_token")]
	public string AccessToken { get; set; } = "";

	[JsonPropertyName("refresh_token")]
	public string RefreshToken { get; set; } = "";
}
