# OmegaDrive — indizi WUA con variante HID/USBSTOR (#341)

Quando WMI/SetupAPI fornisce l'Hardware ID specifico di un dispositivo
HID, per esempio `HID\VID_046D&PID_C534&MI_00&COL01`, e una voce
WUA Type=Driver dichiara soltanto `HID\VID_046D&PID_C534`, i due
identificatori **non sono uguali**. Se Windows non espone un
CompatibleID uguale, il motore di confronto esatto non li collega.

Lo stesso accade con i modelli storage USB strutturati, per esempio
`USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer_Blade&Rev_1.00` rispetto
a `USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer_Blade`.

Il nuovo percorso genera **solo chiavi diagnostiche** a partire dagli
Hardware ID locali già verificati dalle policy HID/USBSTOR: per HID
rimuove gli attributi specifici `REV`, `MI`, `IG`, `COL` ma
mantiene VID+PID; per USBSTOR rimuove unicamente il suffisso
strutturato `&Rev_` mantenendo tipo+vendor+product. La stringa WUA
deve corrispondere **esattamente** al modello base validato. Non
utilizziamo nomi marketing, valori vendor-only, classi generiche,
instance path, ID di altri modelli o cataloghi automatici.

Solo WUA Type=Driver con GUID valido e fonte ufficiale può produrre
un'annotazione `ModelOnlyReview`, indicizzata anche nel report
aggregato WUA→PnP. Non incrementa la lista `Ordinary`, non abilita
`Aggiorna tutto`, non modifica download, staging o installazione.

Se Windows presenta già quel modello tra i Compatible ID veri, il
matching esatto conserva la precedenza e segue le policy di
installazione esistenti. La sola somiglianza di modello non prova
sottosistema OEM, versione nuova, ranking Windows, firma CAT o
compatibilità.

Test: HID Logitech con REV/MI/IG/COL, storage SanDisk Rev,
near miss e classi generiche, GUID/fonte/firmware, precedence,
indice WUA e report copiabile anonimo. QA fisica Recoil 16 e
confronto Driver Easy (#91/#136) rimangono da svolgere.
