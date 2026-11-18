# Matrice di 8 categorie: fail-closed sui budget WUA/PnP (#547)

## Problema
La diagnosi anonima `WuaPnpAssociationAudit` interrompe le correlazioni
oltre i limiti, ma `ReferenceDriverCoverage` (Home e report copiabile)
continuava a enumerare il payload e poteva mostrare `0 candidati`
come misura verificata. Un payload IPC fuori budget non attesta
l'assenza di driver NVIDIA, Intel o Killer.

## Correzione
- La matrice esamina al massimo **8193** righe PnP, per identificare e
  rifiutare il superamento del budget di **8192** righe grezze.
- All'interno del budget: **2048 istanze DeviceId distinte** (chiave
  case-insensitive e normalizzazione degli spazi ASCII di contorno #545),
  **262144 Hardware/Compatible ID PnP**.
- Prima del merge WUA: massimo **4000** righe di offerte e
  **2000000** identificatori contabilizzati secondo il criterio
  diagnostico già usato dall'audit WUA→PnP.
- Se un limite è superato, otto righe statiche riportano
  *non valutabile* senza eseguire la correlazione costosa;
  nessun GUID, ID, percorso o titolo WMI viene copiato.
- Le sorgenti Type=Software restano separate e informazionali;
  nessun nuovo candidato per l'installazione è creato.
- Test di regressione per il punto di superamento dei budget,
  duplicati ammessi e testo Home/report.

## Validazione
CI Windows build/test e pubblicazione su stesso commit sono tracciate
nell'issue #547. QA hardware reale, campionamento di 8 candidati
Driver Easy e disponibilità OEM restano #91/#136/#85.
