# Win32 path identity in OEM ZIP and installer ZIP (#526)

## Problema
A differenza del CAB già corretto in #525, il validatore dei nomi
ZIP OEM accettava segmenti di file con nome di dispositivo DOS
(`CON.inf`, `LPT9.cat`, `COM¹.sys`) e segmenti terminati da punto
o spazio. Questa discrepanza può falsare l'identità dei file nei
report di confronto INF/driver, pur essendo un'analisi sola lettura.
Il gate del setup rifiutava nomi DOS ASCII, non le varianti COM/LPT
con cifre apicali Win32.

## Modifica
- `OemArchiveInspector.SafePath`: normalizza `\\` e `/`,
  rifiuta un doppio separatore terminale, punto/spazio finale di
  ogni segmento, dispositivi riservati anche con estensione e
  cifre apicali `¹²³` per `COM`/`LPT`.
- `SetupPayloadArchive.IsSafeName`: allinea il rifiuto ai
  dispositivi DOS con cifre apicali, nel preflight **prima** della
  scrittura nell'area temporanea.
- `OemZipWin32PathIdentityTests`: ZIP sintetici con nomi ambigui,
  directory/traversal implicitamente normalizzate, casi Windows
  validi e conferma che il setup rifiuti il payload senza creare staging.

Il parsing ZIP OEM rimane read-only. Questi controlli non certificano
firme CAT/INF, integrità SYS/DLL, compatibilità PnP o ranking
Windows SetupAPI. La validazione con archivi autentici PCSpecialist
e Recoil 16 resta tracciata in #122/#124/#128/#136.
