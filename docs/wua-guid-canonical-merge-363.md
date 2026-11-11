# Merge GUID WUA: rappresentazioni canoniche e tracciamento (#363)

## Problema

Lo stesso UpdateID Windows Update è un GUID, ma può essere esposto con
trattini in formato D, con parentesi graffe B, senza trattini N o con
parentesi tonde P. Il precedente merge era case-insensitive ma non
**format-insensitive**: due pass Type=Driver potevano mantenere duplicati,
separare Hardware ID di NVIDIA/Killer/Realtek e non condividere il veto
BrowseOnly dello stesso aggiornamento.

## Correzione

In \`WindowsDriverCandidateMerge.Merge\`, prima di interrogare il dizionario,
si normalizza **solo** un identificativo parseable via \`Guid.TryParse\`:
\`Guid.ToString("D")\`. Sia la chiave sia \`DriverUpdateInfo.Id\` assumono
la forma canonica. Il merge preesistente di metadata, priorità sorgenti
WUA, Firmware/BrowseOnly e ID dispositivo resta invariato.

Identificativi non-GUID utilizzati da test sintetici non vengono convertiti
in un altro valore e non diventano installabili per questa modifica.
Nessun titolo, seriale o Device Instance ID viene utilizzato per dedurre
l'identità dell'update.

## Verifica e limiti

I test verificano quattro forme B/N/D/P, entrambe le sequenze di arrivo
BrowseOnly/Recommended e OEM non verificato/WUA, più ID per lo stesso GUID,
differenti GUID reali, input non-GUID legacy e predicati di installazione
che devono restare prudenziali.

Non cambiano query WUA, download, installazione, OEM, regole di firma,
budget IPC o limite dei driver. Il benchmark Recoil 16 di #91/#136 e
il collaudo hardware reale restano necessari.
