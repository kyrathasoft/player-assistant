import hashlib
import importlib.util
import io
import json
import errno
import pathlib
import stat
import sys
import tempfile
import types
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
MODULE_PATH = ROOT / "restore_dreamhost_pwa.py"
SPEC = importlib.util.spec_from_file_location("restore_dreamhost_pwa", MODULE_PATH)
restore = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = restore
SPEC.loader.exec_module(restore)


class DreamHostRestoreTests(unittest.TestCase):
    def test_target_contract_is_fixed_to_current_dreamhost_layout(self):
        self.assertEqual(restore.SSH_HOST, "pdx1-shared-a1-13.dreamhost.com")
        self.assertEqual(restore.SSH_USER, "dh_4gg2za")
        self.assertEqual(restore.ORIGIN, "https://bryanmiller.us")
        self.assertEqual(restore.PUBLIC_ROOT, "/home/dh_4gg2za/bryanmiller.us")
        self.assertEqual(restore.PRIVATE_ROOT, "/home/dh_4gg2za/player-assistant-broker")
        self.assertEqual(restore.PHP_BINARY, "/usr/bin/php")
        self.assertEqual(
            restore.DEFAULT_KEY,
            pathlib.Path(r"C:\Users\Bryan\.ssh\dreamhost_player_assistant"),
        )

    def test_parser_defaults_to_read_only_preflight(self):
        arguments = restore.build_parser().parse_args([])
        self.assertEqual(arguments.action, "preflight")

    def test_install_requires_exact_production_confirmation(self):
        with self.assertRaisesRegex(ValueError, "confirmation"):
            restore.require_install_confirmation(None)
        with self.assertRaisesRegex(ValueError, "confirmation"):
            restore.require_install_confirmation("example.com")
        restore.require_install_confirmation("bryanmiller.us")

    def test_transaction_ids_are_strict(self):
        restore.validate_transaction_id("20260815T120000Z-deadbeef")
        for value in ["../manifest", "deadbeef", "20260815T120000Z-DEADBEEF"]:
            with self.assertRaisesRegex(ValueError, "transaction ID"):
                restore.validate_transaction_id(value)

    def test_artifact_discovery_requires_valid_sidecar(self):
        with tempfile.TemporaryDirectory() as temporary:
            dist = pathlib.Path(temporary)
            installer = dist / "install-player-assistant-web.php"
            expected_installer = dist / "canonical-installer.php"
            payload = dist / "player-assistant-web-payload-1.2.3.tar"
            sidecar = pathlib.Path(str(payload) + ".sha256")
            installer.write_text("<?php\n", encoding="utf-8")
            expected_installer.write_text("<?php\n", encoding="utf-8")
            payload.write_bytes(b"payload")
            digest = hashlib.sha256(b"payload").hexdigest()
            sidecar.write_text(f"{digest}  {payload.name}\n", encoding="utf-8")
            artifacts = restore.discover_artifacts(dist, expected_installer=expected_installer)
            self.assertEqual(artifacts.payload_sha256, digest)
            sidecar.write_text(f"{'0' * 64}  {payload.name}\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "checksum"):
                restore.discover_artifacts(dist, expected_installer=expected_installer)
            sidecar.write_text(f"{digest}  {payload.name}\n", encoding="utf-8")
            installer.write_text("<?php // replaced\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "installer.*integrity"):
                restore.discover_artifacts(dist, expected_installer=expected_installer)

    def test_install_command_pins_paths_and_local_verification(self):
        command = restore.build_install_command(
            "/home/dh_4gg2za/.player-assistant-restore/run/payload.tar",
            "/home/dh_4gg2za/.player-assistant-restore/run/install.php",
        )
        self.assertIn("--origin=https://bryanmiller.us", command)
        self.assertIn("--public-root=/home/dh_4gg2za/bryanmiller.us", command)
        self.assertIn("--private-root=/home/dh_4gg2za/player-assistant-broker", command)
        self.assertIn("--verification=local", command)
        self.assertNotIn("--config-source", command)

    def test_parse_installer_result_uses_only_final_json_object(self):
        parsed = restore.parse_installer_result(
            "notice\n" + json.dumps({"status": "installed_pending_https_verification", "transaction_id": "20260815T120000Z-deadbeef"}) + "\n"
        )
        self.assertEqual(parsed["transaction_id"], "20260815T120000Z-deadbeef")
        with self.assertRaisesRegex(ValueError, "JSON"):
            restore.parse_installer_result("notice only")

    def test_remote_cleanup_is_restricted_to_approved_run_directory(self):
        restore.validate_remote_run_directory(
            "/home/dh_4gg2za/.player-assistant-restore/20260815T120000Z-deadbeef"
        )
        for value in [
            "/home/dh_4gg2za/.player-assistant-restore",
            "/home/dh_4gg2za/.player-assistant-restore/../player-assistant-broker",
            "/home/dh_4gg2za/player-assistant-broker",
        ]:
            with self.assertRaisesRegex(ValueError, "run directory"):
                restore.validate_remote_run_directory(value)

    def test_upload_verifies_remote_sha256_not_only_size(self):
        class FakeSftp:
            def __init__(self, corrupt=False):
                self.files = {}
                self.corrupt = corrupt

            def put(self, local, remote, confirm=True):
                self.files[remote] = pathlib.Path(local).read_bytes()

            def chmod(self, remote, mode):
                return None

            def posix_rename(self, source, target):
                data = self.files.pop(source)
                if self.corrupt:
                    data = bytes([data[0] ^ 1]) + data[1:]
                self.files[target] = data

            def stat(self, remote):
                return types.SimpleNamespace(st_size=len(self.files[remote]))

            def open(self, remote, mode):
                return io.BytesIO(self.files[remote])

        with tempfile.TemporaryDirectory() as temporary:
            local = pathlib.Path(temporary) / "artifact.bin"
            local.write_bytes(b"verified-upload")
            session = restore.DreamHostSession()
            session.sftp = FakeSftp()
            session.upload_file(local, "/remote/artifact.bin")
            session.sftp = FakeSftp(corrupt=True)
            with self.assertRaisesRegex(restore.RestoreError, "SHA-256 mismatch"):
                session.upload_file(local, "/remote/artifact.bin")

    def test_transaction_discovery_fails_closed(self):
        class ListingFailure:
            def lstat(self, root):
                return types.SimpleNamespace(st_mode=stat.S_IFDIR | 0o700)

            def normalize(self, root):
                return root

            def listdir(self, root):
                raise PermissionError(errno.EACCES, "denied")

        session = restore.DreamHostSession()
        session.sftp = ListingFailure()
        with self.assertRaisesRegex(restore.RestoreError, "transaction root"):
            session.unresolved_transactions()

        class MissingRoot:
            def lstat(self, root):
                raise FileNotFoundError(errno.ENOENT, "missing")

        session.sftp = MissingRoot()
        self.assertEqual(session.unresolved_transactions(), [])

    def test_transaction_discovery_reports_malformed_and_unknown_state(self):
        class FakeSftp:
            def lstat(self, root):
                return types.SimpleNamespace(st_mode=stat.S_IFDIR | 0o700)

            def normalize(self, root):
                return root

            def listdir(self, root):
                return [
                    "not-a-transaction",
                    "20260815T120000Z-deadbeef",
                    "20260815T120001Z-deadbee0",
                ]

            def open(self, path, mode):
                if "deadbeef" in path:
                    return io.BytesIO(json.dumps([]).encode())
                return io.BytesIO(json.dumps({"status": "mystery"}).encode())

        session = restore.DreamHostSession()
        session.sftp = FakeSftp()
        self.assertEqual(
            session.unresolved_transactions(),
            [
                {"transaction_id": "not-a-transaction", "status": "invalid_directory_name"},
                {"transaction_id": "20260815T120000Z-deadbeef", "status": "invalid_manifest"},
                {"transaction_id": "20260815T120001Z-deadbee0", "status": "unknown_state"},
            ],
        )

    def test_transaction_discovery_rejects_symlinked_root(self):
        class SymlinkRoot:
            def lstat(self, root):
                return types.SimpleNamespace(st_mode=stat.S_IFLNK | 0o777)

            def normalize(self, root):
                return "/tmp/redirected"

        session = restore.DreamHostSession()
        session.sftp = SymlinkRoot()
        with self.assertRaisesRegex(restore.RestoreError, "transaction root"):
            session.unresolved_transactions()

    def test_transaction_action_result_requires_exact_id_and_terminal_status(self):
        transaction_id = "20260815T120000Z-deadbeef"
        restore.validate_transaction_action_result(
            "finalize", transaction_id, {"status": "finalized", "transaction_id": transaction_id}
        )
        restore.validate_transaction_action_result(
            "rollback", transaction_id, {"status": "rolled_back", "transaction_id": transaction_id}
        )
        for action, result in [
            ("finalize", {"status": "finalized", "transaction_id": "20260815T120001Z-deadbee0"}),
            ("finalize", {"status": "rolled_back", "transaction_id": transaction_id}),
            ("rollback", {"status": "finalized", "transaction_id": transaction_id}),
        ]:
            with self.assertRaisesRegex(restore.RestoreError, "transaction result"):
                restore.validate_transaction_action_result(action, transaction_id, result)

    def test_cleanup_command_reconfines_canonical_staging_paths(self):
        run = "/home/dh_4gg2za/.player-assistant-restore/20260815T120000Z-deadbeef"
        command = restore.build_cleanup_command(run)
        self.assertIn('readlink -f /home/dh_4gg2za/.player-assistant-restore', command)
        self.assertIn(f'readlink -f {run}', command)
        self.assertIn('[ ! -L /home/dh_4gg2za/.player-assistant-restore ]', command)
        self.assertIn(f'[ ! -L {run} ]', command)

    def test_https_verification_rejects_redirects_and_invalid_content(self):
        class FakeResponse:
            status = 200

            def __init__(self, url, body, final_url=None):
                self.url = url
                self.body = body
                self.final_url = final_url or url

            def __enter__(self):
                return self

            def __exit__(self, *args):
                return False

            def read(self, limit):
                return self.body

            def geturl(self):
                return self.final_url

        class FakeOpener:
            def __init__(self, bodies, redirect=False):
                self.bodies = bodies
                self.redirect = redirect

            def open(self, request, timeout):
                path = request.full_url.removeprefix(restore.ORIGIN)
                final_url = "https://redirect.example/" if self.redirect else request.full_url
                return FakeResponse(request.full_url, self.bodies[path], final_url)

        valid = {
            "/scarlethorizons/pwa/manifest.webmanifest": restore.LOCAL_MANIFEST.read_bytes(),
            "/scarlethorizons/pwa/service-worker.js": restore.LOCAL_SERVICE_WORKER.read_bytes(),
            "/scarlethorizons/api/v1/health": json.dumps({
                "service": "player-assistant-broker", "schema_version": 7, "status": "ok"
            }).encode(),
        }
        result = restore.verify_public_https(opener=FakeOpener(valid))
        self.assertEqual(result["/scarlethorizons/pwa/service-worker.js"], len(valid["/scarlethorizons/pwa/service-worker.js"]))
        with self.assertRaisesRegex(restore.RestoreError, "redirect"):
            restore.verify_public_https(opener=FakeOpener(valid, redirect=True))
        invalid = dict(valid)
        invalid["/scarlethorizons/pwa/manifest.webmanifest"] = b"{}"
        with self.assertRaisesRegex(restore.RestoreError, "manifest"):
            restore.verify_public_https(opener=FakeOpener(invalid))
        invalid = dict(valid)
        invalid["/scarlethorizons/api/v1/health"] = b'{"status":"ok"}'
        with self.assertRaisesRegex(restore.RestoreError, "health"):
            restore.verify_public_https(opener=FakeOpener(invalid))


if __name__ == "__main__":
    unittest.main()
