# INF preflight: quote rigorose per TargetOS e CatalogFile (#504)

## Problema
Il parser INF condiviso `InfCommaFields.TrySplit` era già adottato
per la scoperta PnP, `CopyFiles` e `Needs` (#502/#503), ma
`InfPlatformPreflight` continuava a spezzare `Manufacturer` con
`Split(',')` e a ripulire `CatalogFile` e `Signature` con
`Trim('"')`. Così `CatalogFile.NTamd64=""nvidia.cat""`, che non è
un nome di catalogo interpretato con quote INF valide, poteva
trasformarsi nel semplice `nvidia.cat` e provocare l'associazione
positiva di un file locale omonimo nella verifica guidata CAT.

## Correzione
- `ReadDeclaredModelSections` e `SelectPreferredModelSections`
  riutilizzano il parser quote-aware che già legge il Manufacturer
  per gli Hardware ID. Un campo malformato in fondo alla riga non
  può abilitare i Models o alterare la selezione TargetOSVersion.
- `Signature` e `CatalogFile` vengono decodificati come **uno
  e un solo campo**. Le quote doppie escaped rimangono un carattere
  letterale e non sono tolte arbitrariamente. Le virgolette
  incongruenti vengono respinte.
- Un CatalogFile specifico per architettura presente ma malformato
  non è sostituito da una dichiarazione generic più permissiva.
  Restano limiti di lunghezza, basename locale, blocco UNC/traversal
  e distinzione **CAT presente ≠ firma verificata**.
- Test per valori quoted validi, malformed, quote escaped e
  cataloghi generic/x64; preflight TargetOS NVIDIA/Killer/Realtek;
  ZIP OEM read-only e piano di verifica su cartella sintetica.

## Limiti
Nessun driver o firmware viene installato. L'esistenza di un CAT
non ne dimostra firma, membership dei SYS/DLL, ranking SetupAPI
o idoneità su Recoil 16: le verifiche hardware #85/#136,
le firme reali #128, il ranking #130 e il gap Driver Easy #91
restano da completare. La firma dell'app #283 richiede certificato.
