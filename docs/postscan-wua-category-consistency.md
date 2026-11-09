# OmegaDrive — contatori Consigliati/Facoltativi/Avanzati (#332)

## Problema e comportamento corretto
Prima la Home contava come "Consigliati" tutti i risultati che
non erano hidden, advanced o BrowseOnly, anche se `QuickUpdatePolicy`
li escludeva da Aggiorna tutto per GUID malformato, fonte sconosciuta
o firmware/BIOS/UEFI. Il numero della Home non corrispondeva quindi
al filtro "Consigliati" e agli aggiornamenti davvero eleggibili.

Ora `DriverSearchSummary` applica **la stessa funzione**
`QuickUpdatePolicy.IsEligible` per i Consigliati e usa il predicato
condiviso `CompactResultFilter.IsOptionalReview` per i facoltativi
WUA attendibili, non hidden e non advanced. Tutto il resto rimane
in "Avanzati / nascosti". Le tre categorie sono mutuamente esclusive
e sommano a tutti i risultati confermati. Il filtro "Aggiornabili"
mostra i medesimi candidati dei Consigliati, quello "Facoltativi"
mostra soltanto review di veri WUA BrowseOnly, quello "Avanzati /
nascosti" tutti gli altri. Il filtro "Tutti" resta completo.

Le voci firmware, gli ID GUID corrotti, le fonti OEM/manuali senza
provenienza WUA e i candidati advanced/hidden non vengono più
presentati erroneamente come installabili. Non cambia alcuna
policy di WUA/download/installazione: è un allineamento UI e report,
non scoperta di nuovi driver. I test sintetici coprono NVIDIA,
Intel/Killer BrowseOnly, Realtek nascosto, firmware, GUID non valido
e provenienza non verificata. Confronto reale Driver Easy 8-vs-2
e QA Recoil 16 restano aperti (#91/#136).
