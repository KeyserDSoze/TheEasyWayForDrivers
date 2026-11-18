# USB Bluetooth vendor handoffs for generic Windows devices (#559)

When Windows has not yet configured a Bluetooth USB radio, WMI may show
"Generic USB Device" and PNPClass Unknown/Other/USB/System rather than
Bluetooth. The PnP record can nevertheless contain a valid primary
USB VID+PID and the standard USB wireless controller class tuple
`USB\\Class_E0&SubClass_01&Prot_01` as CompatibleID.

The OEM manual-source selector now recognizes that **joint evidence**
for Intel (including Intel/Killer Bluetooth support), Realtek,
Qualcomm/Atheros, Broadcom and MediaTek. It requires a strictly parsed
primary USB VID+PID from the same node, the exact USB class token,
a consistent recognized vendor and an absent/generic PNPClass.
An explicitly unrelated Camera/Printer/HID/Display class vetoes the
fallback; incomplete or padded class tokens, foreign primary devices
and vendor-only IDs do not get a handoff.

This only increases visibility of an existing official HTTPS **manual**
support page. It does NOT query a per-device vendor catalog or infer a
newer driver, and the generic USB class token remains insufficient
for exact WUA/INF matching. `Aggiorna tutto` is unchanged.

Core regression tests cover the five vendors, legacy generic classes,
near-miss class tokens, invalid/foreign primary USB models and
absence of private identifiers in UI metadata. Real-device
multivendor Bluetooth QA remains #100/#91/#136; Windows E2E #85.
