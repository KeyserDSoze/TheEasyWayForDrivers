# OmegaDrive — GUID duplicato e precedenza della fonte WUA (#344)

## Problema confermato nel codice
Un GUID Type=Driver può comparire in più pass WUA verificati. Il merge
`WindowsDriverCandidateMerge` deve preservare gli ID specifici
provenienti da **tutte le fonti WUA riconosciute**, anche se il primo
pass è già pieno. Tuttavia un record sintetico/manuale non confermato,
con **lo stesso GUID**, poteva contaminare il risultato nei casi:

- un titolo di fonte sconosciuta contenente «BIOS/Firmware/UEFI»
  veniva preferito dalla logica fail-closed, nascondendo una voce
  display WUA verificata;
- un record WUA **avanzato/nascosto/opzionale**, non idoneo a Update
  All, poteva ricevere gli ID hardware e l'incompletezza provenienti
  da una fonte esterna non riconosciuta.

La collisione del GUID non autentica la provenienza di un record.

## Regola di merge
Quando un GUID ha una rappresentazione con **origine WUA
riconosciuta** e una con origine sconosciuta, conserva soltanto i dati
di quella riconosciuta, indipendentemente dall'ordine di arrivo.
Il controllo usa gli stessi nomi esatti già ammessi dal piano WUA
(anche il suffisso ` · avanzato`, coerente con il flag advanced),
non un testo libero, un titolo o un nome produttore.

Due pass WUA riconosciuti continuano a:
1. unire i singoli Hardware ID con cap 1024 e selezione equa dei vendor;
2. preferire i metadati ordinari;
3. **escludere** l'installazione quando *qualunque* WUA verificato
   dello stesso GUID segnala BrowseOnly/optional;
4. applicare il veto firmware/BIOS/UEFI se qualunque pass WUA
   riconosciuto lo dichiara.

Nessuna modifica a ricerche WUA, fonti OEM, lista installabile,
download o recheck privilegiato. I soli record esterni restano
review-only; questo è un requisito di provenienza, non una
certificazione che un update sia nuovo o sicuro.

## Test
Regressioni xUnit Core su entrambe le sequenze di merge, falso
Firmware da fonte esterna, associazioni Killer estranee per WUA
advanced/hidden/optional, due pass ufficiali NVIDIA/Killer,
veto firmware/BrowseOnly ufficiali e isolamento Update All.

L'8-vs-2 di Driver Easy richiede ancora QA Windows Recoil 16 e
confronto di vere fonti OEM (#91/#136).
