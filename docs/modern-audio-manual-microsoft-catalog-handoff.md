# OmegaDrive — ricerca manuale Microsoft Update Catalog per audio SNDW / ACP (#357)

## Perché serve

Con #353, #354 e #356, OmegaDrive recupera e correla modelli
audio SoundWire SNDW e AMD ACP quando sono specifici e provengono
dalla stessa istanza PnP, anche in risposte Windows Update con
oltre 1.024 Hardware ID.

Il pulsante **ricerca manuale nel Microsoft Update Catalog** usava
invece solo i formati PCI, USB, HID, HD Audio, Intel Audio, SWC, ACPI
NVIDIA e SoundWire SDCA. Un codec con ID pubblici come
\`SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00\` o
\`ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2\` non aveva
un link, anche quando l'utente desiderava verificare manualmente
altre fonti oltre alle offerte WUA ricevute.

## Cosa cambia

\`MicrosoftUpdateCatalogSearch\` usa la stessa validazione
\`ModernAudioPnpMatchPolicy.IsSpecificHardwareId\` già impiegata
dal matcher WUA/INF:

- SNDW richiede \`CLASS\` + \`MAN\` + \`PART\`, con eventualmente
  una coppia \`VER\` + \`UID\` valida. Il link utilizza soltanto
  il **modello pubblico** \`SNDW\CLASS_XX&MAN_YYYY&PART_ZZZZ\`:
  \`VER\` e \`UID\` non sono trasmessi.
- ACP richiede \`DEVTYPE\` + \`DEVREV\` + \`VEN\` + \`DEV\`,
  tutti valori esadecimali a lunghezza fissa. Il link usa la
  rappresentazione pubblica completa, necessaria per distinguere
  modelli ACP, senza DeviceID, path o seriali.
- Gli Hardware ID hanno precedenza sui Compatible ID. Il caso
  solo-compatibile è indicato esplicitamente come evidenza più
  debole.
- Vengono sempre scartati identificativi generici \`CLASS\`,
  \`MAN\`, \`DEVTYPE\` e \`DEVREV\` incompleti, token malevoli,
  suffissi inventati e identificativi di istanza privati.

L'URL è generato esclusivamente per il dominio HTTPS fisso
\`www.catalog.update.microsoft.com/Search.aspx?q=...\` ed è
aperto solo da un'azione esplicita dell'utente. **OmegaDrive
non interroga il Catalog automaticamente**, non legge i risultati
della pagina, non sa se Microsoft vi abbia indicizzato questo
modello e non trasforma il link in un driver disponibile.

## Limitazioni / sicurezza

Un link di ricerca NON dimostra che il driver sia applicabile,
più recente, firmato CAT, adatto al produttore OEM o installabile.
\`Target.IsInstallable\` e \`Target.CatalogWasQueried\` restano false.
Nessuna modifica a WUA, \`QuickUpdatePolicy\`, download,
\`Update All\`, pass Microsoft Update, oppure inventario locale.

Test sintetici verificano la normalizzazione, URL, privacy di
seriali/UID/revisioni locali, priorità Hardware/Compatible,
rifiuto di input incompleti e indipendenza tra link manuale,
WUA e matching INF. Il confronto reale PCSpecialist Recoil 16,
compresi i driver NVIDIA RTX 4060, Intel/Killer e audio,
rimane aperto (#91, #136, #85).
