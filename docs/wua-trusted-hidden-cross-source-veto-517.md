# WUA Hidden: veto condiviso tra fonti sullo stesso GUID (#517)

## Diagnosi
Due pass WUA verificati possono dichiarare lo stesso GUID con metadati
diversi: `IsHidden=true` in un pass, ordinario nell'altro.
`QuickUpdatePolicy.AllOrdinaryIds` già applicava il veto BrowseOnly/firmware,
ma non un `IsHidden` proveniente da altra fonte WUA fidata.
Il merge di provenienza privilegiava la rappresentazione ordinaria
e perdeva il flag Hidden. UI, fingerprint e Update All potevano
presentare il GUID come ordinario nonostante l'avviso da altro pass.

## Regole introdotte
- `QuickUpdatePolicy.AllOrdinaryIds`: almeno un record WUA ordinario e
  **nessun** record WUA trusted con `IsHidden`, `IsOptional` o
  firmware/BIOS dello stesso GUID, normalizzato D/B/N/P.
- `WindowsDriverCandidateMerge.Merge`: conserva titolo e Hardware IDs
  della rappresentazione preferita, ma unisce i flag `IsHidden` e
  `IsOptional` **solo** da sorgenti WUA riconosciute.
- `DeviceWuaPresentationGate.Filter`: privilegia il record Hidden
  nel gruppo diagnostico ReviewOnly quando restano più copie.
- Un'origine OEM sconosciuta non può falsificare un veto; un pass
  advanced non nascosto non annulla l'offerta di un pass ordinario.
- L'installazione richiede comunque verifica indipendente del
  servizio in tempo reale; non vengono modificati WUA o Driver Store.

## Test
`TrustedHiddenWuaGuidVetoTests`: GUID D/B/N/P, due ordini di pass,
merge e PnP NVIDIA/Killer, review per dispositivo, fingerprint,
Compact Update All, spoofing OEM e advanced. Modificata la precedente
aspettativa `OrdinaryWuaMetadataOutranksHiddenSameGuidInEitherArrivalOrder`
per mantenere la preferenza di display, non l'eligibility.

QA Windows e Recoil 16 reali ancora aperte (#94/#85/#92/#136).
Firma Authenticode del prodotto ancora bloccata da #283.
