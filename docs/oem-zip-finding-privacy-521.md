# Privacy dei risultati confronto OEM ZIP/CAB (#521)

## Difetto
Il confronto PnP/INF ZIP/CAB crea `LocalOemDriverComparison.Finding`
con nome del dispositivo, basename INF e versione installata.
I tre campi attraversavano `TrimUntrusted`, che limitava a 90/50
caratteri e rimuoveva CR/LF, ma **non** impediva a un nome INF
`PRIVATE_ORDER_...`, a un PnP Instance ID usato come nome o a un
messaggio WMI contenente percorsi di essere copiati nella UI e nei
report diagnostici. Il precedente fix #419 riguardava soltanto i
basenames su disco importati dalla **cartella** locale.

## Correzione
- `Finding.DeviceName`: usa `PnpDeviceDisplayName.Create`
  con classe e ID PnP originale, mantenendo etichette commerciali
  come NVIDIA GeForce, Intel Killer e Realtek ma non seriali,
  Instance ID o percorsi.
- `Finding.InfName`: espone solo basename `.inf` ASCII entro 90
  caratteri e privi di marker privati / PnP. Il controllo si applica
  all'intera stringa **prima** della lunghezza massima; quando non è
  sicura si vede l'etichetta statica
  `file INF OEM (nome riservato)`.
- `Finding.InstalledVersion`: mostra solo componenti numeriche
  separate da punti, senza dati WMI testuali arbitrari; altrimenti
  `non verificata`.
- Matching hardware, qualità dell'inventario, versioni INF originali,
  classificazione firmware e priorità dei risultati restano invariati.
  I risultati rimangono read-only e non installabili.

## Verifiche
`OemZipFindingPrivacyBoundaryTests` copre filename segreti brevi e
lunghi con segreto dopo il 90° carattere, nomi dispositivo che
contengono Instance ID/seriali/percorsi, versione WMI malformata,
nomi ordinari NVIDIA/Killer/Realtek e scelta della versione OEM
migliore fra INF multipli.

Il livello **catalogo raw** conserva necessariamente il percorso
interno ZIP per individuare i file: questa modifica riguarda le
etichette dei `Finding` esposte nei risultati di confronto.
Non prova autenticità dei pacchetti, compatibilità SetupAPI o firma CAT.
