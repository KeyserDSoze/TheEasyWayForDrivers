# Discovery source coverage and vendor comparison — #89 #91 #92

## What is now actually queried

The **Analyze PC** button uses Windows Update Agent (WUA), with **four independent passes**:

| Source | WUA search criteria | Server selection | Evidence |
|---|---|---|---|
| Windows configured | non-installed, non-hidden driver | configured source (often WSUS or Microsoft Update) | completed / empty / failed |
| Windows Update online | non-installed, non-hidden driver | direct Windows Update | completed / empty / failed |
| Windows configured · advanced | includes hidden drivers, potentially superseded | configured source | completed / empty / failed |
| Windows Update online · advanced | includes hidden drivers, potentially superseded | direct Windows Update | completed / empty / failed |

Each pass **records count before deduplication and outcome**, without showing raw
COM exceptions to users. A failed pass contributes **no partial results**;
successful passes still produce results. Requested cancellation propagates.
If all sources fail, "zero updates" is **not** presented as a verified fact.

WUA candidates are still subject to the matching and Windows PnP policy of the
host PC. Newer versions are not necessarily more appropriate for that PC.
Download and install remain opt-in with user confirmation.

## OEM providers: routing, not magically a catalog query

The app also detects applicable Intel/NVIDIA/AMD and system OEM support channels.
These indicate **where official updates might be found**. None is currently
a general-purpose API that enumerates every vendor update in the Home scan.

The UI now displays the exact distinction: WUA queries = queried; Intel,
NVIDIA, Killer/Intel and notebook manufacturer = detected, **not queried for
individual vendor packages**. On a qualifying machine, the Home shows
selected installed driver versions and count of WUA Hardware ID matches.
An absent WUA match means **vendor update status unknown**.

The existing explicit, read-only Dell Driver Pack, Lenovo scripting and HP CMSL
workflows remain available under Advanced features and follow their existing
manual prerequisites/consent. They do not silently become install sources.

## Driver Easy comparison: observed, not validated (#91)

The third-party screenshot shows eight candidates (Intel graphics, Bluetooth,
PROSet Wi-Fi extension, Killer networking/application and Wi-Fi adapter; NVIDIA
Laptop GPU, HD Audio and Platform Controllers). Without that machine's
actual **installed versions, Hardware IDs/Compatible IDs, Windows build,
system OEM model** and signed original packages from vendors, there is no
basis to certify the eight as necessary or compatible.

For each target, manual acceptance should collect *non-sensitive*
hardware evidence and check against official Intel DSA/NVIDIA App and the
notebook OEM first. Treat extension/component/audio packages differently from
a physical PnP adapter; do not compare a standalone component by headline
version alone.

## CI and operational acceptance

- [x] Per-pass result modeling and tests for success, empty, offline failure and cancellation.
- [x] Serialised service-to-desktop coverage command tests, legacy IPC preserved.
- [x] Version evidence read-only and ID correlation tests, no "outdated" claim.
- [ ] Windows VM tests: one failed WUA pass, WSUS policy, offline, all passes failed.
- [ ] Real hardware validation of all eight third-party suggestions with vendor signatures,
  INF architecture/OS, matching IDs and notebook OEM requirements.
- [ ] Direct vendor catalog enumeration only where supported/documented APIs exist and
  can be used safely; never claim WHQL merely because another tool displays it.

**No automatic vendor driver downloads, arbitrary installer execution,
unsigned-package installation, firmware or BIOS changes.**
