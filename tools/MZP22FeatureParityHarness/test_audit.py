"""Adversarial unit checks for source feature diagnostics, completely offline."""

import unittest
from dataclasses import replace

from audit import FEATURES, ROOT, audit, classify, documented_names, mask_comments, registration, statement


def get(api):
    return next(f for f in FEATURES if f.api == api)


class FeatureAuditTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.report = audit(ROOT)

    def test_all_declared_contracts_have_expected_status(self):
        for item in self.report["features"]:
            with self.subTest(api=item["api"]):
                self.assertTrue(item["doc_declared"])
                self.assertEqual(item["status"], item["expected_source_status"])
                self.assertGreater(item["line"], 0)

    def test_no_source_path_present_is_marked_as_runtime_validated(self):
        for item in self.report["features"]:
            with self.subTest(api=item["api"]):
                self.assertEqual(item["behavior"], "UNTESTED_OFFLINE")
                self.assertEqual(item["v131_binary_parity"], "UNTESTED")

    def test_failures_are_actual_source_missing_functions(self):
        statuses = {item["api"]: item["status"] for item in self.report["features"]}
        self.assertEqual(sum(x.startswith("FAIL_") for x in statuses.values()), 10)
        self.assertEqual(sum(x == "SOURCE_PATH_PRESENT" for x in statuses.values()), 4)

    def test_comment_spoofing_does_not_create_registration(self):
        feature = get("merchant:buyItem")
        fake = '// Fill(m, "buyItem", Stub("buyItem", DynValue.False));\n'
        fake += '/* Fill(m, "buyItem", Stub("buyItem", DynValue.False)); */\n'
        self.assertEqual(classify(fake, feature)[0], "ABSENT")

    def test_literal_double_slash_is_not_comment(self):
        code = 'var url = "https://example.invalid/path"; // comment\n'
        masked = mask_comments(code)
        self.assertIn('https://example.invalid/path', masked)
        self.assertNotIn('comment', masked)

    def test_multiline_statement_not_cut_at_inner_semicolons(self):
        snippet = 'Fill(q, "path", DynValue.NewCallback((_, a) => { WarnSnow("quest:path"); return DynValue.Nil; }));'
        self.assertEqual(statement(snippet, 0), snippet)

    def test_removed_binding_is_absent(self):
        f = get("quest:objectiveText")
        src = 'Fill(q, "text", DynValue.NewCallback((_, a) => Str("ok")));'
        self.assertEqual(classify(src, f)[0], "ABSENT")

    def test_unexpected_dynamic_implementation_demands_review(self):
        f = get("quest:objectiveText")
        src = 'Fill(q, "objectiveText", DynValue.NewCallback((_, a) => Str(_launch.QuestObjectiveText(42))));'
        self.assertEqual(classify(src, f)[0], "REVIEW")

    def test_changed_helper_does_not_look_like_stub(self):
        f = get("merchant:buyItem")
        source = '''DynValue Stub(string fn, DynValue ret) => DynValue.NewCallback((_, a) =>
            { return DynValue.NewBoolean(_launch.BuyItem(_pid)); });
            Fill(m, "buyItem", Stub("buyItem", DynValue.False));'''
        self.assertEqual(classify(source, f)[0], "REVIEW")

    def test_foreach_name_must_be_present_in_actual_group(self):
        f = get("mount:toggleRiding")
        source = '''DynValue M(string fn, DynValue ret) => DynValue.NewCallback((_, a) => { WarnSnowNotYet("mount:" + fn); return ret; });
          foreach (var n in new[] { "hasMount", "feedMount" }) Fill(mount, n, M(n, DynValue.False));'''
        self.assertEqual(classify(source, f)[0], "ABSENT")

    def test_foreach_no_helper_definition_demands_review(self):
        f = get("mount:toggleRiding")
        source = 'foreach (var n in new[] { "hasMount", "toggleRiding" }) Fill(mount, n, M(n, DynValue.False));'
        self.assertEqual(classify(source, f)[0], "REVIEW")

    def test_documentation_parser_requires_real_signature(self):
        self.assertNotIn("mount:toggleRiding", documented_names({"categories": [
            {"methods": [{"sig": [{"ns": "mount:", "name": "getEnergy"}]}]}
        ]}))

    def test_registered_dynamic_source_does_not_prove_ack(self):
        feature = get("npc:replyByIdAndChangeMap")
        source = 'Fill(npc, "replyByIdAndChangeMap", DynValue.NewCallback((_, a) => _launch.NpcReplyById(_pid, 3)));'
        self.assertEqual(classify(source, feature)[0], "SOURCE_PATH_PRESENT")
        self.assertNotIn("ACK", classify(source, feature)[2])


if __name__ == "__main__":
    unittest.main(verbosity=2)
