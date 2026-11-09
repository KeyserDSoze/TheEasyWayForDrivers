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
