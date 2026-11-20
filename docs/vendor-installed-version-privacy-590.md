# Driver evidence labels: private-name and version boundary (#590)

Windows device inventory data is read-only and may include display text
sourced from WMI, signed-driver metadata or IPC. OEM support handoffs and
NVIDIA/Intel/Killer installed-driver diagnostics must never echo raw PnP
instance IDs, user paths, private tokens or invisible Unicode in their
public-facing names or installed-version fields.

Both `VendorSourceDiscovery` and `VendorDriverEvidenceFinder` now apply
`PnpDeviceDisplayName.Create` to native device names before presentation.
The evidence finder also sanitizes before text-based vendor routing. PnP
Hardware/Compatible IDs are not rewritten: association with genuine WUA
updates still relies exclusively on existing identity validation.

`InstalledDriverVersionDisplay.Create` accepts only 2–4 dotted decimal
Windows driver-version components, each from 0 to 65535. Any other content
is replaced **entirely** with "non disponibile" rather than truncated or
treated as a known driver version. No outside driver package is queried;
there is no change to OEM eligibility, SetupAPI rank or Update All.

This work does not close the real-PC Recoil 16 comparison (#91/#136), the
NVIDIA hardware QA (#99) or Windows 11 E2E coverage (#85).
