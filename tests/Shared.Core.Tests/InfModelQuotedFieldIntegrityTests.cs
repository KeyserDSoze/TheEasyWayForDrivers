using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #502: a balanced but syntactically misplaced quote in Models/Manufacturer
/// must not yield any partial hardware identity to OEM discovery or CAT audit.
/// </summary>
public sealed class InfModelQuotedFieldIntegrityTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static DriverInfo Device(string pnpId, string instance = "PRIVATE_INSTANCE") =>
        new(instance, "Microsoft generic device", "Microsoft", "System",
            "Microsoft", "25.0.0.1",
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "oem12.inf", true, true, 0, [pnpId], []);

    private static string Inf(string modelLine,
        string manufacturer = "Vendor=Models,NTamd64.10.0") => $"""
        [Version]
        Signature="$Windows NT$"
        Class=Display
        CatalogFile=driver.cat
        DriverVer=11/10/2026,32.0.0.1
        [Manufacturer]
        {manufacturer}
        [Models.NTamd64.10.0]
        {modelLine}
        """;

    private static MemoryStream Archive(params (string Name, string Inf)[] entries)
    {
        var data = new MemoryStream();
        using (var zip = new ZipArchive(data, ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach (var (name, inf) in entries)
            {
                using var writer = new StreamWriter(
                    zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(inf);
            }
        }
        data.Position = 0;
        return data;
    }

    [Theory]
    [InlineData("Killer=Install,PCI\\VEN_8086&DEV_51F1,\"PCI\\VEN_10DE&DEV_28A0\"junk")]
    [InlineData("Killer=Install,PCI\\VEN_8086&DEV_51F1,PCI\\VEN_10DE&DEV_28A0\"\"")]
    [InlineData("Killer=Install,PCI\\VEN_8086&DEV_51F1,ab\"cd\"")]
    [InlineData("Killer=Install,\"PCI\\VEN_8086&DEV_51F1\"suffix")]
    public void BalancedButMalformedModelsLineCannotCreatePartialPnpMatch(
        string models)
    {
        var inf = Inf(models);
        // All supplied rows have balanced quotes, unlike the #389 case.
        Assert.NotEmpty(InfLogicalLines.Read(inf).ToArray());
        Assert.Throws<InvalidDataException>(() =>
            InfModelEvidence.ReadDeclaredIds(inf));
        Assert.Throws<InvalidDataException>(() =>
            InfModelEvidence.ReadMatchingIds(inf, [Device(Killer)]));
        Assert.Throws<InvalidDataException>(() =>
            InfPlatformPreflight.Assess(inf, Device(Killer),
                "x64", 10, 0, 26100, false));
    }

    [Theory]
    [InlineData("Vendor=Models,NTamd64.10.0,\"NTarm64\"junk")]
    [InlineData("Vendor=\"Models\"suffix,NTamd64.10.0")]
    [InlineData("Vendor=Models\"extra\",NTamd64.10.0")]
    public void MalformedManufacturerRecordCannotAuthorizeModelSection(
        string manufacturer)
    {
        var text = Inf($"Gpu=Install,{Nvidia}", manufacturer);
        Assert.Throws<InvalidDataException>(() =>
            InfModelEvidence.ReadDeclaredIds(text));
        Assert.Throws<InvalidDataException>(() =>
            InfModelEvidence.ReadMatchingIds(text, [Device(Nvidia)]));
    }

    [Fact]
    public void FullyQuotedValidManufacturerAndModelsRetainPrimaryAndCompatible()
    {
        var inf = Inf($"Gpu=\"Install\",\"{Nvidia}\",\"{Killer}\"",
            "Vendor=\"Models\",\"NTamd64.10.0\",\"NTarm64.10.0\"");
        var ids = InfModelEvidence.ReadDeclaredIds(inf);
        Assert.Equal(2, ids.Count);
        Assert.Contains(ids, row => row.HardwareId == Nvidia &&
            row.IsPrimaryHardwareId);
        Assert.Contains(ids, row => row.HardwareId == Killer &&
            !row.IsPrimaryHardwareId);
        Assert.Equal(2, InfModelEvidence.MatchStrength(Device(Nvidia), ids));
        Assert.Equal(1, InfModelEvidence.MatchStrength(Device(Killer), ids));
        var matched = InfModelEvidence.ReadMatchingIds(inf, [Device(Nvidia)]);
        Assert.True(Assert.Single(matched).IsPrimaryHardwareId);
    }

    [Fact]
    public void DoubledQuoteInsideQuotedPnpIsNotTrimmedIntoRealHardwareId()
    {
        // Inside a quoted field, "" encodes a literal " at the end.
        // The old .Trim('"') invented a valid NVIDIA hardware identity.
        var inf = Inf("Gpu=Install,\"" + Nvidia + "\"\"\"");
        Assert.Empty(InfModelEvidence.ReadDeclaredIds(inf));
        Assert.Empty(InfModelEvidence.ReadMatchingIds(inf, [Device(Nvidia)]));
        Assert.Equal(0, InfModelEvidence.MatchStrength(Device(Nvidia),
            InfModelEvidence.ReadDeclaredIds(inf)));
    }

    [Fact]
    public void CommaWithinQuotedInvalidPrimaryDoesNotTurnAliasesIntoExact()
    {
        var inf = Inf("Gpu=Install,\"" + Nvidia + "," + Killer + "\"," + Realtek);
        var ids = InfModelEvidence.ReadDeclaredIds(inf);
        var only = Assert.Single(ids);
        Assert.Equal(Realtek, only.HardwareId);
        Assert.False(only.IsPrimaryHardwareId);
        Assert.Equal(1, InfModelEvidence.MatchStrength(Device(Realtek), ids));
        Assert.Equal(0, InfModelEvidence.MatchStrength(Device(Nvidia), ids));
    }

    [Fact]
    public void MalformedInfIsRejectedWhileOtherZipNvidiaDriverRemainsVisible()
    {
        var bad = Inf($"Wifi=Install,{Killer},\"{Nvidia}\"junk");
        var good = Inf($"Gpu=Install,{Nvidia}");
        using var archive = Archive(
            ("PRIVATE_ORDER_44/broken.inf", bad),
            ("official/nvidia.inf", good));
        var inventory = OemZipInfEvidence.Inspect(archive);
        Assert.Equal(2, inventory.InfFiles.Count);
        Assert.Equal(1, inventory.RejectedInfCount);
        Assert.Equal(1, inventory.InfWithDeclaredIds);

        archive.Position = 0;
        var result = LocalOemDriverComparison.Compare(archive,
            [Device(Nvidia, "PRIVATE_NVIDIA_INSTANCE"),
             Device(Killer, "PRIVATE_KILLER_INSTANCE")],
            "x64", 10, 0, 26100);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("nvidia.inf", finding.InfName);
        Assert.True(finding.HasExactHardwareId);
        Assert.Equal(1, result.RejectedInfCount);
        Assert.Equal(1, result.PotentialNewerDevices);
        Assert.DoesNotContain("PRIVATE_ORDER_44", result.Summary);
        Assert.DoesNotContain("PRIVATE_INSTANCE", result.Summary);
        Assert.False(result.IsInstallable);
        Assert.False(result.IsVerifiedCompatible);
    }

    [Fact]
    public void CorruptInfDoesNotConsumeCatCheckOrHideLegitimateKiller()
    {
        var root = Directory.CreateTempSubdirectory("Omega-InfQuotes-502-").FullName;
        try
        {
            var badDir = Path.Combine(root, "bad");
            var goodDir = Path.Combine(root, "good");
            Directory.CreateDirectory(badDir);
            Directory.CreateDirectory(goodDir);
            File.WriteAllText(Path.Combine(badDir, "bad.inf"),
                Inf($"Gpu=Install,{Nvidia},\"{Killer}\"junk"));
            File.WriteAllText(Path.Combine(badDir, "driver.cat"), "FAKE CAT");
            File.WriteAllText(Path.Combine(goodDir, "good.inf"),
                Inf($"Wifi=Install,{Killer}"));
            File.WriteAllText(Path.Combine(goodDir, "driver.cat"), "FAKE CAT");
            var plan = OemFolderCatalogAuditPlan.Create(
                root, [Device(Nvidia), Device(Killer)], "x64");
            Assert.Equal(2, plan.InfExamined);
            Assert.Equal(1, plan.UnreadableInf);
            Assert.Equal(1, plan.MatchedInf);
            var selected = Assert.Single(plan.Checks);
            Assert.Equal("good.inf", Path.GetFileName(selected.InfPath));
            Assert.False(plan.IsComplete);
            Assert.False(plan.IsFullPackageVerified);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NormalMultilineManufacturerWithQuotedDecorationsStillMatches()
    {
        var inf = "[Manufacturer]\nVendor=\"Models\",\"NTamd64.10.0\",\\\n" +
            "\"NTarm64.10.0\"\n[Models.NTamd64.10.0]\n" +
            $"Gpu=Install,\"{Nvidia}\",\"{Realtek}\"\n";
        var entries = InfModelEvidence.ReadMatchingIds(
            inf, [Device(Nvidia), Device(Realtek)]);
        Assert.Equal(2, entries.Count);
        Assert.True(entries[0].IsPrimaryHardwareId);
        Assert.False(entries[1].IsPrimaryHardwareId);
    }

    [Fact]
    public void AbnormallyWideModelsRecordFailsClosedWithoutPartialIdentity()
    {
        var aliases = string.Join(",", Enumerable.Repeat("Invalid", 1024));
        var inf = Inf($"Gpu=Install,{Nvidia}," + aliases);
        Assert.Throws<InvalidDataException>(() =>
            InfModelEvidence.ReadMatchingIds(inf, [Device(Nvidia)]));
    }

    [Fact]
    public void ExistingCopyFilesQuotedPayloadParsingKeepsSameSemantics()
    {
        var inf = """
            [DefaultInstall]
            CopyFiles="@nvlddmkm.sys","DriverFiles"
            [DriverFiles]
            "renamed.dll","realtek.dll"
            [SourceDisksNames]
            1="OEM, driver media",,,
            [SourceDisksFiles.amd64]
            nvlddmkm.sys=1
            realtek.dll=1
            """;
        var report = InfDependencyPreview.Analyze(
            inf, ["nvlddmkm.sys", "realtek.dll"], "x64");
        Assert.Equal(2, report.PresentCount);
        Assert.Equal(0, report.MissingCount);
        Assert.False(report.IsCompletePackage);
    }
}
