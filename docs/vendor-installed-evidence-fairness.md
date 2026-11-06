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

## Ampliamento #190: Realtek, Broadcom, Qualcomm e MediaTek nel riepilogo Home

Il pannello *Fonti ufficiali* riconosceva già Realtek audio/rete, Broadcom,
Qualcomm/Atheros e MediaTek da Hardware/Compatible ID, ma il riepilogo
*Versioni installate* in Home e la fonte consigliata della scheda driver
continuavano a usare un classificatore separato e più ristretto.

Dalla correzione #190, `VendorDriverEvidenceFinder` riusa la classificazione
di supporto `VendorSourceDiscovery.GetRecognizedSupportVendor` per le quattro
famiglie, preservando lo stesso limite di 50/80 righe e la scelta round-robin.
Non serve avere un driver Realtek o MediaTek già installato con il nome
corretto: un dispositivo `Network Controller`, `Generic radio device`
o un codec `HDAUDIO/INTELAUDIO` con ID rilevato può apparire tra le
**versioni installate/verificabili**. L'eventuale conteggio dei candidati
WUA si basa esclusivamente su uguaglianza Hardware/Compatible ID, non
sul produttore o sul titolo dell'aggiornamento.

Il campo «Fonte consigliata» sulla scheda dispositivo utilizza la stessa
classificazione quando il dispositivo è riconosciuto. Per codec audio
interni `HDAUDIO` e `INTELAUDIO` si segnala anche l'OEM del notebook
quando noto. Questo è importante quando Intel Smart Sound è il provider
del codec, ma l'ID del chip identifica Realtek: il brand del driver
host non cambia il produttore del codec.

### Test e limiti

- Realtek PCI Ethernet e HDAUDIO/INTELAUDIO, Broadcom PCI/USB Bluetooth,
  Qualcomm/Atheros PCI/USB, MediaTek PCI/USB con nomi Windows generici.
- Hardware/Compatible ID, coerenza fra fonte di supporto, Home e scheda,
  priorità WUA realmente correlato, deduplica GUID, fonti WUA fallite,
  inventario Intel dominante e near-miss PCI/USB.
- Nessuna fotocamera USB, root port PCI o ID sconosciuto diventa un
  suggerimento driver, e il riepilogo non copia seriali o Instance ID PnP.
- Non viene interrogato un nuovo catalogo OEM: il supporto ufficiale
  resta una **verifica manuale**, mai un driver scaricabile con
  «Aggiorna tutto». L'assenza di una corrispondenza WUA non conferma
  che il driver sia aggiornato. QA sul Recoil 16 ancora necessaria:
  #91, #100, #136.

---

## Limiti da non confondere

Il fatto che NVIDIA ora **compaia** nel riepilogo non dimostra che Windows Update o un catalogo OEM offrano un pacchetto più recente e installabile per questo specifico notebook. Il benchmark «2 vs 8» rimane aperto (#91, #136); servono dati reali di compatibilità e pacchetti firmati ufficiali.

Riferimenti: #91, #95, #100, #136, #151, #160.
