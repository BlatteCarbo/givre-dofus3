import json
from pathlib import Path
import tempfile
import unittest

from prepare_review import prepare


class ReviewReceiptTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        root = Path(self.tmp.name)
        self.game = root / "Dofus-dofus3"
        self.data = root / "GivreClone.App" / "bin" / "Release" / "net10.0" / "data"
        self.data.mkdir(parents=True)
        self.old = root / "old.proto"
        self.new = root / "new.proto"
        self.old.write_bytes(b'message Test { optional int64 test = 1; }' * 4000)
        self.new.write_bytes(self.old.read_bytes())
        (self.data / "bundle-build.json").write_text('{"build":"3.7.11.11"}')
        for filename in ("matching_current.json", "fields.json"):
            (self.data / filename).write_text('SYNTHETIC-NO-GAME-MAPPING')
        files = {
            "Dofus_Data/StreamingAssets/version": b"Version=3.7.12.12\n",
            "Dofus.exe": b"MZtest",
            "GameAssembly.dll": b"MZassembly",
            "Dofus_Data/il2cpp_data/Metadata/global-metadata.dat": b"\xaf\x1b\xb1\xfa",
        }
        for filename, data in files.items():
            p = self.game.joinpath(*filename.split("/"))
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_bytes(data)

    def tearDown(self):
        self.tmp.cleanup()

    def run_review(self, **overrides):
        kwargs = dict(game_root=self.game, data_root=self.data,
                      source_proto=self.old, target_proto=self.new,
                      source_build="3.7.11.11", target_build="3.7.12.12",
                      operator_confirmed=True, write=False)
        kwargs.update(overrides)
        return prepare(**kwargs)

    def test_dry_run_never_writes_receipt(self):
        report = self.run_review()
        self.assertEqual(report["mode"], "dry_run")
        self.assertFalse((self.data / "compatibility-review.json").exists())
        self.assertFalse(report["serverAck"])
        self.assertFalse(report["gameplayInjectionAuthorized"])

    def test_explicit_write_keeps_original_mapping_build(self):
        report = self.run_review(write=True)
        self.assertEqual(report["mode"], "written")
        receipt = json.loads((self.data / "compatibility-review.json").read_text())
        self.assertEqual(receipt["sourceMappingBuild"], "3.7.11.11")
        self.assertEqual(receipt["installedGameBuild"], "3.7.12.12")
        self.assertEqual(receipt["sourceProtoSha256"], receipt["targetProtoSha256"])
        self.assertEqual(json.loads((self.data / "bundle-build.json").read_text())["build"], "3.7.11.11")

    def test_changed_proto_never_accepted(self):
        self.new.write_bytes(self.new.read_bytes() + b'\nmessage Changed {}')
        with self.assertRaisesRegex(ValueError, "DIFFERENT"):
            self.run_review(write=True)
        self.assertFalse((self.data / "compatibility-review.json").exists())

    def test_changed_target_version_never_accepted(self):
        (self.game / "Dofus_Data/StreamingAssets/version").write_text("Version=3.7.13.13\n")
        with self.assertRaisesRegex(ValueError, "version"):
            self.run_review(write=True)

    def test_requires_explicit_operator_confirmation(self):
        with self.assertRaisesRegex(ValueError, "confirmation"):
            self.run_review(operator_confirmed=False, write=True)


if __name__ == "__main__":
    unittest.main()
