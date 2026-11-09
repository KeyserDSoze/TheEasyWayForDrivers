# Home: Intel e Killer con driver generico restano visibili (#291)

## Difetto
Il report diagnostico a otto categorie (#91, #285) riconosce correttamente
Intel GPU e Killer AX1675i da **Hardware ID PCI e codici CC_03/CC_02**
anche se Windows mostra nome "PCI Device" e classe Unknown/System.
Prima la Home raggruppava solo con `PNPClass=Display/Net/Bluetooth` o
nome Wi-Fi/Wireless: **lo stesso dispositivo risultava assente dalla
Home**, impedendo anche il pulsante di consultazione Driver Store locale.

## Correzione
`HomeVendorCoverageSummary` condivide le stesse regole conservatrici
`PciClassIdentity.HasDisplayClass`, `HasNetworkClass` e
`KillerWifiHardwareDetector.IsKnownAx1675i`; sono applicate
**solo dopo riconoscimento del produttore** tramite
`VendorSourceDiscovery`. Il sottosistema esatto e classe PCI network
sono obbligatori per usare il nome Killer; un adattatore Intel generico
rimane Intel e un bridge Host/Root Port non è diventato update.

Il pulsante "Consulta driver locali" ora preferisce un AX1675i con
PnPClass generica a una GPU Intel e richiama solo l'attuale
SetupAPI/PnPUtil read-only su esplicita azione dell'utente.

## Sicurezza e prove
Fixture con scheda Intel grafica generica + Killer con SUBSYS noto e
CC_0280: due dispositivi Home, due GUID WUA **confermati solo con ID
esatti** e le rispettive righe nel report di otto componenti;
controesempi su chipset CC_0600, SUBSYS errato, classe Camera
contraddittoria, WUA failed/partial e privacy clipboard.

Non cambia WUA, `QuickUpdatePolicy`, scaricamenti, firme, firmware
o installazioni. Un dispositivo PnP classificato **non equivale**
a un driver aggiornabile. Test Recoil 16 sul sistema reale restano
#91/#136/#85, e la UX generale #95.
