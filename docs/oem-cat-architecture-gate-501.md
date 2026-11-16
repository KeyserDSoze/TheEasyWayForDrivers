# Verifica CAT/INF OEM: architettura Models prima dei 12 slot (#501)

## Difetto
`OemFolderCatalogAuditPlan` utilizza il lettore completo
`InfModelEvidence.ReadMatchingIds` per correlare Hardware ID e
Compatible ID PnP nei file INF locali. Quel lettore enumera
intenzionalmente **tutte le sezioni Models dichiarate** (x64, ARM64,
x86, generiche) per non perdere un modello oltre la soglia 500.
Ma la successiva selezione delle 12 verifiche di firma CAT+membership
non filtrava i Models `NTarm64` su x64, e viceversa. Un INF di altra
architettura poteva così occupare controlli che servivano per NVIDIA
Display, Intel/Killer e Realtek sull'architettura effettiva.

## Correzione
- `InfPlatformPreflight.IsExplicitArchitectureMismatch` riutilizza
  il medesimo `CheckSection` del preflight con versione OS ignota
  (`-1`). Vengono scartate **solo** le sezioni Models che dichiarano
  esplicitamente un'architettura conosciuta diversa dalla selezione.
- `OemFolderCatalogAuditPlan` filtra ciascun ID mirato **prima** di
  costruire gli ordinal PnP per il piano firme. Se tutti i modelli
  corrispondenti erano solo di altra architettura, non pianifica la
  firma di quell'INF, incrementa `ArchitectureMismatchInf` e
  considera incompleto il report, senza rivelare percorsi o ID privati.
- I Models generici (`[Models]`, `[Models.NT]`) e le decorazioni
  sconosciute non vengono spacciati per incompatibili; restano
  diagnostica di sola revisione.
- Gli INF con dichiarazioni x64 e ARM64 differenti restano eleggibili
  per i dispositivi effettivamente elencati nella sezione pertinente.
- Intatti limiti 12 check CAT, 384 INF, 96 MiB, fairness firmware,
  symlink prohibition, catalogo nello stesso folder e budget Windows.

## Limiti
Questo filtro decide soltanto la differenza **esplicita** di
architettura CPU. Non verifica selezione TargetOSVersion specifica
di build Windows, ProductType/SuiteMask, rank SetupAPI, firma CAT,
membership di ogni SYS/DLL o installabilità. QA su hardware e
cataloghi OEM reali ancora da eseguire (#128/#130/#136).
