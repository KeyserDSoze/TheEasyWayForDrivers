# Discovery+ 2026 — INF, Lenovo e indicatori

## Analisi INF locale (#40)
La scheda Discovery+ offre un pulsante **Analizza INF locale**. Il file viene scelto manualmente, letto in sola lettura e limitato a 1 MiB. Il parser legge gli Hardware ID PCI/USB/ACPI nelle sezioni `Models` dichiarate da `[Manufacturer]`, ignorando le sezioni non referenziate. Mostra corrispondenze Hardware ID esatte, Compatible ID, o nessuna. **Non carica, importa, esegue o installa l'INF**. Un ID presente non prova che il file sia firmato, sia per l'architettura corretta, sia per il Windows installato, o che il package sia completo.

Limitazione deliberata: sintassi INF complessa (include/needs, decorazioni TargetOSVersion, strings localizzate, source disks, sezioni incrociate, cat/security) non viene interpretata. Il risultato è soltanto un indizio. Non utilizzare il parser per dichiarare compatibilità effettiva o automatizzare installazioni.

## Normalizzazione Lenovo (#41)
La release del modulo Lenovo.Client.Scripting espone pubblicamente i campi ID, Name, Category, Version, PackageType, ReleaseDate. OmegaDrive legge solo una proiezione esplicita di tali proprietà, usa `PackageType=2` e rifiuta i tipi BIOS/firmware e le risposte prive di ID/nome. Risultati mostrati nella scheda OEM e in Discovery+ come **pacchetti di catalogo non installabili**. Non c'è match Hardware ID per dispositivo né invocazione di download/installer. Richiede modulo Lenovo ufficiale già installato e conferma utente. Sorgente: https://www.powershellgallery.com/packages/Lenovo.Client.Scripting/2.4.1/Content/Public%5CFind-LnvUpdate.ps1.

## Indicatori Discovery+ (#42)
La colonna Stato distingue il candidato WUA da *solo catalogo, non installabile*. Selezionando un risultato WUA e un dispositivo installato, la vista spiega qualità Hardware ID e relazione della versione numerica in modo conservativo. I dati OEM non vengono considerati confrontabili senza metadati INF/OS/firma verificati.

### QA richiesta
CI Windows e unit test non equivalgono a prove su PC reali. Issue #5 raccoglie verifica con INF realmente installati, modelli Lenovo, proprietà del modulo e risultati WUA. Nessun codice modifica la policy di installazione WUA in questa fase.
