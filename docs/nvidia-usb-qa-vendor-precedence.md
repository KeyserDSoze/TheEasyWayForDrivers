# OmegaDrive — NVIDIA USB e precedenza OEM per diagnosi 8 categorie (#349)

## Bug

Il parser pubblico di modelli PnP riconosce già il Vendor ID USB
**0955** come NVIDIA e la selezione bounded degli Hardware ID WUA
lo conserva tra le famiglie NVIDIA. Il rilevatore
`NvidiaHardwareDetector` però riconosceva soltanto GPU PCI, audio
HDAUDIO e componenti ACPI/SWC. Un dispositivo USB NVIDIA con
`USB\VID_0955&PID_7019`, nome e provider generici Microsoft,
non compariva nei collegamenti al supporto del produttore.

Contemporaneamente, il confronto privacy-safe delle otto categorie
Intel/Killer/NVIDIA (#91) usava vecchi nomi/provider per la
classificazione anche quando un **modello PnP validato**
dimostrava che il dispositivo appartiene a Realtek, Qualcomm
o un altro produttore. Un codec Realtek chiamato ancora
«NVIDIA Audio» poteva gonfiare la riga NVIDIA HD Audio.

## Correzione

- `NvidiaHardwareDetector`: aggiunto il riconoscimento di ID USB
  `VID_0955&PID_XXXX` **completi e validati**, riusando
  `MicrosoftUpdateCatalogSearch.TryGetPublicModelKey`.
  Sono esclusi vendor-only, PID non valido, suffix arbitrari,
  instance/seriali e altri bus.
- `VendorSourceDiscovery` e `DriverSourceAdvisor` ottengono
  automaticamente il collegamento ufficiale NVIDIA già definito.
  Nessuna richiesta di rete o sorgente aggiuntiva viene interrogata.
- `ReferenceDriverCoverage.ClassifyDevice` verifica ora
  `PnpKnownVendorIdentity.FromDriver` per le categorie
  NVIDIA, Intel e Killer, oltre al veto PCI già esistente.
  Un'identità PnP riconosciuta di un **altro vendor** o
  `Conflicting` prevale su nome/provider stale.
  I dispositivi con modello ignoto e gli ID NVIDIA veri
  mantengono il comportamento precedente.

Un **generico dispositivo USB NVIDIA** può ricevere un handoff
al supporto ufficiale, ma non viene trasformato in una
**NVIDIA RTX GPU** o in un Platform Controller all'interno delle
otto categorie per il solo VID del produttore. I conteggi
degli aggiornamenti restano limitati ai veri GUID WUA Type=Driver
con associati Hardware ID esatti e pass riusciti.

## Limitazioni

Nessun driver extra è stato trovato o installato, non sono
stati inventati GUID, versioni OEM o nuove origini catalogo.
Un handoff non è un driver approvato per `Aggiorna tutto`.
La verifica sul PCSpecialist Recoil 16 (#91, #136)
e la compatibilità Nvidia/Intel/Killer restano da effettuare
su Windows reale. Tutti i test automatizzati sono sintetici,
privacy-safe e non condividono dati PnP privati.
