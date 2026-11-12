# PCI class-only: evitare identità vendor discordanti (#369)

## Situazione

Con l'inventario \`Win32_PnPEntity\` che riporta soltanto
\`PCI\CC_0300\` o \`PCI\CC_0280\`, OmegaDrive usa la SetupAPI
**sullo stesso PnP Device Instance ID** per ottenere VEN+DEV.
Quando la risposta nativa è corrotta/incoerente e contiene
contemporaneamente \`VEN_10DE\` NVIDIA e \`VEN_8086\` Killer,
il recupero precedente conservava entrambi come Hardware ID validi.
Una sola riga PnP poteva così correlarsi a driver di due vendor.

## Regola fail-closed

Se il WMI primario è PCI class-only e più vendor PCI distinti
sono presenti nei modelli specifici validati di SetupAPI,
**scartare l'intero recupero hardware nativo ambiguo**, mantenendo
l'identità generica WMI originale. Non scegliere arbitrariamente il
primo modello o il vendor dedotto da titolo/provider.

Se WMI ha già un VEN specificato, SetupAPI conserva solo i suoi
modelli validati (regola già esistente). I Compatible ID nativi/signed
per un recupero PCI parziale ora rispettano il bus PCI e il VEN
univoco recuperato; le classi PCI generiche rimangono ammesse.
Senza vendor validato, un Compatible ID VEN+DEV di altro origine
non diventa una corrispondenza WUA exact. Se la lista native è
ambigua, SignedDriver non può sovrascrivere tale ambiguità.

## Non cambia

- Query SetupAPI/WMI, budget di letture e set PnP.
- Priorità agli Hardware/Compatible ID completi originari WMI.
- Policy WUA/INF, ranking, installazioni/download e
  identificazione vendor tramite nomi.
- Sicurezza report: mai copiare PnP instance ID/seriali.

I test simulati coprono vendor discordanti NVIDIA/Killer/Realtek,
VEN-only vincolato, CompatibleID cross-vendor/cross-bus,
hardware specifico legittimo e report anonimo.

Questo hardening riduce falsi positivi, **non prova che nuovi driver
siano disponibili**. #91 e il benchmark Recoil 16 #136 rimangono
aperti, come firma Authenticode #283.


## Vendor PCI: non bloccare fonti legittime per suffix invalidi (#410, 12 novembre 2026)

Nel percorso di sola **consultazione OEM**, `VendorSourceDiscovery.HasForeignPciVendor` considerava prima autoritativo un ID che iniziava con `PCI\\VEN_XXXX&DEV_YYYY&`, anche se dopo la e commerciale compariva un campo sconosciuto o malformato. Il parser PnP principale lo rifiutava, ma il veto secondario poteva nascondere il supporto per una vera NVIDIA RTX/Killer/Intel attestata da un altro ID compatibile validato. Ora il gate per vendor PCI utilizza la **stessa grammatica di modello pubblico completo** di `MicrosoftUpdateCatalogSearch.TryGetPublicModelKey` già condivisa con il riconoscimento PnP: i soli suffix SUBSYS/REV/CC validi preservano la precedenza del vendor primario, gli altri non attribuiscono autorità.

I test distinguono il recupero della consultazione NVIDIA/Intel/Killer da falsi conflitti PCI e preservano il veto di un modello Intel/Realtek realmente validato e di due modelli primari discordanti. Non viene creato un'offerta di aggiornamento né modificato alcun ID WUA; prove e numero di driver realmente disponibili su Recoil 16 restano #91/#99/#136.
