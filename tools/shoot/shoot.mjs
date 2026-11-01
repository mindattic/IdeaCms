#!/usr/bin/env node
/**
 * ma-shoot — photographs every MindAttic project for its Ideas brochure page.
 *
 * WHY A MANIFEST AND NOT DETECTION
 * --------------------------------
 * A survey of the 34 repos found static sites, Vite apps, ASP.NET hosts, WPF apps, console tools and
 * pure libraries — and heuristics get it wrong in ways that are worse than useless: a stray
 * `index.html` in a docs folder outranks the real application, and Prose's project scan turns up
 * eight .csproj files, several of them inside `.claude/worktrees` and not source at all. Worse, one
 * repo often has SEVERAL visual surfaces (Prose has both a CLI and an observer UI), so "the" shot for
 * a repo is not even a well-formed idea. So each shot is declared in shots.json: reviewable,
 * re-runnable, and correctable by editing one entry instead of fighting a guess.
 *
 * DRIVERS
 *   web-static  serve the repo over http and shoot it
 *   web-cmd     run a dev/build command, wait for its URL, shoot it
 *   console     run a command, capture stdout, compose it as a terminal card
 *   diagram     render Mermaid source to a static SVG (for repos with no UI at all)
 *
 * Usage:
 *   node shoot.mjs                     # every enabled shot
 *   node shoot.mjs --only hyperspace   # one project (repeatable)
 *   node shoot.mjs --list              # what would run
 *   node shoot.mjs --keep-open         # leave browsers open on failure for debugging
 */
import { chromium } from 'playwright';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import fsp from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { serveDir } from './lib/serve.mjs';
import { terminalHtml } from './lib/terminal.mjs';
import { renderMermaid, sourceFor } from './lib/diagram.mjs';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(HERE, '..', '..');        // IdeaCms
const PROJECTS_ROOT = path.resolve(REPO_ROOT, '..');      // D:\Projects\MindAttic
const OUT = path.join(HERE, 'out');
const TMP = path.join(HERE, '.tmp');

const argv = process.argv.slice(2);
const flag = (n) => argv.includes(n);
const values = (n) => argv.reduce((a, v, i) => (argv[i - 1] === n ? [...a, v] : a), []);

const only = values('--only').map((s) => s.toLowerCase());
const LIST = flag('--list');
const DRY = flag('--dry-run');

const manifest = JSON.parse(await fsp.readFile(path.join(HERE, 'shots.json'), 'utf8'));
const defaults = manifest.defaults ?? {};

const log = (...a) => console.log('[shoot]', ...a);
const warn = (...a) => console.error('[shoot]', ...a);

/** Waits for an http endpoint to answer, or gives up. */
async function waitForUrl(url, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      const res = await fetch(url, { redirect: 'manual' });
      if (res.status > 0) return true;
    } catch { /* not up yet */ }
    await new Promise((r) => setTimeout(r, 400));
  }
  return false;
}

/** Runs a command to completion, capturing stdout+stderr. Never throws on a non-zero exit. */
function runCapture(cmd, cwd, timeoutMs = 120000, env = {}) {
  return new Promise((resolve) => {
    const child = spawn(cmd, {
      cwd, shell: true, windowsHide: true,
      env: { ...process.env, ...env, FORCE_COLOR: '1', NO_COLOR: '' },
    });
    let out = '';
    const done = (code) => resolve({ code, out });
    const timer = setTimeout(() => { try { child.kill(); } catch {} done(-1); }, timeoutMs);
    child.stdout?.on('data', (d) => { out += d.toString(); });
    child.stderr?.on('data', (d) => { out += d.toString(); });
    child.on('close', (code) => { clearTimeout(timer); done(code); });
    child.on('error', (e) => { clearTimeout(timer); out += String(e); done(-1); });
  });
}

/** Starts a long-running process (a dev server) and returns a handle that can kill its whole tree. */
function startProcess(cmd, cwd, env = {}) {
  const child = spawn(cmd, {
    cwd, shell: true, windowsHide: true,
    env: { ...process.env, ...env },
  });
  let log = '';
  child.stdout?.on('data', (d) => { log += d.toString(); });
  child.stderr?.on('data', (d) => { log += d.toString(); });
  return {
    child,
    get log() { return log; },
    stop() {
      try {
        // A dev server spawns children (node, dotnet, msbuild); killing the shell alone orphans them
        // and leaves the port held, so the next run picks up a stale app.
        if (process.platform === 'win32') spawn('taskkill', ['/pid', child.pid, '/T', '/F'], { windowsHide: true });
        else child.kill('-SIGTERM');
      } catch { /* already gone */ }
    },
  };
}

async function shootPage(browser, url, shot, outFile) {
  const vp = shot.viewport ?? defaults.viewport ?? [1440, 900];
  const ctx = await browser.newContext({
    viewport: { width: vp[0], height: vp[1] },
    deviceScaleFactor: shot.scale ?? defaults.scale ?? 2,
    colorScheme: shot.colorScheme ?? 'dark',
    reducedMotion: 'no-preference',
  });
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(String(e)));

  await page.goto(url, { waitUntil: shot.waitUntil ?? 'networkidle', timeout: shot.gotoTimeoutMs ?? 45000 });

  for (const sel of shot.waitFor ?? []) {
    await page.waitForSelector(sel, { timeout: 20000 }).catch(() => errors.push(`waitFor missed: ${sel}`));
  }
  // Anything the shot wants clicked/dismissed before the picture — cookie bars, a splash, a tab.
  for (const act of shot.actions ?? []) {
    try {
      if (act.click) await page.click(act.click, { timeout: 8000 });
      if (act.press) await page.keyboard.press(act.press);
      if (act.evaluate) await page.evaluate(act.evaluate);
      if (act.waitMs) await page.waitForTimeout(act.waitMs);
    } catch (e) { errors.push(`action failed (${JSON.stringify(act)}): ${e.message}`); }
  }
  // A 3D scene needs frames, not a load event: networkidle fires long before the first render.
  await page.waitForTimeout(shot.settleMs ?? defaults.settleMs ?? 2500);

  const target = shot.clip ? page.locator(shot.clip) : page;
  await target.screenshot({ path: outFile, animations: 'disabled' });
  await ctx.close();
  return errors;
}

async function driveWebStatic(browser, repoDir, shot, outFile) {
  const dir = shot.dir ? path.join(repoDir, shot.dir) : repoDir;
  const server = await serveDir(dir, { indexFile: shot.file ?? 'index.html' });
  try {
    return await shootPage(browser, server.origin + (shot.path ?? '/'), shot, outFile);
  } finally {
    server.close();
  }
}

async function driveWebCmd(browser, repoDir, shot, outFile) {
  const cwd = shot.cwd ? path.join(repoDir, shot.cwd) : repoDir;

  if (shot.build) {
    const built = await runCapture(shot.build.replaceAll('{tmp}', TMP), cwd, shot.buildTimeoutMs ?? 300000);
    if (built.code !== 0) return [`build failed (${built.code}): ${built.out.slice(-500)}`];
  }

  // A built bundle is just files — serve them rather than keeping a dev server alive.
  if (shot.serveDir) {
    const server = await serveDir(shot.serveDir.replaceAll('{tmp}', TMP), { indexFile: shot.file ?? 'index.html' });
    try {
      return await shootPage(browser, server.origin + (shot.path ?? '/'), shot, outFile);
    } finally { server.close(); }
  }

  const proc = startProcess(shot.run, cwd, shot.env ?? {});
  try {
    const url = shot.url;
    const ready = await waitForUrl(shot.readyUrl ?? url, shot.readyTimeoutMs ?? 120000);
    if (!ready) return [`app never answered ${shot.readyUrl ?? url}. Last output:\n${proc.log.slice(-800)}`];
    return await shootPage(browser, url + (shot.path ?? ''), shot, outFile);
  } finally {
    proc.stop();
  }
}

async function driveConsole(browser, repoDir, shot, outFile) {
  const cwd = shot.cwd ? path.join(repoDir, shot.cwd) : repoDir;
  const res = await runCapture(shot.run, cwd, shot.timeoutMs ?? 180000, shot.env ?? {});
  const text = (shot.text ?? res.out).trim();
  if (!text) return [`command produced no output (exit ${res.code}): ${shot.run}`];

  const html = terminalHtml(text, {
    theme: shot.theme ?? 'amber',
    title: shot.title ?? shot.run.slice(0, 60),
    cols: shot.cols ?? 100,
    rows: shot.rows ?? null,
    fontSize: shot.fontSize ?? 15,
  });

  const ctx = await browser.newContext({
    viewport: { width: 1600, height: 1000 },
    deviceScaleFactor: shot.scale ?? defaults.scale ?? 2,
  });
  const page = await ctx.newPage();
  await page.setContent(html, { waitUntil: 'load' });
  await page.locator('.term').screenshot({ path: outFile, omitBackground: true });
  await ctx.close();
  // A non-zero exit is fine (a --help often exits 1); an empty capture is what actually fails.
  return res.code === 0 || shot.allowNonZeroExit !== false ? [] : [`exit ${res.code}`];
}

async function driveDiagram(browser, repoDir, shot, outFile) {
  const source = sourceFor(shot, HERE);
  const svg = await renderMermaid(browser, source, { theme: shot.theme ?? 'dark' });
  await fsp.writeFile(outFile, svg, 'utf8');
  return [];
}

const DRIVERS = {
  'web-static': driveWebStatic,
  'web-cmd': driveWebCmd,
  'console': driveConsole,
  'diagram': driveDiagram,
};

/** A diagram is vector; everything else is a raster capture. */
const extFor = (shot) => shot.format ?? (shot.driver === 'diagram' ? 'svg' : 'png');

// ---------------------------------------------------------------------------------------------

const planned = [];
for (const entry of manifest.repos) {
  if (entry.enabled === false) continue;
  if (only.length && !only.includes(entry.slug.toLowerCase())) continue;
  for (const shot of entry.shots ?? []) {
    if (shot.enabled === false) continue;
    planned.push({ entry, shot });
  }
}

if (LIST || DRY) {
  log(`${planned.length} shot(s) planned:`);
  for (const { entry, shot } of planned) {
    console.log(`  ${entry.slug}/${shot.id}  [${shot.driver}]  ${shot.run ?? shot.file ?? shot.url ?? ''}`);
  }
  process.exit(0);
}

await fsp.mkdir(OUT, { recursive: true });
await fsp.mkdir(TMP, { recursive: true });

const browser = await chromium.launch({
  headless: true,
  args: ['--use-gl=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist',
         '--hide-scrollbars', '--force-device-scale-factor=1'],
});

const results = [];
for (const { entry, shot } of planned) {
  const repoDir = path.join(PROJECTS_ROOT, entry.repo);
  const name = `${entry.slug}-${shot.id}.${extFor(shot)}`;
  const outFile = path.join(OUT, name);
  const driver = DRIVERS[shot.driver];

  if (shot.driver !== 'diagram' && !fs.existsSync(repoDir)) {
    results.push({ name, ok: false, note: `no repo at ${repoDir}` }); continue;
  }
  if (!driver) { results.push({ name, ok: false, note: `unknown driver "${shot.driver}"` }); continue; }

  process.stdout.write(`[shoot] ${entry.slug}/${shot.id} … `);
  const started = Date.now();
  let errors = [];
  try {
    errors = await driver(browser, repoDir, shot, outFile) ?? [];
  } catch (e) {
    errors = [e.message];
  }
  const took = ((Date.now() - started) / 1000).toFixed(1);
  const exists = fs.existsSync(outFile);
  const size = exists ? fs.statSync(outFile).size : 0;

  if (exists && size > 0) {
    console.log(`ok (${(size / 1024).toFixed(0)} KB, ${took}s)${errors.length ? ' [warnings]' : ''}`);
    results.push({ name, ok: true, size, note: errors.join(' | ') });
  } else {
    console.log(`FAILED (${took}s)`);
    results.push({ name, ok: false, note: errors.join(' | ') || 'no file produced' });
  }
}

await browser.close();

console.log('');
const ok = results.filter((r) => r.ok);
log(`${ok.length}/${results.length} captured -> ${OUT}`);
for (const r of results.filter((r) => !r.ok)) warn(`  FAILED ${r.name}: ${r.note}`);
for (const r of ok.filter((r) => r.note)) warn(`  warn   ${r.name}: ${r.note}`);

await fsp.writeFile(path.join(OUT, 'index.json'),
  JSON.stringify({ capturedUtc: new Date().toISOString(), results }, null, 2));

process.exit(results.every((r) => r.ok) ? 0 : 1);
