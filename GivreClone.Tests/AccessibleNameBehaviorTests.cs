extern alias F06App;

using F06App::GivreClone.App.Behaviors;
using Xunit;

namespace GivreClone.Tests;

public class AccessibleNameBehaviorTests
{
    [Fact]
    public void Plain_text_content_keeps_its_own_name()
        => Assert.Null(AccessibleNameBehavior.Derive("Infobulle", "Lancer", new[] { "Lancer" }));

    [Fact]
    public void Icon_button_takes_its_tooltip()
        => Assert.Equal("Supprimer", AccessibleNameBehavior.Derive(" Supprimer ", null, new string?[0]));

    [Fact]
    public void Composed_content_joins_distinct_visible_texts_up_to_three()
        => Assert.Equal("Bots · 3 actifs · Prêt",
            AccessibleNameBehavior.Derive(null, null, new[] { "Bots", " ", "3 actifs", "Bots", "Prêt", "Ignoré" }));

    [Fact]
    public void Nothing_readable_leaves_the_button_unnamed()
        => Assert.Null(AccessibleNameBehavior.Derive(null, null, new string?[] { null, "  " }));
}
