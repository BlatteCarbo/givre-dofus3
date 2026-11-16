using GivreClone.App.Services.Signals;
using Xunit;

namespace GivreClone.Tests;

public class IncidentSubjectTests
{
    [Fact]
    public void Known_bot_names_character_label_server_and_session()
        => Assert.Equal("Bastionbot (main) [Draconiros] · session 4120", IncidentSubject.Describe("Bastionbot", "main", "Draconiros", 4120));

    [Fact]
    public void Two_unidentified_bots_stay_distinguishable_by_session()
        => Assert.NotEqual(IncidentSubject.Describe(null, null, null, 1), IncidentSubject.Describe(null, null, null, 2));

    [Fact]
    public void Missing_fields_are_named_not_left_blank()
        => Assert.Equal("personnage non identifié (sans libellé) [serveur inconnu] · session 7", IncidentSubject.Describe(" ", "", null, 7));
}
