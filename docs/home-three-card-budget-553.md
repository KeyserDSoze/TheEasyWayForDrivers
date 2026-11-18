Home three-card WUA/PnP diagnostic budget (#553)

The three Home cards previously matched each Windows GUID against every device and all its identifiers without checking the diagnostic limits already enforced by the eight-category report and WUA/PnP audit. They could show a misleading measured zero or spend excessive CPU on legacy IPC data.

Now the same shared gate checks 8192 raw PnP rows, 2048 distinct normalized instances, 262144 PnP IDs, 4000 raw WUA updates and 2 million WUA IDs before merging GUIDs or indexing. Home reads at most one row past the raw bound; when PnP is unverified, it suppresses measured device counts and the local Driver Store inspection button. When only WUA exceeds limits, it retains verified PnP counts but marks WUA associations not evaluable, not zero.

A single category-bit index for strict Hardware/Compatible IDs replaces the per-device nested matcher. A distinct GUID is counted once in each category, while its explicit IDs may match multiple categories. OEM metadata, titles and weak model-only hints cannot create matches or installation eligibility. The local Driver Store selection is separately bounded and remains read-only.

New tests cover all five diagnostic budgets, the admitted padded duplicate case, cross-category same-GUID matches and unverified inventories. Synthetic CI does not establish actual driver availability on the Recoil 16; QA #91/#136 and Windows E2E #85 remain open.
