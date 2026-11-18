# Transport IPC: errori anonimi e log minimizzati (#550)

## Problema
`NamedPipeServer` inviava `exception.Message` al Desktop nei frame
`error` per errori di validazione e per eccezioni generiche dei provider
WUA/OEM. L'eccezione può includere percorsi locali, nomi di utenti,
endpoint o Hardware ID. Inoltre il livello di trasporto registrava
l'intera eccezione, il percorso dell'eseguibile client, il nome utente
impersonato dalla named pipe e i comandi sconosciuti non verificati.

## Modifica
- I frame `error` sono **stabili e privi di riferimenti locali**:
  richiesta/JSON non valido e guasto del servizio restano distinti.
- Il JSON malformato riceve un frame di errore; non si interrompe
  semplicemente la connessione senza risposta.
- I comandi annotati nei log devono appartenere alla stessa allowlist
  esatta dei sette nomi IPC noti; tutti gli altri diventano
  «comando non riconosciuto».
- I log di trasporto conservano soltanto il comando sicuro, PID e
  tipo/HRESULT dell'eccezione, senza `Message`, stack, user identity o
  executable path. Nessun output originale delle query WUA/OEM è copiato.
- Il PID è diagnostica locale; non autentica da solo il processo:
  **`ClientProcessVerifier` e ACL della pipe restano invariati**.
- Test xUnit su input e messaggi che contengono valori sintetici privati,
  contratto CI `Test-IpcCommandContract.ps1` contro regressioni del server.

## Limiti
Questa misura limita la disclosure **del trasporto named-pipe**. Non
convalida i log interni degli altri provider né sostituisce test E2E
con processi autorizzati/negati realmente installati in Windows 11.
Questi lavori restano #85. Nessun cambiamento ai criteri di installazione.
