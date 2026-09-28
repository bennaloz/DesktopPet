# Pipeline Blender di Zaira

Blender 5.2 portable in `C:\develop\personal\tools\`. Sorgente: `assets/zaira/tripo.glb`, il GLB
esportato da Tripo (lo scheletro di Tripo viene buttato); `assets/zaira/multiview/` sono le viste da cui
Tripo l'ha generato. Intermedi e anteprime finiscono in `assets/zaira/work/`, ignorata da git.
I percorsi sono relativi al repo (`paths.py`), gli script si lanciano da qualunque cartella.

1. `prep.py` – importa, raddrizza (muso verso -Y), centra, piedi a z=0 → `work/mesh.blend`
2. `rig.py` – decimazione a ~25k facce, scheletro da gatto, pesi via proxy voxel,
   pesi della coda procedurali, stacco delle facce che incollavano coda e sedere → `work/rig.blend`
   `front_toes.py` – divide la mano delle zampe davanti in metacarpo e dita (`Finger.L/R`): sdraiata le dita
   restano piatte a terra invece di puntare in alto. Lavora sul `rig.blend` esistente (la coda e' stata
   rifatta dopo `rig.py`), una volta sola.
   `belly.py` – osso `Belly` sotto la schiena che porta la pelle del ventre: nelle pose a terra la pancia
   scende sul pavimento e si allarga (senza, resta sollevata dove la porta il gatto in piedi). Stesso uso.
   `scapula.py` – scapole (`Scapula.L/R`) fra petto e braccio: nel passo e nella corsa ruotano con la zampa e
   portano la spalla avanti e indietro. Stesso uso. Ordine: `rig.py`, poi `front_toes.py`, `belly.py`,
   `scapula.py` (ognuno salta se l'ha gia' fatto).
3. `anim.py` – animazioni procedurali (IK planare per le zampe) → `work/anim.blend`. Le clip di riposo
   (seduta, sdraiarsi, accucciata, ritirare le zampe, pagnotta) escono in due versioni, `X` con la coda
   avvolta a sinistra e `X_R` a destra: il gioco tiene la coda dal lato di chi guarda (`anims_right`).
4. `export.py` – texture a 2K, GLB con tutte le azioni → `cats/zaira/zaira.glb`

Controllo visivo: `sheet.py` + `tile.py` (foglio di fotogrammi per ogni animazione).

    blender.exe --background --python rig.py
