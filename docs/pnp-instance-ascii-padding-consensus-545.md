# Identità PnP con spazi ASCII di contorno (#545)

## Difetto
Snapshot WMI/SignedDriver e IPC legacy possono rappresentare lo stesso
DeviceId con spazi ASCII iniziali/finali diversi. In passato il consenso
PnP, la matrice OEM e l'audit WUA contavano tali righe come istanze separate.
Modelli NVIDIA e Realtek contraddittori della stessa istanza potevano
sopravvivere, oppure un duplicato consumava uno slot delle 2048 istanze.

## Correzione
- Confronto PnP con la sola chiave DeviceId privata degli spazi ASCII
  esterni, mantenendo StringComparer.OrdinalIgnoreCase.
- Stessa funzione interna condivisa da consenso, matrice OEM e audit WUA.
- Rimangono 2048 istanze massime e 8192 righe raw; le righe duplicate
  entro il budget vengono considerate tutte per il veto di hardware
  primario contraddittorio.
- DeviceId originale, HardwareIds e CompatibleIds NON vengono riscritti.
  Non si eliminano TAB, CR/LF o spazi Unicode; nessun nuovo ID viene
  sintetizzato per WUA, SetupAPI o installazione.

## Regressioni
Test per modelli NVIDIA/Realtek incompatibili nello stesso DeviceId con
spazi in entrambi gli ordini, dispositivo Killer indipendente, alias
successivo a 2048 identità distinte e DeviceId vuoto/spazi.
L'audit resta anonimo; nessun download, installer o driver attivato.
La validazione runtime Windows (#85, #136) rimane necessaria.
