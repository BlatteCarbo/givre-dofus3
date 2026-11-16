"""Offline synthetic receipts only; no partner material, HTTP or secrets."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

MODULE = Path(__file__).with_name("partner_review_contract.py")
SPEC = importlib.util.spec_from_file_location("partner_review_contract", MODULE)
mod = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(mod)


class PartnerRightsTests(unittest.TestCase):
    def setUp(self):
        self.work = tempfile.TemporaryDirectory(prefix="dofusdb-review-test-")
        self.addCleanup(self.work.cleanup)
        self.source = Path(self.work.name) / "source.json"
        self.source.write_bytes(b'{"synthetic":true,"version":"3.7.11.11"}')
        self.args = dict(
            source_path=self.source,
            source_surface="api",
            game_version="3.7.11.11",
            operator_label="operator-local",
            agreement_reference="partner-scope-demo",
            permit_ai=True,
            permit_mcp=True,
            statement=mod.STATEMENT,
            scope_site=False,
            scope_api=True,
            valid_until="2099-12-31",
        )

    def receipt(self):
        return mod.create_attestation(**self.args)

    def verify(self, data=None, **override):
        kwargs = dict(source_path=self.source, required_surface="api",
                      required_version="3.7.11.11")
        kwargs.update(override)
        return mod.verify_attestation(self.receipt() if data is None else data, **kwargs)

    def test_attested_scope_is_not_independent_verification(self):
        got = self.verify()
        self.assertTrue(got["allowed"])
        self.assertEqual("OPERATOR_ATTESTED_NOT_VERIFIED", got["status"])
        self.assertFalse(got["independentlyVerified"])
        self.assertFalse(got["runtimeAuthorized"])

    def test_incomplete_scope_blocks(self):
        with self.assertRaises(mod.ReviewRefused):
            mod.create_attestation(**(self.args | {"permit_ai": False}))
        with self.assertRaises(mod.ReviewRefused):
            mod.create_attestation(**(self.args | {"permit_mcp": False}))

    def test_source_site_not_implied_by_api_permission(self):
        self.assertFalse(self.verify(required_surface="site")["allowed"])

    def test_wrong_source_version_blocks(self):
        self.assertFalse(self.verify(required_version="3.7.12.12")["allowed"])

    def test_unknown_and_invalid_version_block(self):
        for version in ("UNKNOWN", "3.7", "3.7.011.11", "3.7.11.11'"):
            with self.subTest(version=version):
                with self.assertRaises(mod.ReviewRefused):
                    mod.create_attestation(**(self.args | {"game_version": version}))

    def test_changes_to_source_invalidate_attestation(self):
        claim = self.receipt()
        self.source.write_bytes(b'{"synthetic":false}')
        self.assertFalse(self.verify(claim)["allowed"])

    def test_claim_digest_tampering_fails(self):
        claim = self.receipt()
        claim["sourceSha256"] = "0" * 64
        self.assertFalse(self.verify(claim)["allowed"])

    def test_claim_rights_tampering_fails(self):
        claim = self.receipt()
        claim["scope"]["site"] = True
        self.assertFalse(self.verify(claim)["allowed"])

    def test_cannot_claim_independent_verification(self):
        claim = self.receipt()
        claim["independentlyVerified"] = True
        self.assertFalse(self.verify(claim)["allowed"])

    def test_cannot_claim_runtime_access(self):
        claim = self.receipt()
        claim["runtimeAuthorized"] = True
        self.assertFalse(self.verify(claim)["allowed"])

    def test_explicit_attestation_statement_required(self):
        with self.assertRaises(mod.ReviewRefused):
            mod.create_attestation(**(self.args | {"statement": "yes"}))

    def test_unknown_agreement_reference_refused(self):
        with self.assertRaises(mod.ReviewRefused):
            mod.create_attestation(**(self.args | {"agreement_reference": ""}))

    def test_expired_receipt_refused(self):
        with self.assertRaises(mod.ReviewRefused):
            mod.create_attestation(**(self.args | {"valid_until": "2000-01-01"}))

    def test_large_source_refused(self):
        with self.assertRaises(mod.ReviewRefused):
            mod.create_attestation(**(self.args | {"source_path": Path(self.work.name)}))

    def test_no_duplicate_json_fields(self):
        path = Path(self.work.name) / "duplicate.json"
        path.write_text('{"format":"x","format":"y"}', encoding="utf-8")
        with self.assertRaises(mod.ReviewRefused):
            mod.read_receipt(path)

    def test_malformed_scope_refused(self):
        claim = self.receipt()
        claim["scope"]["mcp"] = "yes"
        self.assertFalse(self.verify(claim)["allowed"])

    def test_extra_field_refused(self):
        claim = self.receipt()
        claim["downloadURL"] = "https://example.invalid"
        self.assertFalse(self.verify(claim)["allowed"])

    def test_receipt_size_limit(self):
        path = Path(self.work.name) / "giant.json"
        path.write_text("x" * 9000)
        with self.assertRaises(mod.ReviewRefused):
            mod.read_receipt(path)


if __name__ == "__main__":
    unittest.main()
