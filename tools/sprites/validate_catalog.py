"""Build a content-addressed custom-sprite catalogue outside normal PR checks."""

import argparse
from concurrent.futures import ThreadPoolExecutor
import csv
import gzip
import hashlib
import io
import json
from pathlib import Path
import re
import time
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

from PIL import Image

SCHEMA = 1
TILE_SIZE = 96
COLUMNS = 20
ORIGIN = "https://infinitefusion.net/customsprites/spritesheets/spritesheets_custom/"
SPRITE = re.compile(r"([1-9][0-9]*)\.([1-9][0-9]*)([a-zA-Z]*)")
MAX_SHEET_BYTES = 64 * 1024 * 1024


def canonical(value):
    """Return stable bytes shared by catalogue identity and the Ruby consumer."""
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode()


def digest(data):
    """Identify the exact validated source bytes."""
    return hashlib.sha256(data).hexdigest()


def atomic_write(path, data):
    """Keep the previous complete result when a scan fails."""
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(data)
    temporary.replace(path)


def permitted_sprites(index_path, credits_path):
    """Intersect exact indexed variants with the existing main/temp author rules."""
    indexed = {line.strip()[:-4] for line in index_path.read_text(encoding="utf-8-sig").splitlines()
               if line.strip().endswith(".png") and SPRITE.fullmatch(line.strip()[:-4])}
    permitted = set()
    with credits_path.open(encoding="utf-8-sig", newline="") as source:
        for row in csv.reader(source):
            if (len(row) >= 3 and row[0] in indexed and row[1].strip().lower() != "japeal"
                    and row[2].strip().lower() in ("main", "temp")):
                permitted.add(row[0])
    if not permitted:
        raise ValueError("No permitted custom sprites in the index and credits.")
    return sorted(permitted)


def sheet_key(sprite):
    """Map the head and alternate to the upstream sheet name."""
    head, _, alternate = SPRITE.fullmatch(sprite).groups()
    return f"{head}/{head}{alternate}.png"


def inspect_sheet(data):
    """Decode every tile; file existence or a valid PNG header is insufficient."""
    with Image.open(io.BytesIO(data)) as source:
        if source.format != "PNG":
            raise ValueError("A sprite sheet must be PNG.")
        source.load()
        if source.width != TILE_SIZE * COLUMNS or source.height % TILE_SIZE:
            raise ValueError(f"Unexpected sprite sheet dimensions: {source.size}.")
        alpha = source.convert("RGBA").getchannel("A")
        visible = []
        for index in range(COLUMNS * source.height // TILE_SIZE):
            x, y = (index % COLUMNS) * TILE_SIZE, (index // COLUMNS) * TILE_SIZE
            if alpha.crop((x, y, x + TILE_SIZE, y + TILE_SIZE)).getbbox() is not None:
                visible.append(index)
        return {"status": "ok", "sha256": digest(data), "width": source.width,
                "height": source.height, "visible_tiles": visible}


def refresh_sheet(key, previous=None, opener=urlopen):
    """Reuse validated tile results on 304; never turn transport errors into exclusions."""
    headers = {"User-Agent": "Ironmon-Sprite-Validation/1"}
    if previous and previous.get("status") == "ok":
        if previous.get("etag"):
            headers["If-None-Match"] = previous["etag"]
        if previous.get("last_modified"):
            headers["If-Modified-Since"] = previous["last_modified"]
    request = Request(ORIGIN + key, headers=headers)
    for attempt in range(4):
        try:
            with opener(request, timeout=90) as response:
                data = response.read(MAX_SHEET_BYTES + 1)
                if len(data) > MAX_SHEET_BYTES:
                    raise ValueError(f"Sprite sheet exceeds size limit: {key}")
                result = inspect_sheet(data)
                result["etag"] = response.headers.get("ETag")
                result["last_modified"] = response.headers.get("Last-Modified")
                return result
        except HTTPError as error:
            if error.code == 304 and previous and previous.get("status") == "ok":
                return previous
            if error.code == 404:
                return {"status": "not_found"}
            if error.code not in (429, 500, 502, 503, 504) or attempt == 3:
                raise
        except (URLError, TimeoutError):
            if attempt == 3:
                raise
        time.sleep(2 ** attempt)
    raise RuntimeError(f"Failed to validate {key}")


def build_catalog(index_path, credits_path, sheets):
    """Publish an auditable decision for every permitted variant, with no partial scans."""
    permitted = permitted_sprites(index_path, credits_path)
    visible = []
    excluded = {"transparent": [], "outside_sheet": [], "not_found": []}
    for sprite in permitted:
        key = sheet_key(sprite)
        sheet = sheets[key]
        body = int(SPRITE.fullmatch(sprite)[2])
        if sheet["status"] == "not_found":
            excluded["not_found"].append(sprite)
        elif sheet["status"] != "ok":
            raise ValueError(f"Incomplete sheet validation: {key}")
        elif body >= COLUMNS * sheet["height"] // TILE_SIZE:
            excluded["outside_sheet"].append(sprite)
        elif body not in sheet["visible_tiles"]:
            excluded["transparent"].append(sprite)
        else:
            visible.append(sprite)
    if not visible:
        raise ValueError("Validation produced no usable sprites.")
    payload = {"schema_version": SCHEMA, "tile_size": TILE_SIZE, "columns": COLUMNS,
               "origin": ORIGIN, "inputs": {"custom_sprites": digest(index_path.read_bytes()),
                                           "credits": digest(credits_path.read_bytes())},
               "sprites": visible, "excluded": excluded,
               "sheets": {key: {k: v for k, v in sheets[key].items()
                                 if k not in ("etag", "last_modified", "visible_tiles")}
                          for key in sorted({sheet_key(sprite) for sprite in permitted})}}
    return {"catalogue_id": digest(canonical(payload)), "catalogue": payload}


def check_catalog(document):
    """Validate the compact input without opening or downloading image sheets."""
    payload = document["catalogue"]
    if (payload["schema_version"] != SCHEMA or payload["tile_size"] != TILE_SIZE
            or payload["columns"] != COLUMNS or payload["origin"] != ORIGIN
            or document["catalogue_id"] != digest(canonical(payload))):
        raise ValueError("Invalid catalogue identity or schema.")
    sprites = payload["sprites"]
    if not sprites or sprites != sorted(set(sprites)) or not all(SPRITE.fullmatch(s) for s in sprites):
        raise ValueError("Invalid visible sprite list.")
    for sprite in sprites:
        sheet = payload["sheets"][sheet_key(sprite)]
        if sheet["status"] != "ok" or not re.fullmatch(r"[0-9a-f]{64}", sheet["sha256"]):
            raise ValueError("Visible sprite does not identify a validated sheet.")
    return document


def main():
    """Scan a complete explicit source snapshot, or verify a published catalogue offline."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--index", type=Path)
    parser.add_argument("--credits", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--cache", type=Path)
    parser.add_argument("--local-sheets", type=Path)
    parser.add_argument("--known-404", type=Path)
    parser.add_argument("--check", type=Path)
    args = parser.parse_args()
    if args.check:
        document = check_catalog(json.loads(gzip.decompress(args.check.read_bytes())))
    else:
        if not all((args.index, args.credits, args.output, args.cache)):
            parser.error("A scan requires --index, --credits, --output and --cache.")
        keys = sorted({sheet_key(sprite) for sprite in permitted_sprites(args.index, args.credits)})
        cached = json.loads(args.cache.read_text()) if args.cache.exists() else {}
        previous = cached.get("sheets", {}) if cached.get("schema_version") == SCHEMA else {}
        known_404 = set(json.loads(args.known_404.read_text())) if args.known_404 else set()

        def scan(key):
            if not args.local_sheets:
                return key, refresh_sheet(key, previous.get(key))
            path = args.local_sheets / key
            if not path.exists() and ORIGIN + key in known_404:
                return key, {"status": "not_found"}
            # An absent local download is not evidence of a missing upstream sprite.
            return key, inspect_sheet(path.read_bytes())

        sheets = {}
        with ThreadPoolExecutor(max_workers=4) as executor:
            for key, result in executor.map(scan, keys):
                sheets[key] = result
                if len(sheets) % 100 == 0:
                    print(f"Validated {len(sheets)} / {len(keys)} sheets", flush=True)
        document = check_catalog(build_catalog(args.index, args.credits, sheets))
        atomic_write(args.cache, canonical({"schema_version": SCHEMA, "sheets": sheets}))
        atomic_write(args.output, gzip.compress(canonical(document) + b"\n", mtime=0))
    payload = document["catalogue"]
    print(f"Catalogue {document['catalogue_id']}: {len(payload['sprites'])} visible variants; "
          + ", ".join(f"{reason}: {len(entries)}" for reason, entries in payload["excluded"].items()))


if __name__ == "__main__":
    main()
