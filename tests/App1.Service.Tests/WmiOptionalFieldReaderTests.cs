using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

/// <summary>
/// Synthetic WMI variants; no Windows driver/registry changes or network.
/// </summary>
public sealed class WmiOptionalFieldReaderTests
{
    private sealed class HostileVariant
    {
        public override string ToString() =>
            throw new InvalidOperationException("PRIVATE_INSTANCE");
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4060 Laptop GPU")]
    [InlineData("Intel(R) Killer Wi-Fi AX1675i")]
    [InlineData("Realtek(R) Audio")]
    [InlineData("oem42.inf")]
    [InlineData("32.0.15.7010")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\PRIVATE_PNP_INSTANCE")]
    public void ValidWmiScalarTextRemainsAvailable(string value)
    {
        Assert.Equal(value, WmiOptionalFieldReader.ReadString(() => value));
    }

    [Theory]
    [InlineData("management")]
    [InlineData("com")]
    [InlineData("access")]
    [InlineData("security")]
    [InlineData("cast")]
    public void OneOptionalGetterFailureDoesNotEraseOtherFields(string kind)
    {
        object? Broken() => throw kind switch
        {
            "management" => new ManagementException("PRIVATE_SERIAL"),
            "com" => new COMException("PRIVATE_DEVICE"),
            "access" => new UnauthorizedAccessException("PRIVATE_PATH"),
            "security" => new SecurityException("PRIVATE_USER"),
            _ => new InvalidCastException("PRIVATE_COM")
        };

        var deviceId = WmiOptionalFieldReader.ReadString(() =>
            @"PCI\VEN_10DE&DEV_28A0\PRIVATE_INSTANCE");
        var name = WmiOptionalFieldReader.ReadString(Broken);
        var deviceClass = WmiOptionalFieldReader.ReadString(() => "Display");
        var manufacturer = WmiOptionalFieldReader.ReadString(Broken);
        var safeName = PnpDeviceDisplayName.Create(
            name, deviceClass, deviceId);

        Assert.Equal("Scheda video (nome non disponibile)", safeName);
        Assert.Null(manufacturer);
        Assert.DoesNotContain("PRIVATE", safeName);

        var row = WmiSignedDriverFieldReader.Create(
            WmiOptionalFieldReader.ReadString(Broken),
            WmiOptionalFieldReader.ReadString(() => "32.0.15.7010"),
            WmiOptionalFieldReader.ReadString(Broken),
            WmiOptionalFieldReader.ReadString(() => "nv_dispig.inf"),
            WmiOptionalFieldReader.ReadBoolean(Broken));
        Assert.Null(row.Provider);
        Assert.Equal("32.0.15.7010", row.Version);
        Assert.Null(row.Date);
        Assert.Equal("nv_dispig.inf", row.InfName);
        Assert.False(row.IsSigned);

        // A later independent signed-driver row remains usable.
        Assert.Equal("Intel", WmiOptionalFieldReader.ReadString(() => "Intel"));
        Assert.True(WmiOptionalFieldReader.ReadBoolean(() => true));
    }

    [Fact]
    public void MalformedVariantsAreNotConvertedOrClaimedAsSigned()
    {
        var hostile = new HostileVariant();
        Assert.Null(WmiOptionalFieldReader.ReadString(() => hostile));
        Assert.Null(WmiOptionalFieldReader.ReadString(() => 42));
        Assert.Null(WmiOptionalFieldReader.ReadString(() => null));
        Assert.Null(WmiOptionalFieldReader.ReadString(() => ""));
        Assert.Null(WmiOptionalFieldReader.ReadString(() => "   "));
        Assert.Null(WmiOptionalFieldReader.ReadString(() => "NVIDIA\0PRIVATE"));
        Assert.Null(WmiOptionalFieldReader.ReadString(() => "NVIDIA\nPRIVATE"));
        Assert.Null(WmiOptionalFieldReader.ReadString(() =>
            new string('X', WmiOptionalFieldReader.MaximumTextLength + 1)));
        Assert.Equal(WmiOptionalFieldReader.MaximumTextLength,
            WmiOptionalFieldReader.ReadString(() =>
                new string('A', WmiOptionalFieldReader.MaximumTextLength))?.Length);
        Assert.False(WmiOptionalFieldReader.ReadBoolean(() => hostile));
        Assert.False(WmiOptionalFieldReader.ReadBoolean(() => "true"));
        Assert.False(WmiOptionalFieldReader.ReadBoolean(() => 1));
        Assert.False(WmiOptionalFieldReader.ReadBoolean(() => null));
        Assert.False(WmiOptionalFieldReader.ReadBoolean(() => false));
        Assert.True(WmiOptionalFieldReader.ReadBoolean(() => true));
    }

    [Fact]
    public void OptionalGetterFailureDoesNotTriggerWholeQueryFailure()
    {
        var empty = (IReadOnlyDictionary<string, string>)
            new Dictionary<string, string>();
        var query = WmiOptionalMetadataQuery.Read(() =>
        {
            var rows = new Dictionary<string, string>();
            var missing = WmiOptionalFieldReader.ReadString(() =>
                throw new COMException("PRIVATE_SERIAL"));
            Assert.Null(missing);
            var version = WmiOptionalFieldReader.ReadString(
                () => "23.160.0.4");
            Assert.NotNull(version);
            rows["PRIVATE_KILLER_INSTANCE"] = version;
            return (IReadOnlyDictionary<string, string>)rows;
        }, empty);

        Assert.True(query.Completed);
        Assert.True(WmiOptionalMetadataQuery.HasUsableRows(query));
        Assert.Equal("23.160.0.4",
            query.Value["PRIVATE_KILLER_INSTANCE"]);

        // Enumeration failure, unlike one optional getter, invalidates
        // the WHOLE metadata snapshot via the existing policy.
        var failed = WmiOptionalMetadataQuery.Read(
            () => throw new COMException("PRIVATE_SERIAL"), empty);
        Assert.False(failed.Completed);
        Assert.Empty(failed.Value);
    }

    [Fact]
    public void CancellationAndProgrammingErrorsStillPropagate()
    {
        Assert.Throws<OperationCanceledException>(() =>
            WmiOptionalFieldReader.ReadString(() =>
                throw new OperationCanceledException()));
        Assert.Throws<OperationCanceledException>(() =>
            WmiOptionalFieldReader.ReadBoolean(() =>
                throw new OperationCanceledException()));
        Assert.Throws<InvalidOperationException>(() =>
            WmiOptionalFieldReader.ReadString(() =>
                throw new InvalidOperationException("bug")));
        Assert.Throws<InvalidOperationException>(() =>
            WmiOptionalFieldReader.ReadBoolean(() =>
                throw new InvalidOperationException("bug")));
        Assert.Throws<ArgumentNullException>(() =>
            WmiOptionalFieldReader.ReadString(null!));
        Assert.Throws<ArgumentNullException>(() =>
            WmiOptionalFieldReader.ReadBoolean(null!));
    }
}
