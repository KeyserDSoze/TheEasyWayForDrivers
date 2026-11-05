# Discovery WUA per componenti software OEM — issue #134

## Il divario che vogliamo misurare

Nel PCSpecialist Recoil 16, il software terzo Driver Easy mostrava
otto elementi, tra cui Intel PROSet/Wireless WiFi Software extension,
Killer Networking Software, GPU/audio/framework NVIDIA. OmegaDrive
v0.0.64 consulta WUA con `Type='Driver'` in sei possibili pass
(configurato, Windows Update online, Microsoft Update già registrato,
ordinario/avanzato).

Windows Update Agent distingue formalmente **Type='Driver'**
e **Type='Software'**. Non è quindi corretto assumere che tutte le
suite del produttore vengano restituite dal primo filtro. Questo
incremento consulta anche Type Software senza promettere che vi
siano nuovi driver per il dispositivo specifico.

Documentazione ufficiale:
- https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iupdatesearcher-search
- https://learn.microsoft.com/en-us/windows/win32/wua_sdk/iwindowsdriverupdate-properties
- https://support.microsoft.com/en-us/servicing/os/windows/docs/2025/10/understanding-driver-updates

## Come funziona

Solo durante **Analizza il PC** o ricerca *Comprehensive*:

1. Si conserva esattamente l'attuale ricerca di `Type='Driver'`
   (ordinaria, opzionale/hidden, superseded, WU online/MU registrato),
   con i candidati WUA installabili nella consueta lista.
2. In pass **indipendenti** si interrogano i servizi WUA esistenti
   mediante `IsInstalled=0 and IsHidden=0 and Type='Software'`:
   - servizio di aggiornamento già configurato;
   - Windows Update online;
   - Microsoft Update online **solo se già registrato**.
3. Nessun servizio viene installato/registrato, nessuna policy Windows
   cambiata. I pass restituiscono solo metadati.
4. Un classificatore locale di titoli sceglie **possibili** software
   per GPU/audio/rete/chipset/extension che citano un produttore
   Intel, Killer, NVIDIA, AMD, Realtek, Qualcomm, Broadcom o MediaTek.
   I titoli contenenti Firmware, BIOS, UEFI, Defender, Office o
   aggiornamenti generici vengono scartati. Questa inferenza non
   dimostra alcun Hardware ID o reale necessità di aggiornamento.
5. La UI mostra gli elementi in **Componenti software del
   produttore · da verificare**, sezione espandibile, chiusa
   di default e **separata** dall'elenco WUA Driver e da
   Aggiorna tutto. Ogni riga indica la fonte e che è Software
   informativo, non installabile con OmegaDrive.
6. Le fonti software fallite producono uno stato **parziale**
   e non un falso «zero componenti».
7. Il report di copertura copiabile include solo **conteggi**
   e nome di fonte tra quelli fissati da OmegaDrive; nessun
   Hardware ID, seriale, percorso, title o numero d'ordine.

## Limiti e sicurezza

- Query `Type='Software'` è **solo read-only**:
  nessun AcceptEula, download, `InstallAsync` o Windows service
  registration. Nessun accesso agli update Type Software nella
  `DriverUpdateInfo` che alimenta i comandi WUA di installazione.
- Titoli non sono dati di compatibilità né firmware verificato.
  Classificazione basata sul testo, soggetta a falsi positivi/
  negativi; non esegue matching PnP né ranking Windows.
- `MaxRawSearchResults=4000` per pass; se eccede il limite,
  il pass fallisce chiuso e i risultati parziali vengono scartati.
- Max 32 evidenze software distinte visualizzate per scansione.
  Doppioni WUA con lo stesso GUID vengono uniti senza inflazionare
  il totale, e gli elementi oltre limite sono conteggiati.
- Non interpretiamo `Type='Software'` come driver di Windows,
  update WHQL o pacchetto pronto per il PC. Vengono **esclusi
  da Aggiorna tutto**, selezione default e notifiche driver.
- La query Software aumenta il lavoro online della ricerca
  approfondita e può richiedere più tempo. I guasti non fanno
  sparire i candidati Driver già recuperati.

## Test

- [x] Regole query WUA `Type='Software'`, Comprehensive-only,
  MU registrato vs non registrato.
- [x] Fixture PROSet/Wireless, Killer Networking, NVIDIA
  SoftwareComponent/Audio, Intel Bluetooth, AMD/Realtek.
- [x] Scarto di firmware, BIOS, UEFI, Windows, Microsoft Office,
  software generico e falsi title privati/invalidi.
- [x] Dedup GUID inter-servizio, max 32 con overflow esplicito.
- [x] `DriverSearchReport` JSON IPC con Software separato da
  Driver; failure parziale non significa assenza driver WUA.
- [x] Test di policy: nessun software classificato come driver
  installabile o promosso in `Aggiorna tutto`.
- [x] Contratto statico UI WPF: lista Type Software dedicata,
  chiusa e nascosta fino a dati della scansione.
- [ ] CI Windows build/test e installer.
- [ ] Collaudo reale sul PCSpecialist Recoil 16: confronto
  candidato per candidato delle otto righe della issue #91,
  provando fonti WUA Type Driver, WUA Type Software, OEM
  per-device e archivi locali.

## Da fare dopo questo incremento

Il vero aggiornamento di driver richiede **applicabilità per-device**
con Hardware ID/SUBSYS/CHID, modello notebook, versioni,
firma e policy OEM. Le query Software non danno automaticamente
questa prova; un componente che compare nel pannello resta
informativo anche quando sembra corrispondere a Driver Easy.

La prossima fase può cercare fonti ufficiali con interfacce
machine-readable e licenze/permessi pubblici documentati,
oppure usare i pacchetti ufficiali del produttore già
scaricati con il confronto INF/CAT della v0.0.63.

Rif #134 #91 #100 #130.
