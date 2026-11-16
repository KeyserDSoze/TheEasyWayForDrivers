# [Strings] quote-aware per audit dipendenze INF (#506)

## Diagnosi
`InfDependencyPreview.Analyze` usava `Trim('"')` sui valori di `[Strings]`.
Con un record `Binary="""nvidia.sys"""` una stringa con virgolette
letterali escaped veniva ridotta a `nvidia.sys` e poteva generare un
riferimento OEM locale apparentemente `Present`, quindi un candidato
errato per l'audit membership CAT/INF. Lo stesso vale per campi quoted
bilanciati ma posizionati male.

## Correzione
- Usa il tokenizzatore già condiviso `InfCommaFields.TrySplit(value, 1)`.
- Valori quoted leciti conservano le virgolette letterali se escaped;
  un valore con quote letterali non è ammesso come token semplice per
  file/sezione nel preview: nessun nome sorgente inventato.
- Il token non valido non pregiudica altre dichiarazioni `[Strings]`
  indipendenti; i duplicati continuano a non essere utilizzabili.
- Nessuna espansione ricorsiva dei token e nessun testo privato OEM
  inserito nelle avvertenze.

## Verifica
`InfDependencyStringsQuotedScalarTests`: quote escaped, quoted
malformati, token normali quoted/unquoted, sorgenti multiple
NVIDIA/Realtek, token annidati e piano CAT filesystem read-only.

Una sorgente `Present` indica soltanto la sua presenza apparente.
Non dimostra firma CAT valida, membership, applicabilita Windows,
ranking SetupAPI o installabilita. QA hardware #85/#136, crittografia
CAT reale #128 e ranking #130 restano aperti.
