"""Small synthetic image tests; no upstream downloads or installed sprite library."""

import gzip
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock
from urllib.error import HTTPError

from PIL import Image

import validate_catalog as validator


def png(visible=(), mode="RGBA"):
    """Create one row with exactly the requested nontransparent pixels."""
    image = Image.new("RGBA", (1920, 96))
    for index in visible:
        image.putpixel((index * 96 + 95, 95), (10, 20, 30, 1))
    stream = io.BytesIO()
    image.convert(mode).save(stream, format="PNG")
    return stream.getvalue()


class CatalogTests(unittest.TestCase):
    """Cover tile indexing, credit policy, transport handling and reproducibility."""

    def test_visible_alpha_checks_entire_tile(self):
        self.assertEqual([1, 19], validator.inspect_sheet(png((1, 19)))["visible_tiles"])
        self.assertEqual([], validator.inspect_sheet(png())["visible_tiles"])

    def test_opaque_rgb_and_palette_transparency(self):
        self.assertEqual(list(range(20)), validator.inspect_sheet(png(mode="RGB"))["visible_tiles"])
        image = Image.new("P", (1920, 96))
        image.putpalette([0, 0, 0, 255, 0, 0] + [0] * 762)
        image.info["transparency"] = 0
        image.putpixel((2 * 96, 0), 1)
        stream = io.BytesIO()
        image.save(stream, format="PNG")
        self.assertEqual([2], validator.inspect_sheet(stream.getvalue())["visible_tiles"])

    def test_corrupt_and_wrong_dimensions_fail(self):
        with self.assertRaises(Exception):
            validator.inspect_sheet(b"\x89PNG\r\n\x1a\ninvalid")
        stream = io.BytesIO()
        Image.new("RGBA", (96, 96)).save(stream, format="PNG")
        with self.assertRaises(ValueError):
            validator.inspect_sheet(stream.getvalue())

    def test_metadata_policy_and_decision_report(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            index, credits = root / "index", root / "credits"
            index.write_text("1.1.png\n1.1a.png\n1.2.png\n1.3.png\n1.4.png\n1.20.png\n1.5a.png\n")
            credits.write_text("1.1,artist,main,\n1.1a,artist,temp,\n1.2,japeal,main,\n"
                               "1.3,artist,alt,\n1.4,artist,main,\n1.20,artist,main,\n"
                               "1.5a,artist,main,\n1.6,artist,main,\n")
            sheets = {"1/1.png": validator.inspect_sheet(png((1,))),
                      "1/1a.png": {"status": "not_found"}}
            result = validator.check_catalog(validator.build_catalog(index, credits, sheets))
            self.assertEqual(["1.1"], result["catalogue"]["sprites"])
            self.assertEqual(["1.4"], result["catalogue"]["excluded"]["transparent"])
            self.assertEqual(["1.20"], result["catalogue"]["excluded"]["outside_sheet"])
            self.assertEqual(["1.1a", "1.5a"], result["catalogue"]["excluded"]["not_found"])
            self.assertEqual(result, validator.build_catalog(index, credits, sheets))
            result["catalogue"]["sprites"].append("1.4")
            with self.assertRaises(ValueError):
                validator.check_catalog(result)
            with self.assertRaises(KeyError):
                validator.build_catalog(index, credits, {})

    def test_not_modified_reuses_only_previously_validated_sheet(self):
        previous = dict(validator.inspect_sheet(png((2,))), etag='"unchanged"')

        def not_modified(request, timeout):
            self.assertEqual('"unchanged"', request.get_header("If-none-match"))
            raise HTTPError(request.full_url, 304, "Not modified", {}, None)

        self.assertEqual(previous, validator.refresh_sheet("1/1.png", previous, not_modified))

    def test_visible_temp_variant_survives_a_transparent_main(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            index, credits = root / "index", root / "credits"
            index.write_text("1.1.png\n1.1a.png\n")
            credits.write_text("1.1,artist,main,\n1.1a,artist,temp,\n")
            sheets = {"1/1.png": validator.inspect_sheet(png()),
                      "1/1a.png": validator.inspect_sheet(png((1,)))}
            result = validator.build_catalog(index, credits, sheets)
            self.assertEqual(["1.1a"], result["catalogue"]["sprites"])

    def test_missing_local_download_preserves_previous_catalogue(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            index, credits, output = root / "index", root / "credits", root / "output"
            index.write_text("1.1.png\n")
            credits.write_text("1.1,artist,main,\n")
            output.write_bytes(b"previous complete catalogue")
            arguments = ["validator", "--index", str(index), "--credits", str(credits),
                         "--output", str(output), "--cache", str(root / "cache"),
                         "--local-sheets", str(root)]
            with mock.patch("sys.argv", arguments), self.assertRaises(FileNotFoundError):
                validator.main()
            self.assertEqual(b"previous complete catalogue", output.read_bytes())
            self.assertFalse((root / "cache").exists())

    def test_changed_sheet_replaces_cached_visibility(self):
        class Response(io.BytesIO):
            headers = {"ETag": '"new"'}

        previous = dict(validator.inspect_sheet(png((2,))), etag='"old"')
        result = validator.refresh_sheet("1/1.png", previous, lambda *args, **kw: Response(png((3,))))
        self.assertEqual([3], result["visible_tiles"])
        self.assertNotEqual(previous["sha256"], result["sha256"])

    def test_only_404_becomes_an_unavailable_sheet(self):
        def failure(code):
            def open_request(request, timeout):
                raise HTTPError(request.full_url, code, "Failure", {}, None)
            return open_request

        self.assertEqual({"status": "not_found"}, validator.refresh_sheet("1/1.png", opener=failure(404)))
        with self.assertRaises(HTTPError):
            validator.refresh_sheet("1/1.png", opener=failure(403))
        with self.assertRaises(HTTPError):
            validator.refresh_sheet("1/1.png", opener=failure(304))

    def test_committed_catalogue_is_complete_and_content_addressed(self):
        path = Path(__file__).resolve().parents[2] / "resources/sprites/validated_custom_sprites.json.gz"
        document = validator.check_catalog(json.loads(gzip.decompress(path.read_bytes())))
        self.assertTrue(document["catalogue"]["sprites"])


if __name__ == "__main__":
    unittest.main()
