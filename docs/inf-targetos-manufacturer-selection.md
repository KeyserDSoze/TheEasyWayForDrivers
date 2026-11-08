# Preflight INF: selezione effettiva delle sezioni TargetOSVersion (#254)

## Problema nella v0.0.121

`InfPlatformPreflight.Assess` verificava prima che un INF contenesse
un Hardware ID NVIDIA/Killer/Realtek in una sezione `Models` richiamata
dal `[Manufacturer]`, poi considerava sufficiente che
**una qualunque** sezione con quel modello avesse un requisito
Windows già soddisfatto.

Questo non coincide con la logica di Windows SetupAPI.
Microsoft specifica che, **per ogni riga del Manufacturer**, Windows
sceglie la sezione Models con la decorazione TargetOSVersion
**più specifica applicabile**. La sezione più recente può essere
vuota o contenere modelli completamente diversi: in quel caso
le vecchie sezioni **non sono un fallback automatico**.

Fonti Microsoft:

- https://learn.microsoft.com/en-us/windows-hardware/drivers/install/combining-platform-extensions-with-operating-system-versions
- https://learn.microsoft.com/en-us/windows-hardware/drivers/install/inf-manufacturer-section

## Caso riproducibile

Un INF locale può dichiarare:

```ini
[Version]
Signature="$Windows NT$"
DriverVer=11/08/2026,32.0.16.9500
[Manufacturer]
%Vendor%=Models,NTamd64.10.0...22000,NTamd64.10.0...26100

[Models.NTamd64.10.0...22000]
%Gpu%=Install,PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA

[Models.NTamd64.10.0...26100]
; nessun modello per questa GPU
```

Su Windows build **22631** la sezione 22000 può essere selezionata;
su Windows build **26100** prevale la sezione 26100,
e il match Nvidia **non è presente**. Prima del fix OmegaDrive
poteva dichiarare «Hardware ID esatto» e perfino «versione
numericamente superiore» sul PC con build 26100.

## Correzione

Il nuovo passo di preflight:

1. Analizza fino a **256 righe Manufacturer** con non più di
   **24 decorazioni** ciascuna, nel limite INF già esistente
   di **4 MiB** e delle righe logiche bounded.
2. Per **ogni riga indipendente** considera solo sezioni Models
   dichiarate esplicitamente per l'architettura e la versione
   Windows note; non permette che ARM64 oscuri AMD64.
3. Ordina le decorazioni applicabili per
   **Major.Minor.BuildNumber** e seleziona solo la più specifica,
   senza richiedere che contenga il device desiderato.
4. Se l'Hardware ID è **solo in una sezione precedente oscurata**,
   il preflight restituisce `NoHardwareMatch`, non un aggiornamento
   suggerito; se esistono vincoli ProductType/SuiteMask
   non verificabili che possono cambiare la scelta,
   restituisce `ManualReviewRequired`.
5. Se una **seconda riga Manufacturer** seleziona indipendentemente
   la sezione con il device, il matching rimane possibile.
   Un alias Compatible ID nella sezione selezionata non eredita
   la forza di un Hardware ID primario presente solo nella
   sezione vecchia.

Il parser di inventario `InfModelEvidence` può continuare a
elencare **tutte** le dichiarazioni INF a scopo diagnostico;
il controllo di applicabilità `InfPlatformPreflight` evita ora
di promuovere a «potenzialmente più recente» un match
di una sezione che Windows non sceglierebbe.

## Sicurezza, limiti e QA

La selezione rimane un'**approssimazione read-only e fail-closed**.
Il ranking reale SetupAPI, la firma CAT, le dipendenze SYS/DLL,
l'intero supporto OEM e l'installabilità **non sono verificati**
da un match TargetOSVersion.

Test sintetici: NVIDIA RTX, Intel/Killer, Realtek,
Windows build 22631/26100, sezione vuota, modello diverso,
alias Compatible ID, righe Manufacturer indipendenti,
architetture ARM64/AMD64, vincoli ProductType sconosciuti,
ZIP, cartella estratta, assenza di falsi suggerimenti
e di dati privati nei riepiloghi.

Nessun nuovo download/catalogo/aggiornamento WUA viene creato.
La verifica reale dei **2 contro 8** driver Driver Easy
sul Recoil 16 resta aperta (#91, #136).
