# OmegaDrive — PnP Identity fallback dal driver WMI locale (#158)

## Problema riscontrato

Per confrontare un driver Windows Update (WUA) con un dispositivo installato, OmegaDrive deve disporre di un `HardwareID` o `CompatibleID` corrispondente. L'inventario usa `Win32_PnPEntity`, che può non fornire uno o entrambi gli array. Il servizio interrogava già `Win32_PnPSignedDriver` per versione, INF e firma, ma non ne leggeva i campi documentati `HardWareID` e `CompatID`.

Microsoft descrive queste proprietà:
- [Win32_PnPSignedDriver](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/whqlprov/win32-pnpsigneddriver)
- [Win32_PnPEntity](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-pnpentity)

## Modifica tecnica

Il servizio include `HardWareID` e `CompatID` nella SELECT di `Win32_PnPSignedDriver`, senza query aggiuntive o modifiche Windows.

Per ogni voce `Win32_PnPEntity`:
1. Cerca un record firmato soltanto mediante **DeviceID esatto**, case-insensitive, in una tabella locale; mai per nome/brand/DeviceClass.
2. Conserva integralmente gli array `HardwareID[]` e `CompatibleID[]` quando Windows li ha restituiti.
3. Se **un array specifico è vuoto**, prova a recuperare il corrispondente valore scalare del record driver. L'Hardware ID firmato non diventa Compatible ID e viceversa.
4. Valida rigorosamente l'identificativo recuperato: ID bus qualificato con barra inversa, ASCII controllato, massimo 512 caratteri, niente stringhe multiple, command line, path, caratteri di controllo, NUL, slash o quote. Scarta valori ambiguamente formattati.
5. Se l'altro array contiene già lo stesso ID, evita duplicati. Una corrispondenza WUA rimane basata su uguaglianza **esatta** degli identificativi, non su prefissi di vendor o titolo.
6. Se `Win32_PnPSignedDriver` fallisce, mantiene il comportamento di fallback indipendente e fail-closed della PR #157: nessun metadato firmato parziale e nessun ID inventato.

`DriverInfo.PnpIdsRecoveredFromSignedDriver` è un nuovo campo opzionale (default `false`) per compatibilità con il vecchio IPC JSON. La Home mostra un contatore discreto in `HomeSourceSummary` soltanto se il recupero ha interessato qualche dispositivo; il report copiabile riporta **solo un conteggio** dei dispositivi arricchiti, senza Hardware ID, Device Instance ID, seriali, versioni o nomi locali.

## Cosa significa (e cosa NON significa)

- **Può aumentare** le associazioni tra dispositivi già visibili e aggiornamenti **già restituiti** da WUA, soprattutto con nomi Microsoft generici o liste ID PnP incomplete.
- Non aumenta direttamente i pacchetti che WUA offre; non interroga cataloghi NVIDIA/Intel/Killer aggiuntivi.
- Non significa che un driver è più recente, compatibile con OEM/CHID o pronto per installazione. `Aggiorna tutto`, `QuickUpdatePolicy`, download, ranking e installazioni rimangono invariati.

## Validazione

- [x] Matching NVIDIA GPU con Hardware ID recuperato e nome generico; rilevamento fonte ufficiale senza inventare un update.
- [x] Compatible-only fallback con forza WUA minore, Killer Wi-Fi e due array indipendenti.
- [x] Liste PnP esistenti sempre prioritarie, nessuna aggiunta da record firmati quando già complete.
- [x] ID duplicati, prefissi simili, input multipli, controlli, caratteri bidi, path, quote e limite 512.
- [x] Report privacy-safe e IPC JSON precedente/nuovo.
- [ ] CI Windows: build .NET 10, xUnit Core/Service, contratti IPC/WPF.
- [ ] QA reale su PCSpecialist Recoil 16: confrontare quanti device hanno ID PnP originari e quanti vengono recuperati (senza pubblicare gli ID), NVIDIA RTX 4060, NVIDIA HD Audio/Platform e Intel Killer; verificare esatte versioni e matching WUA in locale.
- [ ] Verifica di compatibilità OEM/driver ranking/firma per singoli pacchetti: issue #91/#130, non inclusa.

Riferimenti: #91, #100, #136, #156, #158.
