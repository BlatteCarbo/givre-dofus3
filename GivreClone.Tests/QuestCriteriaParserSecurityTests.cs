using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

public sealed class QuestCriteriaParserSecurityTests
{
    [Theory]
    [InlineData("Qf=230)")]
    [InlineData("(Qf=230")]
    [InlineData("(Qf=230))")]
    [InlineData("Qf=230|")]
    [InlineData("Qf=230&")]
    [InlineData("Qf=230||Qf=231")]
    [InlineData("Qf=230&&Qf=231")]
    [InlineData("MI=420,0)")]
    public void Malformed_predicates_never_open_routes(string criterion)
        => Assert.False(QuestCriteria.Eval(criterion, id => id == 230, _ => 0));

    [Fact]
    public void Valid_compound_expression_still_follows_expected_operator_precedence()
    {
        Assert.True(QuestCriteria.Eval("(Qf=230&MI=420,1)|Qf=231",
            id => id == 230, element => element == 420 ? 1 : null));
        Assert.False(QuestCriteria.Eval("(Qf=230&MI=420,1)|Qf=231",
            id => id == 230, element => element == 420 ? 0 : null));
    }

    [Theory]
    [InlineData("MI=420,1|")]
    [InlineData("(MI=420,1")]
    [InlineData("MI=420,1&Qf=230")]
    [InlineData("MI=420,1|NOT")]
    [InlineData("MI=420,1|MI?421,0")]
    [InlineData("MI=420,1|MI=invalid")]
    [InlineData("MI=420,1)garbage")]
    public void Only_syntactically_valid_interactive_conditions_count_as_interactive_only(string criterion)
        => Assert.False(QuestCriteria.IsInteractiveStateOnly(criterion));

    [Fact]
    public void Valid_interactive_only_expression_is_supported()
        => Assert.True(QuestCriteria.IsInteractiveStateOnly("(MI=420,1|MI=421,0)&MI=422,0"));
}
