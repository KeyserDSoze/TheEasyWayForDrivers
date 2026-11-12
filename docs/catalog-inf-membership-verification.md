# Discovery+ — verifica INF appartenente a un CAT firmato (incremento #52)

## Nuovo controllo opzionale

Microsoft documenta esplicitamente il comando `SignTool verify /kp /c CatalogFileName.cat DriverFileName.inf` per **verificare che l'INF specificato sia membro del CAT selezionato e che il file rispetti la policy di firma dei driver Windows**. SignTool fa parte del **Windows SDK**, non è garantito che sia presente su ogni PC.

OmegaDrive continua a offrire il calcolo SHA-256 e `Get-AuthenticodeSignature` del CAT (v0.0.31). Questa versione aggiunge la verifica del *singolo INF* tramite il SignTool già installato. Se non trova SignTool in una directory SDK versionata sotto `Program Files (x86)\Windows Kits\10\bin\<versione>\<architettura>\signtool.exe`, mostra **non verificato**. Non usa PATH, eseguibili della directory corrente, download o installazioni automatiche del SDK.

### Controlli di sicurezza

- L'utente sceglie manualmente un file `.inf` locale. Il CAT è ricavato dalla direttiva `CatalogFile` sicura dell'INF.
- Sono richiesti INF e CAT nella stessa cartella; nessun percorso UNC, dispositivo remoto o file reparse-point/symlink come input.
- Limiti: INF 1 MiB, CAT 32 MiB, output del processo limitato, timeout 40 secondi (più timeout chiamante).
- Esecuzione SignTool senza shell, con argomenti separati via `ProcessStartInfo.ArgumentList` per evitare che il nome del file diventi un comando.
- Solo exit code **0** significa che il comando SignTool ha verificato **questo INF** nel CAT indicato. Qualsiasi exit code diverso da 0 => rifiutato. SDK mancante/errore/timeout => non verificato.
- L'output libero di SignTool non viene mostrato all'utente perché potrebbe contenere percorsi locali o messaggi non attendibili.
- Quando un digest INF atteso è stato inserito ma risulta errato o malformato, il controllo SignTool viene saltato; la procedura non può diventare un esito positivo.

### Limiti (non accettare un pacchetto OEM completo)

La verifica del singolo INF **non dimostra** che ogni SYS, DLL e payload sia incluso nel CAT né che tutte le dipendenze siano presenti o compatibili. Non equivale alla verifica completa del driver e **non abilita installazioni**. Restano aperti: controllo di ogni file del package, policy OEM, architettura/TargetOSVersion su hardware reale, revoche/chain secondo policy, verifiche post-installazione, e QA Windows 11. Vedere issue #46 e #5.

Microsoft: [SignTool — verifica driver Windows](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/signtool), [WINTRUST_CATALOG_INFO](https://learn.microsoft.com/en-us/windows/win32/api/wintrust/ns-wintrust-wintrust_catalog_info).

## Verifica manuale Windows 11

Con Windows SDK già installato, selezionare un INF e il suo CAT ufficiale nella stessa cartella, confrontare l'esito con `SignTool verify /kp /c <CAT> <INF>` eseguito manualmente, poi verificare i casi CAT estraneo, INF modificato, CAT non attendibile, SDK assente, file mancanti e timeout. **Non** installare driver durante questi test. Registrare anomalie in issue #5.

## Audit cartelle OEM: indice PnP rigoroso anche nella selezione dei 12 controlli (#423)

I modelli INF vengono già correlati solo agli ID PnP locali
specifici e sintatticamente verificati. Lo stesso vincolo ora vale
per l'indice \`ordinalsById\` usato nel piano read-only
\`OemFolderCatalogAuditPlan\` per ripartire i **12 slot** delle
verifiche CAT/INF tra dispositivi realmente distinti. Il codice
non usa più \`Trim()\` per convertire ID locali con TAB/CR/LF,
instance path o modelli troppo generici in un'identità hardware:
solo gli **spazi ASCII esterni**, innocui e già ammessi dal
matcher condiviso, vengono rimossi.

Un test con 13 pacchetti e slot contesi verifica che un solo
dispositivo reale con cinque cloni IPC malformati non prevalga
su un INF che corrisponde a tre dispositivi differenti. Un
secondo test preserva la compatibilità con \`CompatibleIds\`
ben formati e semplici spazi ASCII. Non cambiano il numero massimo
di controlli, i limiti INF/CAT, la verifica crittografica vera,
la selezione manuale dell'utente o le regole di installazione.
I CAT sintetici dei test non sono mai considerati firmati.
Le prove con CAT ufficiali e hardware reale rimangono #128/#136.
