# OmegaDrive — INF moderni Intel SST e componenti software DCH (#164)

## Bug individuato

Il parser read-only `InfModelEvidence.ReadDeclaredIds` già riconosceva un insieme chiuso di enumeratori PnP (`PCI`, `USB`, `HDAUDIO`, `SWD`, ecc.), ma rifiutava due bus documentati/utilizzati da Windows:

- `INTELAUDIO\\FUNC_01&VEN_10EC...`: codec Realtek dietro Intel Smart Sound Technology, potenzialmente presenti in pacchetti audio OEM.
- `SWC\\...`: dispositivo software component creato tramite `AddComponent` in un INF DCH e associato a un pacchetto `Class=SoftwareComponent`. In questo percorso può apparire un identificativo hardware del componente, per esempio `SWC\\VEN_10DE&DEV_...`, senza il nome completo del produttore nella stringa visualizzata.

La mancanza nella **allowlist** generava falsi `0 ID nei file INF` o `0 dispositivi correlati` anche quando un pacchetto locale conteneva una sezione Models corrispondente. Nell'inventario PnP alcuni codec `INTELAUDIO` erano già riconosciuti, ma il confronto ZIP/cartella non era coerente.

## Intervento

1. Ampliati **solo** i bus consentiti dal parser INF: `INTELAUDIO` e `SWC`. Non cambia il formato o la semantica della comparazione: identità PnP **esatta** e assenza di path, nomi arbitrari, spazi o separatori non ammessi.
2. `NvidiaHardwareDetector` riconosce ora `SWC\\VEN_10DE&...` da Hardware ID e Compatible ID anche quando Windows mostra un nome/provider generico. Il prefisso deve essere completo e delimitato: `SWC\\VEN_10DE0...` e `SWC\\VEN_10DE` da soli non sono prova di NVIDIA.
3. `HardwareIdentityParser` descrive `SWC` come **componente software PnP (DCH)** senza attribuire un produttore o promettere disponibilità di aggiornamenti.
4. Il percorso già esistente `OemZipInfEvidence → LocalOemDriverComparison → InfPlatformPreflight`, condiviso dal confronto cartella OEM e dall'elaborazione INF locali, può adesso identificare entrambi i tipi. Rimangono invariati limiti 1 MiB per INF, 96 INF, 500 identificativi per parser, controllo architettura/OS e `IsInstallable=false`.
5. I dispositivi SWC NVIDIA con nomi Windows generici possono comparire nelle fonti ufficiali **di sola consultazione** e nella diagnostica delle versioni, non nel flusso di installazione WUA salvo candidato realmente ricevuto da Windows.

## Test sintetici

- INF `INTELAUDIO` Realtek e `SWC` SoftwareComponent DCH dentro uno ZIP OEM, corrispondenza PnP e preflight architettura x64/Windows coerente.
- Cartella OEM locale con gli stessi due INF, riutilizzando il medesimo parser senza eseguire setup.exe.
- Parser con maiuscole/minuscole, `SWC\\VID...` Microsoft, path traversal, bus sconosciuti, identificativi malformati.
- NVIDIA SWC con PnP generico riconosciuto, falso vendor `VEN_10DE0` respinto, `VEN_10DE` senza delimitatore respinto.
- `Compatible ID` rimane evidenza più debole: non diventa prova che il pacchetto è più recente/applicabile.
- Nessuna conferma di firma CAT/WHQL o compatibilità OEM da un singolo match; ogni risultato ZIP/cartella rimane non installabile.

## Documentazione ufficiale

- Microsoft [INF AddComponent Directive](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/inf-addcomponent-directive) — `SWC\\` generato da Windows per i componenti software.
- Microsoft [Using a Component INF File](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/using-a-component-inf-file) — INF SoftwareComponent DCH autonomi.
- Microsoft Update Catalog [esempio risultati Realtek INTELAUDIO](https://www.catalog.update.microsoft.com/Search.aspx?q=INTELAUDIO%5CFUNC_01%26VEN_10EC%26DEV_0236) — dimostra che questa famiglia di identificatori è utilizzata nel catalogo; **non** indica che le voci siano applicabili al PC dell'utente.

## Limiti e QA aperti

- [ ] CI Windows .NET 10: compilazione, test Core + Service, WPF/IPC, pubblicazione nuova release.
- [ ] QA su PCSpecialist Recoil 16 con *pacchetti ufficiali ZIP/CAB/cartella già ottenuti dall'utente*: misurare differenza tra INF analizzati prima e dopo, verificare GPU/HD Audio/Platform, componenti DCH e Realtek.
- [ ] Validazione firme CAT, appartenenza INF/SYS/DLL, ranking SetupAPI reale per pacchetti non staged prima di considerare installazione (issue #130).
- [ ] Il divario Driver Easy «8 vs 2» rimane aperto (#91): questa modifica migliora il numero di **corrispondenze INF in pacchetti locali**, non il numero di offerte Windows Update.

Rif. #91, #99, #100, #126, #130, #136, #164.
