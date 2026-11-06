# OmegaDrive — INF OEM con più di 500 identificativi: matching mirato completo (#166)

## Problema reale individuato

L'analisi `InfModelEvidence.ReadDeclaredIds` mantiene un'anteprima dei primi **500 ID** trovati nelle sezioni Models del file INF. Questa limitazione è utile per la memoria e per le diagnostiche generali, ma il vecchio `LocalOemDriverComparison.Compare` usava l'anteprima anche per **decidere se un file potesse essere rilevante** per il PC. Se una GPU NVIDIA, scheda Wi-Fi Intel/Killer o altro dispositivo era elencato al 501° modello o più tardi, il sistema non apriva quel file per effettuare il matching con l'hardware.

Anche `InfPlatformPreflight.Assess` si fermava ai primi 500 identificativi: un semplice fix dell'elenco ZIP avrebbe comunque prodotto un falso `NoHardwareMatch` nel preflight.

La documentazione Microsoft conferma che un INF contiene una sezione Models per i dispositivi dichiarati dal produttore e può elencare numerosi hardware ID/compatible ID:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/install/inf-models-section
- https://learn.microsoft.com/en-us/windows-hardware/drivers/install/inf-manufacturer-section

## Implementazione

- **Anteprima invariata:** `ReadDeclaredIds` mantiene il massimo di 500 ID, per proteggere le visualizzazioni generaliste e mantenere retrocompatibilità.
- **Nuovo matching dedicato:** `InfModelEvidence.ReadMatchingIds(inf, dispositivi)` legge l'intero file INF **già limitato a 1 MiB** ma materializza soltanto gli identificativi PnP presenti nell'inventario Windows, confrontandoli per **uguaglianza esatta case-insensitive**. Un dispositivo non viene escluso per la posizione della sua riga nel file.
- **Limiti fail-closed:** massimo 2048 dispositivi e 4096 corrispondenze per INF; se il numero dei risultati mirati supera il limite, genera un errore esplicito anziché produrre un esito falso o parziale. I controlli esistenti su modelli, enumeratori, dimensioni degli archivi ZIP, decompressione e nomi di file rimangono attivi.
- **Confronto ZIP e cartelle:** `LocalOemDriverComparison` esamina tutti gli INF **leggibili** validati nell'archivio, anziché saltare quelli che non hanno il dispositivo nei primi 500 ID. Il percorso cartella estratta condivide lo stesso motore e riceve il fix.
- **Preflight coerente:** `InfPlatformPreflight.Assess` utilizza il matching completo mirato quando viene selezionato un dispositivo, mantenendo separati Hardware ID esatto e Compatible ID, e applicando gli stessi controlli su architettura e TargetOSVersion.
- **Avviso diagnostico:** `OemZipInfEvidence` segnala esplicitamente quando un'anteprima contiene almeno 500 ID e non va interpretata come indice completo; il report comparativo conferma che il confronto mirato ha letto l'intero INF.

## Risultato e limiti

Questo intervento **può recuperare corrispondenze OEM locali reali** altrimenti escluse, in particolare per INF con molti modelli hardware. Non introduce una sorgente di aggiornamenti online, non installa pacchetti, non prova firme CAT, certificazione WHQL, ranking SetupAPI o applicabilità finale al notebook.

`IsInstallable=false`, `IsVerifiedCompatible=false` e la policy `Aggiorna tutto` restano invariati. Le versioni maggiori sono **indizi numerici**, non suggerimenti automatici di installazione.

## Test

- [x] Un INF con **620 modelli Intel estranei** e poi GPU NVIDIA o Wi‑Fi Killer esatti: l'anteprima generale continua a mostrare 500 righe, mentre il nuovo matching trova l'ID alla fine.
- [x] ZIP OEM e cartella locale con più INF, un risultato unico per dispositivo, versioni numeriche e report privi di Device Instance ID.
- [x] `InfPlatformPreflight` sullo stesso file: architettura AMD64 coerente, architettura ARM64 rifiutata anche quando il target appare oltre il limite.
- [x] Compatible ID debole non classificato come nuovo driver sufficientemente attendibile, vendor/DEV simili ma diversi non vengono associati.
- [x] ID INF malformato escluso, massimo 2048 dispositivi e dimensione INF 1 MiB.
- [ ] CI Windows .NET 10, xUnit Core/Service, IPC/WPF.
- [ ] QA Recoil 16 (#136): confrontare pacchetti OEM autentici con file INF grandi; contare match aggiuntivi senza seriali/Hardware ID nei log.
- [ ] Verifica firma CAT, file payload e ranking SetupAPI (#130).

Riferimenti: #91, #100, #120, #126, #130, #136, #166.
