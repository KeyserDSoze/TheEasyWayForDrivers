# WUA Type=Driver: riconciliazione del conteggio grezzo per fonte (#533)

## Problema
Un report IPC legacy può contenere una riga `DriverUpdateInfo` con
origine `Windows Update online` anche se il pass COM della stessa
fonte dichiara `Completed` con `Candidates=0`. Il precedente filtro
di provenienza accettava la riga e poteva farla apparire nella Home,
nella lista per-device e nella selezione di Aggiorna tutto.
Un pass con 1 risultato grezzo e 2 righe attribuite è ugualmente
incoerente. Il problema è nel report ricevuto, non dimostra una
anomalia del server Microsoft.

## Correzione
- `WindowsDriverCandidateMerge.IsVerifiablePassForReport` verifica
  sia l'identità/il risultato del pass sia che il **numero di righe
  Type=Driver con GUID/origine WUA validi attribuite alla fonte**
  non superi il suo `Candidates` grezzo.
- `CompletedKnownSources` non usa fonti incoerenti per autenticare
  offerte o ID PnP; altre fonti indipendenti con contatori validi
  restano utilizzabili anche per GUID condivisi.
- `DriverSearchReport.IsPartial` e la diagnostica fonte della Home
  e di `Copia copertura` segnalano il difetto come dati non verificati,
  senza esportare GUID, hardware ID, hostname, URL o errori COM raw.
- `WuaDriverSourceRawCountIntegrityTests` copre zero/uno, overcount,
  fonte sana indipendente, GUID su più fonti e pass validi.

Non si creano nuovi driver, non si eseguono installazioni OEM e il
controllo non conferma la disponibilità di pacchetti NVIDIA/Intel.
Restano #91/#136 per Recoil16 reale e #283 per firma Authenticode.
