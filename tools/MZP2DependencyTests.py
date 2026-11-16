#!/usr/bin/env python3
"""Offline contract tests for MZP2 lexical inventory, no DLLs or process access."""

from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("MZP2DependencyInventory.py")
spec = importlib.util.spec_from_file_location("mzp2_inventory", SCRIPT)
assert spec and spec.loader
lib = importlib.util.module_from_spec(spec)
spec.loader.exec_module(lib)


class InventoryContractTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="mzp2-src-only-")
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name) / "GivreWork"
        self.root.mkdir()

    def put(self, name: str, data: str):
        dst = self.root / name
        dst.parent.mkdir(parents=True, exist_ok=True)
        dst.write_text(data, encoding="utf-8")
        return dst

    def test_no_sources_means_no_success(self):
        result = lib.collect(self.root)
        self.assertFalse(result["sourceProviderVerified"])
        self.assertFalse(result["prodCompilationCertified"])
        self.assertFalse(result["runtimeSessionOrAckCertified"])
        self.assertEqual(result["references"], [])

    def test_using_and_local_symbol_yield_line_level_evidence(self):
        self.put("GivreClone.Protocol/Forge.cs", "using GivreCore.Protobuf;\nclass A { byte[] F() => RawProtobuf.Encode(new ProtoField[0]); }\n")
        result = lib.collect(self.root)
        self.assertEqual(result["topFive"]["P1_PROTOBUF"]["count"], 2)
        self.assertEqual(result["topFive"]["P1_PROTOBUF"]["examples"][0]["line"], 2)
        self.assertIn("sha256", result["referencingFiles"]["GivreClone.Protocol/Forge.cs"])

    def test_unrelated_word_is_not_a_dependency(self):
        self.put("GivreClone.Protocol/Unrelated.cs", "class B { string RawProtobuf = \"not a dependency\"; }\n")
        result = lib.collect(self.root)
        self.assertEqual(result["topFive"]["P1_PROTOBUF"]["count"], 0)

    def test_comment_line_does_not_add_false_reference(self):
        self.put("GivreClone.Engine/Foo.cs", "// using GivreCore.Mitm;\n// TcpProxyServer\nclass X {}\n")
        result = lib.collect(self.root)
        self.assertEqual(result["topFive"]["P2_MITM_CAPTURE"]["count"], 0)
        self.assertEqual(result["references"], [])

    def test_alias_is_recorded_but_not_promoted(self):
        self.put("GivreClone.App/Alias.cs", "using CoreAccount = GivreLauncher.Core.Accounts.Account;\n")
        result = lib.collect(self.root)
        self.assertEqual(result["references"][0]["symbol"], "GivreLauncher.Core.Accounts.Account")
        self.assertEqual(result["referencingFiles"]["GivreClone.App/Alias.cs"]["references"][0]["kind"], "using")
        self.assertFalse(result["sourceProviderVerified"])

    def test_foreign_provider_declaration_is_candidate_only(self):
        self.put("GivreClone.Engine/Fake.cs", "namespace GivreCore.Mitm;\nclass TcpProxyServer {}\n")
        result = lib.collect(self.root)
        self.assertEqual(len(result["candidateProviderDeclarations"]), 1)
        self.assertFalse(result["sourceProviderVerified"])

    def test_generated_stub_folder_is_excluded(self):
        self.put("tools/MZP0RecoveryHarness/MZP0GivreCoreStubs.cs", "namespace GivreCore.Mitm; class TcpProxyServer {}\n")
        result = lib.collect(self.root)
        self.assertEqual(result["candidateProviderDeclarations"], [])

    def test_relay_assembly_is_not_core(self):
        self.put("GivreRelayAgent/GivreRelayAgent.csproj", "<Project><PropertyGroup><AssemblyName>GivreRelay</AssemblyName></PropertyGroup></Project>")
        self.put("GivreRelayAgent/AgentLink.cs", "namespace GivreRelayAgent; class AgentLink {}\n")
        result = lib.collect(self.root)
        self.assertFalse(result["relaySource"]["isSubstituteForCoreOrLauncher"])
        self.assertEqual(result["relaySource"]["assemblyName"], "GivreRelay")
        self.assertEqual(len(result["relaySource"]["files"]), 1)

    def test_snapshot_hash_changes_on_source_edit(self):
        target = self.put("GivreClone.Engine/Frame.cs", "using GivreCore.Mitm;\nclass Frame { TcpProxyServer Proxy; }\n")
        first = lib.collect(self.root)
        self.assertEqual(first["topFive"]["P2_MITM_CAPTURE"]["count"], 1)
        target.write_text("using GivreCore.Mitm;\nclass Frame { CapturedMessage Packet; }\n", encoding="utf-8")
        second = lib.collect(self.root)
        self.assertNotEqual(first["referencingFiles"]["GivreClone.Engine/Frame.cs"]["sha256"],
                            second["referencingFiles"]["GivreClone.Engine/Frame.cs"]["sha256"])

    def test_json_serialization_is_deterministic(self):
        self.put("GivreClone.App/B.cs", "using GivreLauncher.Core.Proxy;\nclass B { ProxySettings P; }\n")
        a = json.dumps(lib.collect(self.root), sort_keys=True)
        b = json.dumps(lib.collect(self.root), sort_keys=True)
        self.assertEqual(a, b)

    def test_real_project_reference_is_recorded_not_replaced(self):
        self.put("GivreClone.Protocol/GivreClone.Protocol.csproj",
                 '<Project><ItemGroup><ProjectReference Include="..\\..\\GivreProxy\\GivreCore\\GivreCore.csproj" /></ItemGroup></Project>')
        result = lib.collect(self.root)
        self.assertEqual(result["projectLinks"], [{"project": "GivreClone.Protocol/GivreClone.Protocol.csproj",
                                                   "include": "..\\..\\GivreProxy\\GivreCore\\GivreCore.csproj"}])
        self.assertFalse(result["sourceProviderVerified"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
