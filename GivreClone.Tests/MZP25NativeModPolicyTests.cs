using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public sealed class MZP25NativeModPolicyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Invalid_pid_is_rejected_even_when_ownership_callback_would_allow_it(int pid)
    {
        Assert.False(MZP25NativeModPolicy.MayControl(pid, true, _ => true));
        Assert.False(MZP25NativeModPolicy.MayControl(pid, false, _ => true));
    }

    [Fact]
    public void Clone_rejects_unowned_client_and_requires_live_ownership()
    {
        int checkedPid = 0;
        Assert.False(MZP25NativeModPolicy.MayControl(123, true, pid =>
        {
            checkedPid = pid;
            return false;
        }));
        Assert.Equal(123, checkedPid);
        Assert.True(MZP25NativeModPolicy.MayControl(123, true, pid => pid == 123));
    }

    [Fact]
    public void Historical_non_clone_path_keeps_its_existing_semantics()
    {
        Assert.True(MZP25NativeModPolicy.MayControl(456, false,
            _ => throw new Xunit.Sdk.XunitException("callback must not run in legacy mode")));
    }

    [Fact]
    public void Clone_cannot_admit_unknown_owner_delegate()
    {
        Assert.Throws<ArgumentNullException>(() => MZP25NativeModPolicy.MayControl(111, true, null!));
    }
}
