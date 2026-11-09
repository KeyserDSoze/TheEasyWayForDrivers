# Diagnostica pass WUA: policy, rete e timestamp (#92)

## Motivo e semantica
Un pass WUA fallito non equivale a una ricerca che abbia restituito zero
driver. `DriverSearchPassAudit` distingue ora `PolicyRestricted` da
`NetworkError` in base **solo** agli HRESULT COM ufficialmente documentati.
I codici sconosciuti rimangono `Failed`. Le ricerche `Partial`
continuano a NON generare offerte installabili, e il risultato
`NotRegistered` indica una fonte mai interrogata, non un errore.

### HRESULT classificati
- `PolicyRestricted`: `0x80240025`, `0x8024002E`, `0x8024002F`.
- `NetworkError`: `0x8024001F`, `0x8024401C`, `0x80244022`,
  `0x80244023`, `0x8024402C`.
- Tutti gli altri errori rimangono `Failed`; non deduciamo la
  disponibilità di driver OEM né assumiamo che il PC sia offline
  solo perché il server/proxy non risponde.

Documentazione Microsoft:
https://learn.microsoft.com/en-us/security-updates/windowsupdateservices/21742981
https://learn.microsoft.com/en-us/windows/win32/wua_sdk/wua-networking-error-codes-

## Tracciabilità e compatibilità
Ogni query tentata porta `DriverSearchPassStatus.AttemptedAtUtc`, UTC
dell'inizio del pass. È un parametro **opzionale** alla fine del record:
payload IPC precedenti senza la proprietà rimangono compatibili;
il servizio Microsoft Update non registrato resta senza timestamp.
Nel report `Copia copertura` viene mostrata l'ora UTC in
`yyyy-MM-dd HH:mm:ss UTC` insieme alla fonte e all'esito.

I callback di logging dei pass driver/software/cache e della verifica
registrazione Microsoft Update registrano HRESULT, non il testo
eccezione che potrebbe includere percorsi, proxy o endpoint privati.
Il report aggregato non copia GUID, Hardware ID, URL del server,
titoli update o dati personali.

## Test e limiti
Test con HRESULT policy/HTTP/DNS/connessione/unknown, ricerca conclusa
con zero risultati, compatibilità JSON IPC legacy, timestamp e
privacy del report. I risultati delle fonti realmente riuscite restano
validi se altri pass falliscono.

**Restano aperti:** validazione reale Windows offline, rete aziendale
e WSUS in #92, test E2E #85, benchmark PC Recoil 16 #136. Non cambia
`QuickUpdatePolicy`, download, installazioni, firmware o OEM cataloghi.
