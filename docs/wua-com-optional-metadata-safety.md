# OmegaDrive — metadati WUA scalari e primaria PnP sicuri (#328)

`WuaDriverCandidateReader` ora valida **per singola proprietà** gli
scalari testuali COM WUA prima di usarli nella UI e nel controllo di
firmware/BIOS. Sono accettate soltanto stringhe effettive, non vuote,
entro un budget distinto per `Title` (512), `DriverClass` (128),
informazioni su vendor/modello (1024) e `Description` (4096).
Caratteri di controllo, NUL, CR/LF, bidi invisibili e surrogate
malformate non sono considerati metadati verificati.

Se `Title` o `DriverClass` non sono validi, il GUID WUA ancora
confermato può essere mostrato soltanto in **review**, senza
installazione automatica. Campi informativi incompleti non invalidano
il risultato WUA; si mostrano come sconosciuti. Il singolo
`DriverHardwareID` primario richiede una sintassi PnP bus-qualified
rigorosa senza instance path e non può superare 512 caratteri;
gli ID addizionali COM validi del medesimo update non vengono persi.

Il filtro non registra i valori COM, non estrapola ID da nome o titolo,
non esegue software OEM e mantiene invariati i gate di
`QuickUpdatePolicy`, il controllo GUID e la ri-verifica Windows Update
al download. Test Service sintetici: stringhe lunghe e malformate,
NVIDIA e audio, firmware, primary ID non valido, revisione obbligatoria,
cancellazione, privacy. QA Recoil 16 (#136/#91) e firma Authenticode
(#283) restano indipendenti.
