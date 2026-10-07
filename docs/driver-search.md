# Driver search

OmegaDrive uses the documented Windows Update Agent (WUA) API for automatic
driver discovery.

## Recommended search

Use this for normal operation.

OmegaDrive searches online using the update service already configured on the
machine and requests:

`IsInstalled=0 and IsHidden=0 and Type='Driver'`

The results are treated as normal driver updates and can be selected for
installation.

## Comprehensive search

Use this when the recommended search appears incomplete.

OmegaDrive keeps all recommended results and performs additional WUA passes:

1. direct Windows Update online search;
2. configured-service search with potentially superseded updates included;
3. direct Windows Update search with potentially superseded updates included.

The broad passes use:

`IsInstalled=0 and Type='Driver'`

This can expose hidden or otherwise advanced candidates that the normal UI path
does not emphasize.

## Safety rules

- Results are deduplicated by WUA update ID.
- Hidden and advanced-only results are not selected automatically.
- Advanced results do not make a device appear to have a normal recommended
  update.
- Installation still requires an explicit user action.
- OmegaDrive does not scrape the Microsoft Update Catalog or vendor download
  pages.
- OEM vendor integrations remain separate official handoff/status providers.

## Result labels

- **Consigliato**: normal result from a non-hidden search.
- **Facoltativo**: WUA marks the update as browse-only when that property is
  available.
- **Avanzato**: found only by a broader comprehensive-search pass.
- **Nascosto**: WUA reports the update as hidden.

The **Fonte ricerca** column shows whether the candidate came from the machine's
configured update service or from an explicit Windows Update online pass.


## Discovery summary UI

The update page exposes four large navigation cards:

- **Consigliati per il PC** filters to normal recommended WUA results.
- **Facoltativi** filters to browse-only WUA results.
- **Fonti OEM** opens the OEM provider page.
- **Avanzati** filters to hidden and comprehensive-only candidates.

This keeps the common path obvious while preserving access to deeper results.
The OEM card intentionally reports applicable official update channels rather
than inventing a count of updates that vendor tools have not enumerated.
