# OEM support links: final browser-open policy (#394)

OmegaDrive opens the manual OEM support rows through VendorSourceButton_Click. Before #394 the final check in VendorSourceDiscovery.IsTrustedOfficialUrl allowed *any* HTTPS path/query/fragment on an approved domain. An approved hostname alone does not ensure the generated link is the intended official handoff and could propagate local identifiers in a modified URL.

## Allowed destinations

- Exact, hard-coded support URLs for Intel (DSA, Wi-Fi/Killer, Bluetooth), NVIDIA Driver Search, AMD, Realtek, Broadcom, Qualcomm, MediaTek, Logitech and Razer.
- Exact Dell catalog support and Command Update support URLs (both slash variants), HP support and Lenovo support links used in the read-only OEM metadata/applicability rows.
- NVIDIA GeForce editorial news paths **only** on the exact official host under /en-us/geforce/news/, after the RSS policy, with canonical absolute URI, non-empty article path, no query/fragment/credentials/alternate port and no encoded/ambiguous path. News is an announcement, not a per-device update.

All other same-domain paths, arbitrary query and fragment parameters, userinfo, host lookalikes and other schemes are refused by the final browser-open gate. The manual OEM grouping already uses an exact source URL dictionary and remains unchanged.

## Non-goals and verification

This change does not query new vendor catalog endpoints, download anything, install a driver, or change Windows Update selection. The source identifies official places for manual checking; it does not verify device applicability or signature. Tests cover all emitted fixed links, canonical NVIDIA news, query/fragment tampering and unapproved same-host paths. Windows CI and real-machine QA remain distinct.