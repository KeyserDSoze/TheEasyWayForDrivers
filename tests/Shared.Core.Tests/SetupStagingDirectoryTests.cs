using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #530: the setup's elevated scratch directory must not be created
/// beneath a predictable extra parent before ZIP/junction preflight.
/// </summary>
public sealed class SetupStagingDirectoryTests
{
    [Fact]
    public void CreatesDistinctImmediateTempChildrenWithoutLegacyParent()
    {
        var first = SetupStagingDirectory.Create();
        var second = SetupStagingDirectory.Create();
        try
        {
            Assert.NotEqual(first, second);
            var expectedParent = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Path.GetTempPath()));
            foreach (var path in new[] { first, second })
            {
                Assert.True(Directory.Exists(path));
                Assert.StartsWith(SetupStagingDirectory.Prefix,
                    Path.GetFileName(path));
                Assert.Equal(expectedParent,
                    Path.GetDirectoryName(path),
                    OperatingSystem.IsWindows() ?
                        StringComparer.OrdinalIgnoreCase :
                        StringComparer.Ordinal);
                Assert.DoesNotContain(
                    $"{Path.DirectorySeparatorChar}TheEasyWayForDrivers" +
                    $"{Path.DirectorySeparatorChar}", path,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    [Fact]
    public void UniqueStagingStillSupportsLegitimateSetupZipExtraction()
    {
        using var payload = new MemoryStream();
        using (var zip = new ZipArchive(payload, ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach (var path in new[] {
                "Service/App1.Service.exe",
                "Desktop/App2.Desktop.exe",
                "Service/runtime/hostfxr.dll"
            })
            {
                var entry = zip.CreateEntry(path);
                using var output = entry.Open();
                output.Write(Encoding.UTF8.GetBytes("synthetic"));
            }
        }
        payload.Position = 0;
        var staging = SetupStagingDirectory.Create();
        try
        {
            SetupPayloadArchive.Extract(payload, staging);
            Assert.Equal("synthetic", File.ReadAllText(Path.Combine(
                staging, "Service", "App1.Service.exe")));
            Assert.Equal("synthetic", File.ReadAllText(Path.Combine(
                staging, "Desktop", "App2.Desktop.exe")));
            Assert.True(File.Exists(Path.Combine(
                staging, "Service", "runtime", "hostfxr.dll")));
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }
    
    [Fact]
    public void FirstPayloadWriteIsExclusiveAndDoesNotTruncateExistingFile()
    {
        var staging = SetupStagingDirectory.Create();
        try
        {
            using (var output = SetupStagingDirectory.CreatePayloadFile(staging))
                output.WriteByte(42);
            var zip = Path.Combine(staging, "payload.zip");
            Assert.Equal(new byte[] { 42 }, File.ReadAllBytes(zip));
            Assert.Throws<IOException>(() =>
                SetupStagingDirectory.CreatePayloadFile(staging));
            Assert.Equal(new byte[] { 42 }, File.ReadAllBytes(zip));
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    [Fact]
    public void PreexistingLinkedAncestorFailsBeforePayloadIsWrittenOutsideRoot()
    {
        var scratch = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-StagingPrewrite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var outside = Path.Combine(scratch, "outside");
        var link = Path.Combine(scratch, "redirect");
        Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateSymbolicLink(link, outside);
            var staging = Path.Combine(link, "missing-child");
            var error = Assert.Throws<InvalidDataException>(() =>
                SetupStagingDirectory.CreatePayloadFile(staging));
            Assert.Contains("collegamento", error.Message);
            Assert.DoesNotContain(scratch, error.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.GetFileSystemEntries(outside));
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public void MissingOrdinaryAncestorValidationHasNoSideEffects()
    {
        var scratch = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-StagingAbsent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var missing = Path.Combine(scratch, "one", "two");
            SetupStagingDirectory.EnsureNoReparseAncestors(missing);
            Assert.False(Directory.Exists(Path.Combine(scratch, "one")));
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }


    [Fact]
    public void WindowsStagingRootHasProtectedDaclAndExpectedOwner()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var staging = SetupStagingDirectory.Create();
        try
        {
            // Independently read back the actual filesystem descriptor,
            // not just the SDDL used for the CreateDirectoryW call.
            SetupStagingDirectory.VerifyPrivateWindowsRoot(staging);
            Assert.True(Directory.Exists(staging));

            using (var payload = SetupStagingDirectory.CreatePayloadFile(staging))
                payload.WriteByte(7);
            Assert.Equal(new byte[] { 7 },
                File.ReadAllBytes(Path.Combine(staging, "payload.zip")));
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    [Fact]
    public void WindowsOrdinaryTempChildDoesNotPassPrivateAclGate()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var tempChild = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-UntrustedAcl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempChild);
        try
        {
            // Ordinary creation inherits an unprotected DACL from Temp.
            var error = Assert.Throws<InvalidDataException>(() =>
                SetupStagingDirectory.VerifyPrivateWindowsRoot(tempChild));
            Assert.DoesNotContain(tempChild, error.Message,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempChild);
        }
    }


    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WindowsPrivateDaclRejectsDuplicateAndExtraAceFlags(bool duplicate)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var actor = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("SID unavailable.");
        var staging = SetupStagingDirectory.Create();
        try
        {
            // Windows coalesces identical ACEs. Give the duplicate SID
            // a different inheritance flag to preserve all three entries:
            // the user appears twice, Administrators is absent. The other
            // case has three distinct SIDs but SYSTEM is inherit-only.
            var sddl = duplicate
                ? $"D:P(A;OICI;FA;;;{actor})(A;OICINP;FA;;;{actor})(A;OICI;FA;;;SY)"
                : $"D:P(A;OICI;FA;;;{actor})(A;OICIIO;FA;;;SY)(A;OICI;FA;;;BA)";
            ApplyWindowsDacl(staging, sddl);
            var readback = ReadWindowsDacl(staging);
            Assert.True((readback.ControlFlags &
                ControlFlags.DiscretionaryAclProtected) != 0);
            Assert.Equal(3, readback.DiscretionaryAcl?.Count);

            var error = Assert.Throws<InvalidDataException>(() =>
                SetupStagingDirectory.VerifyPrivateWindowsRoot(staging));
            Assert.DoesNotContain(staging, error.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(actor, error.Message,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    private static void ApplyWindowsDacl(string directory, string sddl)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(
                sddl, 1, out var descriptor, out _))
            throw new IOException("Unable to prepare test ACL.");
        try
        {
            if (!SetFileSecurityW(directory, 4, descriptor))
                throw new IOException(
                    $"Unable to update test ACL (Win32 {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    private static RawSecurityDescriptor ReadWindowsDacl(string directory)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();

        if (!GetFileSecurityW(directory, 4, null, 0, out var needed) &&
            Marshal.GetLastWin32Error() != 122)
            throw new IOException("Unable to read test ACL length.");
        var data = new byte[checked((int)needed)];
        if (!GetFileSecurityW(directory, 4, data, needed, out _))
            throw new IOException("Unable to read test ACL.");
        return new RawSecurityDescriptor(data, 0);
    }

    [DllImport("advapi32.dll", EntryPoint =
        "ConvertStringSecurityDescriptorToSecurityDescriptorW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string sddl, uint revision, out IntPtr descriptor, out uint length);

    [DllImport("advapi32.dll", EntryPoint = "SetFileSecurityW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileSecurityW(
        string fileName, uint securityInformation, IntPtr descriptor);

    [DllImport("advapi32.dll", EntryPoint = "GetFileSecurityW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileSecurityW(
        string fileName, uint requestedInformation,
        [Out] byte[]? descriptor, uint length, out uint needed);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);


    [Fact]
    public void WindowsRelativePayloadCreateNeverFollowsExistingSymlink()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var staging = SetupStagingDirectory.Create();
        var outside = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-OutsidePayload-" + Guid.NewGuid().ToString("N"));
        var link = Path.Combine(staging, "payload.zip");
        File.WriteAllText(outside, "untouched");
        try
        {
            File.CreateSymbolicLink(link, outside);
            var failure = Record.Exception(() =>
            {
                using var payload = SetupStagingDirectory.CreatePayloadFile(staging);
            });
            Assert.IsType<IOException>(failure);
            Assert.Equal("untouched", File.ReadAllText(outside));
            Assert.True(File.Exists(link));
        }
        finally
        {
            if (File.Exists(link))
                File.Delete(link);
            Directory.Delete(staging, recursive: true);
            File.Delete(outside);
        }
    }

    [Fact]
    public void WindowsNativePayloadHandleBlocksStageRenameWhileOpen()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var staging = SetupStagingDirectory.Create();
        var moved = staging + "-moved";
        try
        {
            using (var payload = SetupStagingDirectory.CreatePayloadFile(staging))
            {
                payload.WriteByte(97);
                var error = Record.Exception(() => Directory.Move(staging, moved));
                Assert.True(error is IOException or UnauthorizedAccessException);
                Assert.False(Directory.Exists(moved));
            }
            Directory.Move(staging, moved);
            Assert.Equal(new byte[] { 97 },
                File.ReadAllBytes(Path.Combine(moved, "payload.zip")));
        }
        finally
        {
            if (Directory.Exists(moved))
                Directory.Delete(moved, recursive: true);
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }


    [Fact]
    public void WindowsPinnedStagingAclIsVerifiedOnSameHandle()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var staging = SetupStagingDirectory.Create();
        try
        {
            using (var anchor = SetupDirectoryAnchor.Open(staging))
            {
                SetupStagingDirectory.VerifyPrivateWindowsRoot(anchor);
                Assert.Single(Directory.GetFiles(
                    staging, ".OmegaDrive-Anchor-*.lock"));

                // Change the existing directory ACL WHILE its verified
                // handle remains open. The next check must inspect the
                // modified descriptor on that same handle, not another path.
                var actor = WindowsIdentity.GetCurrent().User?.Value
                    ?? throw new InvalidOperationException("SID unavailable.");
                ApplyWindowsDacl(staging, $"D:P(A;OICI;FA;;;{actor})");

                var error = Assert.Throws<InvalidDataException>(() =>
                    SetupStagingDirectory.VerifyPrivateWindowsRoot(anchor));
                Assert.DoesNotContain(staging, error.Message,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(actor, error.Message,
                    StringComparison.OrdinalIgnoreCase);
            }

            Assert.Empty(Directory.GetFiles(
                staging, ".OmegaDrive-Anchor-*.lock"));
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    [Fact]
    public void WindowsClosedStagingAnchorCannotPassAclVerification()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var staging = SetupStagingDirectory.Create();
        try
        {
            var anchor = SetupDirectoryAnchor.Open(staging);
            anchor.Dispose();
            Assert.Throws<PlatformNotSupportedException>(() =>
                SetupStagingDirectory.VerifyPrivateWindowsRoot(anchor));
            Assert.Empty(Directory.GetFiles(
                staging, ".OmegaDrive-Anchor-*.lock"));
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }


    [Fact]
    public void WindowsAnchoredPayloadReadRemovesExactlyOpenedFileOnDispose()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var staging = SetupStagingDirectory.Create();
        try
        {
            var path = Path.Combine(staging, "payload.zip");
            var content = Encoding.UTF8.GetBytes("embedded synthetic ZIP");
            using (var destination = SetupStagingDirectory.CreatePayloadFile(staging))
                destination.Write(content);

            using (var anchor = SetupDirectoryAnchor.Open(staging))
            {
                using (var input = SetupStagingDirectory
                    .OpenPayloadForExtraction(anchor, staging))
                {
                    using var result = new MemoryStream();
                    input.CopyTo(result);
                    Assert.Equal(content, result.ToArray());
                }

                Assert.False(File.Exists(path));
                Assert.Single(Directory.GetFiles(
                    staging, ".OmegaDrive-Anchor-*.lock"));
            }

            Assert.Empty(Directory.GetFileSystemEntries(staging));
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    [Fact]
    public void WindowsAnchoredPayloadReadRejectsOutsideSymlinkWithoutDeletingIt()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var staging = SetupStagingDirectory.Create();
        var outside = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-PayloadCanary-" + Guid.NewGuid().ToString("N"));
        var link = Path.Combine(staging, "payload.zip");
        File.WriteAllText(outside, "outside remains untouched");
        try
        {
            File.CreateSymbolicLink(link, outside);
            using (var anchor = SetupDirectoryAnchor.Open(staging))
            {
                var failure = Record.Exception(() =>
                {
                    using var input = SetupStagingDirectory
                        .OpenPayloadForExtraction(anchor, staging);
                });
                Assert.True(failure is IOException or InvalidDataException);
                Assert.Equal("outside remains untouched",
                    File.ReadAllText(outside));
                Assert.True(File.Exists(link));
            }

            Assert.Empty(Directory.GetFiles(staging,
                ".OmegaDrive-Anchor-*.lock"));
        }
        finally
        {
            if (File.Exists(link))
                File.Delete(link);
            Directory.Delete(staging, recursive: true);
            File.Delete(outside);
        }
    }

    [Fact]
    public void WindowsAnchoredPayloadReadDoesNotCreateMissingZip()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var staging = SetupStagingDirectory.Create();
        try
        {
            using (var anchor = SetupDirectoryAnchor.Open(staging))
            {
                Assert.Throws<IOException>(() =>
                    SetupStagingDirectory.OpenPayloadForExtraction(
                        anchor, staging));
                Assert.False(File.Exists(Path.Combine(staging, "payload.zip")));
            }

            Assert.Empty(Directory.GetFileSystemEntries(staging));
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }


    [Fact]
    public void WindowsEphemeralPayloadExtractsZipThroughSameHandleAndDisposesCleanly()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var staging = SetupStagingDirectory.Create();
        try
        {
            using (var anchor = SetupDirectoryAnchor.Open(staging))
            {
                SetupStagingDirectory.VerifyPrivateWindowsRoot(anchor);
                using (var payload =
                    SetupStagingDirectory.CreateEphemeralWindowsPayloadFile(anchor))
                {
                    using (var zip = new ZipArchive(payload, ZipArchiveMode.Create,
                        leaveOpen: true))
                    {
                        foreach (var name in new[]
                        {
                            "Service/App1.Service.exe",
                            "Desktop/App2.Desktop.exe",
                            "Service/runtime/hostfxr.dll"
                        })
                        {
                            using var entry = zip.CreateEntry(name).Open();
                            entry.Write(Encoding.UTF8.GetBytes("single handle"));
                        }
                    }

                    payload.Position = 0;
                    SetupPayloadArchive.Extract(payload, staging);
                    Assert.Equal("single handle", File.ReadAllText(
                        Path.Combine(staging, "Service", "App1.Service.exe")));
                    Assert.Equal("single handle", File.ReadAllText(
                        Path.Combine(staging, "Desktop", "App2.Desktop.exe")));
                }

                Assert.False(File.Exists(Path.Combine(staging, "payload.zip")));
            }

            Assert.Empty(Directory.GetFiles(staging,
                ".OmegaDrive-Anchor-*.lock", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    [Fact]
    public void WindowsEphemeralPayloadRemovesInvalidZipOnFailure()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var staging = SetupStagingDirectory.Create();
        try
        {
            using (var anchor = SetupDirectoryAnchor.Open(staging))
            {
                using (var payload =
                    SetupStagingDirectory.CreateEphemeralWindowsPayloadFile(anchor))
                {
                    payload.Write(Encoding.UTF8.GetBytes("not-a-zip"));
                    payload.Position = 0;
                    Assert.ThrowsAny<InvalidDataException>(() =>
                        SetupPayloadArchive.Extract(payload, staging));
                }
                Assert.False(File.Exists(Path.Combine(staging, "payload.zip")));
            }
            Assert.Empty(Directory.GetFileSystemEntries(staging));
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    [Fact]
    public void WindowsEphemeralPayloadRejectsExistingSymlinkWithoutChangingTarget()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var staging = SetupStagingDirectory.Create();
        var canary = Path.Combine(Path.GetTempPath(),
            "OmegaDrive-EphemeralCanary-" + Guid.NewGuid().ToString("N"));
        var link = Path.Combine(staging, "payload.zip");
        File.WriteAllText(canary, "untouched");
        try
        {
            File.CreateSymbolicLink(link, canary);
            using (var anchor = SetupDirectoryAnchor.Open(staging))
            {
                Assert.Throws<IOException>(() =>
                    SetupStagingDirectory.CreateEphemeralWindowsPayloadFile(
                        anchor));
                Assert.Equal("untouched", File.ReadAllText(canary));
                Assert.True(File.Exists(link));
            }
            Assert.Empty(Directory.GetFiles(staging,
                ".OmegaDrive-Anchor-*.lock"));
        }
        finally
        {
            if (File.Exists(link))
                File.Delete(link);
            Directory.Delete(staging, recursive: true);
            File.Delete(canary);
        }
    }

}
