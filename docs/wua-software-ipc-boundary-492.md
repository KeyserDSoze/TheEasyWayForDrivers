# WUA Type=Software: convalida del boundary IPC e fairness (#492)

Gli elementi WUA `Type=Software` sono **solo indizi informativi** e non sono
mai driver installabili. Le API `SelectFair` e `Merge` accettavano un GUID
ben formato anche quando il titolo o la fonte IPC non erano stati verificati
da `TryCreate`. Un record malformato iniziale poteva quindi nascondere un
secondo record attendibile dello stesso GUID, oppure consumare slot della
preview (massimo 32) e della quota di omissioni.

## Correzione

- `SelectFair` e `Merge` riapplicano `TryCreate` a ciascun elemento
  **prima** del dedup GUID e delle quote di fairness; righe nulle, titoli
  non sicuri, fonti spurie, GUID falsi non occupano slot.
- `ConfirmedForReport` ignora elementi IPC null senza eccezione, mantenendo
  l'esigenza di una fonte WUA Type=Software completata e attestata.
- I titoli software respingono anche `SECRET_`, `ORDER_`, `SERIAL_`,
  `FUNC_`, `MI_`; `PROSet/Wireless` e nomi Unicode visibili restano
  compatibili con la grammatica esistente.
- GUID D/B/N/P deduplicati sul valore, first-seen solo tra record validi;
  nessun nuovo GUID, nessuna nuova applicabilità PnP e nessuna installazione.

I test verificano titoli commerciali, dati privati, sorgenti false,
collisioni, fairness 32 slot, omissioni e report anonimi. Il benchmark
Recoil 16 e i cataloghi reali rimangono da verificare (#91/#136/#85);
firma Authenticode ancora separata (#283).
