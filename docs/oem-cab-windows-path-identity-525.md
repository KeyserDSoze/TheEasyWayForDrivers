# CAB OEM: identità Win32 dei percorsi CFFILE (#525)

## Difetto
Il parser CAB `OemCabIndex` normalizzava `\\` in `/` e rifiutava
duplicati case-insensitive, ma poteva ancora considerare distinti nomi
che Windows interpreta come alias (punto o spazio terminale) e accettare
nomi DOS riservati con estensione (`CON.inf`, `COM1.inf`, `LPT9.cat`).
Un CFFILE chiamato `drivers` poteva inoltre coesistere con
`drivers/gpu.inf`: impossibile rappresentarli come file ordinari
nella stessa cartella Windows.

## Correzione
- Ogni segmento del percorso rifiuta punto/spazio terminale e nomi
  DOS riservati `CON`, `PRN`, `AUX`, `NUL`, `COM1..9`,
  `LPT1..9`, anche con estensione, con casing variabile e cifre
  apicali Windows `¹²³` per COM/LPT.
- L'indice completo verifica che nessun percorso CFFILE sia anche
  antenato implicito di un altro CFFILE, indipendentemente dall'ordine.
- Restano le quote, il limite CFFILE/CFDATA (#524), i controlli UTF-8 e
  i nomi ordinari in cartelle annidate. Non si espandono i dati CAB.

## Regressioni e limiti
`OemCabWindowsPathIdentityTests` usa CAB sintetici con nomi riservati,
alias, collisioni parent/child nei due ordini, casing differente,
duplicati dopo normalizzazione e cartelle INF/CAT valide.

È un controllo di **integrità dell'indice read-only**, non una verifica
crittografica del catalogo CAT, della compatibilità SetupAPI o della
installabilità. Nessun file OEM reale è stato testato con questi fixture;
QA hardware e firme restano in #122, #124 e #128.
