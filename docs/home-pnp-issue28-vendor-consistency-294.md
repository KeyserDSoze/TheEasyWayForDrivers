# Home — vendor PCI attendibile e problema Windows codice 28 (#294)

## Difetto
Dopo #293, la Home chiamava ancora il detector NVIDIA senza verificare
un vendor PCI VEN+DEV contraddittorio; un driver Intel/Realtek con provider
"NVIDIA" poteva essere contato nel riquadro sbagliato. Inoltre i tre
riquadri nascondevano gli errori PnP (in particolare il codice 28,
driver **non installato**), nonostante l'inventario Windows fosse riuscito.

## Correzione
- `HomeVendorCoverageSummary.Classify` applica la stessa guardia
  `VendorSourceDiscovery.HasForeignPciVendor(device,"10DE")` usata
  dalla fonte di supporto, installato e matrice Intel/Killer/NVIDIA.
- Le schede mantengono i GUID WUA Type=Driver correlati **solo mediante
  HardwareId o CompatibleId esatti**; Type=Software, URL OEM e nomi
  non aumentano i candidati aggiornabili.
- `DeviceStatus` distingue `ConfigManagerErrorCode==28` come
  `driver mancante (Windows codice 28)` e gli altri codici PnP
  non-zero come `problema Windows`. Se è fallita la query WMI
  opzionale delle versioni ma il codice Windows è zero, NON
  mostra un driver mancante.
- La selezione `Consulta driver locali` si basa sulle categorie
  corrette e resta una ricerca Driver Store read-only su azione utente.
- Test: vendor PCI Intel e Realtek etichettati NVIDIA, GPU NVIDIA
  etichettata Intel, WUA esatti, hint software esclusi, duplicati,
  codici 28/10, WMI non riuscita, scan fallita/parziale,
  export senza PNPDeviceID/HWID/versioni/seriali.

## Sicurezza e QA
Un driver **mancante** è un problema Windows, non un nuovo pacchetto
individuato da WUA o un'indicazione che un download OEM sia sicuro.
`QuickUpdatePolicy`, firmware e installazione non cambiano. Non
sostituisce il test sul notebook Recoil 16 (#91/#136) né la QA
interattiva Windows 11 (#85) e la firma Authenticode (#283).
