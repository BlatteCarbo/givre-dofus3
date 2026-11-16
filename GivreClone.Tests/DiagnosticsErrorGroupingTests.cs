extern alias F06App;

using V = F06App::GivreClone.App.ViewModels;
using Xunit;

namespace GivreClone.Tests;

public sealed class DiagnosticsErrorGroupingTests
{
    [Fact]
    public void Identical_rejections_collapse_into_one_group_without_losing_occurrences()
    {
        using var vm = new V.MZP0OperatorViewModel();
        int total = vm.JournalLimit + 50;   // au-delà du plafond du journal
        for (int i = 0; i < total; i++) Assert.False(vm.Accept(null));

        var group = Assert.Single(vm.ErrorGroups);
        Assert.Equal("NO_SOURCE", group.Code);
        Assert.Equal(total, group.Count);
        Assert.Equal(total, vm.ErrorOccurrenceCount);
        Assert.Equal(total, vm.RejectedCount);
        Assert.True(group.FirstUtc <= group.LastUtc);
        Assert.Equal(vm.JournalLimit, vm.VisibleJournal.Count);   // le journal reste borné, le groupe garde tout
    }
}
