# Diagnostica Recoil 16: otto categorie di confronto — #137

## Obiettivo

Il pulsante **Copia report copertura** nella schermata post-scan aggiunge una
matrice aggregata per le otto famiglie viste nello screenshot Driver Easy (#91):
Intel grafica, Intel Bluetooth, Intel PROSet/Wi-Fi extension,
Killer Networking Software, Killer scheda Wi-Fi, NVIDIA GPU,
NVIDIA HD Audio, NVIDIA Platform Controllers.

Questa matrice è un **confronto di visibilità**, non una proposta di
aggiornamento né una verifica di sicurezza. Non sostituisce il test reale #136.

## Significato dei tre contatori

- **Dispositivi PnP classificati**: PnP presente nell'inventario locale,
  raggruppato in una delle sei categorie hardware per classe, ID vendor
  e/o denominazione. Una scheda Intel non viene definita Killer solo
  perché ha VEN_8086; è necessaria evidenza del branding Killer.
  Nomi generici Microsoft con VEN_10DE, HDAUDIO VEN_10DE o ACPI NVDA
  restano rilevabili nei gruppi NVIDIA pertinenti.
- **Candidati WUA Type=Driver per ID**: GUID distinti di Windows Update Agent
  con HardwareID o CompatibleID correlati al dispositivo tramite
  `DriverUpdateMatcher`. La correlazione non garantisce installabilità.
  Non si attribuisce alcun candidato ai due gruppi esclusivamente software.
- **Titoli WUA Type=Software**: solo indizi testuali classificati in
  categorie fisse; NON sono associati tramite ID a dispositivi, non sono
  driver confermati, non alimentano `Aggiorna tutto`, installazione o
  notifiche. Titoli non classificabili non entrano nella matrice e
  rimangono nel conteggio software aggregato.

Gli stati **ricerca non verificata** e **inventario non verificato** sono
espliciti: uno zero per una fonte non interrogata non dimostra che il driver
sia assente. Il software Type=Software viene interrogato solo dove
previsto da Comprehensive; un nome presente in Driver Easy non implica
che Windows Update lo pubblichi.

## Privacy e sicurezza

Il testo copiato contiene soltanto nomi di categorie hardcoded e conteggi.
Non esporta ID PnP, seriali, nomi personalizzati, percorsi, versioni driver,
titoli WUA originali, UID aggiornamento o numero ordine PCSpecialist.
Non vengono interrogati nuovi servizi, scaricati pacchetti né richieste
installazioni.

Il rilevamento Intel/AMD richiede ora un delimitatore esatto dopo
`PCI\\VEN_8086`, `USB\\VID_8087` e `PCI\\VEN_1002`, per non
classificare per errore ID con prefissi somiglianti.

## Collaudo da fare sul Recoil 16 (issue #136)

1. Avvia **Analizza il PC** con rete disponibile e copia il report
   tramite **Copia report copertura**.
2. Confronta gli otto gruppi con la schermata Driver Easy.
   Per ciascuno separa PnP presente, WUA Driver per ID,
   WUA Software solo informativo e pacchetto OEM ufficiale.
3. Ripeti con fonti parziali/offline, verificando che resti
   scritto **non verificato** dove la query è fallita.
4. Convalida eventuali pacchetti reali NVIDIA/Intel/Killer con
   PnP Hardware ID/SUBSYS, OS, INF, firma CAT, ranking e fonte
   notebook OEM, senza pubblicare questi identificativi in issue.
5. Su Windows 11 verifica interfaccia e report a DPI 100/125/150%,
   assicurando che Type Software non entri in `Aggiorna tutto`.

I test sintetici di Shared.Core non sostituiscono l'E2E hardware.
