using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class AccountsLogicTests
{
    [Theory]
    [InlineData("bob@mail.com:s3cret", "bob@mail.com", "s3cret")]
    [InlineData("bob;s3cret", "bob", "s3cret")]
    [InlineData("bob\ts3cret", "bob", "s3cret")]
    [InlineData("bob:pa:ss:word", "bob", "pa:ss:word")]   // le mot de passe peut contenir ':'
    [InlineData("bob", "bob", "")]                         // login seul
    [InlineData("  bob : x ", "bob", " x")]   // la ligne est nettoyée aux deux bouts avant découpe
    public void Account_lines_are_split_on_the_first_separator(string line, string login, string pass)
    {
        var a = Assert.Single(AccountsLogic.ParseAccounts(line).Valid);
        Assert.Equal((login, pass), (a.Login, a.Password));
    }

    [Fact]
    public void Blank_comments_empty_logins_and_duplicates()
    {
        var r = AccountsLogic.ParseAccounts("# comptes\n\nalice:1\n:oops\nALICE:2\nbad login:3\nbob:4\r\n");
        Assert.Equal(new[] { "alice", "bob" }, r.Valid.Select(a => a.Login));
        Assert.Equal(1, r.Duplicates);
        Assert.Equal(new[] { (4, "identifiant vide"), (6, "identifiant invalide") }, r.Errors.Select(e => (e.Line, e.Reason)));
    }

    [Fact]
    public void Summary_and_errors_never_contain_passwords()
    {
        var r = AccountsLogic.ParseAccounts("alice:topsecret\nbad login:topsecret\n:topsecret");
        Assert.DoesNotContain("topsecret", r.Summary);
        Assert.All(r.Errors, e => Assert.DoesNotContain("topsecret", e.Reason));
        Assert.Equal("1 compte(s) valide(s), 2 ligne(s) ignorée(s)", r.Summary);
    }

    [Fact]
    public void Status_is_case_insensitive_against_existing_accounts()
    {
        Assert.Equal(AccountsLogic.ImportStatus.AlreadyExists, AccountsLogic.StatusOf("Alice", new[] { "bob", "alice" }));
        Assert.Equal(AccountsLogic.ImportStatus.New, AccountsLogic.StatusOf("carol", new[] { "bob", "alice" }));
        Assert.Equal(AccountsLogic.ImportStatus.New, AccountsLogic.StatusOf("x", Array.Empty<string>()));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 5, false)]
    [InlineData(2, 5, null)]
    [InlineData(5, 5, true)]
    [InlineData(6, 5, true)]
    public void All_selected_is_tristate(int selected, int total, bool? expected)
        => Assert.Equal(expected, AccountsLogic.AllSelectedState(selected, total));

    [Fact]
    public void Selection_text()
    {
        Assert.Equal("", AccountsLogic.SelectionText(0, 8));
        Assert.Equal("3 / 8 sélectionné(s)", AccountsLogic.SelectionText(3, 8));
    }

    [Theory]
    [InlineData("Bob", null)]
    [InlineData("Jean-Marc", null)]
    [InlineData("Élodie", null)]
    [InlineData("Bo", "3 caractères minimum")]
    [InlineData("", "3 caractères minimum")]
    [InlineData(null, "3 caractères minimum")]
    [InlineData("Abcdefghijklmnopqrstu", "20 caractères maximum")]
    [InlineData("1abc", "doit commencer par une lettre")]
    [InlineData("Jean-Marc-Luc", "un seul tiret autorisé")]
    [InlineData("Jean-", "ne peut pas finir par un tiret")]
    [InlineData("Jean_x", "lettres et tiret uniquement")]
    [InlineData("Jean x", "lettres et tiret uniquement")]
    [InlineData("Jean2", "lettres et tiret uniquement")]
    public void Name_rule(string? name, string? error) => Assert.Equal(error, AccountsLogic.ValidateName(name));

    [Fact]
    public void Suggested_names_are_always_valid_and_reproducible()
    {
        for (int seed = 0; seed < 500; seed++)
        {
            var n = AccountsLogic.SuggestName(new Random(seed));
            Assert.Null(AccountsLogic.ValidateName(n));
        }
        Assert.Equal(AccountsLogic.SuggestName(new Random(7)), AccountsLogic.SuggestName(new Random(7)));
    }

    [Fact]
    public void Creation_colors()
    {
        var c = AccountsLogic.RandomColors(new Random(1));
        Assert.Equal(AccountsLogic.ColorSlots, c.Length);
        Assert.All(c, v => Assert.InRange(v, 0, 0xFFFFFF));
        Assert.All(AccountsLogic.ResetColors(), v => Assert.Equal(AccountsLogic.NoColor, v));
        Assert.Equal("#00FF7F", AccountsLogic.ColorLabel(0x00FF7F));
        Assert.Equal("#00000A", AccountsLogic.ColorLabel(10));
        Assert.Equal("—", AccountsLogic.ColorLabel(AccountsLogic.NoColor));
    }

    [Fact]
    public void Account_file_supports_a_label_and_passwords_with_colons()
    {
        var (valid, errors) = AccountsLogic.ParseAccountFile("-- comptes"+NL+"# autre"+NL+NL+"alice:pw1"+NL+"bob:pa:ss:Farmeur 2"+NL+"carol:x:"+NL+"nocolon"+NL+" :oops"+NL+"ALICE:dup"+NL+"dave:p:w:d:Banquier");
        Assert.Equal(new[] { "alice", "bob", "carol", "dave" }, valid.Select(v => v.Login));
        Assert.Equal(new (string, string?)[] { ("pw1", null), ("pa:ss", "Farmeur 2"), ("x", null), ("p:w:d", "Banquier") }, valid.Select(v => (v.Password, v.Label)));
        Assert.Equal(new[] { (7, "format identifiant:motdepasse attendu"), (8, "identifiant invalide") }, errors.Select(e => (e.Line, e.Reason)));
    }

    [Fact]
    public void Account_file_errors_never_contain_passwords()
    {
        var (_, errors) = AccountsLogic.ParseAccountFile("bad login:topsecret:lbl");
        Assert.All(errors, e => Assert.DoesNotContain("topsecret", e.Reason));
        Assert.Single(errors);
    }

    private const string NL = "\n";
}
