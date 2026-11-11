# WUA per-source completion in all diagnostic views (#373)

Il servizio WUA committa un pass Type=Driver solo dopo che COM ha
restituito ed enumerato integralmente un risultato concluso. Un record
legacy IPC invece potrebbe contenere un aggiornamento con
`SearchSource = Windows Update online` anche quando tale pass è stato
`Failed` / `Partial`, mentre un pass diverso è `Completed`.
La vecchia diagnostica accettava ogni GUID con nome fonte riconosciuto
se **qualunque** pass aveva esito positivo.

`WindowsDriverCandidateMerge.ConfirmedForReport` verifica che il GUID
sia valido, la fonte sia una delle query Type=Driver dell'app e **quella
specifica fonte** sia nel report con esito `Completed`. Non accetta
indizi Type=Software, titoli da ricerca parziale, cache locale o fonti
sconosciute. Nessuna fonte fallita può contaminare gli ID hardware di
un altro pass per lo stesso GUID.

Home tre vendor, otto categorie, audit WUA→PnP, report copiabile,
inventario OEM per produttore, pannello versioni installate, **elenco
principale degli aggiornamenti** e **notifiche Desktop** riutilizzano
tale filtro. La Home non considera conclusa una ricerca se non si è
completato alcun pass Type=Driver riconosciuto. L'azione di selezione
WUA lavora unicamente sulle righe già convalidate prima di apparire
nell'interfaccia. I report rimangono aggregati e anonimi;
gli stati fallimento/NetworkError/PolicyRestricted/Partial sono esposti
separatamente senza dichiarare il PC già aggiornato.

Questa modifica è **solo per diagnostica e presentazione**: la
selezione WUA ordinaria `QuickUpdatePolicy` e il recheck nel servizio
privilegiato rimangono invariati. Non introduce nuove offerte NVIDIA,
Killer, Realtek o cataloghi OEM e non misura il Recoil 16 reale.
Le issue #91, #92, #136 e #283 conservano i propri gate QA.
