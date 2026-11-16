"""Adversarial isolated tests; no running provider, session, socket, or build."""
from pathlib import Path
import tempfile
import unittest
import zipfile

import scanner


class ProviderEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="mzp7-provider-")
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.repo = self.root / "GivreWork"
        self.repo.mkdir()

    def project(self, name="GivreCore", source=None, folder=None):
        folder = folder or self.root / "Providers" / name
        folder.mkdir(parents=True, exist_ok=True)
        path = folder / (name + ".csproj")
        path.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><AssemblyName>' + name + '</AssemblyName></PropertyGroup></Project>', encoding="utf-8")
        if source is not None:
            (folder / "Provider.cs").write_text(source, encoding="utf-8")
        return path

    def test_project_requires_matching_name_namespace_and_all_types(self):
        path = self.project(source="namespace GivreCore.Protobuf; public class ProtoField {} public enum WireType {} public class RawProtobuf {}")
        res = scanner.inspect_project(path, self.root)["providers"]["GivreCore"]
        self.assertEqual(res["level"], "SOURCE_PARTIAL_PROVIDER_SURFACE")
        self.assertIn("TcpProxyServer", res["requiredTypeNamesNotObserved"])
        self.assertFalse(res["runtimeOrAbiCertified"])
        self.assertFalse(res["externalSourceProvenanceCertified"])
        (path.parent / "Mitm.cs").write_text("namespace GivreCore.Mitm; public class TcpProxyServer {} public class CapturedMessage {} public enum Direction {}", encoding="utf-8")
        (path.parent / "Injection.cs").write_text("namespace GivreCore.Injection; public class DllInjector {}", encoding="utf-8")
        full = scanner.inspect_project(path, self.root)["providers"]["GivreCore"]
        self.assertEqual(full["level"], "SOURCE_NAME_SURFACE_CANDIDATE")
        self.assertFalse(full["runtimeOrAbiCertified"])

    def test_fake_namespace_types_in_comments_or_strings_do_not_promote(self):
        path = self.project(source='// namespace GivreCore.Protobuf; class ProtoField {}\nclass Other { string t = "namespace GivreCore.Mitm; class TcpProxyServer {}"; }')
        self.assertEqual(scanner.inspect_project(path, self.root)["providers"]["GivreCore"]["level"], "SOURCE_PARTIAL_PROVIDER_SURFACE")
        self.assertEqual(scanner.inspect_project(path, self.root)["source"]["namespaces"], [])

    def test_provider_namespace_and_type_must_share_source_file(self):
        path = self.project(source="namespace GivreCore.Protobuf; class NotAProviderType {}")
        (path.parent / "Other.cs").write_text("namespace Other; class ProtoField {} class RawProtobuf {}", encoding="utf-8")
        value = scanner.inspect_project(path, self.root)["providers"]["GivreCore"]
        self.assertEqual(value["requiredTypeNamesObserved"], [])

    def test_namesake_project_cannot_pass_as_launcher(self):
        path = self.project("GivreLauncher.Core", 'namespace Unrelated; public class GameLauncher {}')
        self.assertEqual(scanner.inspect_project(path, self.root)["providers"]["GivreLauncher.Core"]["level"], "SOURCE_PARTIAL_PROVIDER_SURFACE")

    def test_assembly_name_contradicts_filename(self):
        path = self.project("GivreCore", "namespace GivreCore.Protobuf; class ProtoField {}")
        path.write_text('<Project><PropertyGroup><AssemblyName>Different</AssemblyName></PropertyGroup></Project>', encoding="utf-8")
        inspected = scanner.inspect_project(path, self.root)
        self.assertTrue(inspected["assemblyNameContradictsProject"])
        self.assertFalse(inspected["providers"]["GivreCore"]["projectNameFound"])

    def test_zip_requires_project_and_source_and_checks_declared_assembly(self):
        filename = self.repo / "provider.zip"
        with zipfile.ZipFile(filename, "w") as z:
            z.writestr("GivreCore/GivreCore.csproj", '<Project Sdk="Microsoft.NET.Sdk" />')
            z.writestr("GivreCore/A.cs", "namespace GivreCore.Protobuf; public class ProtoField {}")
        result = scanner.inspect_archive(filename)
        self.assertEqual(result["level"], "ARCHIVED_SOURCE_DECLARATIONS")
        self.assertEqual(result["providers"]["GivreCore"]["level"], "SOURCE_PARTIAL_PROVIDER_SURFACE")

    def test_zip_slip_member_rejected(self):
        filename = self.repo / "bad.zip"
        with zipfile.ZipFile(filename, "w") as z:
            z.writestr("../GivreCore/GivreCore.csproj", "<Project />")
        with self.assertRaisesRegex(ValueError, "ARCHIVE_PATH_OR_LINK_REJECTED"):
            scanner.inspect_archive(filename)

    def test_zip_source_ratio_cap(self):
        filename = self.repo / "bomb.zip"
        with zipfile.ZipFile(filename, "w", compression=zipfile.ZIP_DEFLATED) as z:
            z.writestr("GivreCore/GivreCore.csproj", '<Project Sdk="Microsoft.NET.Sdk" />')
            z.writestr("GivreCore/Fake.cs", "A" * (200 * 1024))
        with self.assertRaisesRegex(ValueError, "ARCHIVE_COMPRESSION_LIMIT"):
            scanner.inspect_archive(filename)

    def test_bogus_dll_is_binary_evidence_only(self):
        path = self.repo / "GivreCore.dll"
        path.write_bytes(b"GivreCore.Protobuf.RawProtobuf\x00not-a-real-managed-assembly")
        result = scanner.inspect_binary(path)
        self.assertEqual(result["level"], "BINARY_BYTES_ONLY")
        self.assertEqual(result["definedTypes"], [])
        self.assertFalse(result["providerApiCertified"])

    def test_dir_bounded_and_no_recursive_probe(self):
        directory = self.repo / "Detached"
        directory.mkdir()
        nested = directory / "GivreCore"
        nested.mkdir()
        self.project(folder=nested)
        self.assertEqual(scanner.scan_candidate(directory, self.root)[0]["level"], "NO_CANDIDATE_IN_TOP_LEVEL")
        for i in range(scanner.MAX_DIR_ENTRIES + 1):
            (directory / f"entry{i}.txt").touch()
        self.assertEqual(scanner.scan_candidate(directory, self.root)[0]["reason"], "DIRECTORY_LIMIT")

    def test_outside_root_and_symlink_rejected(self):
        with self.assertRaisesRegex(ValueError, "OUTSIDE_APPROVED_ROOT"):
            scanner.root_guard(self.root.parent / "untrusted.dll", self.root)
        outside = self.root / "escape.dll"
        target = self.root.parent / ("not-used-" + self.root.name)
        try:
            outside.symlink_to(target)
        except (OSError, NotImplementedError):
            self.skipTest("host does not allow symlink creation")
        with self.assertRaisesRegex(ValueError, "REPARSE_POINT"):
            scanner.root_guard(outside, self.root)

    def test_unknown_extension_rejected_without_reading(self):
        path = self.repo / "givre-live-mini.dmp"
        path.write_bytes(b"NEVER_READ_THIS")
        result = scanner.scan_candidate(path, self.root)[0]
        self.assertEqual(result["reason"], "UNSUPPORTED_EXTENSION")

    def test_missing_provider_never_becomes_success(self):
        for relative in scanner.CONSUMERS:
            path = self.repo / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('<Project><ItemGroup><ProjectReference Include="../../GivreProxy/GivreCore/GivreCore.csproj" /></ItemGroup></Project>', encoding="utf-8")
        report = scanner.run(self.repo, self.root, ["../missing/GivreCore.csproj"])
        self.assertEqual(report["verdict"]["GivreCore"], "UNRESOLVED_PROVIDER")
        self.assertFalse(report["certified"]["cs0246Resolved"])
        self.assertEqual(report["candidates"][0]["level"], "MISSING")


if __name__ == "__main__":
    unittest.main(verbosity=2)
