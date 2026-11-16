# Discovery+: confine privacy e provenienza WUA (#536)

## Difetto
`DiscoveryResultAdapter.FromWindowsUpdate` era pubblico e copiava in
`DiscoveryResult` dati grezzi di `DriverUpdateInfo`, inclusi titolo,
versione, Hardware ID principale e lista aggiuntiva, senza imporre
la policy di provenienza WUA usata nel resto del programma. In
particolare un identificatore PnP istanza o un percorso privato poteva
comparire nel testo `ID dichiarato:` ed entrare nel filtro per-device
della vista Discovery+.

## Correzione
- `FromWindowsUpdate` accetta solo GUID e nomi fonte WUA Type=Driver
  riconosciuti, con variante avanzata solo se marcata come tale.
- Il titolo non sicuro viene **redatto**, senza perdere la riga WUA
  valida. Il numero di versione è limitato a una singola versione
  compatta priva di path/email/marcatori privati.
- Hardware/Compatible ID con più segmenti, path locali, controlli o
  grammatica di modello non correlabile vengono esclusi sia dai testi
  sia dal matching per-device Discovery+. Le entry supplementari sono
  deduplicate e limitate alla quota IPC. Se un campo è stato rifiutato
  o troncato, `WuaIdsIncomplete=true` evita false conclusioni
  di assenza di correlazioni.
- `WuaReadOnlyTitlePolicy` estende `CUSTOMER_` alle anteprime
  Type=Driver parziali/cache.
- Test sintetici su NVIDIA GPU, Killer Wi-Fi, Realtek audio, token
  privati, Unicode invisibile, GUID/fonti, over-budget e matching.

## Limiti
L'adapter è **solo presentazione**. Il controllo della fonte non
attesta da solo un pass COM Completed: il chiamante Desktop deve
continuare a usare i risultati di fonti completate e verificate.
Nessuna autorizzazione di installazione WUA/OEM deriva da questa
conversione. Restano QA hardware #91/#136 e #85.
