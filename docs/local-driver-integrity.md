# Discovery+ — integrità locale INF/CAT (incremento #49)

OmegaDrive offre un controllo **in sola lettura** sui file `.inf` scelti manualmente. Questa fase **NON valida il pacchetto driver nella sua interezza e NON autorizza installazioni OEM**.

## Cosa viene verificato

1. **SHA-256 dell'INF:** calcolato dai byte del file con `System.Security.Cryptography.SHA256`. Un hash calcolato senza riferimento **non prova che un file sia autentico**.
2. **Confronto SHA-256:** se l'utente inserisce 64 cifre esadecimali del digest atteso **ottenuto da un canale ufficiale attendibile**, OmegaDrive confronta i byte con `CryptographicOperations.FixedTimeEquals`. Stati: corrisponde, non corrisponde, atteso assente, atteso malformato. Non scarica automaticamente digest da siti terzi.
3. **SHA-256 del CAT locale**, se la direttiva `CatalogFile` nell'INF indica un nome semplice `.cat` nella stessa cartella. È sempre presentato come impronta **senza digest esterno atteso**.
4. **Firma Authenticode del CAT:** su richiesta manuale dell'utente, il desktop esegue un comando **Windows PowerShell integrato nel sistema**: `Get-AuthenticodeSignature -LiteralPath`. Lo script è costante, il percorso entra solo tramite variabile d'ambiente, senza concatenazione di comandi. La risposta codificata come JSON viene verificata, limitata e interpretata con fallimento prudente. Mostra `Valid`, non firmato, non attendibile o non verificabile secondo la risposta del sistema operativo.

### Limiti di sicurezza IMPORTANTI

- `Valid` **riguarda la firma Authenticode del CAT**, **non** prova che l'INF o i file `.sys` appartengano a quel CAT. Un CAT perfettamente firmato ma estraneo a un INF non rende sicuro l'INF.
- Non viene verificata la firma per ogni componente, l'intera catena di distribuzione OEM, le revoche offline secondo policy, tutti gli hash dei file o la policy Windows di firma driver kernel.
- Il controllo di architettura/TargetOSVersion di #44 continua a essere preliminare e non sostituisce SetupAPI.
- Un hash atteso copiato da una fonte non attendibile **non costituisce prova di provenienza**.
- Le operazioni sono locali; non si inviano seriali, file INF o hash a server terzi. Nessuna installazione o importazione PnP viene avviata.
- Limiti: INF 1 MiB, CAT 32 MiB, output firma 16 KiB, timeout 25 secondi. Il processo Windows PowerShell non scarica moduli.

**Per validazione reale del driver e per eventuali future installazioni OEM**, completare la Issue #46: verifica dell'appartenenza di ogni INF/SYS al CAT attraverso API native Windows, firma/catena, compatibilità e payload completo. Non abilitare installazioni OEM prima di una security review. Test runtime Windows 11 in #5.

Riferimento: [Get-AuthenticodeSignature (Microsoft)](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.security/get-authenticodesignature).

Issue #49; parent #46.
