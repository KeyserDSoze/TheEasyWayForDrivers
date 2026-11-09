# OmegaDrive — WUA BrowseOnly escluso dall'installazione rapida (#330)

Per un candidato WUA Type=Driver, `BrowseOnly=true` significa
offerta facoltativa e non raccomandata. Il provider registrava gia
`IsOptional` ma non lo filtrava nella selezione rapida; il servizio
privilegiato non lo ricontrollava prima di `AcceptEula` e download.

`QuickUpdatePolicy` ora esclude gli opzionali da `Aggiorna tutto`.
Il servizio legge `Title`, `DriverClass`, `IsHidden`, `BrowseOnly`
da COM con gli stessi limiti e controlli Unicode della discovery.
Sono necessari titolo e classe validi, flag esplicitamente false e
assenza di firmware/BIOS/UEFI. Gli errori del singolo getter COM
producono valori sconosciuti e fail-closed, non "false".
Le flag devono essere valori booleani reali, non stringhe "False".

Il candidato opzionale rimane in revisione; solo i GUID confermati
WUA ordinari e ri-verificati possono essere installati. Un batch
incompleto non avvia alcun download. Non cambiano le fonti OEM o la
verifica di firma CAT; test QA sul Recoil 16 restano #136 e #91.

## v0.0.191 — optionalità WUA conservata per GUID fra più fonti (#331)

La verifica della #330 può essere aggirata in discovery se due pass WUA
**completati e attendibili** restituiscono lo stesso GUID con valori
`BrowseOnly` discordanti: il merge preferiva il metadato ordinario e
perdeva `IsOptional=true`. Ora l'evidenza facoltativa proveniente da
una qualunque fonte WUA verificata rimane associata al GUID, anche se
il titolo e il primary ID più completi provengono dalla fonte ordinaria.
La voce rimane nel filtro **Facoltativi/revisione** ed è esclusa da
`Aggiorna tutto`. Gli ID PnP confermati da entrambi i pass restano
utilizzabili per il confronto hardware, senza creare un secondo driver.
Una fonte web/OEM sconosciuta non può contaminare il GUID WUA con
`IsOptional` e non può prestare HardwareID al driver ordinario.
Il recheck privilegiato del servizio continua a richiedere la prova
`BrowseOnly=false` prima di AcceptEula e download. Nessun aumento
fittizio della copertura rispetto a Driver Easy; QA fisica #136 aperta.
