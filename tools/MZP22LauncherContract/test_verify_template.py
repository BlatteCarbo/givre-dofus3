import tempfile
import unittest
from pathlib import Path

from verify_template import BASE, WINDOWS, PINS, default_root, verify


class LauncherContractTests(unittest.TestCase):
    def setUp(self):
        self.test_dir = tempfile.TemporaryDirectory(prefix="mzp22-launcher-test-")
        self.root = Path(self.test_dir.name)

    def tearDown(self):
        self.test_dir.cleanup()

    def test_absent_configs_fail_closed(self):
        with self.assertRaises(ValueError):
            verify(self.root)

    def test_forged_configs_are_rejected(self):
        (self.root / BASE).write_text('arguments:\n  - "--port"\n', encoding="utf-8")
        (self.root / WINDOWS).write_text('maxInstances: true\n', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "digest differs"):
            verify(self.root)

    def test_expected_pins_and_required_pairs(self):
        self.assertEqual(len(PINS), 2)
        self.assertTrue(all(len(digest) == 64 for digest in PINS.values()))

    def test_current_installed_release_contract(self):
        report = verify(default_root())
        self.assertEqual(report["status"], "STATIC_TEMPLATE_VERIFIED_SESSION_REQUIRED")
        self.assertEqual(len(report["argument_template"]), 21)
        self.assertFalse(report["gameplay_or_ticket_certified"])


if __name__ == "__main__":
    unittest.main()
