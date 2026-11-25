"use strict";

const repository = "Nobbinobb/infinitefusion-hoenn-ironmon";
const releasesUrl = `https://github.com/${repository}/releases`;
const releasesApi = `https://api.github.com/repos/${repository}/releases`;
const gameTag = /^v(\d+\.\d+\.\d+)$/;
const updaterTag = /^vupdater-((\d+\.\d+)\.\d+)$/;
const setupFilename = "Ironmon-Setup-win-x64.exe";
const recentLimit = 5;
const releaseCacheKey = "ironmon-get-started:releases:v1";
const releaseCacheDuration = 5 * 60_000;
const zipCompatibility = new Map([
  ["1.3.2", /^0\.10\.2$/],
  ["1.2.2", /^0\.(?:10\.[01]|8\.(?:[0-9]|1[0-5]))$/],
  ["6.8.0", /^0\.(?:2\.(?:[1-9]|[1-9][0-9]+)|[3-6]\.\d+|7\.[0-9])$/],
]);
const noteElements = new Set("a blockquote br caption code dd del details div dl dt em h1 h2 h3 h4 h5 h6 hr kbd li ol p pre s span strong sub summary sup table tbody td tfoot th thead tr tt ul".split(" "));
const alertClass = /^markdown-alert(?:-(?:note|tip|important|warning|caution|title))?$/;
const dateFormatter = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeZone: "UTC" });
const rowTemplate = document.querySelector("#release-row");
const seriesTemplate = document.querySelector("#updater-series");
const requestError = document.querySelector("#request-error");
const savedReleaseFeed = document.querySelector("#saved-release-feed");
const retry = document.querySelector("#retry");
const zipGameVersion = document.querySelector("#zip-game-version");
const zipMatchResult = document.querySelector("#zip-match-result");
const zipMatchStatus = document.querySelector("#zip-match-status");
const zipMatchDownloads = document.querySelector("#zip-match-downloads");
const zipMatchSetup = document.querySelector("#zip-match-setup");
const zipFilter = document.querySelector("#zip-filter");
const zipFilterLabel = document.querySelector("#zip-filter-label");
let gameReleaseFeed = null;

const game = {
  list: document.querySelector("#ironmon-releases"),
  history: document.querySelector("#ironmon-history"),
  status: document.querySelector("#ironmon-status"),
  emptyMessage: "No Ironmon ZIP downloads are published.",
};
const updater = {
  list: document.querySelector("#updater-releases"),
  status: document.querySelector("#updater-status"),
  emptyMessage: "No standalone updater releases are published.",
};

async function loadReleaseFeed(signal) {
  const releases = new Map();
  for (let page = 1; ; page += 1) {
    const response = await fetch(`${releasesApi}?per_page=100&page=${page}`, {
      headers: { Accept: "application/vnd.github.full+json" },
      signal,
    });
    if (!response.ok) {
      const error = new Error(`Release feed returned HTTP ${response.status}`);
      if (response.status === 403 || response.status === 429) {
        const reset = Number(response.headers.get("X-RateLimit-Reset")) || 0;
        const retryAfter = Number(response.headers.get("Retry-After")) || 0;
        error.retryAt = Math.max(reset * 1000, Date.now() + Math.max(60, retryAfter) * 1000);
      }
      throw error;
    }

    const entries = await response.json();
    if (!Array.isArray(entries)) throw new Error("Invalid release feed");
    for (const entry of entries) {
      if (entry?.draft === false && typeof entry.tag_name === "string" && entry.published_at) {
        const { draft, tag_name, prerelease, published_at, body, body_html, assets } = entry;
        releases.set(tag_name, {
          draft, tag_name, prerelease, published_at, body, body_html,
          assets: assets?.map(({ name, state }) => ({ name, state })),
        });
      }
    }

    if (!response.headers.get("Link")?.includes('rel="next"')) break;
  }
  return [...releases.values()].sort((a, b) => Date.parse(b.published_at) - Date.parse(a.published_at));
}

async function loadLatestGameTag(signal) {
  try {
    const response = await fetch(`${releasesApi}/latest`, {
      headers: { Accept: "application/vnd.github+json" },
      signal,
    });
    if (!response.ok) return null;

    const release = await response.json();
    return release?.draft === false && release.prerelease === false && gameTag.test(release.tag_name)
      ? release.tag_name
      : null;
  } catch {
    return null;
  }
}

function readCachedReleaseFeed() {
  try {
    const cached = JSON.parse(localStorage.getItem(releaseCacheKey));
    return Array.isArray(cached?.releases) && Number.isFinite(cached.nextCheckAt) ? cached : null;
  } catch {
    return null;
  }
}

async function loadReleaseData(signal) {
  const cached = readCachedReleaseFeed();
  if (cached && cached.nextCheckAt > Date.now()) return cached;

  let feed;
  try {
    const releases = await loadReleaseFeed(signal);
    const latestTag = await loadLatestGameTag(signal);
    feed = { releases, latestTag, saved: false, nextCheckAt: Date.now() + releaseCacheDuration };
  } catch (error) {
    let saved = cached;
    if (!saved) {
      const response = await fetch("release-feed.json", { signal: AbortSignal.timeout(5_000) });
      if (!response.ok) throw error;
      saved = await response.json();
      if (!Array.isArray(saved?.releases)) throw error;
    }
    feed = { ...saved, saved: true, nextCheckAt: error.retryAt ?? Date.now() + 60_000 };
  }

  try {
    localStorage.setItem(releaseCacheKey, JSON.stringify(feed));
  } catch {
    // The release list can still render when browser storage is unavailable.
  }
  return feed;
}

function releaseNotesUrl(tag) {
  return `${releasesUrl}/tag/${encodeURIComponent(tag)}`;
}

function releaseDownloadUrl(release, filename) {
  return release.assets?.some(asset => asset.state === "uploaded" && asset.name === filename)
    ? `${releasesUrl}/download/${encodeURIComponent(release.tag_name)}/${encodeURIComponent(filename)}`
    : null;
}

function renderSetup(releases) {
  const newestInstaller = releases.filter(release =>
    gameTag.test(release.tag_name) && releaseDownloadUrl(release, setupFilename)
  ).sort((a, b) => b.tag_name.localeCompare(a.tag_name, "en", { numeric: true }))[0];
  if (!newestInstaller) return;

  const setup = document.querySelector("#setup-download");
  setup.href = releaseDownloadUrl(newestInstaller, setupFilename);
  setup.textContent = "Download Setup";
}

function showZip(release, container, flavor, filename, accessibleLabel) {
  const url = releaseDownloadUrl(release, filename);
  if (!url) return;

  const link = container.querySelector(`[data-flavor="${flavor}"]`);
  link.href = url;
  link.setAttribute("aria-label", accessibleLabel);
  link.hidden = false;
  container.hidden = false;
}

function appendNotes(source, target, baseUrl) {
  for (const child of source.childNodes) {
    if (child.nodeType === Node.TEXT_NODE) {
      target.append(child.textContent);
      continue;
    }
    if (!noteElements.has(child.localName)) continue;

    const element = document.createElement(child.localName);
    if (child.localName === "a" && child.hasAttribute("href")) {
      try {
        const url = new URL(child.getAttribute("href"), baseUrl);
        if (url.protocol === "https:" || url.protocol === "http:") element.href = url.href;
      } catch {
        // Keep the link text when the address is invalid.
      }
    }
    for (const className of child.classList) {
      if (alertClass.test(className)) element.classList.add(className);
    }
    appendNotes(child, element, baseUrl);
    target.append(element);
  }
}

function enableNotes(row, release, title, channel) {
  const toggle = row.querySelector(".release-toggle");
  const notes = row.querySelector(".release-notes");
  const link = row.querySelector(".release-source");
  const notesUrl = releaseNotesUrl(release.tag_name);
  notes.id = `notes-${release.tag_name}`;
  toggle.setAttribute("aria-controls", notes.id);
  toggle.setAttribute("aria-label", channel ? `${title} release notes, ${channel}` : `${title} release notes`);
  link.href = notesUrl;

  let rendered = false;
  toggle.addEventListener("click", () => {
    notes.hidden = !notes.hidden;
    toggle.setAttribute("aria-expanded", String(!notes.hidden));
    if (notes.hidden || rendered) return;

    const body = notes.querySelector(".release-notes-body");
    if (release.body_html?.trim()) {
      // Parse in a template, then copy only the notes markup and safe links.
      const source = document.createElement("template");
      source.innerHTML = release.body_html;
      appendNotes(source.content, body, notesUrl);
    } else {
      body.classList.add("notes-plaintext");
      body.textContent = release.body?.trim() || "No release notes were published for this version.";
    }
    rendered = true;
  });
}

function createReleaseRow(release, section, version, latestTag) {
  const row = rowTemplate.content.cloneNode(true);
  const name = row.querySelector(".release-name");
  const date = row.querySelector("time");
  const publishedAt = new Date(release.published_at);
  const isGame = section === game;
  const title = isGame ? `Ironmon ${version}` : `Standalone updater ${version}`;
  const isLatest = isGame && !release.prerelease && release.tag_name === latestTag;
  let channel = "";
  if (isGame && release.prerelease) channel = "Test release";
  else if (isLatest) channel = "Latest";

  name.textContent = `v${version}`;
  enableNotes(row, release, title, channel);
  if (Number.isFinite(publishedAt.getTime())) {
    date.dateTime = publishedAt.toISOString();
    date.textContent = dateFormatter.format(publishedAt);
  } else {
    date.hidden = true;
  }

  if (!isGame) return row;

  if (channel) {
    const label = row.querySelector(".channel");
    label.textContent = channel;
    label.classList.toggle("channel-latest", isLatest);
    label.hidden = false;
  }
  const packages = row.querySelector(".release-packages");
  const zipBase = `Ironmon-v${version}-win-x64`;
  showZip(release, packages, "self-contained", `${zipBase}.zip`, `Download ${title} ZIP with runtime included`);
  showZip(release, packages, "runtime-required", `${zipBase}-runtime-required.zip`, `Download ${title} ZIP with runtime required`);
  return packages.hidden ? null : row;
}

function renderGameReleases() {
  const { releases, latestTag } = gameReleaseFeed;
  const gameVersion = zipGameVersion.value;
  const matches = [];
  for (const release of releases) {
    const match = gameTag.exec(release.tag_name);
    if (!match) continue;
    const version = match[1];
    if (gameVersion && !zipCompatibility.get(gameVersion)?.test(version)) continue;
    const row = createReleaseRow(release, game, version, latestTag);
    if (row) matches.push({ row, release, version });
  }
  const rows = matches.map(({ row }) => row);
  const older = rows.slice(recentLimit);
  const emptyMessage = gameVersion ? "No published ZIP matches the selected game version." : game.emptyMessage;
  game.status.textContent = rows.length ? "" : emptyMessage;
  game.status.hidden = rows.length > 0;
  game.list.replaceChildren(...rows.slice(0, recentLimit));
  game.history.hidden = older.length === 0;
  game.history.querySelector("summary").textContent = `${older.length} older ${older.length === 1 ? "release" : "releases"}`;
  game.history.querySelector("ul").replaceChildren(...older);

  zipFilter.hidden = !gameVersion;
  zipMatchResult.hidden = !gameVersion;
  zipMatchDownloads.hidden = !rows.length;
  zipMatchSetup.hidden = rows.length > 0;
  if (!gameVersion) return;

  zipFilterLabel.textContent = gameVersion === "other"
    ? "Game version not listed"
    : `Matching ${zipGameVersion.selectedOptions[0].textContent}`;
  const recommended = matches.sort((a, b) =>
    Number(a.release.prerelease) - Number(b.release.prerelease)
    || b.version.localeCompare(a.version, "en", { numeric: true })
  )[0];
  if (!recommended) {
    zipMatchStatus.textContent = "No matching ZIP is listed. Use Setup in a new folder to install a supported game version.";
  } else if (recommended.release.prerelease) {
    zipMatchStatus.textContent = `Ironmon v${recommended.version} matches your game and is available as a test release.`;
  } else {
    zipMatchStatus.textContent = `Ironmon v${recommended.version} is the newest compatible stable release.`;
  }
}

function renderUpdaterHistory(releases) {
  const contents = document.createDocumentFragment();
  const groups = new Map();
  for (const release of releases) {
    const match = updaterTag.exec(release.tag_name);
    if (!match) continue;
    const [, version, series] = match;
    let list = groups.get(series);
    if (!list) {
      const group = seriesTemplate.content.cloneNode(true);
      group.querySelector("h3").textContent = series;
      list = group.querySelector("ul");
      groups.set(series, list);
      contents.append(group);
    }
    list.append(createReleaseRow(release, updater, version));
  }
  updater.status.textContent = groups.size ? "" : updater.emptyMessage;
  updater.status.hidden = groups.size > 0;
  updater.list.replaceChildren(contents);
}

async function refreshReleases() {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 15_000);
  requestError.hidden = true;
  savedReleaseFeed.hidden = true;
  for (const section of [game, updater]) {
    section.status.textContent = "Loading releases…";
    section.status.hidden = false;
    section.list.setAttribute("aria-busy", "true");
  }
  try {
    const feed = await loadReleaseData(controller.signal);
    gameReleaseFeed = feed;
    renderSetup(feed.releases);
    zipGameVersion.disabled = false;
    renderGameReleases();
    renderUpdaterHistory(feed.releases);
    savedReleaseFeed.hidden = !feed.saved;
  } catch {
    requestError.hidden = false;
    game.status.hidden = true;
    updater.status.hidden = true;
  } finally {
    game.list.setAttribute("aria-busy", "false");
    updater.list.setAttribute("aria-busy", "false");
    clearTimeout(timeout);
  }
}

retry.addEventListener("click", refreshReleases);
zipGameVersion.addEventListener("change", renderGameReleases);
document.querySelector("#find-matching-zip").addEventListener("click", () => {
  document.querySelector("#install-paths").open = true;
  document.querySelector("#install-zip").checked = true;
  zipGameVersion.focus({ preventScroll: true });
});
document.querySelector("#show-all-zips").addEventListener("click", () => {
  zipGameVersion.value = "";
  renderGameReleases();
  document.querySelector("#ironmon-title").focus({ preventScroll: true });
});
refreshReleases();
