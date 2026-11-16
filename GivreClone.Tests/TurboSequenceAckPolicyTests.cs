using GivreClone.App.Services;
using Xunit;
namespace GivreClone.Tests;
public class TurboSequenceAckPolicyTests
{
    [Theory]
    [InlineData(31, 200, 190, 100, true)] // same action ID acknowledged during an earlier cast
    [InlineData(31, 200, 190, 205, false)] // visible client already acknowledged this sequence
    [InlineData(31, 200, 190, 200, false)] // equal timestamp is valid (millisecond clock)
    [InlineData(31, 0, 190, 100, true)] // no SequenceStart mapping: request remains the lower bound
    [InlineData(31, 180, 190, 185, true)] // stale outer-sequence marker cannot validate an older ACK
    [InlineData(31, 0, 0, 100, false)] // no generation evidence: retain conservative behavior
    [InlineData(0, 200, 190, 100, false)]
    public void AckBelongsToTheCurrentRequestAndSequence(long id, long start, long request, long ack, bool expected) =>
        Assert.Equal(expected, TurboSequenceAckPolicy.ShouldInject(id, start, request, ack));
    [Fact] public void FirstOccurrenceIsAcknowledged() => Assert.True(TurboSequenceAckPolicy.ShouldInject(31, 200, 190, null));
}
