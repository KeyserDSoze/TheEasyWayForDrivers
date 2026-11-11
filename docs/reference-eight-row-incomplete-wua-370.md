# Benchmark otto componenti: ID WUA incompleti e falsi zeri (#370)

## Errore precedente

La tabella Intel UHD/Bluetooth/PROSet/Killer/NVIDIA viene usata per
confrontare le 8 segnalazioni Driver Easy (#91) con i GUID Type=Driver
effettivamente restituiti da WUA. Il servizio consente una lista di
Hardware ID con limite IPC 1.024 per GUID: quando il provider comunica
\`AdditionalHardwareIdsIncomplete\`, alcune associazioni PnP di quel
GUID **non sono state trasportate**.

La Home a tre vendor e il report di audit evidenziano già questo limite.
La tabella delle otto categorie, inclusa la copia anonima QA, lo perdeva
e stampava \`0 candidati\` senza avvertire che non è una prova negativa.

## Modifica

\`ReferenceDriverCoverage.Create\` passa ora al record di ciascuna
categoria hardware un indicatore \`WuaAssociationsIncomplete\`, derivato
**solo** da GUID Type=Driver da sorgenti WUA riconosciute dopo merge
canonicamente deduplicato e almeno una ricerca completata. Le categorie
con zero dispositivi o soltanto Software non ricevono l'indicatore.

Quando PnP è verificato e una categoria ha dispositivi, sia la
presentazione guidata sia il report copiabile mostrano:

- **0 match osservati, assenza non verificabile** quando i GUID
  osservati non si correlano agli ID disponibili;
- **ulteriori associazioni possibili** quando esistono match già
  verificati e la coda degli ID WUA non era esaustiva.

Questo **non** incrementa i conteggi di \`WindowsDriverCandidates\`,
non trasforma i modelli generici in match, non genera nuovi GUID,
non permette nuove installazioni e non interroga altri provider.

## QA e privacy

Test con GPU NVIDIA, audio NVIDIA, Killer Wi-Fi, Intel Bluetooth, stesso
GUID da due fonti WUA, ID completi, fonti OEM non verificate, GUID
malformati, ricerca WUA fallita, pass parziale e report copiato.
Non vengono stampati instance ID, Hardware ID, UpdateID o dati OEM.

I test non misurano nuove offerte Windows sul Recoil 16. Il confronto
Driver Easy vs OmegaDrive #91 e la QA hardware #136 restano aperti.
