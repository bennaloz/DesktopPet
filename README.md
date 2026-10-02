# Zaira Desktop Pet

Un gatto 3D che vive sul desktop di Windows: cammina sulla barra delle applicazioni e sui bordi
delle finestre, ci salta sopra, fa gli zoomies, dorme sul trespolo, mangia dalla ciotola, insegue
i bocconcini, si prende col mouse e si accarezza.

Per ora il gatto è un **segnaposto**: la volpe CC0 di Quaternius, ricolorata di grigio. Il modello
di Zaira si inserisce dopo senza toccare il codice (vedi sotto).

## Avvio

Doppio clic su `run.bat`. Per chiudere: icona nella system tray → **Esci**.

## Stato (notte del 24/09)

**Verificato** (test automatici e autotest dal vivo, con finestre vere):
- 59 test sulla logica: superfici e occlusioni, salti, arrampicata, percorsi, bisogni, comportamenti,
  30 minuti simulati con finestre che si spostano/chiudono;
- dal vivo: sale su una finestra (saltando o arrampicandosi), la segue quando si sposta, cade quando si chiude;
  mangia, dorme sul trespolo, zoomies, bocconcino; presa, lancio, carezze e doppio clic (eventi iniettati);
- la regione della finestra (ciò che si vede e si clicca) contiene gatto, ciotola e trespolo e lascia fuori il resto;
- consumi: ~10% di un core, ~280 MB RAM, GPU trascurabile.

**Non verificabile stanotte** (sessione Windows bloccata: niente mouse vero, niente cattura dello schermo):
- che a schermo si veda davvero solo il gatto sul desktop (trasparenza reale) e che i clic fuori dal gatto
  arrivino alle finestre sotto;
- l'icona nella system tray e il suo menu.

Primi 2 minuti di prova, se qualcosa non va: `%APPDATA%\Godotpp_userdata\Zaira Desktop Pet\zaira.log`.

## Da fare

- **Salto**: la pancia penzola ancora in volo (le cosce tirano giu' la pelle della pancia: pesi o posa).
  Ora c'e' l'osso `Belly` (tools/blender/belly.py): nelle clip di volo si puo' tirare su la pancia.
- **Retro delle cosce**: con la coda nuova (alta) si vedono lembi chiari sul retro delle cosce che prima la coda
  pendente copriva. Da tre quarti verso chi guarda non si vedono; solo girandola in braccio.
- **Rialzarsi dalla pagnotta**: manca la transizione inversa (pagnotta -> accucciata -> seduta), per ora e'
  una dissolvenza. Fatte (28/09): seduta -> accucciata a sfinge (2 s, zampe davanti una alla volta) ->
  pagnotta (1,6 s, zampe ritirate una alla volta), coda sempre dal lato di chi guarda.
  Provato e scartato (28/09) il modello Tripo gia' in posa al posto del rig: lo scambio fra due modelli fa
  l'effetto Transformer e il collo del Tripo si deforma. Le pose si fanno col rig, i GLB Tripo servono solo
  da riferimento.
- **Acciambellata**: da rivedere con lo stesso metodo (riferimento Tripo, pancia a terra, tempi di gatto).
- **"z" del sonno troppo in alto**: l'altezza viene dalla taglia del gatto in piedi (`SizePx.Y` in `Main`),
  la posa acciambellata e' molto piu' bassa.
- **Autotest del giro**: il controllo "mangia con il muso sopra la ciotola" a volte non scatta (serve che
  mangi piu' di 0,8 s, e "restless" porta subito la fame a 0,1): aspettare `Eat` con `Until` come per
  zoomies e sonno.
- **Pipeline Blender**: `rig.blend` non si rigenera piu' dal solo `rig.py` (la coda e' stata rifatta dopo);
  gli script che lo modificano (`front_toes.py`, `belly.py`, `scapula.py`, `chest_in.py`) vanno lanciati in
  ordine, e sono idempotenti. Il `rig.blend` buono e' in `assets/zaira/work/` (ignorata da git): farne una
  copia prima di esperimenti.

## Gli altri animali (29/09)

Oltre a Zaira ci sono **Bretzel**, il coniglio ariete di Valentina, e **Sally**, la golden retriever della
sorella. Uno alla volta: dal menu dell'icona, **Animale** (il programma riparte con quello scelto e se lo
ricorda, `pet.txt` nella cartella utente), oppure all'avvio:

    run.bat -- --cat=bretzel
    run.bat -- --cat=sally

### Eseguibile da regalare (02/10)

    python tools/build_release.py --pets bretzel,sally,zaira --default bretzel --name DesktopPet

Fa `export/DesktopPet.zip` (la cartella con `DesktopPet.exe` e i file .NET accanto: va scompattata tutta).
`--pets` sceglie gli animali dentro, `--default` quello del primo avvio. Servono i template di export di
Godot 4.7.2 mono (in `%APPDATA%\Godot\export_templates\4.7.2.stable.mono`). I GLB degli animali vanno nel
pacchetto cosi' come sono (importer `keep`): il gioco li carica a runtime con `GltfDocument`.

Ogni animale ha la sua cartella `cats/<nome>/` (modello + `profile.json`, con `species`: `cat`, `rabbit`, `dog`)
e il suo salvataggio (`save-<nome>.json`; il vecchio `save.json` resta di Zaira). Il cervello e' diviso:
`PetBrain` (comune: bisogni, braccio, volo, coccole, ciotola, bocconcini, viaggio) e una classe per specie
(`CatBrain`, `BunnyBrain`, `DogBrain`), cosi' possono divergere o in futuro stare insieme sullo schermo.

- **Bretzel**: solo sul pavimento. Saltello lento da coniglio domestico (anteriori una alla volta, poi i
  posteriori insieme) e corsa a mezzo balzo (si allunga in volo, atterra sulle anteriori, i posteriori
  arrivano ai lati e davanti), dai dati di letteratura. Pagnotta, all'erta, lavata al muso, binky nelle
  corse pazze, flop sul fianco se si addormenta contento, thump quando la pappa manca. Orecchie a molla
  (`EarModifier`). Pipeline: `tools/blender/bretzel_rig.py` → `bretzel_anim.py` → `bretzel_export.py`.
- **Sally**: stesso scheletro di Zaira, quindi le sue clip (`PET=sally` davanti a `anim.py` ed
  `export.py`; il rig viene da `sally_rig.py`, che ricolora anche il pelo grigio della texture). Passo,
  trotto contento, galoppo nelle corse pazze, solo sul pavimento; seduto, poi sdraiato a sfinge; abbaia per
  la pappa; la coda scodinzola (stile `dog`) anche da fermo e alle coccole. Il tiragraffi c'e' solo per Zaira.

Da fare per loro:
- Bretzel: le orecchie possono solo dondolare poco (nella mesh sono il fianco della testa); la "z" del sonno
  e' tarata sull'altezza in piedi. La pancia del modello e' scura e ruvida (la foto non la vedeva): le pose
  che la mostrano (seduto dritto, flop visto dal davanti) vanno evitate. Pelle della meta' posteriore pesata
  per geometria in `bretzel_rig.py` (30/09); resta una piega sulla spalla nell'atterraggio della corsa.
- Sally: seduta, sdraiata (bacino su un fianco), galoppo e caduta da cane sotto `PET=sally` in `anim.py`
  (30/09); passo, trotto, salto e le altre clip sono ancora quelle di Zaira. Un lembo sul retro della coscia
  al galoppo.

## Revisione delle animazioni (30/09)

Doppio clic su `review.bat`: apre nel browser l'editor di revisione (`tools/review/`, server locale su
`127.0.0.1:8765`). Si sceglie l'animale e la clip, si guarda a velocità piena o rallentata, col pavimento che
scorre alla velocità del gioco e la scia dei piedi (un piede appoggiato deve restare fermo sui quadretti). In pausa
si correggono zampe (trascinando il pallino del piede), testa, collo, schiena, pancia, bacino, coda e orecchie;
**Salva feedback** scrive posa corretta, istante, nota e due screenshot in `review/<animale>/<clip>/`, e ogni clip
si segna approvata o da rifare (`review/<animale>/stato.json`). Le clip cambiate da un nuovo export tornano "da
vedere" col bollino **nuova**. Gli screenshot restano fuori da git. Test: `python -m unittest
tools/review/test_serve.py` e `node --test tools/review/lib.test.mjs`.

## Cosa provare

| Azione | Come |
|---|---|
| Prenderla in braccio | premi sul gatto e trascina; muovendo il mouse (o la rotella) la fai girare; rilascia per lasciarla cadere, anche lanciandola |
| Accarezzarla | passa il mouse avanti e indietro sopra il gatto senza premere: si ferma e fa le fusa (♥) |
| Darle da mangiare | doppio clic sulla ciotola rossa per riempirla; quando ha fame ci va da sola. Se la ciotola è vuota si siede lì e reclama (!) |
| Bocconcino | tray → **Lancia un bocconcino**: cade dall'alto, lei corre a prenderlo |
| Spostare ciotola e trespolo | trascinali; ricadono sulla superficie sotto (anche sopra una finestra) |
| Salti sulle finestre | nessuna azione: quando esplora salta sui bordi superiori delle finestre visibili (non su quelle massimizzate). Se sposti la finestra lei la segue, se la chiudi o la minimizzi cade |
| Arrampicata | se il bordo è troppo alto per un salto (oltre ~460 px) si aggrappa al lato della finestra, sale e ci monta sopra. Anche mentre sale, se sposti la finestra la segue e se la chiudi cade |
| Zoomies | quando la voglia di giocare è al massimo corre avanti e indietro e salta dove capita |
| Dormire | quando è stanca va sul trespolo e dorme acciambellata, girata col muso verso di te (z) |
| Stato | passa il mouse sull'icona della tray: fame, energia, voglia di giocare, contentezza, ciotola |
| Se sparisce | tray → **Chiama Zaira** |
| Pausa | tray → **Pausa** |

I bisogni cambiano in tempo reale (fame piena in circa 2 ore, stanca dopo circa 2 ore sveglia) e vengono
salvati in `%APPDATA%\Godot\app_userdata\Zaira Desktop Pet\save.json`. Il log è nello stesso posto (`zaira.log`).

## Mettere Zaira al posto della volpe

1. Crea `cats/zaira/` con il GLB riggato (es. `zaira.glb`) e un `profile.json` copiato da `cats/fox/profile.json`.
2. In `profile.json` imposta `model`, `length_px` (lunghezza a schermo) e, per ogni azione, i nomi
   delle animazioni presenti nel GLB (`idle, walk, run, jump, fall, land, sit, sleep, eat, meow, purr, held`).
   Le azioni mancanti ricadono su una simile (es. `sleep` → `sit`).
3. Se il modello guarda di lato o all'indietro, correggi `yaw_offset_deg` (90 / 180 / -90).
4. `material_colors` serve solo alla volpe: per Zaira, con la texture di Tripo, va tolto.

La cartella `cats/zaira` ha la precedenza sulla volpe; per forzarne una: `run.bat -- --cat=fox`.

## Autotest

```
run.bat -- --selftest            giro dei comportamenti con screenshot
run.bat -- --selftest-windows    serve tools/test-window.ps1 in parallelo: sale su una finestra, la segue, cade
                                 (finestra alta, da scalare: tools/test-window.ps1 -Top 200 -Height 520 -MoveAt 22 -CloseAt 40)
run.bat -- --selftest-input      presa, lancio, carezze, doppio clic (eventi iniettati in Godot)
run.bat -- --selftest-mouse      mouse vero: in parallelo tools/test-mouse.ps1 (sessione sbloccata!)
dotnet test tests/Core.Tests     test della logica (superfici, fisica, percorsi, comportamenti)
```

Screenshot e log finiscono in `%APPDATA%\Godot\app_userdata\Zaira Desktop Pet\`.

## Struttura

- `src/Core` — logica pura senza Godot, testata: mappa delle superfici, fisica, percorsi, bisogni, comportamenti, salvataggio.
- `src/Platform` — Win32: finestre visibili, monitor, stile della finestra overlay.
- `src/Game` — Godot: overlay trasparente, gatto 3D, props, mouse, tray, autotest.
- `docs/superpowers` — spec e piano.
