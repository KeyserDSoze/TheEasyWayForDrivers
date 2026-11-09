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
