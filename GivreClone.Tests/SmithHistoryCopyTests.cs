extern alias F06App;
using SmithMagicViewModel = F06App::GivreClone.App.ViewModels.SmithMagicViewModel;
using SmithSessionHistoryRow = F06App::GivreClone.App.ViewModels.SmithSessionHistoryRow;
using Xunit;

namespace GivreClone.Tests;

public sealed class SmithHistoryCopyTests
{
    [Fact]
    public void Copied_history_keeps_every_session_title_and_its_full_detail_in_order()
    {
        var text = SmithMagicViewModel.HistoryText(new[]
        {
            new SmithSessionHistoryRow("03/11 10:00 | ui | terminé", "4 runes | 3 000 k debites\nCoût par tentative : 1 250 k"),
            new SmithSessionHistoryRow("03/11 11:00 | lua | en cours", "1 runes | 0 k debites"),
        });
        Assert.Equal("03/11 10:00 | ui | terminé\n4 runes | 3 000 k debites\nCoût par tentative : 1 250 k\n\n03/11 11:00 | lua | en cours\n1 runes | 0 k debites", text);
    }
}
