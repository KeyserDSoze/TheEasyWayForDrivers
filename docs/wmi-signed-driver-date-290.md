# WMI signed driver: DriverDate malformata non distrugge inventario (#290)

## Difetto e correzione
`Win32_PnPSignedDriver.DriverDate` e' **facoltativa**. Il percorso
precedente catturava solo `ArgumentOutOfRangeException` dal decoder
DMTF WMI. Un'altra stringa malformata poteva alzare `FormatException`
e abortire **l'intera ricerca PnP**, anche con ID NVIDIA/Intel/Killer
validi. `WmiSignedDriverFieldReader` valuta ora la singola data con
grammatica DMTF conservativa di 25 caratteri e restituisce `null`
se wildcard, incompleta, fuori range o non decodificabile.

`WmiDriverInventory` preserva `DriverVersion`, `InfName`,
`IsSigned` e `DriverProviderName` della stessa riga anche quando la
data e' inutilizzabile. Il parser non legge hardware, non inventa
versioni e non modifica la fonte primaria `Win32_PnPEntity` né
l'arricchimento SetupAPI.

Gli errori **strutturali** della query firmata continuano a propagare
verso `WmiOptionalMetadataQuery.Read` che scarta lo snapshot
parziale. Cancellazione e privacy invariati: nessun raw DMTF,
percorso o Device Instance ID nei log/UI.

## Test
Date DMTF valide 2026, null e wildcard, formati incompatibili,
date impossibili, stringhe >1 MB, tre righe NVIDIA/Killer/Realtek
con date miste: le versioni e i provider restano disponibili.
E' una correzione **di resilienza dell'inventario**, non incrementa
le offerte WUA `Type=Driver`, non scarica driver, non valida firma
o installabilita'. QA hardware #136/#85 e gap #91 restano aperti.
