using System.Buffers.Binary;
using System.Text;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #525: Read-only CAB CFFILE names must have a unique, ordinary Win32
/// file identity. Synthetic headers only; no CFDATA extraction or drivers.
/// </summary>
public sealed class OemCabWindowsPathIdentityTests
{
    private static MemoryStream Cabinet(params string[] paths)
    {
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x4643534du); // MSCF
            writer.Write(0u); // reserved1
            writer.Write(0u); // cabinet size, patched below
            writer.Write(0u); // reserved2
            writer.Write(44u); // first CFFILE after header + CFFOLDER
            writer.Write(0u); // reserved3
            writer.Write((byte)3);
            writer.Write((byte)1);
            writer.Write((ushort)1); // one folder
            writer.Write(checked((ushort)paths.Length));
            writer.Write((ushort)0); // flags
            writer.Write((ushort)0); // setID
            writer.Write((ushort)0); // iCabinet
            writer.Write(0u); // first CFDATA offset, patched below
            writer.Write((ushort)0); // zero data blocks: directory fixture
            writer.Write((ushort)0); // compression type
            foreach (var path in paths)
            {
                writer.Write(8u); // declared uncompressed bytes
                writer.Write(0u); // offset in folder, never dereferenced
                writer.Write((ushort)0); // folder index
                writer.Write((ushort)0); // date
                writer.Write((ushort)0); // time
                writer.Write((ushort)0); // attributes
                writer.Write(Encoding.UTF8.GetBytes(path));
                writer.Write((byte)0);
            }
            var firstData = checked((uint)output.Position);
            writer.Write(new byte[16]); // opaque CFDATA boundary
            var size = checked((uint)output.Position);
            output.Position = 8;
            writer.Write(size);
            output.Position = 36;
            writer.Write(firstData);
        }
        return new MemoryStream(output.ToArray());
    }

    [Theory]
    [InlineData("CON.inf")]
    [InlineData("port/aux.txt")]
    [InlineData("NUL")]
    [InlineData("drivers/PrN.cat")]
    [InlineData("COM1.inf")]
    [InlineData("Lpt9.sys")]
    [InlineData("port/com¹.inf")]
    [InlineData("port/LPT².cat")]
    [InlineData("graphics.inf.")]
    [InlineData("drivers./gpu.inf")]
    [InlineData("drivers /gpu.inf")]
    [InlineData("folder/wifi.inf ")]
    public void Win32ReservedOrAliasedSegmentsCannotBeEvidence(string name)
    {
        using var cab = Cabinet(name);
        var error = Assert.Throws<InvalidDataException>(() =>
            OemCabIndex.Inspect(cab));
        Assert.DoesNotContain(name, error.Message);
    }

    [Theory]
    [InlineData("drivers", "drivers/gpu.inf")]
    [InlineData("drivers/gpu.inf", "drivers")]
    [InlineData("Drivers", "drivers/GPU.inf")]
    [InlineData("chipset/network", "CHIPSET/network/killer.inf")]
    [InlineData("chipset/network/killer.inf", "chipset/network")]
    public void FileCannotAlsoBeAnImplicitDirectoryInAnyOrder(
        string first, string second)
    {
        using var cab = Cabinet(first, second);
        var error = Assert.Throws<InvalidDataException>(() =>
            OemCabIndex.Inspect(cab));
        Assert.Contains("file/cartella", error.Message);
        Assert.DoesNotContain(first, error.Message);
        Assert.DoesNotContain(second, error.Message);
    }

    [Fact]
    public void OrdinaryNestedInfAndCatSiblingsRemainVisibleButUnverified()
    {
        using var cab = Cabinet("drivers/nvidia/gpu.inf",
            "drivers/intel/killer.inf", "drivers/nvidia/gpu.cat");
        var report = OemCabIndex.Inspect(cab);
        Assert.Equal(3, report.Entries.Count);
        Assert.Equal("drivers/nvidia/gpu.inf", report.Entries[0].Path);
        Assert.False(report.IsAuthenticated);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void DuplicateNormalizedSeparatorsRemainRejected()
    {
        using var cab = Cabinet(@"drivers\Gpu.inf", "DRIVERS/gpu.inf");
        Assert.Throws<InvalidDataException>(() => OemCabIndex.Inspect(cab));
    }
}
