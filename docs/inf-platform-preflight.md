# Discovery+ — verifica preliminare INF per piattaforma (v0.0.29)

Questa fase è un **preflight non vincolante**, in sola lettura, per un file `.inf` scelto manualmente. Non installa, importa né esegue pacchetti. **Nessun risultato equivale a compatibilità Windows confermata**, né a una firma digitale verificata.

## Verifiche introdotte

- Legge la direttiva `Signature` in `[Version]` come controllo **di formato INF**, non come firma crittografica.
- Usa solo i modelli e le decorazioni TargetOSVersion effettivamente dichiarati in `[Manufacturer]`.
- Distingue architetture `NTamd64`, `NTarm64`, `NTx86` e, quando sono presenti decorazioni numeriche interpretabili, controlla Major.Minor Windows e build minima. Su Windows 11 il numero di versione NT resta 10.0; le build rilevate, ad esempio 22000, 22631 e 26100, sono usate soltanto per il controllo preliminare.
- Accetta Hardware ID esatto o Compatible ID e segnala quando la corrispondenza è genericamente compatibile.
- Se una decorazione include ProductType, SuiteMask, uno schema non gestito o una sezione non decorata, restituisce **verifica manuale necessaria** anziché attribuire compatibilità.
- Legge `DriverVer` se presente.
- Cerca la direttiva `CatalogFile` o la variante architetturale pertinente; accetta esclusivamente il nome di file locale `.cat` senza separatori o percorsi arbitrari. Verifica **solo l'esistenza nella cartella selezionata**: la presenza del file non prova la correttezza crittografica o la copertura dell'INF.

## Cosa NON controlla (e perché)

- Firma Authenticode del `.cat`, appartenenza degli hash dell'INF e dei binari al catalogo, catena certificati, revoche e policy kernel-mode signing.
- Scelta esatta effettuata da Windows SetupAPI tra più sezioni concorrenti e tutte le decorazioni dell'INF (ProductType, SuiteMask, TargetOSVersion altamente specifiche).
- `Include` / `Needs`, `CopyFiles`, dipendenze, TargetOSVersion con casi speciali, modello OEM/BIOS, identità del pacchetto e presenza dei payload.
- Qualsiasi download o installazione. Per una decisione effettiva è necessario usare verifiche Windows native attendibili e test su PC reali.

## Precauzioni

Il limite di lettura è 1 MiB per INF; non vengono aperti percorsi di rete indicati dentro i metadati dell'INF, né eseguiti script o installer. I risultati rimangono informativi nell'app e non modificano i privilegi del servizio Windows.

Riferimenti Microsoft: [INF Manufacturer](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/inf-manufacturer-section), [INF Models](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/inf-models-section), [INF Version](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/inf-version-section).

Issue #44; QA Windows reale in #5.
