# Pipeline Blender di Zaira

Blender 5.2 portable in `C:\develop\personal\tools\`. Sorgente: `assets/zaira/tripo.glb`, il GLB
esportato da Tripo (lo scheletro di Tripo viene buttato); `assets/zaira/multiview/` sono le viste da cui
Tripo l'ha generato. Intermedi e anteprime finiscono in `assets/zaira/work/`, ignorata da git.
I percorsi sono relativi al repo (`paths.py`), gli script si lanciano da qualunque cartella.

1. `prep.py` – importa, raddrizza (muso verso -Y), centra, piedi a z=0 → `work/mesh.blend`
2. `rig.py` – decimazione a ~25k facce, scheletro da gatto, pesi via proxy voxel,
   pesi della coda procedurali, stacco delle facce che incollavano coda e sedere → `work/rig.blend`
3. `anim.py` – animazioni procedurali (IK planare per le zampe) → `work/anim.blend`
4. `export.py` – texture a 2K, GLB con tutte le azioni → `cats/zaira/zaira.glb`

Controllo visivo: `sheet.py` + `tile.py` (foglio di fotogrammi per ogni animazione).

    blender.exe --background --python rig.py
