using TheEasyWayForDrivers.Core.Update;
using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

/// <summary>
/// Synthetic public COM-shaped objects; these tests exercise the SAME
/// late-bound getters used by WindowsUpdateDriverProvider, not a mock
/// that silently evaluates properties before the exception boundary.
/// </summary>
public sealed class WuaSourceMetadataReaderTests
{
    private const string Mu = WindowsDriverSearchPlan.MicrosoftUpdateServiceId;
    private const string Configured = "68db184b-61f7-4b58-a5c8-10e2f0bf85aa";
    private const string Other = "4e8f2c5e-5d37-4ea5-80c4-323d7cef6bb1";
    private const string Source = "Windows Update online · componenti software";

    public sealed class FakeService(string id, bool failGetter = false)
    {
        public string ServiceID => failGetter
            ? throw new InvalidOperationException("SENSITIVE_SERVICE_METADATA")
            : id;
    }

    public sealed class FakeServices(
        IReadOnlyList<object> services, int failAtIndex = -1,
        Action<int>? beforeItem = null)
    {
        public int Count => services.Count;
        public int Calls { get; private set; }

        public object Item(int index)
        {
            Calls++;
            beforeItem?.Invoke(index);
            if (index == failAtIndex)
                throw new InvalidOperationException("SENSITIVE_SERVICE_ITEM");
            return services[index];
        }
    }

    public sealed class ReportedCount(int count)
    {
        public int Count => count;
    }

    [Fact]
    public void RegisteredMicrosoftUpdateAfterUnreadableComServiceIsDetected()
    {
        var source = new FakeServices(
        [
            new FakeService(Configured),
            new FakeService(Other, failGetter: true),
            new FakeService(Mu)
        ]);
        Assert.True(WuaSourceMetadataReader.IsMicrosoftUpdateRegistered(source));
        Assert.Equal(3, source.Calls);
        // Six actual Type=Driver passes and three Software passes can now
        // be planned. No service was registered or modified by OmegaDrive.
        Assert.Equal(6, WindowsDriverSearchPlan.Create(
            TheEasyWayForDrivers.Core.Models.DriverSearchMode.Comprehensive, true).Count);
        Assert.Equal(3, WindowsDriverSearchPlan.CreateRelatedSoftware(
            TheEasyWayForDrivers.Core.Models.DriverSearchMode.Comprehensive, true).Count);
    }

    [Fact]
    public void UnreadableItemDoesNotHideMicrosoftUpdateThatAppearsLater()
    {
        var source = new FakeServices(
        [
            new FakeService(Configured),
            new FakeService(Other),
            new FakeService(Mu)
        ], failAtIndex: 1);
        Assert.True(WuaSourceMetadataReader.IsMicrosoftUpdateRegistered(source));
        Assert.Equal(3, source.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownServiceWithoutConfirmedMicrosoftUpdateNeverMeansNotRegistered(
        bool unreadableGetter)
    {
        var services = new FakeServices(
        [
            new FakeService(Configured),
            new FakeService(Other, unreadableGetter)
        ], failAtIndex: unreadableGetter ? -1 : 1);
        var exception = Assert.Throws<InvalidDataException>(() =>
            WuaSourceMetadataReader.IsMicrosoftUpdateRegistered(services));
        Assert.Contains("non verificata", exception.Message);
        Assert.DoesNotContain("SENSITIVE", exception.Message);
        Assert.DoesNotContain(Other, exception.Message);
    }

    [Fact]
    public void CompleteListWithoutMicrosoftUpdateIsTrulyNotRegistered()
    {
        var source = new FakeServices(
        [
            new FakeService(Configured),
            new FakeService(Other)
        ]);
        Assert.False(WuaSourceMetadataReader.IsMicrosoftUpdateRegistered(source));
        Assert.Equal(2, source.Calls);
    }

    [Theory]
    [InlineData("7971f918-a847-4430-9279-4a52d1efe18d.evil")]
    [InlineData("")]
    [InlineData("not a service GUID")]
    public void InvalidServiceGuidIsNotMistakenForMicrosoftUpdate(
        string serviceId)
    {
        var source = new FakeServices([new FakeService(serviceId)]);
        Assert.Throws<InvalidDataException>(() =>
            WuaSourceMetadataReader.IsMicrosoftUpdateRegistered(source));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(WuaSourceMetadataReader.MaximumRegisteredServices + 1)]
    public void UnboundedServiceInventoryFailsWithoutReadingAnyItem(int count)
    {
        Assert.Throws<InvalidDataException>(() =>
            WuaSourceMetadataReader.IsMicrosoftUpdateRegistered(
                new ReportedCount(count)));
    }

    [Fact]
    public void CancellationDuringEnumerationIsNeverSwallowed()
    {
        using var cancellation = new CancellationTokenSource();
        var services = new FakeServices(
        [
            new FakeService(Configured),
            new FakeService(Other),
            new FakeService(Mu)
        ], beforeItem: index =>
        {
            if (index == 0)
                cancellation.Cancel();
        });
        Assert.ThrowsAny<OperationCanceledException>(() =>
            WuaSourceMetadataReader.IsMicrosoftUpdateRegistered(
                services, cancellation.Token));
    }

    public sealed class FakeIdentity(string updateId)
    {
        public string UpdateID => updateId;
    }

    public sealed class FakeSoftwareUpdate(
        string id, string title, bool failTitle = false,
        bool failIdentity = false)
    {
        public FakeIdentity Identity => failIdentity
            ? throw new InvalidOperationException("SENSITIVE_SOFTWARE_IDENTITY")
            : new FakeIdentity(id);

        public string Title => failTitle
            ? throw new InvalidOperationException("SENSITIVE_SOFTWARE_TITLE")
            : title;
    }

    public sealed class FakeSoftwareUpdates(
        IReadOnlyList<object> entries, int failAtIndex = -1,
        Action<int>? beforeItem = null)
    {
        public int Count => entries.Count;
        public int Calls { get; private set; }

        public object Item(int index)
        {
            Calls++;
            beforeItem?.Invoke(index);
            if (index == failAtIndex)
                throw new InvalidOperationException("SENSITIVE_SOFTWARE_ITEM");
            return entries[index];
        }
    }

    [Fact]
    public void UnreadableMiddleSoftwareTitleCannotEraseNvidiaKillerOrRealtek()
    {
        var nvidiaId = Guid.NewGuid().ToString("D");
        var killerId = Guid.NewGuid().ToString("D");
        var realtekId = Guid.NewGuid().ToString("D");
        var source = new FakeSoftwareUpdates(
        [
            new FakeSoftwareUpdate(nvidiaId,
                "NVIDIA - SoftwareComponent - 32.0.16.2101"),
            new FakeSoftwareUpdate(Guid.NewGuid().ToString("D"),
                "Intel PROSet Wireless extension", failTitle: true),
            new FakeSoftwareUpdate(killerId,
                "Killer Networking Software - 50.26.915.2621"),
            new FakeSoftwareUpdate(realtekId,
                "Realtek Semiconductor - Audio Driver"),
            new FakeSoftwareUpdate(Guid.NewGuid().ToString("D"),
                "Windows Security Intelligence")
        ]);

        var scan = WuaSourceMetadataReader.ReadSoftwareHints(
            source, source.Count, Source);
        Assert.Equal(5, source.Calls);
        Assert.Equal(1, scan.UnreadableTitles);
        Assert.Equal(3, scan.Hints.Count);
        Assert.Equal([nvidiaId, killerId, realtekId],
            scan.Hints.Select(hint => hint.UpdateId).ToArray());
        Assert.All(scan.Hints, hint =>
        {
            Assert.False(hint.IsDriverUpdate);
            Assert.False(hint.IsInstallable);
            Assert.DoesNotContain("SENSITIVE", hint.Detail);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StructuralEnumerationOrIdentityFailureDiscardsWholeSoftwarePass(
        bool failIdentity)
    {
        var source = new FakeSoftwareUpdates(
        [
            new FakeSoftwareUpdate(Guid.NewGuid().ToString("D"),
                "NVIDIA HD Audio Platform Controller"),
            new FakeSoftwareUpdate(Guid.NewGuid().ToString("D"),
                "Intel Wireless Bluetooth", failIdentity: failIdentity)
        ], failAtIndex: failIdentity ? -1 : 1);

        Assert.ThrowsAny<Exception>(() =>
            WuaSourceMetadataReader.ReadSoftwareHints(
                source, source.Count, Source));
        // No SoftwareReadResult can be merged. One successful item is not
        // grounds for pretending a broken WUA collection is complete.
    }

    [Fact]
    public void InvalidGuidAndFirmwareTitleAreNeverDriverSuggestions()
    {
        var source = new FakeSoftwareUpdates(
        [
            new FakeSoftwareUpdate("not-a-guid", "Intel Wireless Bluetooth"),
            new FakeSoftwareUpdate(Guid.NewGuid().ToString("D"),
                "NVIDIA UEFI Firmware"),
            new FakeSoftwareUpdate(Guid.NewGuid().ToString("D"),
                "Intel PROSet Wireless extension")
        ]);
        var result = WuaSourceMetadataReader.ReadSoftwareHints(
            source, source.Count, Source);
        Assert.Single(result.Hints);
        Assert.Equal(0, result.UnreadableTitles);
    }

    [Fact]
    public void CancellationDuringSoftwareEnumerationNeverReturnsPartialHints()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new FakeSoftwareUpdates(
        [
            new FakeSoftwareUpdate(Guid.NewGuid().ToString(),
                "NVIDIA HD Audio Platform Controller"),
            new FakeSoftwareUpdate(Guid.NewGuid().ToString(),
                "Killer Networking Software")
        ], beforeItem: i =>
        {
            if (i == 0)
                cancellation.Cancel();
        });
        Assert.ThrowsAny<OperationCanceledException>(() =>
            WuaSourceMetadataReader.ReadSoftwareHints(
                source, source.Count, Source, cancellation.Token));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(WuaDriverRelatedSoftwareClassifier.MaxRawSearchResults + 1)]
    public void SoftwareEntriesCountBeyondSecurityLimitFails(int count)
    {
        Assert.Throws<InvalidDataException>(() =>
            WuaSourceMetadataReader.ReadSoftwareHints(
                new FakeSoftwareUpdates([]), count, Source));
    }
}
