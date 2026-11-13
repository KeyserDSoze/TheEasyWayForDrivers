# OmegaDrive — WUA e INF: modello specifico per USB/audio/SWC (#324)

## Perché
`DriverUpdateMatcher.IsCorrelatableHardwareId` controllava la specificità
solo per PCI; per tutti gli altri bus l'uguaglianza letterale era
sufficiente. Un ID come `USB\\VID_8087`, `HDAUDIO\\FUNC_01` o
`SWC\\VEN_10DE` può appartenere a più dispositivi. Considerarlo
un match specifico altera la diagnostica e può presentare come correlato
un driver WUA a un device che non ha un modello identificato.

## Modifica
Per **PCI, USB, HDAUDIO, INTELAUDIO e SWC**, prima di accettare
la correlazione usiamo la grammatica stretta già definita in
`MicrosoftUpdateCatalogSearch.TryGetPublicModelKey`: rispettivamente
`VEN+DEV`, `VID+PID`, `FUNC+VEN+DEV`, `FUNC+VEN+DEV` e
`VEN+DEV`. Il matching resta la **uguaglianza integrale** degli ID
originali, con suffix OEM/REV conservati; non si converte il generico
in una corrispondenza valida, né si associa per nome o brand.
Gli altri bus Windows (es. ACPI, HID) mantengono il comportamento
precedente di uguaglianza esatta, così non si perdono casi non in scope.

Il filtro condiviso influenza la correlazione WUA→PnP, il tasto Update
per dispositivo, la matrice diagnostica per vendor e gli ID INF
verificati localmente. Le voci OEM/Type=Software non diventano
offerte installabili; `Aggiorna tutto` resta sottoposto ai gate
Windows Update preesistenti. I test sintetici non sostituiscono la QA
fisica Recoil 16 (#91/#136), e la firma Authenticode resta #283.

## Verifica del token PnP locale nei confronti WUA (#418)

Anche un match diagnostico **esatto** deve richiedere un singolo ID PnP ben formato sia dall'offerta WUA sia dal dispositivo Windows. Un vecchio snapshot WMI/IPC può contenere un ID con spazi ai bordi, un path di istanza aggiuntivo, un token senza bus o caratteri di controllo. La sola normalizzazione ammessa è togliere semplici spazi ASCII esterni; TAB, CR/LF, spazi Unicode, percorsi di istanza e token senza bus non devono diventare un match.

`DriverUpdateMatcher.IsCorrelatableHardwareId` applica ora anche `PnpIdentifierFallback.IsStrictSingleId`, prima delle regole specifiche PCI, USB, HID, USBSTOR, audio e SWC. Gli altri bus Windows (ad esempio ACPI e ROOT) mantengono il confronto **esatto** quando il token è ben formato; non si tenta di indovinare la compatibilità di un ID sconosciuto. `MatchStrength` richiede la stessa verifica per HardwareID e CompatibleID **locali**, non soltanto per quelli WUA.

Gli indici dell'audit WUA→PnP e della matrice anonima a otto categorie condividono la stessa convalida, incluse le sole correzioni innocue di spazi ASCII esterni. Un dispositivo NVIDIA può essere contato come presente nell'inventario pur senza un match WUA se l'unico ID locale è malformato. Le regole sono *read-only*: non modificano installazione, enumerazione WUA, policy `Aggiorna tutto`, cataloghi OEM o disponibilità reale. Test sintetici verificano anche la conservazione di modelli PCI, HID, ACPI e ROOT validi; #91/#136 richiedono ancora QA Windows reale.

## Merge multi-fonte WUA: nessuna promozione di whitespace Unicode (#430)

Due pass WUA `Type=Driver` completati possono riportare lo **stesso GUID** con HardwareID primari e aggiuntivi diversi. Il consolidatore `WindowsDriverCandidateMerge.MergeDeviceIds` usava `Trim()`: un identificativo secondario malformato con NBSP (`U+00A0`), EM SPACE (`U+2003`) o NARROW NBSP (`U+202F`) intorno al vero ID NVIDIA/Intel veniva trasformato in un ID valido **prima** di arrivare al matcher rigoroso. L'uguaglianza PnP poteva così essere presentata come esatta soltanto grazie alla pulizia implicita del merge.

Ora la deduplicazione del primary HardwareID elimina **soltanto spazi ASCII** esterni; le entry secondarie con whitespace non ASCII o caratteri di controllo vengono scartate e soltanto il padding ASCII innocuo resta normalizzabile. La versione primaria originale rimane invariata. Non vengono inventati modelli o identità OEM: i validi ID GPU NVIDIA, Intel Killer e Realtek continuano a essere associati e la selezione equa delle 1024 entry WUA resta immutata.

Test sintetici verificano multi-pass con lo stesso GUID, Unicode nel secondario e nel primary dell'altro pass, dedup D/B, audit WUA→PnP **non attribuito**, e casi positivi ASCII per NVIDIA/Killer. Questo fix è **solo diagnostico**: la disponibilità di aggiornamenti reali e la QA Recoil 16 (#91/#99/#136) rimangono non verificate; non cambia `QuickUpdatePolicy` né rende installabili pacchetti OEM.
