# OmegaDrive — copertura equilibrata del riepilogo versioni OEM (#160)

## Bug in Home

Dopo `Analizza il PC`, `HomeInstalledDriverEvidenceText` mostra le versioni locali e le corrispondenze per ID con gli aggiornamenti Windows Update. Prima della correzione `VendorDriverEvidenceFinder.Find` ordinava tutte le righe per produttore e applicava `Take(50)`: un PC con molti dispositivi AMD/Intel poteva nascondere completamente **NVIDIA GPU, NVIDIA HD Audio, NVIDIA Platform Controllers o Killer** in questo riepilogo anche quando erano presenti nell'inventario Windows.

La diagnostica delle fonti OEM `VendorSourceDiscovery` era stata corretta con la issue #151, ma il secondo percorso era rimasto vulnerabile allo stesso effetto.

## Soluzione

- Budget invariato (**50** righe per default, massimo **80**) per non saturare la Home. Selezione **round-robin** fra produttori AMD, Intel, Killer/Intel, NVIDIA riconosciuti.
- A parità di slot, vengono privilegiati i gruppi e le righe con **candidati WUA realmente correlati per Hardware ID o Compatible ID**; quindi dispositivi con problemi Windows; poi ordine deterministico.
- Un Device Instance ID PnP duplicato viene contato una volta (comparazione case-insensitive). Lo stesso `UpdateID` restituito da pass WUA diversi conta **una sola volta** per dispositivo. Nessun matching per titolo o marca.
- Quando **tutte** le fonti WUA sono fallite, il riepilogo mostra «ricerca Windows Update non verificata», non zero candidati.
- Quando **almeno una** fonte WUA è riuscita ma altre sono fallite, ogni riga mostra **ricerca WUA parziale**. Una riga senza matching dice «0 correlazioni ... dalle fonti WUA riuscite», non che nessun aggiornamento esista.
- Anche se WUA restituisce candidati correlati, il testo mantiene **compatibilità da verificare**: ID esatto non garantisce ranking, firma, notebook OEM appropriato né sicurezza installativa.

Il pannello continua a mostrare soltanto **versioni installate e corrispondenze**; `DriverUpdateInfo`, `QuickUpdatePolicy`, installazioni, download, driver store e fonti NVIDIA/Intel ufficiali restano invariati. Nessuna nuova offerta driver viene inventata.

## QA e privacy

- Test sintetico 180 dispositivi Intel + NVIDIA RTX 4060 GPU/HD Audio/Platform + Killer Wi-Fi + AMD: sotto il limite di 50 ogni vendor rimane visibile.
- Limite 1/2 con priorità alla corrispondenza NVIDIA WUA per ID e ai problemi Windows.
- Duplicati di Device Instance ID e di WUA UpdateID su fonti diverse, limite 0/80.
- WUA parziale con zero o uno match, tutte le fonti non verificate, dati privati degli ID PnP mai mostrati nei testi locali.
- [ ] Build Windows .NET 10, xUnit Core/Service, WPF/IPC GitHub Actions.
- [ ] Collaudo Recoil 16: v0.0.75 vs nuova release con molti device Intel/AMD, verifica visibilità GPU/HD Audio/Platform e messaggio fonti parziali.
- [ ] DPI 100/125/150/200%, accessibilità pannello espandibile in Home (issue #95).

## Limiti da non confondere

Il fatto che NVIDIA ora **compaia** nel riepilogo non dimostra che Windows Update o un catalogo OEM offrano un pacchetto più recente e installabile per questo specifico notebook. Il benchmark «2 vs 8» rimane aperto (#91, #136); servono dati reali di compatibilità e pacchetti firmati ufficiali.

Riferimenti: #91, #95, #100, #136, #151, #160.
