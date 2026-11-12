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

Anche un match diagnostico **esatto** deve richiedere un singolo ID PnP ben formato sia dall'offerta WUA sia dal dispositivo Windows. Un vecchio snapshot WMI/IPC può contenere un ID con spazi ai bordi, un path di istanza aggiuntivo, un token senza bus o caratteri di controllo. Usare `Trim` su uno di questi valori per creare un match inventa evidenza che Windows non ha verificato.

`DriverUpdateMatcher.IsCorrelatableHardwareId` applica ora anche `PnpIdentifierFallback.IsStrictSingleId`, prima delle regole specifiche PCI, USB, HID, USBSTOR, audio e SWC. Gli altri bus Windows (ad esempio ACPI e ROOT) mantengono il confronto **esatto** quando il token è ben formato; non si tenta di indovinare la compatibilità di un ID sconosciuto. `MatchStrength` richiede la stessa verifica per HardwareID e CompatibleID **locali**, non soltanto per quelli WUA.

Gli indici dell'audit WUA→PnP e della matrice anonima a otto categorie non riparano gli ID locali con `Trim`. Un dispositivo NVIDIA può essere contato come presente nell'inventario pur senza un match WUA se l'unico ID locale è malformato. Le regole sono *read-only*: non modificano installazione, enumerazione WUA, policy `Aggiorna tutto`, cataloghi OEM o disponibilità reale. Test sintetici verificano anche la conservazione di modelli PCI, HID, ACPI e ROOT validi; #91/#136 richiedono ancora QA Windows reale.
