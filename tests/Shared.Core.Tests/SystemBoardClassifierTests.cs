using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class SystemBoardClassifierTests
{
    [Theory]
    [InlineData("ASUSTeK COMPUTER INC.", "PRIME B650-PLUS")]
    [InlineData("ASUSTeK COMPUTER INC.", "ROG STRIX B550-F GAMING")]
    [InlineData("Micro-Star International Co., Ltd.", "MAG B650 TOMAHAWK WIFI")]
    [InlineData("GIGABYTE TECHNOLOGY CO., LTD.", "B650 AORUS ELITE AX")]
    public void LooksLikeMotherboard_RecognizesBoardFamilies(string vendor, string model) =>
        Assert.True(SystemBoardClassifier.LooksLikeMotherboard(vendor, model));

    [Theory]
    [InlineData("ASUSTeK COMPUTER INC.", "ROG Zephyrus G14")]
    [InlineData("MSI", "Raider GE78")]
    [InlineData("Dell Inc.", "XPS 15")]
    [InlineData("ASUSTeK COMPUTER INC.", "To be filled by O.E.M.")]
    [InlineData("Custom Builder", "B650 AORUS ELITE")]
    public void LooksLikeMotherboard_DoesNotGuessUnsupportedSystems(string vendor, string model) =>
        Assert.False(SystemBoardClassifier.LooksLikeMotherboard(vendor, model));
}
