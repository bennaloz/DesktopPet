// The phone page: the pet's brain (src/Core, compiled to WebAssembly in web/brain) runs the room, three.js draws it.
// The room is the screen, scaled so it is at least ROOM_WIDTH wide: on a phone in portrait the pets come out smaller
// than on the desktop but behave the same (speeds, jumps and sizes are the desktop's, in room units).
import * as THREE from "three";
import { dotnet } from "./_framework/dotnet.js";
import { Pet } from "./pet.js";
import * as P from "./props.js";

const ROOM_WIDTH = 480;        // the narrowest room, in units (CSS px on a screen at least this wide)
const BAR = 64;                // CSS px kept free at the bottom for the buttons
const SAVE_EVERY = 10;         // seconds

const $ = (id) => document.getElementById(id);
const store = {
  get: (k) => { try { return localStorage.getItem("pet:" + k); } catch { return null; } },
  set: (k, v) => { try { localStorage.setItem("pet:" + k, v); } catch { /* private mode: nothing kept */ } },
};

// ---------------------------------------------------------------- the room's size
let scale = 1, W = 0, H = 0;
function measure() {
  const w = window.innerWidth, h = window.innerHeight;
  scale = Math.min(1, w / ROOM_WIDTH);
  W = w / scale; H = (h - BAR) / scale;       // the floor is at H, just above the buttons
}

// ---------------------------------------------------------------- three
const renderer = new THREE.WebGLRenderer({ canvas: $("view"), antialias: true, alpha: true });
renderer.setPixelRatio(Math.min(2, window.devicePixelRatio));
renderer.localClippingEnabled = true;
renderer.outputColorSpace = THREE.SRGBColorSpace;
const scene = new THREE.Scene();
scene.add(new THREE.HemisphereLight(0xffffff, 0x8a7f72, 2.0));
const sun = new THREE.DirectionalLight(0xffffff, 1.6);
sun.position.set(300, 700, 600);
scene.add(sun);
const camera = new THREE.OrthographicCamera(0, 1, 0, -1, 1, 4000);
camera.position.set(0, 0, 1500);

function fitView() {
  measure();
  renderer.setSize(window.innerWidth, window.innerHeight, false);
  camera.left = 0; camera.right = W; camera.top = 0; camera.bottom = -(H + BAR / scale);
  camera.updateProjectionMatrix();
  floor.position.set(W / 2, -H, -300);
  floor.scale.set(W + 40, 1, 1);
}
const floor = new THREE.Mesh(new THREE.BoxGeometry(1, 8, 600), new THREE.MeshStandardMaterial({ color: 0xc9a77c, roughness: 0.9 }));
scene.add(floor);

// ---------------------------------------------------------------- start
async function start() {
  const pets = await (await fetch("pets.json")).json();     // [{id, name}], first = the build's default
  const asked = new URLSearchParams(location.search).get("pet");
  const id = pets.some((p) => p.id === asked) ? asked : pets.some((p) => p.id === store.get("last")) ? store.get("last") : pets[0].id;
  store.set("last", id);
  makeMenu(pets, id);

  const profile = await (await fetch(`cats/${id}/profile.json`)).json();
  $("status").textContent = "sveglio il cervello…";
  const rt = await dotnet.create();
  const api = (await rt.getAssemblyExports(rt.getConfig().mainAssemblyName)).ZairaPet.Web.Api;

  $("status").textContent = `arriva ${profile.name || id}…`;
  const pet = new Pet(profile);
  await pet.load(`cats/${id}/${profile.model}`);
  scene.add(pet.root);

  measure();
  api.Init(profile.species || "cat", W, H, profile.length_px, store.get("save:" + id) || "");
  api.SetPetSize(pet.size.w, pet.size.h);

  const bowl = P.bowl(profile.species === "rabbit");
  const perch = P.perch();
  const shelf = P.shelf();
  let treat = null;
  scene.add(bowl, shelf);
  const isCat = (profile.species || "cat") === "cat";
  if (isCat) scene.add(perch);
  fitView();

  window.addEventListener("resize", () => { fitView(); api.Resize(W, H); });
  setupTouch(api, pet);
  $("fill").onclick = () => api.FillBowl();
  $("treat").onclick = () => api.ThrowTreat(W * (0.25 + Math.random() * 0.5));
  const save = () => store.set("save:" + id, api.Save());
  document.addEventListener("visibilitychange", () => { if (document.hidden) save(); });
  $("status").textContent = "";

  let last = performance.now(), saveIn = SAVE_EVERY;
  function frame(now) {
    const dt = Math.min(0.1, (now - last) / 1000);
    last = now;
    api.Tick(dt);
    const s = api.State();
    pet.play(api.Action(), s[5]);
    pet.animate(dt, s);
    bowl.position.set(s[6], -s[7], 30);
    bowl.userData.food.visible = s[8] > 0.02;
    if (isCat) perch.position.set(s[9], -s[10], -60);
    if (Number.isNaN(s[11])) { if (treat) { scene.remove(treat); treat = null; } }
    else { if (!treat) { treat = P.treat(); scene.add(treat); } treat.position.set(s[11], -s[12], 40); }
    shelf.userData.place(s[15], s[16], s[17], s[18]);
    showEmote(api.Emote(), s, pet);
    renderer.render(scene, camera);
    if ((saveIn -= dt) <= 0) { saveIn = SAVE_EVERY; save(); }
    requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);
}

function showEmote(text, s, pet) {
  const e = $("emote");
  if (e.textContent !== text) e.textContent = text;
  e.style.display = text ? "block" : "none";
  if (!text) return;
  e.style.left = `${s[0] * scale}px`;
  e.style.top = `${(s[1] - pet.size.h - 24) * scale}px`;
}

// ---------------------------------------------------------------- touch
// A finger on the pet: stroking it sideways pets it, lifting it up (or holding still a moment) picks it up by the
// scruff, and letting go throws it gently. A finger on the bowl or the perch moves it; a double tap on the bowl fills it.
function setupTouch(api, pet) {
  const view = $("view");
  let down = null;   // {id, x, y, t, mode: "maybe" | "drag", grabbed}
  let lastTap = 0;
  const room = (e) => ({ x: e.clientX / scale, y: e.clientY / scale });

  view.addEventListener("pointerdown", (e) => {
    view.setPointerCapture(e.pointerId);
    const p = room(e);
    const s = api.State();
    const onPet = Math.abs(p.x - s[0]) < pet.size.w / 2 && p.y < s[1] + 8 && p.y > s[1] - pet.size.h;
    down = { id: e.pointerId, x: p.x, y: p.y, t: performance.now(), mode: onPet ? "maybe" : "drag", grabbed: 0 };
    if (!onPet) {
      down.grabbed = api.Press(p.x, p.y);
      if (down.grabbed === 2) {
        if (performance.now() - lastTap < 350) api.FillBowl();
        lastTap = performance.now();
      }
    }
  });
  view.addEventListener("pointermove", (e) => {
    if (!down || e.pointerId !== down.id) return;
    const p = room(e);
    if (down.mode === "maybe") {
      const dx = p.x - down.x, dy = p.y - down.y;
      if (dy < -22) {   // lifted
        down.mode = "drag";
        down.grabbed = api.Press(down.x, down.y);
        pet.heldSpin = (api.State()[5] || 1) * 65;
      } else if (Math.abs(dx) > 4) {
        api.Stroke(p.x, p.y, Math.hypot(dx, dy));
        down.x = p.x; down.y = p.y;
      }
      return;
    }
    if (down.grabbed) {
      const dt = Math.max(1 / 120, (performance.now() - down.t) / 1000);
      down.t = performance.now();
      if (down.grabbed === 1) pet.heldSpin += (p.x - down.x) * 0.6;
      down.x = p.x; down.y = p.y;
      api.Move(p.x, p.y, dt);
    }
  });
  const up = (e) => {
    if (!down || e.pointerId !== down.id) return;
    if (down.mode === "maybe" && performance.now() - down.t < 300) api.Stroke(down.x, down.y, 12);   // a tap: a pat
    if (down.grabbed) api.Release();
    down = null;
  };
  view.addEventListener("pointerup", up);
  view.addEventListener("pointercancel", up);
  // holding the finger still on the pet picks it up too
  setInterval(() => {
    if (down && down.mode === "maybe" && performance.now() - down.t > 450) {
      down.mode = "drag";
      down.grabbed = api.Press(down.x, down.y);
      pet.heldSpin = (api.State()[5] || 1) * 65;
    }
  }, 100);
}

// ---------------------------------------------------------------- the pet menu
function makeMenu(pets, id) {
  const sel = $("pet");
  for (const p of pets) {
    const o = document.createElement("option");
    o.value = p.id; o.textContent = p.name;
    sel.appendChild(o);
  }
  sel.value = id;
  sel.onchange = () => { store.set("last", sel.value); location.search = "?pet=" + sel.value; };
}

if ("serviceWorker" in navigator) navigator.serviceWorker.register("sw.js").catch(() => {});
start().catch((e) => { $("status").textContent = "qualcosa non va: " + e.message; console.error(e); });
