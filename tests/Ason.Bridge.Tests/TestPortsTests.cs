using Ason.Bridge.Tests.TestSupport;

namespace Ason.Bridge.Tests;

/// <summary>
/// Pins the invariant that makes <see cref="TestPorts.Pair"/> work on every platform. The failure it guards
/// against is invisible on Windows: when the helper let the OS pick (two sockets bound to port 0, kept only
/// when the numbers happened to be neighbours) it passed here and failed on the Linux runner, where ephemeral
/// ports are spread pseudo-randomly - every process-level test then failed with "Could not find two
/// consecutive free ports". So the assertion worth having is not "a pair is found" (that was already true
/// locally) but "the ports come from the range the helper chooses, not from the ephemeral allocator".
/// </summary>
public class TestPortsTests {

    [Fact]
    public void The_pair_is_consecutive_and_comes_from_the_range_the_helper_chooses() {
        for (var round = 0; round < 25; round++) {
            var (first, second) = TestPorts.Pair();

            Assert.Equal(first + 1, second);
            Assert.InRange(first, 20000, 29999);
        }
    }

    [Fact]
    public void Two_pairs_taken_one_after_another_do_not_have_to_be_identical_but_both_hold_two_ports() {
        // The helper releases its reservation before returning, so a later call may hand out the same numbers
        // again. What must never happen is a pair whose neighbour is outside the reserved range.
        var (first, second) = TestPorts.Pair();
        var (nextFirst, nextSecond) = TestPorts.Pair();

        Assert.Equal(first + 1, second);
        Assert.Equal(nextFirst + 1, nextSecond);
        Assert.InRange(nextFirst, 20000, 29999);
    }
}
