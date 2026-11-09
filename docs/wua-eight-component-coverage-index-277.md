# Indicizzazione della matrice diagnostica 8 componenti (issue #277)

## Il collo di bottiglia
La vista Home, i suggerimenti per produttore e il report copiabile
mostrano otto categorie fisse del benchmark Recoil 16: grafica Intel,
Bluetooth, PROSet, Killer Networking, scheda Killer Wi-Fi, GPU/HD
Audio/Platform NVIDIA. Ogni aggiornamento WUA Type=Driver con 1024 ID
aggiuntivi poteva essere confrontato ripetutamente con ogni dispositivo
dell'inventario. Con 500 device e 200 offerte l'algoritmo richiedeva già
100.000 confronti dispositivo/offerta, ciascuno potenzialmente costoso.

## Modifica
\`ReferenceDriverCoverage.Create\` forma un indice locale
\`Hardware/Compatible ID → maschera di categorie\` in sola lettura:
- ogni Device Instance ID distinto è classificato una volta;
- ogni Hardware/Compatible ID PnP viene inserito dopo normalizzazione
  \`Trim\` e confronto case-insensitive, senza cambiare la semantica;
- per ogni aggiornamento Type=Driver si osserva esclusivamente il
  \`HardwareId\` principale e i primi 1024 \`AdditionalHardwareIds\`
  documentati dal motore WUA;
- una maschera permette a un GUID di correlare più categorie;
- un GUID duplicato fra diverse fonti WUA conta una volta per
  categoria, anche quando una fonte dichiara un altro ID;
- software Type=Software resta un *titolo non associato* e non abilita
  aggiornamenti. Un titolo/marchio simile NON fornisce una corrispondenza.

La complessità diventa proporzionale ai PnP ID realmente classificati
più gli ID WUA effettivamente dichiarati, non al prodotto dei conteggi
device×offerte. Nessun catalogo esterno viene chiamato.

## Sicurezza e QA
Il report mostra solo nomi di otto categorie compilate nel codice e
contatori; non copia Instance ID, Hardware ID, GUID, titolo offerta o
identificativi personali. Nessuna modifica a \`QuickUpdatePolicy\`,
selezione, download, ranking OEM o installazione.

Test: 500 device/200 offerte (NVIDIA GPU/HD Audio/Platform,
Intel Bluetooth e Killer AX1675i), GUID multi-sorgente, ID
compatibili, maiuscole/minuscole e spazi, falsi prefissi,
cap di 1024 ID, software-only e privacy del report.

Il confronto reale 2 vs 8 su Recoil 16 (#91/#136) resta da eseguire
sull'hardware. Questo ottimizza la visibilità delle offerte WUA
già restitute: non crea nuove offerte NVIDIA/Intel/Killer.
