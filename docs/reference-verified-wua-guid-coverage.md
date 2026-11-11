# OmegaDrive — copertura otto categorie: GUID WUA verificati (#345)

## Correzione
Nel report diagnostico a otto categorie Intel/Killer/NVIDIA, i
`DriverUpdateInfo` provenienti da un IPC legacy/malformato potevano
essere contati anche con ID non-GUID o `SearchSource` sconosciuta.
Le card Home e l'audit WUA-PnP invece normalizzavano e deduplicavano
già gli ID WUA.

Ora il confronto per le otto categorie conteggia soltanto record che:
- appartengono a un report con almeno **un pass Type=Driver Completed**;
- contengono un GUID valido e un'origine WUA riconosciuta dal piano,
  inclusi pass ` · avanzato` coerenti col flag;
- vengono uniti **per GUID** con il merge bounded e il veto di
  provenienza già condivisi con la UI Home.

Se due pass WUA dichiarano lo stesso GUID ma ID PnP differenti,
la singola offerta può risultare correlata a due componenti del PC
(es. GPU e audio) senza diventare due offerte installabili.
Un update con Hardware ID solo per modello SUBSYS/rev rimane un
**indizio di revisione**, non un match esatto. Le stringhe
`Type=Software` continuano ad apparire soltanto nella colonna
separata, mai fra i driver installabili.

La correzione non aggiunge fonti OEM, non interroga Microsoft
Update al posto di Windows e non modifica il gate Update All o la
re-verifica WUA privilegiata. I test sintetici non sostituiscono
la misura reale delle 8 righe Driver Easy sul Recoil 16 (#91/#136).
