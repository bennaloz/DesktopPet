// Editor di revisione delle animazioni: guarda le clip dei GLB del gioco, metti in pausa, correggi la posa, salva
// il feedback per Claude. Spec: docs/superpowers/specs/2026-09-30-review-editor-design.md
import * as THREE from "three";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";
import { OrbitControls } from "three/addons/controls/OrbitControls.js";
import { TransformControls } from "three/addons/controls/TransformControls.js";
import * as SkeletonUtils from "three/addons/utils/SkeletonUtils.js";
import * as L from "./lib.js";

const $ = (id) => document.getElementById(id);
const DEG = Math.PI / 180;
const FPS = 30;
const SQUARE = 0.1;                       // floor squares, in body lengths
const TRAIL_SECONDS = 0.6;
const LEG_COLORS = { hindL: 0xe11d48, hindR: 0xf59e0b, foreL: 0x2563eb, foreR: 0x10b981 };

// ------------------------------------------------------------------ scene

const box = $("canvasBox");
const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
box.appendChild(renderer.domElement);
const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(22, 1, 0.01, 100);
const orbit = new OrbitControls(camera, renderer.domElement);
orbit.enableDamping = false;
scene.add(new THREE.HemisphereLight(0xffffff, 0x8a7f72, 2.0));
const sun = new THREE.DirectionalLight(0xffffff, 1.8);
sun.position.set(2, 4, 3);
scene.add(sun);

const pivot = new THREE.Group();          // turns the pet to the view; carries floor, trail and paw handles
scene.add(pivot);
const holder = new THREE.Group();         // model scaled to 1 body length, facing +Z, feet on y = 0
pivot.add(holder);

const floor = makeFloor();
pivot.add(floor);
const trail = makeTrail();
pivot.add(trail.points);

const tc = new TransformControls(camera, renderer.domElement);
tc.setSize(0.8);
scene.add(tc.getHelper());

new ResizeObserver(() => {
  const w = box.clientWidth, h = box.clientHeight;
  renderer.setSize(w, h, false);
  camera.aspect = w / Math.max(1, h);
  camera.updateProjectionMatrix();
  if (S.model) setView(S.view);
}).observe(box);

function makeFloor() {
  const c = document.createElement("canvas");
  c.width = c.height = 64;
  const g = c.getContext("2d");
  g.fillStyle = "#d9d3ca"; g.fillRect(0, 0, 64, 64);
  g.fillStyle = "#b9b0a3"; g.fillRect(0, 0, 32, 32); g.fillRect(32, 32, 32, 32);
  const tex = new THREE.CanvasTexture(c);
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.colorSpace = THREE.SRGBColorSpace;
  const size = 8;
  tex.repeat.set(size / (2 * SQUARE), size / (2 * SQUARE));
  tex.anisotropy = 8;
  // see-through: clips played in the air in the game (falling, jumping, held) reach below the feet's floor
  const m = new THREE.Mesh(new THREE.PlaneGeometry(size, size),
    new THREE.MeshBasicMaterial({ map: tex, transparent: true, opacity: 0.6, depthWrite: false }));
  m.rotation.x = -Math.PI / 2;
  m.position.y = -0.001;
  return m;
}

function makeTrail() {
  const max = 4 * 400;
  const geo = new THREE.BufferGeometry();
  geo.setAttribute("position", new THREE.BufferAttribute(new Float32Array(max * 3), 3));
  geo.setAttribute("color", new THREE.BufferAttribute(new Float32Array(max * 3), 3));
  geo.setDrawRange(0, 0);
  const points = new THREE.Points(geo, new THREE.PointsMaterial({
    size: 5, sizeAttenuation: false, vertexColors: true, depthTest: false,
  }));
  points.renderOrder = 10;
  points.frustumCulled = false;
  return { points, max, items: [] };     // items: {x, y, z (floor frame), color, at (clip clock)}
}

// ------------------------------------------------------------------ state

const loader = new GLTFLoader();
const S = {
  pets: [], pet: null, profile: null,
  model: null, ghost: null, meshes: [], bones: new Map(), ghostBones: new Map(), rest: new Map(),
  legs: [], markers: new Map(), pinned: new Set(),
  clips: [], clip: null, sampler: [], ghostSampler: [],
  fingerprints: {}, state: {}, feedback: [],
  t: 0, playing: false, rate: 1, dist: 0, clock: 0,
  facing: 1, view: "desktop", height: 0.5, fwd: new THREE.Vector3(0, 0, 1),
  selected: null, edited: false, dirty: false, undo: [],
};

window.review = S;   // for poking at it from the browser console

const boneOf = (name) => S.bones.get(name) ?? S.bones.get(name.replace(/\./g, ""));
const ghostBoneOf = (name) => S.ghostBones.get(name) ?? S.ghostBones.get(name.replace(/\./g, ""));

// ------------------------------------------------------------------ loading

async function start() {
  S.pets = await (await fetch("/api/pets")).json();
  const sel = $("pet");
  for (const p of S.pets) sel.add(new Option(p.name ?? p.id, p.id));
  const remembered = load("pet");
  sel.value = S.pets.some((p) => p.id === remembered) ? remembered : S.pets[0]?.id;
  sel.onchange = () => { if (confirmDiscard()) openPet(sel.value); else sel.value = S.pet.id; };
  await openPet(sel.value);
}

async function openPet(id) {
  const pet = S.pets.find((p) => p.id === id);
  status(`carico ${pet.name}…`);
  save("pet", id);
  $("pet").value = id;
  const gltf = await loader.loadAsync(`/cats/${id}/${pet.model}?v=${Date.now()}`);
  dropModel();
  S.pet = pet;
  S.profile = pet;
  S.model = gltf.scene;
  S.clips = gltf.animations;
  holder.add(S.model);
  pivot.rotation.set(0, 0, 0);
  S.bones = collectBones(S.model);
  for (const [name, b] of S.bones) S.rest.set(name, [b.position.clone(), b.quaternion.clone(), b.scale.clone()]);
  S.meshes = [];
  S.model.traverse((o) => { if (o.isSkinnedMesh) { S.meshes.push(o); o.frustumCulled = false; } });
  fitModel();
  makeGhost();
  S.legs = L.legChains([...S.bones.keys()]);
  makeMarkers();
  S.fingerprints = {};
  for (const c of S.clips) S.fingerprints[c.name] = L.clipFingerprint(c.tracks);
  S.skin = L.skinFingerprint(S.meshes.map((m) => ({
    index: m.geometry.attributes.skinIndex.array, weight: m.geometry.attributes.skinWeight.array })));
  await refreshReview();
  const names = L.orderClips(S.clips.map((c) => c.name), S.profile);
  const last = load(`clip:${id}`);
  openClip(names.includes(last) ? last : names[0]);
}

function collectBones(root) {
  const map = new Map();
  root.traverse((o) => { if (o.isBone) map.set(o.userData.name ?? o.name, o); });
  return map;
}

/** Like Godot's FitToLength: the longer of X/Z becomes 1, feet on the floor; plus facing +Z. */
function fitModel() {
  holder.position.set(0, 0, 0);
  holder.rotation.set(0, 0, 0);
  holder.scale.setScalar(1);
  holder.updateMatrixWorld(true);
  const head = (boneOf("Head") ?? boneOf("Neck")).getWorldPosition(new THREE.Vector3());
  const hips = (boneOf("Hips") ?? S.model).getWorldPosition(new THREE.Vector3());
  const f = head.sub(hips);
  holder.rotation.y = -Math.atan2(f.x, f.z);
  // forward in the GLB's own frame (for the feedback's metres)
  S.fwd.set(f.x, 0, f.z).normalize();
  holder.updateMatrixWorld(true);
  let b = new THREE.Box3().setFromObject(S.model, true);
  const size = b.getSize(new THREE.Vector3());
  holder.scale.setScalar(1 / Math.max(size.x, size.z));
  holder.updateMatrixWorld(true);
  b = new THREE.Box3().setFromObject(S.model, true);
  const c = b.getCenter(new THREE.Vector3());
  holder.position.set(-c.x, -b.min.y, -c.z);
  holder.updateMatrixWorld(true);
  S.height = b.max.y - b.min.y;
}

function makeGhost() {
  S.ghost = SkeletonUtils.clone(S.model);
  S.ghost.traverse((o) => {
    if (!o.isMesh) return;
    o.frustumCulled = false;
    o.material = (Array.isArray(o.material) ? o.material : [o.material]).map((m) => {
      // pushed back in depth, so where it matches the pose it hides behind the real body and only the parts that
      // moved show as a pale-blue outline of where they were
      const g = new THREE.MeshBasicMaterial({
        color: 0x7cc4ff, transparent: true, opacity: 0.35, depthWrite: false,
        polygonOffset: true, polygonOffsetFactor: 4, polygonOffsetUnits: 40,
      });
      g.name = m.name;
      return g;
    });
    if (o.material.length === 1) o.material = o.material[0];
    o.renderOrder = 5;
  });
  S.ghost.visible = false;
  holder.add(S.ghost);
  S.ghostBones = collectBones(S.ghost);
}

function makeMarkers() {
  for (const m of S.markers.values()) pivot.remove(m);
  S.markers.clear();
  for (const leg of S.legs) {
    const m = new THREE.Mesh(new THREE.SphereGeometry(0.014, 16, 12),
      new THREE.MeshBasicMaterial({ color: LEG_COLORS[leg.id] ?? 0xffffff, depthTest: false }));
    m.renderOrder = 20;
    m.userData.leg = leg;
    pivot.add(m);
    S.markers.set(leg.id, m);
  }
}

function dropModel() {
  tc.detach();
  S.selected = null;
  for (const o of [S.model, S.ghost]) {
    if (!o) continue;
    holder.remove(o);
    o.traverse((x) => {
      if (!x.isMesh) return;
      x.geometry.dispose();
      for (const m of [x.material].flat()) { m.map?.dispose(); m.dispose(); }
    });
  }
  S.rest.clear();
  S.clip = null;
  resetEdits();
}

async function refreshReview() {
  const [state, feedback] = await Promise.all([
    fetch(`/api/state/${S.pet.id}`).then((r) => r.json()),
    fetch(`/api/feedback/${S.pet.id}`).then((r) => r.json()),
  ]);
  S.state = state;
  S.feedback = feedback;
  renderClipList();
  renderFeedback();
  renderClipInfo();
}

// ------------------------------------------------------------------ clips and time

function openClip(name) {
  const clip = S.clips.find((c) => c.name === name);
  if (!clip) return;
  S.clip = clip;
  save(`clip:${S.pet.id}`, name);
  S.sampler = makeSampler(S.model, clip);
  S.ghostSampler = makeSampler(S.ghost, clip);
  S.loc = L.locomotion(name, S.profile);
  S.speed = L.clipSpeed(name, S.profile);
  S.facing = L.facingFor(name, S.profile);
  S.t = 0;
  S.dist = 0;
  trail.items = [];
  resetEdits();
  applyTime();
  setView(S.view);
  renderClipList();
  renderClipInfo();
  renderFeedback();
  $("verdictNote").value = S.state[name]?.nota ?? "";
  const loc = S.loc ? ` · nel gioco ${S.loc.refSpeedPx} px/s (${S.loc.action})` : "";
  status(`${S.pet.name} · ${name}${loc}`);
}

/**
 * The clip's tracks, each with its node and interpolant. Sampled by hand rather than with an AnimationMixer: the
 * mixer writes a bone only when its value changed since its last update, so after an edit (or back to rest) at the
 * same time it would leave the bone as it is.
 */
function makeSampler(root, clip) {
  const out = [];
  for (const track of clip.tracks) {
    const { nodeName, propertyName } = THREE.PropertyBinding.parseTrackName(track.name);
    const node = THREE.PropertyBinding.findNode(root, nodeName);
    if (node && ["position", "quaternion", "scale"].includes(propertyName))
      out.push({ node, prop: propertyName, interp: track.createInterpolant() });
  }
  return out;
}

/** Pose everything at S.t: bones back to rest, then the clip on top (bones it does not animate stay at rest). */
function applyTime() {
  for (const [bones, sampler] of [[S.bones, S.sampler], [S.ghostBones, S.ghostSampler]]) {
    for (const [name, b] of bones) {
      const r = S.rest.get(name);
      if (r) { b.position.copy(r[0]); b.quaternion.copy(r[1]); b.scale.copy(r[2]); }
    }
    for (const { node, prop, interp } of sampler) {
      node[prop].fromArray(interp.evaluate(S.t));
      if (prop === "quaternion") node.quaternion.normalize();
    }
  }
  holder.updateMatrixWorld(true);
  updateMarkers();
  renderClock();
}

function moveTime(t) {
  const d = S.clip.duration;
  const next = ((t % d) + d) % d;
  let dt = next - S.t;
  if (S.playing && dt < 0) dt += d;   // wrapped round while playing: the floor keeps going forwards
  if (S.loc && $("floorMoves").checked) S.dist += dt * S.loc.rate;
  S.clock += Math.abs(dt);
  S.t = next;
  applyTime();
}

function setPlaying(on) {
  if (on && !confirmDiscard()) return;
  if (on) resetEdits();
  S.playing = on;
  $("play").textContent = on ? "⏸" : "▶";
  if (on) { tc.detach(); S.selected = null; renderSelection(); }
  updateMarkers();
}

function step(n) {
  if (!confirmDiscard()) return;
  resetEdits();
  setPlaying(false);
  moveTime(L.stepFrame(S.t, n, S.clip.duration, FPS));
}

function renderClock() {
  if (!S.clip) return;
  const d = S.clip.duration;
  const frames = Math.max(1, Math.round(d * FPS));
  $("scrub").value = String(S.t / d);
  $("clock").textContent =
    `${S.t.toFixed(3)} s · fot. ${Math.round(S.t * FPS) % frames + 1}/${frames} · fase ${(S.t / d).toFixed(2)}`;
}

// ------------------------------------------------------------------ views

function setView(view) {
  S.view = view;
  for (const b of document.querySelectorAll("#views button")) b.classList.toggle("on", b.dataset.view === view);
  const f = S.facing;
  const tq = S.profile?.three_quarter_deg ?? 25;
  pivot.rotation.y = f * (view === "desktop" ? 90 - tq : 90) * DEG;
  pivot.updateMatrixWorld(true);
  // far enough to show 1.5 body lengths across and the whole height, whatever the shape of the window
  const tan = Math.tan((camera.fov / 2) * DEG);
  const D = Math.max(0.75 / (tan * camera.aspect), (S.height * 0.5 + 0.12) / tan) * 1.05, y = S.height * 0.5;
  const target = new THREE.Vector3(0, y, 0);
  const pos = {
    desktop: [0, y, D], lato: [0, y, D],
    "tre-quarti": [D * Math.sin(40 * DEG) * Math.cos(18 * DEG), y + D * Math.sin(18 * DEG), D * Math.cos(40 * DEG) * Math.cos(18 * DEG)],
    dietro: [-f * D, y + 0.2, 0.001],
    alto: [0, D, 0.001],
  }[view];
  camera.position.set(...pos);
  orbit.target.copy(target);
  orbit.update();
  updateMarkers();
}

// ------------------------------------------------------------------ editing

function lateralWorld() {
  return new THREE.Vector3(1, 0, 0).applyQuaternion(pivot.getWorldQuaternion(new THREE.Quaternion())).normalize();
}

const v1 = new THREE.Vector3(), v2 = new THREE.Vector3(), v3 = new THREE.Vector3(), v4 = new THREE.Vector3();
const q1 = new THREE.Quaternion();

/** CCD in the body's side plane (every joint turns about the body's side axis): the paw goes to its handle. */
function solveLeg(leg) {
  const chain = leg.bones.map(boneOf);
  const eff = chain[chain.length - 1];
  const links = chain.slice(0, -1);
  const target = S.markers.get(leg.id).getWorldPosition(new THREE.Vector3());
  const axis = lateralWorld();
  // small steps, hip first: every joint takes its share instead of the one next to the paw doing it all
  for (let it = 0; it < 150; it++) {
    if (eff.getWorldPosition(v2).distanceTo(target) < 1e-4) break;
    for (const link of links) {
      const lp = link.getWorldPosition(v1);
      const ep = eff.getWorldPosition(v2).sub(lp).projectOnPlane(axis);
      const tp = v3.copy(target).sub(lp).projectOnPlane(axis);
      if (ep.lengthSq() < 1e-12 || tp.lengthSq() < 1e-12) continue;
      const ang = ep.angleTo(tp);
      if (ang < 1e-5) continue;
      const sign = Math.sign(v4.crossVectors(ep, tp).dot(axis)) || 1;
      const local = axis.clone().applyQuaternion(link.getWorldQuaternion(q1).invert()).normalize();
      link.rotateOnAxis(local, sign * Math.min(ang, 1.5 * DEG));
      link.updateMatrixWorld(true);
    }
  }
}

function effectorOf(leg) {
  return boneOf(leg.bones[leg.bones.length - 1]);
}

function updateMarkers() {
  const show = !S.playing;
  for (const leg of S.legs) {
    const m = S.markers.get(leg.id);
    m.visible = show;
    if (!S.pinned.has(leg.id)) m.position.copy(pivot.worldToLocal(effectorOf(leg).getWorldPosition(v1)));
  }
  S.ghost && (S.ghost.visible = S.edited && $("ghost").checked);
}

function snapshot() {
  return {
    bones: [...S.bones].map(([n, b]) => [n, b.position.clone(), b.quaternion.clone()]),
    markers: [...S.markers].map(([id, m]) => [id, m.position.clone()]),
    pinned: new Set(S.pinned),
  };
}

function restore(s) {
  for (const [n, p, q] of s.bones) { const b = S.bones.get(n); b.position.copy(p); b.quaternion.copy(q); }
  for (const [id, p] of s.markers) S.markers.get(id).position.copy(p);
  S.pinned = new Set(s.pinned);
  holder.updateMatrixWorld(true);
}

function resetEdits() {
  S.edited = false;
  S.dirty = false;
  S.undo = [];
  S.pinned.clear();
  if (S.clip) applyTime();
  renderEdits();
}

function confirmDiscard() {
  return !S.dirty || confirm("Ci sono correzioni non salvate: le butto via?");
}

function select(sel) {
  S.selected = sel;
  if (!sel) tc.detach();
  else if (sel.leg) {
    tc.attach(S.markers.get(sel.leg.id));
    tc.setMode("translate");
    tc.setSpace("world");
  } else {
    tc.attach(boneOf(sel.bone));
    tc.setMode(sel.bone === "Hips" ? S.hipsMode ?? "rotate" : "rotate");
    tc.setSpace("local");
  }
  renderSelection();
}

tc.addEventListener("dragging-changed", (e) => {
  orbit.enabled = !e.value;
  if (e.value) S.undo.push(snapshot());
  else renderEdits();
});

tc.addEventListener("objectChange", () => {
  S.edited = S.dirty = true;
  if (S.selected?.leg) S.pinned.add(S.selected.leg.id);
  holder.updateMatrixWorld(true);
  for (const leg of S.legs) if (S.pinned.has(leg.id)) solveLeg(leg);
  updateMarkers();
});

// picking: a click (not a drag) on a paw handle or on the body
const ray = new THREE.Raycaster();
let downAt = null;
renderer.domElement.addEventListener("pointerdown", (e) => { downAt = [e.clientX, e.clientY]; });
renderer.domElement.addEventListener("pointerup", (e) => {
  if (!downAt || Math.hypot(e.clientX - downAt[0], e.clientY - downAt[1]) > 4) return;
  if (tc.dragging || tc.axis) return;
  pick(e);
});

function pick(e) {
  if (!S.model) return;
  if (S.playing) setPlaying(false);
  const r = renderer.domElement.getBoundingClientRect();
  ray.setFromCamera(new THREE.Vector2(((e.clientX - r.left) / r.width) * 2 - 1, -((e.clientY - r.top) / r.height) * 2 + 1), camera);
  const handle = ray.intersectObjects([...S.markers.values()], false)[0];
  if (handle) return select({ leg: handle.object.userData.leg });
  const hit = ray.intersectObjects(S.meshes, false)[0];
  if (!hit) return select(null);
  const name = nearestBone(hit.point);
  const leg = L.legOf(name, S.legs);
  select(leg ? { leg } : { bone: name });
}

function nearestBone(p) {
  let best = null, bestD = Infinity;
  for (const [name, b] of S.bones) {
    const a = b.getWorldPosition(new THREE.Vector3());
    const child = b.children.find((c) => c.isBone);
    const end = child ? child.getWorldPosition(new THREE.Vector3())
      : a.clone().add(new THREE.Vector3(0, 1, 0).applyQuaternion(b.getWorldQuaternion(new THREE.Quaternion()))
        .multiplyScalar(b.parent?.isBone ? a.distanceTo(b.parent.getWorldPosition(new THREE.Vector3())) * 0.6 : 0.02));
    const d = new THREE.Line3(a, end).closestPointToPoint(p, true, new THREE.Vector3()).distanceTo(p);
    if (d < bestD) { bestD = d; best = name; }
  }
  return best;
}

// ------------------------------------------------------------------ what changed

/** Point / direction of the GLB frame (metres) expressed as {avanti, su, sinistra}. */
function axes(v) {
  const left = new THREE.Vector3(0, 1, 0).cross(S.fwd);
  return { avanti: v.dot(S.fwd), su: v.y, sinistra: v.dot(left) };
}

function inModel(root, bone) {
  root.updateMatrixWorld(true);
  return root.worldToLocal(bone.getWorldPosition(new THREE.Vector3()));
}

function pointing(root, bone) {
  const child = bone.children.find((c) => c.isBone);
  const a = inModel(root, bone);
  const tip = child ? inModel(root, child)
    : root.worldToLocal(bone.localToWorld(new THREE.Vector3(0, 1, 0).divide(bone.getWorldScale(new THREE.Vector3()))));
  return tip.sub(a).normalize();
}

function eulerDeg(q) {
  const e = new THREE.Euler().setFromQuaternion(q, "XYZ");
  return [e.x, e.y, e.z].map((x) => +(x / DEG).toFixed(2));
}

const round = (o, k = 1000) => Object.fromEntries(Object.entries(o).map(([n, x]) => [n, Math.round(x * k) / k]));

function computeChanges() {
  const changed = [];
  for (const [name, b] of S.bones) {
    const g = ghostBoneOf(name);
    const turned = 2 * Math.acos(Math.min(1, Math.abs(b.quaternion.dot(g.quaternion)))) / DEG;
    const moved = b.position.distanceTo(g.position);
    if (turned > 0.3 || moved > 1e-4) changed.push(name);
  }
  const bones = {}, summary = [], legs = {};
  for (const name of changed) {
    const b = S.bones.get(name) ?? boneOf(name), g = ghostBoneOf(name);
    bones[name] = { before_deg: eulerDeg(g.quaternion), after_deg: eulerDeg(b.quaternion) };
  }
  for (const leg of S.legs) {
    if (!leg.bones.some((n) => changed.includes(n) || changed.includes(n.replace(/\./g, "")))) continue;
    const end = leg.bones[leg.bones.length - 1];
    const before = axes(inModel(S.ghost, ghostBoneOf(end)));
    const after = axes(inModel(S.model, boneOf(end)));
    const out = end.endsWith(".L") ? 1 : -1;
    legs[leg.id] = { bones: leg.bones, paw: end, before_m: round(before), after_m: round(after) };
    summary.push(L.describe({
      label: leg.label, foot: {
        avanti: (after.avanti - before.avanti) * 100, su: (after.su - before.su) * 100,
        fuori: (after.sinistra - before.sinistra) * 100 * out,
      },
    }));
  }
  for (const name of changed) {
    if (L.legOf(name, S.legs)) continue;
    const b = boneOf(name), g = ghostBoneOf(name);
    const d0 = axes(pointing(S.ghost, g)), d1 = axes(pointing(S.model, b));
    const el = (d) => Math.asin(Math.max(-1, Math.min(1, d.su))) / DEG;
    const az = (d) => Math.atan2(d.sinistra, d.avanti) / DEG;
    const qd = b.getWorldQuaternion(new THREE.Quaternion()).multiply(g.getWorldQuaternion(new THREE.Quaternion()).invert());
    const dir = pointing(S.model, b).transformDirection(S.model.matrixWorld);
    const twist = L.wrapDeg(2 * Math.atan2(qd.x * dir.x + qd.y * dir.y + qd.z * dir.z, qd.w) / DEG);
    const change = { label: L.boneLabel(name), rot: { su: el(d1) - el(d0), lato: L.wrapDeg(az(d1) - az(d0)), rollio: twist } };
    if (b.position.distanceTo(g.position) > 1e-4) {
      const p0 = axes(inModel(S.ghost, g)), p1 = axes(inModel(S.model, b));
      change.move = { avanti: (p1.avanti - p0.avanti) * 100, su: (p1.su - p0.su) * 100, fuori: 0 };
      bones[name].pos_before_m = round(p0);
      bones[name].pos_after_m = round(p1);
    }
    bones[name].points_before = round(d0);
    bones[name].points_after = round(d1);
    summary.push(L.describe(change));
  }
  return { bones, legs, summary };
}

// ------------------------------------------------------------------ saving

function capture(draw) {
  const hidden = [tc.getHelper(), trail.points, ...S.markers.values()].filter((o) => o.visible);
  hidden.forEach((o) => (o.visible = false));
  const bg = getComputedStyle(document.body).backgroundColor;
  renderer.setClearColor(new THREE.Color(bg), 1);
  renderer.render(scene, camera);
  renderer.setClearColor(0x000000, 0);
  const url = draw ? draw(renderer.domElement) : renderer.domElement.toDataURL("image/png");
  hidden.forEach((o) => (o.visible = true));
  return url;
}

async function saveFeedback() {
  const ch = computeChanges();
  const note = $("note").value.trim();
  if (!ch.summary.length && !note) return alert("Né correzioni né nota: non c'è niente da salvare.");
  const edited = snapshot();
  const ghostWas = S.ghost.visible;
  applyTime();
  S.ghost.visible = false;
  const prima = capture();
  restore(edited);
  S.ghost.visible = ghostWas;
  const dopo = capture();
  const d = S.clip.duration;
  const json = {
    pet: S.pet.id, clip: S.clip.name, time: +S.t.toFixed(4), frame: Math.round(S.t * FPS) % Math.max(1, Math.round(d * FPS)) + 1,
    frames: Math.max(1, Math.round(d * FPS)), phase: +(S.t / d).toFixed(3), duration: +d.toFixed(4),
    view: S.view, facing: S.facing, note, summary: ch.summary, legs: ch.legs, bones: ch.bones,
    actions: L.clipActions(S.clip.name, S.profile), fingerprint: S.fingerprints[S.clip.name], skin: S.skin,
    units: "frame: da 1 a frames, a 30 fps, come nella pagina; before_deg/after_deg: rotazione locale dell'osso (Euler XYZ, gradi); *_m: metri del GLB negli assi "
      + "del corpo (avanti = verso il muso, su, sinistra = fianco sinistro dell'animale); points_*: direzione in "
      + "cui punta l'osso negli stessi assi",
  };
  const r = await fetch(`/api/feedback/${S.pet.id}/${S.clip.name}`, {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ json, prima, dopo }),
  });
  if (!r.ok) return alert("Salvataggio fallito: " + (await r.text()));
  S.dirty = false;
  $("note").value = "";
  await refreshReview();
  status(`feedback salvato (${(await r.json()).base})`);
}

async function decide(stato) {
  const name = S.clip.name;
  S.state[name] = {
    stato, nota: $("verdictNote").value.trim(), impronta: S.fingerprints[name], pelle: S.skin,
    quando: localStamp(),
  };
  const r = await fetch(`/api/state/${S.pet.id}`, {
    method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(S.state),
  });
  if (!r.ok) return alert("Salvataggio fallito: " + (await r.text()));
  renderClipList();
  renderClipInfo();
}

// ------------------------------------------------------------------ panels

function clipStatus(name) {
  const st = S.state[name];
  // changed since the decision: the clip's tracks, or the skin (decisions saved before `pelle` existed only
  // compare the tracks)
  const isNew = !!st && (st.impronta !== S.fingerprints[name] || (st.pelle !== undefined && st.pelle !== S.skin));
  const icon = !st || isNew ? "⚪" : st.stato === "approvata" ? "✅" : "🔴";
  return { st, isNew, icon };
}

function renderClipList() {
  const ul = $("clips");
  ul.replaceChildren();
  for (const name of L.orderClips(S.clips.map((c) => c.name), S.profile)) {
    const { isNew, icon } = clipStatus(name);
    const open = S.feedback.filter((f) => f.clip === name).length;
    const li = document.createElement("li");
    li.classList.toggle("current", S.clip?.name === name);
    li.innerHTML = `<span>${icon}</span><span class="name"></span><span>${isNew ? '<span class="badge">nuova</span>' : ""}
      ${open ? `<span class="badge count" title="feedback aperti">${open}</span>` : ""}</span><span class="acts"></span>`;
    li.querySelector(".name").textContent = name;
    li.querySelector(".acts").textContent = L.clipActions(name, S.profile).join(", ") || "nessuna azione";
    li.onclick = () => { if (S.clip?.name !== name && confirmDiscard()) openClip(name); };
    ul.append(li);
  }
}

function renderClipInfo() {
  if (!S.clip) return;
  const { st, isNew } = clipStatus(S.clip.name);
  const bits = [`${S.clip.duration.toFixed(2)} s`];
  if (S.loc) bits.push(`${S.loc.refSpeedPx} px/s nel gioco`);
  if (st) bits.push(isNew ? `cambiata dopo "${st.stato.replace("_", " ")}" del ${st.quando}` : `${st.stato.replace("_", " ")} il ${st.quando}`);
  $("clipInfo").textContent = bits.join(" · ");
}

function renderFeedback() {
  const ul = $("feedback");
  ul.replaceChildren();
  const mine = S.feedback.filter((f) => f.clip === S.clip?.name);
  if (!mine.length) { ul.innerHTML = '<li class="hint" style="cursor:default;border:0">Nessuno per questa clip.</li>'; return; }
  for (const f of mine) {
    const li = document.createElement("li");
    const img = f.dopo ? `<img src="/review/${S.pet.id}/${f.clip}/${f.base}-dopo.png" alt="">` : "";
    li.innerHTML = `${img}<div class="when"></div><div class="text"></div><ul class="sum"></ul>`;
    li.querySelector(".when").textContent = `${f.base} · ${(f.time ?? 0).toFixed(3)} s`;
    li.querySelector(".text").textContent = f.note;
    for (const s of f.summary) { const x = document.createElement("li"); x.textContent = s; li.querySelector(".sum").append(x); }
    li.onclick = () => { if (confirmDiscard()) { resetEdits(); setPlaying(false); moveTime(f.time ?? 0); } };
    ul.append(li);
  }
}

function renderSelection() {
  const sel = S.selected;
  $("selection").hidden = !sel;
  $("selHint").hidden = !!sel;
  if (!sel) return;
  $("selName").textContent = sel.leg ? `${sel.leg.label} (trascina il pallino)` : L.boneLabel(sel.bone);
  $("modeRow").hidden = sel.bone !== "Hips";
}

function renderEdits() {
  $("undo").disabled = !S.undo.length;
  $("reset").disabled = !S.edited;
  $("save").disabled = !S.clip;
  const ul = $("changes");
  ul.replaceChildren();
  if (!S.edited || !S.ghost) return;
  for (const s of computeChanges().summary) { const li = document.createElement("li"); li.textContent = s; ul.append(li); }
}

/** Local date and time, 2026-09-30T17:25 (toISOString would give UTC). */
function localStamp(d = new Date()) {
  const p = (n) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`;
}

function status(text) { $("status").textContent = text; }

// ------------------------------------------------------------------ controls

$("play").onclick = () => setPlaying(!S.playing);
$("prev").onclick = () => step(-1);
$("next").onclick = () => step(1);
$("rate").onchange = (e) => (S.rate = +e.target.value);
$("scrub").addEventListener("input", (e) => {
  const at = +e.target.value * S.clip.duration;   // read first: resetting the pose redraws the slider
  if (!confirmDiscard()) return renderClock();
  resetEdits();
  setPlaying(false);
  moveTime(at);
});
for (const b of document.querySelectorAll("#views button")) b.onclick = () => setView(b.dataset.view);
for (const b of document.querySelectorAll("#modeRow button")) b.onclick = () => {
  S.hipsMode = b.dataset.mode;
  for (const x of document.querySelectorAll("#modeRow button")) x.classList.toggle("on", x === b);
  tc.setMode(S.hipsMode);
};
$("ghost").onchange = updateMarkers;
$("trail").onchange = () => { trail.items = []; };
$("undo").onclick = undo;
$("reset").onclick = () => { if (!S.dirty || confirm("Ripristino la posa originale?")) resetEdits(); select(S.selected); };
$("save").onclick = saveFeedback;
$("approve").onclick = () => decide("approvata");
$("redo").onclick = () => decide("da_rifare");
$("help").onclick = () => $("helpBox").showModal();

function undo() {
  const s = S.undo.pop();
  if (!s) return;
  restore(s);
  S.edited = S.dirty = S.undo.length > 0;
  updateMarkers();
  renderEdits();
}

addEventListener("keydown", (e) => {
  if (e.target.closest?.("textarea, input[type=text], select")) return;
  if (e.key === " ") { e.preventDefault(); setPlaying(!S.playing); }
  else if (e.key === "ArrowLeft") { e.preventDefault(); step(-1); }
  else if (e.key === "ArrowRight") { e.preventDefault(); step(1); }
  else if (e.key === "Escape") select(null);
  else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "z") { e.preventDefault(); undo(); }
});

// a clicked button keeps no focus, so the space bar is always play/pause and never presses it again
document.addEventListener("click", (e) => e.target.closest?.("button, input[type=checkbox]")?.blur());

addEventListener("beforeunload", (e) => { if (S.dirty) e.preventDefault(); });

function load(k) { try { return localStorage.getItem("review:" + k); } catch { return null; } }
function save(k, v) { try { localStorage.setItem("review:" + k, v); } catch { /* private window */ } }

// ------------------------------------------------------------------ loop

const clock = new THREE.Clock();
function frame() {
  requestAnimationFrame(frame);
  const dt = Math.min(clock.getDelta(), 0.1);
  if (S.playing && S.clip) {
    moveTime(S.t + dt * S.rate * S.speed);
    if ($("trail").checked) recordTrail();
  }
  floor.position.z = -(S.dist % (2 * SQUARE));
  drawTrail();
  renderer.render(scene, camera);
}

function recordTrail() {
  for (const leg of S.legs) {
    const p = pivot.worldToLocal(effectorOf(leg).getWorldPosition(new THREE.Vector3()));
    trail.items.push({ x: p.x, y: Math.max(p.y, 0.002), z: p.z + S.dist, color: new THREE.Color(LEG_COLORS[leg.id]), at: S.clock });
  }
  const cut = S.clock - TRAIL_SECONDS * S.speed;
  trail.items = trail.items.filter((i) => i.at >= cut).slice(-trail.max);
}

function drawTrail() {
  const on = $("trail").checked;
  trail.points.visible = on;
  if (!on) return;
  const pos = trail.points.geometry.attributes.position, col = trail.points.geometry.attributes.color;
  trail.items.forEach((it, i) => {
    pos.setXYZ(i, it.x, it.y, it.z - S.dist);
    col.setXYZ(i, it.color.r, it.color.g, it.color.b);
  });
  pos.needsUpdate = col.needsUpdate = true;
  trail.points.geometry.setDrawRange(0, trail.items.length);
}

/** Contact sheet of the current clip, for a quick look from the console: `await review.sheet(8)`. */
S.sheet = async (n = 8, cols = 4, w = 480) => {
  const was = S.t, h = Math.round(w * box.clientHeight / box.clientWidth);
  const out = document.createElement("canvas");
  out.width = cols * w;
  out.height = Math.ceil(n / cols) * h;
  const g = out.getContext("2d");
  for (let i = 0; i < n; i++) {
    moveTime((i / n) * S.clip.duration);
    capture((c) => g.drawImage(c, (i % cols) * w, Math.floor(i / cols) * h, w, h));
    g.fillStyle = "#000";
    g.font = "16px sans-serif";
    g.fillText(`${S.clip.name} ${((i / n) * S.clip.duration).toFixed(2)} s`, (i % cols) * w + 8, Math.floor(i / cols) * h + 20);
  }
  moveTime(was);
  let el = document.getElementById("sheet");
  if (!el) {
    el = document.createElement("img");
    el.id = "sheet";
    el.style.cssText = "position:fixed;inset:0;width:100%;height:100%;object-fit:contain;background:#fff;z-index:9";
    el.onclick = () => el.remove();
    document.body.append(el);
  }
  el.src = out.toDataURL();
  return `${n} fotogrammi`;
};
S.open = (pet, clip) => (pet && pet !== S.pet?.id ? openPet(pet) : Promise.resolve()).then(() => clip && openClip(clip));
S.setView = setView;

start().catch((e) => { status("errore: " + e.message); console.error(e); });
frame();
