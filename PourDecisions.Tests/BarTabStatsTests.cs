using PourDecisions.Services;
using Xunit;

namespace PourDecisions.Tests
{
    // The run tab is static, so these run in one class (xunit serializes tests within a class) and
    // every test starts by applying its own snapshot.
    public class BarTabStatsTests
    {
        [Fact]
        public void Snapshot_replaces_whatever_was_left_from_an_earlier_room()
        {
            // A crewmate from a previous session is still on the tab (quit to the menu, no lobby-menu
            // edge, so nothing cleared it), then we join a different host mid-run.
            BarTabStats.ApplyRunSnapshot("111=5~Old;");
            BarTabStats.ApplyRunSnapshot("222=3~Alice;333=2~Bob;");

            Assert.Equal(0, BarTabStats.TabFor("111"));
            Assert.Equal(3, BarTabStats.TabFor("222"));
            Assert.Equal(2, BarTabStats.TabFor("333"));
            Assert.Equal(5, BarTabStats.TeamRunUnits());
            Assert.Equal(2, BarTabStats.Leaderboard().Count);
        }

        [Fact]
        public void Empty_snapshot_means_an_empty_tab()
        {
            BarTabStats.ApplyRunSnapshot("444=9~Stale;");
            BarTabStats.ApplyRunSnapshot("");

            Assert.Equal(0, BarTabStats.TeamRunUnits());
            Assert.Empty(BarTabStats.Leaderboard());
        }

        [Fact]
        public void Snapshot_names_survive_the_wire_delimiters()
        {
            BarTabStats.ApplyRunSnapshot("555=4~" + System.Uri.EscapeDataString("a;b=c") + ";");

            Assert.Equal(4, BarTabStats.TabFor("555"));
            Assert.Equal("a;b=c", BarTabStats.Leaderboard()[0].Value.Name);
        }
    }
}
