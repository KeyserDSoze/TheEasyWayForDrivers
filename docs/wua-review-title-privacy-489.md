# Titoli WUA read-only: privacy di cache e risultati parziali (#489)

Le anteprime di ricerche WUA Type=Driver **parziali** e della cache **offline**
non rappresentano offerte installabili, ma possono comunque mostrare titoli
ritornati da COM o da snapshot IPC legacy.

## Correzione

Una sola policy `WuaReadOnlyTitlePolicy` limita la visualizzazione di entrambe
le famiglie. Rifiuta titoli oltre 200 caratteri, email, percorsi con slash o
backslash, ":" (come nei criteri precedenti), marcatori grezzi PnP e privati,
controlli, surrogati isolati, carattere U+FFFD, spazi Unicode non ordinari e
marcatori Unicode invisibili/di direzione. Nomi commerciali normali, grafie
accentate visibili, punteggiatura legittima e GUID canonici restano invariati.

I due `TryCreate` validano i titoli al caricamento. Le funzioni `Merge`,
`MergeStaged`, `SelectFair` e `ConfirmedForReport` riapplicano il medesimo
filtro quando ricevono dati legacy o costruiti altrove.

## Garanzie e limiti

- Nessuna promozione dei suggerimenti a `DriverUpdateInfo` o ad "Aggiorna tutto".
- Nessun download/installa driver, scansione OEM, registrazione di servizi o
  cambiamento al matching PnP/INF/SetupAPI.
- Test sintetici per titoli non sicuri, nomi innocui e confini report/IPC.
- Rimane necessario il QA Windows reale #85/#136; il divario Driver Easy
  8-vs-2 in #91 non è risolto da una policy di visualizzazione.
