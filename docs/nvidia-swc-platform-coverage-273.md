# NVIDIA SoftwareComponent / ACPI Platform — matrice degli otto componenti (#273)

## Causa del falso negativo

Il riconoscimento generico dei vendor già distingueva NVIDIA in base
all'Hardware ID, anche quando Windows mostrava `Software Component` oppure
`System Controller`. Tuttavia `ReferenceDriverCoverage` riconosceva la
categoria NVIDIA Platform Controllers soltanto se il **nome** conteneva
Platform o l'ID iniziava con ACPI\\NVDA. I nodi NVIDIA DCH `SWC\\VEN_10DE`
con titolo generico rimanevano assenti dalla matrice Recoil 16, pur
essendo già riconosciuti negli altri riepiloghi della scansione.

## Correzione

`NvidiaHardwareDetector.HasNvidiaPlatformId` è una classificazione di
sola lettura condivisa per Hardware ID e Compatible ID:

- `ACPI\\NVDA` + **quattro cifre esadecimali**;
- `SWC\\VEN_10DE&DEV_` + **quattro cifre esadecimali**;
- eventuali suffissi possono comparire soltanto dopo il confine `&`;
- prefissi simili, codici brevi, caratteri non esadecimali e suffissi
  concatenati sono rifiutati.

La categoria NVIDIA Platform della matrice usa lo stesso controllo.
Le classi e i nomi generici non diventano un vendor per congettura.
GPU Display e HD Audio hanno ancora precedenza quando pertinenti.

## Sicurezza, QA e limite

Test per ID di piattaforma NVIDIA ACPI/SWC positivi e near-miss,
ID compatibili, riga Platform nella matrice degli otto componenti,
dedup per GUID, nessun Device Instance ID nel report copiato.

Questo è un miglioramento della **visibilità dell'hardware già
scansionato**. L'associazione esatta del GUID WUA non certifica
applicabilità, ranking, firma, download o versione più recente:
nessun cambiamento alle policy di installazione / Aggiorna tutto.

La discrepanza 2 vs 8 sul Recoil 16 richiede ancora la validazione
su hardware reale e le fonti ufficiali NVIDIA/OEM (#22/#91/#136).
