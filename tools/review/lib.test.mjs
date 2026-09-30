// node --test tools/review/lib.test.mjs
import test from "node:test";
import assert from "node:assert/strict";
import {
  fnv1a, clipFingerprint, skinFingerprint, clipActions, orderClips, locomotion, clipSpeed, facingFor, boneLabel, legChains, legOf,
  describe, wrapDeg, stepFrame,
} from "./lib.js";

const sally = {
  length_px: 200,
  actions: {
    idle: { anims: ["Idle", "Idle_Look"] },
    walk: { anims: ["Walk"], speed: 1.0, ref_speed_px: 148 },
    lope: { anims: ["Run"], speed: 1.0, ref_speed_px: 521 },
    sit: { anims: ["Sit"], anims_right: ["Sit_R"] },
    sleep: { anims: ["ChinOnPaws"], anims_right: ["ChinOnPaws_R"], speed: 0.7 },
    purr: { anims: ["Sit"], anims_right: ["Sit_R"], speed: 0.6 },
    flop: { anims: ["Flop"], show_side: -1 },
  },
};

test("fnv1a is the standard FNV-1a", () => {
  assert.equal(fnv1a(""), "811c9dc5");
  assert.equal(fnv1a("a"), "e40c292c");
});

test("the fingerprint ignores float noise and track order but not real changes", () => {
  const a = [{ name: "Hips.quaternion", times: [0, 0.5], values: [0, 0, 0, 1, 0.1, 0, 0, 0.995] },
             { name: "Spine.quaternion", times: [0], values: [0, 0, 0, 1] }];
  const noisy = [{ ...a[1] }, { ...a[0], values: a[0].values.map((v) => v + 1e-7) }];
  const changed = [a[0], { ...a[1], values: [0, 0, 0.01, 1] }];
  assert.equal(clipFingerprint(a), clipFingerprint(noisy));
  assert.notEqual(clipFingerprint(a), clipFingerprint(changed));
});

test("actions using a clip, and clips in profile order", () => {
  assert.deepEqual(clipActions("Sit_R", sally), ["sit", "purr"]);
  assert.deepEqual(orderClips(["Zzz", "Run", "Sit_R", "Idle", "Walk", "Sit", "Aim"], sally),
    ["Idle", "Walk", "Run", "Sit", "Sit_R", "Aim", "Zzz"]);
});

test("the floor passes at ref_speed_px / length_px body lengths per second of clip", () => {
  const loc = locomotion("Run", sally);
  assert.equal(loc.action, "lope");
  assert.ok(Math.abs(loc.rate - 521 / 200) < 1e-9);
  assert.equal(locomotion("Sit", sally), null);
  assert.equal(clipSpeed("ChinOnPaws", sally), 0.7);
  assert.equal(clipSpeed("Idle", sally), 1);
});

test("facing: _R right, their plain twins left, show_side wins", () => {
  assert.equal(facingFor("Sit_R", sally), 1);
  assert.equal(facingFor("Sit", sally), -1);
  assert.equal(facingFor("Walk", sally), 1);
  assert.equal(facingFor("Flop", sally), -1);
});

test("bone labels", () => {
  assert.equal(boneLabel("Foot.L"), "zampa post. sx (piede)");
  assert.equal(boneLabel("Hand.R"), "zampa ant. dx (mano)");
  assert.equal(boneLabel("Tail3"), "coda 3");
  assert.equal(boneLabel("Ear2.R"), "orecchio dx 2");
  assert.equal(boneLabel("Head"), "testa");
  assert.equal(boneLabel("Weird"), "Weird");
});

test("legs of the cat/dog rig and of the rabbit rig", () => {
  const dog = ["Hips", "Scapula.L", "UpperArm.L", "Forearm.L", "Hand.L", "Finger.L", "Thigh.L", "Shin.L", "Foot.L", "Toe.L",
    "Scapula.R", "UpperArm.R", "Forearm.R", "Hand.R", "Finger.R", "Thigh.R", "Shin.R", "Foot.R", "Toe.R"];
  const legs = legChains(dog);
  assert.deepEqual(legs.map((l) => l.id), ["hindL", "hindR", "foreL", "foreR"]);
  assert.deepEqual(legs[2].bones, ["UpperArm.L", "Forearm.L", "Hand.L", "Finger.L"]);
  const rabbit = legChains(["UpperArm.R", "Forearm.R", "Hand.R", "Thigh.R", "Shin.R", "Foot.R", "Toe.R"]);
  assert.deepEqual(rabbit.find((l) => l.id === "foreR").bones, ["UpperArm.R", "Forearm.R", "Hand.R"]);
  assert.equal(legOf("Shin.R", rabbit).label, "zampa post. dx");
  assert.equal(legOf("Head", rabbit), null);
});

test("describe says what moved, in words", () => {
  assert.equal(describe({ label: "zampa post. sx", foot: { avanti: 5.12, su: -2, fuori: 0.1 } }),
    "zampa post. sx: 5,1 cm più avanti, 2,0 cm più in basso");
  assert.equal(describe({ label: "testa", rot: { su: 12.4, lato: 0.2, rollio: -3 } }),
    "testa: 12° più in su, 3,0° di rollio a destra");
  assert.equal(describe({ label: "coda 2", rot: { su: 0.1, lato: 0, rollio: 0 } }), "coda 2: ritocco minimo");
});

test("angles wrap and frames step round the loop", () => {
  assert.equal(wrapDeg(190), -170);
  assert.equal(wrapDeg(-180), 180);
  assert.ok(Math.abs(stepFrame(0, -1, 1) - 29 / 30) < 1e-9);
  assert.ok(Math.abs(stepFrame(29 / 30, 1, 1) - 0) < 1e-9);
  assert.ok(Math.abs(stepFrame(0.2334, 1, 1) - 8 / 30) < 1e-9);
});

test("the skin fingerprint changes with the weights, not with float noise", () => {
  const a = [{ index: Uint16Array.from([0, 1, 2, 3]), weight: Float32Array.from([0.5, 0.3, 0.2, 0]) }];
  const noisy = [{ index: a[0].index, weight: a[0].weight.map((w) => w + 1e-5) }];
  const moved = [{ index: a[0].index, weight: Float32Array.from([0.4, 0.4, 0.2, 0]) }];
  assert.equal(skinFingerprint(a), skinFingerprint(noisy));
  assert.notEqual(skinFingerprint(a), skinFingerprint(moved));
});
