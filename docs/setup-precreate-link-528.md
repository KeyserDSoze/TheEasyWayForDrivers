# Setup ZIP staging: controllo reparse PRIMA di creare directory (#528)

## Difetto
`SetupPayloadArchive.Extract` controllava gli antenati reparse/junction
**solo dopo** `Directory.CreateDirectory` per root, directory esplicite
e directory parent dei file. In presenza di un link già esistente nello
staging, `CreateDirectory` può creare una sottocartella fuori dalla
destinazione ancora prima del rifiuto, soprattutto quando il target del
pacchetto include directory nidificate.

## Correzione
- Nuovo preflight di `EnsureNoReparseAncestors` su ogni target **prima**
  di `CreateDirectory`; il controllo resta anche **dopo** la creazione.
- Analisi di tutti gli antenati esistenti tramite `File.GetAttributes`,
  gestendo i percorsi ancora assenti senza crearli in anticipo.
  Errori filesystem diversi da file/directory mancanti restano fatali.
- Nuovi `SetupPayloadPrecreateLinkTests`: destinazione nuova sotto un
  symlink, symlink annidato con directory ZIP esplicita/implicita e caso
  valido con cartelle mancanti.

## Limiti
Difesa di preflight per i collegamenti **già presenti**. Non garantisce
l'assenza di una race TOCTOU fra ispezione e creazione/scrittura: una
soluzione completa richiederebbe primitive Windows basate su handle e
protezione contro sostituzioni concorrenti dei parent. Nessuna driver
installation; verifica solo la preparazione del setup OmegaDrive.
