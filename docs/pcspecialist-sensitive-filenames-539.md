# PCSpecialist: filtro privacy sui nomi dei pacchetti HTML (#539)

## Problema
Il portale PCSpecialist può mostrare file relativi all'ordine del PC.
Il parser salvava soltanto il nome visibile, e già scartava URL, token
firmati e alcuni nomi con `ORDER`/`SERIAL`/`INVOICE`/`TOKEN`.
Ma un filename ASCII come `W11 Nvidia PRIVATE_MACHINE.zip` o
`W11 Killer CUSTOMER_12345.cab` superava `IsSafePackageName`
ed entrava nel preview, nell'elenco completo usato per la
riconciliazione e nei nomi privilegiati del planner locale.

## Correzione
Il filtro unico `PcSpecialistPortalSnapshot.IsSafePackageName`
rifiuta ora, indipendentemente dal case, marker testuali di
ordine, credenziali e identità:
`PRIVATE`, `CUSTOMER`, `SECRET`, `ACCOUNT`, `USERNAME`,
`HOSTNAME`, `PASSWORD`, `EMAIL`, `DEVICEID`,
`INSTANCEID`, `UUID`, oltre a quelli già bloccati.
I marcatori si cercano sull'intero nome, compresa la parte
prima dell'estensione: un carattere ASCII sicuro non implica
che il suo contenuto sia anonimo.

Il parser omette i nomi non sicuri; se il catalogo contiene solo
nomi non sicuri fallisce senza dichiarare assenza di driver.
Preview, planner PCSpecialist e riconciliazione chiamano già lo
stesso metodo; input costruiti direttamente non lo aggirano.
Restano visibili gli archivi pubblici NVIDIA, Killer, Realtek
confrontati in sola lettura.

## Limiti
Si tratta di una redazione **per marcatori riconoscibili**.
Non è possibile dedurre in modo certo che un nome legittimamente
formato non contenga altre informazioni identificative. L'import
non verifica origine del portale, firme CAT, ranking Windows o
applicabilità del driver al PC; la QA Recoil 16 resta #120/#136.
