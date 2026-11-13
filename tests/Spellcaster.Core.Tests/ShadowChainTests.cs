using Spellcaster.Core;
using Xunit;

namespace Spellcaster.Core.Tests;

public class ShadowChainTests
{
    private static ShadowDashDecision Dash(ShadowChain chain, double now, bool ready = false,
        int soul = 99, bool grounded = true)
    {
        var decision = chain.Decide(now, ready, soul, grounded);
        chain.Commit(now, decision, grounded, decision.IsShadow);
        return decision;
    }

    [Fact]
    public void FreeNativeShadowCostsNothingAndStartsWindow()
    {
        var chain = new ShadowChain();
        Assert.False(chain.InWindow(0));
        Assert.Equal(ShadowDashKind.Normal, Dash(chain, 0).Kind);
        var free = Dash(chain, 1, ready: true, soul: 0, grounded: false);
        Assert.Equal(ShadowDashKind.FreeShadow, free.Kind);
        Assert.Equal(0, free.SoulCost);
        Assert.True(chain.CanExtraAirDash(1.3));
        Assert.Equal(0, chain.AirChains);
        Assert.Equal(11, chain.Decide(1.3, false, 11, false).SoulCost);
    }

    [Theory]
    [InlineData(1)] [InlineData(3)] [InlineData(30)] [InlineData(60)] [InlineData(144)] [InlineData(240)]
    public void InclusiveWindowIsIndependentOfTimePartition(int steps)
    {
        var chain = new ShadowChain();
        Dash(chain, 10, ready: true);
        double now = 10;
        for (int i = 0; i < steps; i++)
        {
            now += .3 / steps;
            chain.Observe(now, true, 5, false);
        }
        Assert.True(chain.InWindow(now));
        Assert.Equal(11, chain.Decide(now, false, 99, true).SoulCost);
        Assert.False(chain.InWindow(now + .000001));
        Assert.False(chain.CanExtraAirDash(now + .000001));
        Assert.Equal(ShadowDashKind.Normal, chain.Decide(now + .000001, false, 99, true).Kind);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void CostsAreElevenTwentyTwoThirtyThreeAndRemainCapped(bool grounded)
    {
        var chain = new ShadowChain();
        Dash(chain, 0, ready: true, grounded: grounded);
        chain.ExtendWindow(.25);
        int[] expected = { 11, 22, 33, 33, 33, 33, 33, 33 };
        for (int i = 0; i < expected.Length; i++)
        {
            var next = Dash(chain, (i + 1) * .5, grounded: grounded);
            Assert.Equal(ShadowDashKind.PaidShadow, next.Kind);
            Assert.Equal(expected[i], next.SoulCost);
            Assert.True(next.WaiveDashCooldown);
            Assert.True(next.WaiveShadowCooldown);
            chain.ExtendWindow((i + 1) * .5 + .25);
        }
    }

    [Theory]
    [InlineData(0, 10, false, 0)] [InlineData(0, 11, true, 11)]
    [InlineData(1, 21, false, 0)] [InlineData(1, 22, true, 22)]
    [InlineData(2, 32, false, 0)] [InlineData(2, 33, true, 33)]
    [InlineData(3, 32, false, 0)] [InlineData(3, 33, true, 33)]
    public void OnlyAffordableShadowChainsDebitSoul(int priorChains, int soul, bool shadow, int debit)
    {
        var chain = new ShadowChain();
        Dash(chain, 0, ready: true);
        for (int i = 0; i < priorChains; i++) Dash(chain, (i + 1) * .2);
        var next = Dash(chain, (priorChains + 1) * .2, soul: soul);
        Assert.Equal(shadow, next.IsShadow);
        Assert.Equal(debit, next.SoulCost);
        Assert.Equal(shadow ? ShadowDashKind.PaidShadow : ShadowDashKind.UnaffordableChain, next.Kind);
    }

    [Fact]
    public void OrdinaryFallbackDoesNotRefreshWindowOrAdvancePrice()
    {
        var chain = new ShadowChain();
        Dash(chain, 0, ready: true);
        var fallback = Dash(chain, .2, soul: 10);
        Assert.True(fallback.WaiveDashCooldown);
        Assert.False(fallback.WaiveShadowCooldown);
        Assert.Equal(11, chain.Decide(.3, false, 99, true).SoulCost);
        Assert.False(chain.InWindow(.300001));
        Assert.Equal(ShadowDashKind.Normal, Dash(chain, .4).Kind);
    }

    [Theory]
    [InlineData(0)] [InlineData(11)]
    public void AirChainDoesNotLimitLaterAttemptsEvenWhenSoulIsInsufficient(int soul)
    {
        var chain = new ShadowChain();
        Dash(chain, 0, ready: true, grounded: false);
        Assert.True(chain.CanExtraAirDash(.2));
        var extra = Dash(chain, .2, soul: soul, grounded: false);
        Assert.Equal(soul == 11, extra.IsShadow);
        Assert.Equal(soul, extra.SoulCost);
        Assert.Equal(1, chain.AirChains);
        Assert.True(chain.CanExtraAirDash(.25));
        Assert.Equal(ShadowDashKind.PaidShadow, chain.Decide(.25, false, 99, false).Kind);
    }

    [Fact]
    public void QueriesDoNotSpendExtraAirDashOrAdvancePrice()
    {
        var chain = new ShadowChain();
        Dash(chain, 0, ready: true, grounded: false);
        for (int i = 0; i < 10; i++)
        {
            Assert.True(chain.CanExtraAirDash(.2));
            Assert.Equal(11, chain.Decide(.2, false, 99, false).SoulCost);
            Assert.Equal(0, chain.AirChains);
        }
    }

    [Fact]
    public void LandingKeepsWindowAndPriceThroughDashEnd()
    {
        var chain = new ShadowChain();
        chain.Observe(0, false, 1, false);
        Dash(chain, 0, ready: true, grounded: false);
        Dash(chain, .2, grounded: false);
        chain.Observe(.21, true, 1, false);
        Assert.True(chain.InWindow(.21));
        Assert.Equal(1, chain.AirChains);
        chain.ExtendWindow(.45);
        Assert.Equal(22, chain.Decide(.75, false, 99, true).SoulCost);
        Dash(chain, 2, ready: true);
        Assert.Equal(11, Dash(chain, 2.2).SoulCost);
        // Staying on the ground is not another landing.
        chain.Observe(2.21, true, 1, false);
        Assert.Equal(22, Dash(chain, 2.4).SoulCost);
    }

    [Theory]
    [InlineData(1)] [InlineData(60)] [InlineData(144)]
    public void OneSecondStationaryResetsEvenInAir(int steps)
    {
        var chain = new ShadowChain();
        Dash(chain, 0, ready: true, grounded: false);
        Dash(chain, .2, grounded: false);
        chain.Observe(.4, false, .001, false);
        double now = .4;
        for (int i = 0; i < steps; i++)
        {
            now += .999 / steps;
            chain.Observe(now, false, -.001, false);
        }
        Assert.Equal(1, chain.AirChains);
        chain.Observe(1.4, false, 0, false);
        Assert.Equal(0, chain.AirChains);
        Assert.False(chain.InWindow(1.4));
        Dash(chain, 2, ready: true, grounded: false);
        Assert.Equal(11, Dash(chain, 2.2, grounded: false).SoulCost);
    }

    [Theory]
    [InlineData(1, false)] [InlineData(0, true)]
    public void MovementOrDashingInterruptsIdleReset(double velocity, bool dashing)
    {
        var chain = new ShadowChain();
        Dash(chain, 0, ready: true, grounded: false);
        Dash(chain, .2, grounded: false);
        chain.Observe(.4, false, 0, false);
        chain.Observe(1.3, false, velocity, dashing);
        chain.Observe(1.4, false, 0, false);
        chain.Observe(2.399, false, 0, false);
        Assert.Equal(1, chain.AirChains);
        chain.Observe(2.4, false, 0, false);
        Assert.Equal(0, chain.AirChains);
    }

    [Fact]
    public void RechargedNativeShadowStartsNewFreeRound()
    {
        var chain = new ShadowChain();
        Dash(chain, 0, ready: true);
        Dash(chain, .2);
        Dash(chain, .4, grounded: false);
        var free = Dash(chain, 2, ready: true, soul: 0, grounded: false);
        Assert.Equal(ShadowDashKind.FreeShadow, free.Kind);
        Assert.Equal(0, free.SoulCost);
        Assert.Equal(0, chain.AirChains);
        Assert.Equal(11, Dash(chain, 2.2, grounded: false).SoulCost);
    }

    [Fact]
    public void ExplicitResetClosesWindowAndDiscardsAllTransientState()
    {
        var chain = new ShadowChain();
        Dash(chain, 0, ready: true, grounded: false);
        Dash(chain, .2, grounded: false);
        chain.Reset();
        Assert.False(chain.InWindow(.21));
        Assert.False(chain.CanExtraAirDash(.21));
        Assert.Equal(0, chain.AirChains);
        Dash(chain, 1, ready: true);
        Assert.Equal(11, Dash(chain, 1.2).SoulCost);
    }

    [Fact]
    public void RejectedShadowDoesNotOpenOrRefreshWindow()
    {
        var chain = new ShadowChain();
        chain.Commit(0, chain.Decide(0, true, 0, true), true, actualShadow: false);
        Assert.False(chain.InWindow(0));
        Dash(chain, 1, ready: true);
        chain.Commit(1.2, chain.Decide(1.2, false, 99, true), true, actualShadow: false);
        Assert.False(chain.InWindow(1.31));
        Assert.Equal(11, chain.Decide(1.3, false, 99, true).SoulCost);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(1, 0)] [InlineData(5, 2)] [InlineData(7, 4)]
    [InlineData(21, 10)] [InlineData(32, 16)] [InlineData(-1, 0)]
    public void SharpShadowHalvesAndRoundsToEven(int damage, int expected) =>
        Assert.Equal(expected, ShadowChain.SharpDamage(damage));

    [Theory]
    [InlineData(-1)] [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    public void InvalidTimesCannotPoisonChain(double invalid)
    {
        var chain = new ShadowChain();
        Assert.Throws<ArgumentOutOfRangeException>(() => chain.InWindow(invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => chain.CanExtraAirDash(invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => chain.Decide(invalid, true, 99, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => chain.Commit(invalid, default, true, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => chain.Observe(invalid, true, 0, false));
        Dash(chain, 0, ready: true);
        Assert.Equal(11, Dash(chain, .2).SoulCost);
        Assert.Throws<ArgumentOutOfRangeException>(() => chain.ExtendWindow(invalid));
    }

    [Fact]
    public void ExtendWindowRestartsTheChainIntervalFromDashEnd()
    {
        var chain = new ShadowChain();
        var free = chain.Decide(10, true, 99, true);
        chain.Commit(10, free, true, true);
        Assert.True(chain.InWindow(10.3));
        chain.ExtendWindow(10.25);
        Assert.True(chain.InWindow(10.55));
        Assert.False(chain.InWindow(10.25 + 0.3 + 0.00001));
        var paid = chain.Decide(10.55, false, 99, true);
        Assert.Equal(ShadowDashKind.PaidShadow, paid.Kind);
        Assert.Equal(11, paid.SoulCost);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, true, true, true)]
    public void ActiveOnlyWithShadeCloakDashmasterAndShamanStone(
        bool cloak, bool dashmaster, bool shaman, bool expected) =>
        Assert.Equal(expected, ShadowChain.Active(cloak, dashmaster, shaman));
}
