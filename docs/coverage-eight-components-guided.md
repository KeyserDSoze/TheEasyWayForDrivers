# OmegaDrive — copertura guidata Intel, NVIDIA e Killer (#149)

## Perché
Nelle segnalazioni #91 e #136 Driver Easy riporta otto voci, mentre Windows Update su OmegaDrive ne offre meno. Sono due inventari con origini differenti: **rilevamento PnP**, **candidati WUA Type=Driver**, **titoli WUA Type=Software** e **pacchetti OEM per-device realmente verificati** non sono intercambiabili.

La schermata precedente presentava la lista WUA e un report aggregato da copiare, ma le otto categorie di riferimento erano disponibili soltanto nel report. Se una fonte non rispondeva, alcune righe potevano apparire come uno zero esaustivo anziché come una misura incompleta.

## Nuova UX
Dopo la scansione, in **Produttori · copertura NVIDIA/Intel/Killer e fonti ufficiali**, apri **Perché non vedo NVIDIA, Intel e Killer? · 8 componenti**.

Ogni riga comprende:
- categoria fissa e non identificante (Intel grafica/Bluetooth/PROSet, Killer Software/Wi-Fi, NVIDIA GPU/HD Audio/Platform);
- dispositivi PnP classificati, con inventario dichiarato non verificato quando necessario;
- candidati WUA `Type=Driver` correlati per ID e indicazione esplicita di fonte parziale;
- titoli WUA `Type=Software` **solo informativi**, non associati a un dispositivo né installabili, con indicazione di fonte parziale;
- prossimo passo (verifica OEM/manuale per hardware riconosciuto senza candidato WUA, verifica compatibilità per ID quando WUA offre una voce).

Gli elenchi NVIDIA/Intel e l'area OEM sono chiusi inizialmente per preservare lo spazio della lista principale. La nuova diagnostica **non crea** nuovi `DriverUpdateInfo`, non modifica `QuickUpdatePolicy`, non lancia strumenti OEM e non installa nulla.

## Copertura e sicurezza del report
`ScanCoverageText` riutilizza le stesse categorie e ora annota `ricerca parziale` se è stata interrogata una fonte ma altre sono fallite. Lo stato `Type=Software` è indipendente da `Type=Driver`.

Il report copiato mantiene soltanto categorie predefinite e conteggi; evita nome PC, seriale, Device Instance ID, Hardware ID, percorsi, nomi e versioni personalizzate. Se non è stata verificata alcuna fonte, non dichiara zero aggiornamenti disponibili.

## Validazione
- Test xUnit su inventario non disponibile, GPU NVIDIA con nome Microsoft, ricerca completa/parziale/fallita, categorie Software-only, software parziale non bloccante, update WUA multi-ID e privacy report.
- Contratto PowerShell del WPF XAML per gerarchia della sezione, binding dei cinque campi informativi, separazione dalla lista installabile, reset e aggiornamento.
- GitHub Actions Windows per restore, compilazione .NET 10 e test Core/Service.

## QA reale ancora aperto
- [ ] Windows 11 su PCSpecialist Recoil 16: confronto otto categorie, con 0/1/N candidati e fonti offline/parziali.
- [ ] NVIDIA RTX 4060 Laptop/HD Audio/Platform; Intel UHD, Bluetooth, Killer AX1675i.
- [ ] Finestra 880×610, DPI 100/125/150/200%, contrasto, tastiera e screen reader.
- [ ] Confronto con Driver Easy includendo OS, build e versioni *solo sul dispositivo*: non pubblicare Hardware ID o seriale in issue GitHub.
- [ ] Verificare eventuali cataloghi OEM ufficiali realmente interrogabili con compatibilità documentata. I link NVIDIA App e Intel DSA non equivalgono a una query effettuata.

**Non dichiarare il gap 2 vs 8 risolto** finché non vengono acquisiti pacchetti appropriati, firma, compatibilità e prove hardware reali. Riferimenti #91 #100 #136 #149.
