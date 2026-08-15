#!/usr/bin/env python
"""DreamHost-specific controller for restoring the Player Assistant web stack."""

from __future__ import annotations

import argparse
import dataclasses
import errno
import hashlib
import hmac
import json
import pathlib
import re
import shlex
import stat
import sys
import time
import urllib.error
import urllib.request
from typing import Any

SSH_HOST = "pdx1-shared-a1-13.dreamhost.com"
SSH_USER = "dh_4gg2za"
DEFAULT_KEY = pathlib.Path(r"C:\Users\Bryan\.ssh\dreamhost_player_assistant")
ORIGIN = "https://bryanmiller.us"
ACCOUNT_HOME = "/home/dh_4gg2za"
PUBLIC_ROOT = "/home/dh_4gg2za/bryanmiller.us"
PRIVATE_ROOT = "/home/dh_4gg2za/player-assistant-broker"
PHP_BINARY = "/usr/bin/php"
REMOTE_RUN_ROOT = "/home/dh_4gg2za/.player-assistant-restore"
TRANSACTION_ID_RE = re.compile(r"^[0-9]{8}T[0-9]{6}Z-[a-f0-9]{8}$")
RUN_DIRECTORY_RE = re.compile(
    r"^/home/dh_4gg2za/\.player-assistant-restore/[0-9]{8}T[0-9]{6}Z-[a-f0-9]{8}$"
)
REPOSITORY_ROOT = pathlib.Path(__file__).resolve().parents[2]
LOCAL_MANIFEST = REPOSITORY_ROOT / "pwa" / "manifest.webmanifest"
LOCAL_SERVICE_WORKER = REPOSITORY_ROOT / "pwa" / "service-worker.js"
CANONICAL_INSTALLER_SOURCE = REPOSITORY_ROOT / "pwa" / "online-installer-for-pwa" / "install-player-assistant-web.php"


@dataclasses.dataclass(frozen=True)
class Artifacts:
    installer: pathlib.Path
    payload: pathlib.Path
    sidecar: pathlib.Path
    payload_sha256: str


class RestoreError(RuntimeError):
    pass


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Restore the Player Assistant PWA/API/private broker on the current DreamHost account."
    )
    parser.add_argument(
        "action",
        nargs="?",
        choices=("preflight", "install", "status", "finalize", "rollback"),
        default="preflight",
    )
    parser.add_argument("--transaction-id")
    parser.add_argument("--confirm-production-reinstall")
    return parser


def require_install_confirmation(value: str | None) -> None:
    if value != "bryanmiller.us":
        raise ValueError(
            "Production reinstall confirmation must be exactly 'bryanmiller.us'."
        )


def validate_transaction_id(value: str) -> str:
    if not TRANSACTION_ID_RE.fullmatch(value):
        raise ValueError("The transaction ID is invalid.")
    return value


def validate_remote_run_directory(value: str) -> str:
    if not RUN_DIRECTORY_RE.fullmatch(value):
        raise ValueError("The remote run directory is outside the approved restore root.")
    return value


def sha256_file(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def discover_artifacts(
    dist: pathlib.Path | None = None,
    *,
    expected_installer: pathlib.Path | None = None,
) -> Artifacts:
    if dist is None:
        dist = pathlib.Path(__file__).resolve().parents[1] / "online-installer-for-pwa" / "dist"
    dist = dist.resolve()
    installer = dist / "install-player-assistant-web.php"
    expected_installer = expected_installer or CANONICAL_INSTALLER_SOURCE
    payloads = sorted(dist.glob("player-assistant-web-payload-*.tar"))
    if not installer.is_file() or installer.is_symlink() or len(payloads) != 1:
        raise ValueError("The reusable installer distribution is missing or ambiguous.")
    if not expected_installer.is_file() or expected_installer.is_symlink():
        raise ValueError("The canonical reusable installer source is missing or unsafe.")
    if not hmac.compare_digest(sha256_file(installer), sha256_file(expected_installer)):
        raise ValueError("The reusable installer integrity check failed against its canonical source.")
    payload = payloads[0]
    sidecar = pathlib.Path(str(payload) + ".sha256")
    if not sidecar.is_file():
        raise ValueError("The payload checksum sidecar is missing.")
    digest = sha256_file(payload)
    expected = f"{digest}  {payload.name}"
    if sidecar.read_text(encoding="utf-8").strip() != expected:
        raise ValueError("The payload checksum sidecar does not match the payload.")
    return Artifacts(installer, payload, sidecar, digest)


def build_install_command(remote_payload: str, remote_installer: str) -> str:
    values = [
        PHP_BINARY,
        remote_installer,
        f"--package={remote_payload}",
        f"--origin={ORIGIN}",
        f"--public-root={PUBLIC_ROOT}",
        f"--private-root={PRIVATE_ROOT}",
        "--verification=local",
    ]
    return " ".join(shlex.quote(value) for value in values)


def build_transaction_command(action: str, transaction_id: str, remote_installer: str) -> str:
    validate_transaction_id(transaction_id)
    if action not in {"finalize", "rollback"}:
        raise ValueError("The transaction action is invalid.")
    values = [
        PHP_BINARY,
        remote_installer,
        f"--{action}-transaction={transaction_id}",
        f"--origin={ORIGIN}",
        f"--public-root={PUBLIC_ROOT}",
        f"--private-root={PRIVATE_ROOT}",
    ]
    return " ".join(shlex.quote(value) for value in values)


def parse_installer_result(stdout: str) -> dict[str, Any]:
    lines = [line.strip() for line in stdout.splitlines() if line.strip()]
    if not lines:
        raise ValueError("The installer did not return a final JSON object.")
    try:
        result = json.loads(lines[-1])
    except json.JSONDecodeError as error:
        raise ValueError("The installer did not return a final JSON object.") from error
    if not isinstance(result, dict):
        raise ValueError("The installer final JSON result has the wrong shape.")
    return result


def validate_transaction_action_result(
    action: str,
    transaction_id: str,
    result: dict[str, Any],
) -> None:
    expected_status = {"finalize": "finalized", "rollback": "rolled_back"}.get(action)
    if (
        expected_status is None
        or result.get("status") != expected_status
        or result.get("transaction_id") != transaction_id
    ):
        raise RestoreError("The remote transaction result did not match the requested action and transaction ID.")


def build_cleanup_command(run_directory: str) -> str:
    validate_remote_run_directory(run_directory)
    return f"""set -eu
[ -d {shlex.quote(REMOTE_RUN_ROOT)} ]
[ ! -L {shlex.quote(REMOTE_RUN_ROOT)} ]
[ "$(readlink -f {shlex.quote(REMOTE_RUN_ROOT)})" = {shlex.quote(REMOTE_RUN_ROOT)} ]
[ -d {shlex.quote(run_directory)} ]
[ ! -L {shlex.quote(run_directory)} ]
[ "$(readlink -f {shlex.quote(run_directory)})" = {shlex.quote(run_directory)} ]
[ "$(dirname {shlex.quote(run_directory)})" = {shlex.quote(REMOTE_RUN_ROOT)} ]
cd -P {shlex.quote(run_directory)}
[ "$PWD" = {shlex.quote(run_directory)} ]
rm -f -- ./*.php ./*.tar ./*.sha256
cd /
rmdir -- {shlex.quote(run_directory)}
"""


class DreamHostSession:
    def __init__(self, key_path: pathlib.Path = DEFAULT_KEY) -> None:
        self.key_path = key_path
        self.client = None
        self.sftp = None

    def __enter__(self) -> "DreamHostSession":
        if not self.key_path.is_file():
            raise RestoreError(f"DreamHost SSH key is missing: {self.key_path}")
        try:
            import paramiko
        except ImportError as error:
            raise RestoreError("Paramiko is required: install it with 'python -m pip install paramiko'.") from error
        client = paramiko.SSHClient()
        client.load_system_host_keys()
        client.set_missing_host_key_policy(paramiko.RejectPolicy())
        client.connect(
            hostname=SSH_HOST,
            username=SSH_USER,
            key_filename=str(self.key_path),
            look_for_keys=False,
            allow_agent=False,
            timeout=20,
            banner_timeout=20,
            auth_timeout=20,
        )
        transport = client.get_transport()
        if transport is None or not transport.is_active() or not transport.is_authenticated():
            client.close()
            raise RestoreError("DreamHost SSH transport is not authenticated.")
        transport.set_keepalive(20)
        self.client = client
        self.sftp = client.open_sftp()
        return self

    def __exit__(self, exc_type, exc, traceback) -> None:
        if self.sftp is not None:
            self.sftp.close()
        if self.client is not None:
            self.client.close()

    def execute(self, command: str, timeout: int = 180) -> tuple[int, str, str]:
        if self.client is None:
            raise RestoreError("SSH session is not connected.")
        stdin, stdout, stderr = self.client.exec_command(command, timeout=timeout)
        del stdin
        output = stdout.read().decode("utf-8", errors="replace")
        errors = stderr.read().decode("utf-8", errors="replace")
        status = stdout.channel.recv_exit_status()
        return status, output, errors

    def preflight(self) -> dict[str, str]:
        command = f"""set -eu
[ "$(pwd -P)" = {shlex.quote(ACCOUNT_HOME)} ]
[ "$(readlink -f {shlex.quote(PUBLIC_ROOT)})" = {shlex.quote(PUBLIC_ROOT)} ]
[ "$(readlink -f {shlex.quote(PRIVATE_ROOT)})" = {shlex.quote(PRIVATE_ROOT)} ]
[ ! -L {shlex.quote(PUBLIC_ROOT)} ]
[ ! -L {shlex.quote(PRIVATE_ROOT)} ]
[ -d {shlex.quote(PUBLIC_ROOT + '/scarlethorizons/pwa')} ]
[ -d {shlex.quote(PUBLIC_ROOT + '/scarlethorizons/api')} ]
[ -f {shlex.quote(PRIVATE_ROOT + '/config.php')} ]
[ ! -L {shlex.quote(PRIVATE_ROOT + '/config.php')} ]
[ -f {shlex.quote(PRIVATE_ROOT + '/broker.sqlite')} ]
[ ! -L {shlex.quote(PRIVATE_ROOT + '/broker.sqlite')} ]
[ "$(command -v php)" = {shlex.quote(PHP_BINARY)} ]
{shlex.quote(PHP_BINARY)} -r 'foreach (["phar","pdo_sqlite","sodium","curl","openssl"] as $e) if (!extension_loaded($e)) exit(10);'
printf 'dreamhost-preflight-ok\n'
"""
        status, stdout, stderr = self.execute(command, timeout=45)
        if status != 0 or stdout.strip() != "dreamhost-preflight-ok":
            raise RestoreError(f"DreamHost preflight failed (exit {status}): {stderr.strip()}")
        return {
            "host": SSH_HOST,
            "account": SSH_USER,
            "public_root": PUBLIC_ROOT,
            "private_root": PRIVATE_ROOT,
            "php": PHP_BINARY,
        }

    def create_run_directory(self, run_directory: str) -> None:
        validate_remote_run_directory(run_directory)
        command = f"""set -eu
umask 077
if [ -e {shlex.quote(REMOTE_RUN_ROOT)} ]; then
  [ -d {shlex.quote(REMOTE_RUN_ROOT)} ] && [ ! -L {shlex.quote(REMOTE_RUN_ROOT)} ]
else
  mkdir {shlex.quote(REMOTE_RUN_ROOT)}
fi
[ "$(readlink -f {shlex.quote(REMOTE_RUN_ROOT)})" = {shlex.quote(REMOTE_RUN_ROOT)} ]
mkdir {shlex.quote(run_directory)}
chmod 700 {shlex.quote(REMOTE_RUN_ROOT)} {shlex.quote(run_directory)}
"""
        status, _, stderr = self.execute(command, timeout=30)
        if status != 0:
            raise RestoreError(f"Unable to create the private restore run directory: {stderr.strip()}")

    def upload_file(self, local: pathlib.Path, remote: str, mode: int = 0o600) -> None:
        if self.sftp is None:
            raise RestoreError("SFTP session is not connected.")
        temporary = remote + ".uploading"
        self.sftp.put(str(local), temporary, confirm=True)
        self.sftp.chmod(temporary, mode)
        self.sftp.posix_rename(temporary, remote)
        attributes = self.sftp.stat(remote)
        if attributes.st_size != local.stat().st_size:
            raise RestoreError(f"Remote upload size mismatch: {local.name}")
        remote_digest = hashlib.sha256()
        with self.sftp.open(remote, "rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                remote_digest.update(chunk)
        if not hmac.compare_digest(sha256_file(local), remote_digest.hexdigest()):
            raise RestoreError(f"Remote upload SHA-256 mismatch: {local.name}")

    def upload_installer(self, artifacts: Artifacts, run_directory: str, include_payload: bool) -> dict[str, str]:
        validate_remote_run_directory(run_directory)
        paths = {"installer": run_directory + "/install-player-assistant-web.php"}
        self.upload_file(artifacts.installer, paths["installer"])
        if include_payload:
            paths["payload"] = run_directory + "/" + artifacts.payload.name
            paths["sidecar"] = paths["payload"] + ".sha256"
            self.upload_file(artifacts.payload, paths["payload"])
            self.upload_file(artifacts.sidecar, paths["sidecar"])
        return paths

    def cleanup_run_directory(self, run_directory: str) -> None:
        validate_remote_run_directory(run_directory)
        command = build_cleanup_command(run_directory)
        status, _, stderr = self.execute(command, timeout=30)
        if status != 0:
            raise RestoreError(f"Unable to remove the restore run directory: {stderr.strip()}")

    def _transaction_root_exists_and_is_confined(self, root: str) -> bool:
        if self.sftp is None:
            raise RestoreError("SFTP session is not connected.")
        try:
            attributes = self.sftp.lstat(root)
        except OSError as error:
            if error.errno == errno.ENOENT:
                return False
            raise RestoreError(f"Unable to inspect the installer transaction root: {error}") from error
        if stat.S_ISLNK(attributes.st_mode) or not stat.S_ISDIR(attributes.st_mode):
            raise RestoreError("The installer transaction root is not a confined regular directory.")
        try:
            canonical = self.sftp.normalize(root)
        except OSError as error:
            raise RestoreError(f"Unable to canonicalize the installer transaction root: {error}") from error
        if canonical != root:
            raise RestoreError("The installer transaction root canonical path is invalid.")
        return True

    def unresolved_transactions(self) -> list[dict[str, Any]]:
        if self.sftp is None:
            raise RestoreError("SFTP session is not connected.")
        root = ACCOUNT_HOME + "/.player-assistant-installer-transactions"
        results: list[dict[str, Any]] = []
        if not self._transaction_root_exists_and_is_confined(root):
            return results
        try:
            names = self.sftp.listdir(root)
        except OSError as error:
            raise RestoreError(f"Unable to inspect the installer transaction root: {error}") from error
        if not self._transaction_root_exists_and_is_confined(root):
            raise RestoreError("The installer transaction root disappeared during inspection.")
        for name in names:
            if not TRANSACTION_ID_RE.fullmatch(name):
                results.append({"transaction_id": name, "status": "invalid_directory_name"})
                continue
            manifest_path = root + "/" + name + "/manifest.json"
            if not self._transaction_root_exists_and_is_confined(root):
                raise RestoreError("The installer transaction root disappeared during inspection.")
            try:
                with self.sftp.open(manifest_path, "r") as stream:
                    raw = stream.read()
                    if isinstance(raw, bytes):
                        raw = raw.decode("utf-8")
                    data = json.loads(raw)
            except OSError as error:
                raise RestoreError(f"Unable to read transaction manifest {name}: {error}") from error
            except (UnicodeDecodeError, json.JSONDecodeError):
                results.append({"transaction_id": name, "status": "invalid_manifest"})
                continue
            if not self._transaction_root_exists_and_is_confined(root):
                raise RestoreError("The installer transaction root disappeared during inspection.")
            if not isinstance(data, dict):
                results.append({"transaction_id": name, "status": "invalid_manifest"})
                continue
            status = data.get("status")
            if status in {"preparing", "promoted", "pending_https_verification", "rollback_cleanup", "finalize_cleanup"}:
                results.append({"transaction_id": name, "status": status})
            elif status not in {"verified", "rolled_back"}:
                results.append({"transaction_id": name, "status": "unknown_state"})
        return results


def make_run_id() -> str:
    return time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()) + "-" + hashlib.sha256(str(time.time_ns()).encode()).hexdigest()[:8]


class NoRedirectHandler(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, file_pointer, code, message, headers, new_url):
        return None


def verify_public_https(opener: Any | None = None) -> dict[str, int]:
    expected_files = {
        "/scarlethorizons/pwa/manifest.webmanifest": LOCAL_MANIFEST.read_bytes(),
        "/scarlethorizons/pwa/service-worker.js": LOCAL_SERVICE_WORKER.read_bytes(),
    }
    paths = (*expected_files, "/scarlethorizons/api/v1/health")
    opener = opener or urllib.request.build_opener(NoRedirectHandler())
    results: dict[str, int] = {}
    for path in paths:
        request = urllib.request.Request(ORIGIN + path, headers={"User-Agent": "PlayerAssistantDreamHostRestore/1"})
        try:
            with opener.open(request, timeout=20) as response:
                if response.geturl() != request.full_url:
                    raise RestoreError(f"HTTPS verification rejected a redirect for {path}.")
                body = response.read(1024 * 1024 + 1)
                if response.status != 200 or not body or len(body) > 1024 * 1024:
                    raise RestoreError(f"HTTPS verification failed for {path}.")
        except urllib.error.HTTPError as error:
            if 300 <= error.code < 400:
                raise RestoreError(f"HTTPS verification rejected a redirect for {path}.") from error
            raise RestoreError(f"HTTPS verification failed for {path}: HTTP {error.code}.") from error
        except urllib.error.URLError as error:
            raise RestoreError(f"HTTPS verification failed for {path}: {error.reason}.") from error
        if path in expected_files:
            if not hmac.compare_digest(hashlib.sha256(body).digest(), hashlib.sha256(expected_files[path]).digest()):
                label = "manifest" if path.endswith(".webmanifest") else "service worker"
                raise RestoreError(f"HTTPS {label} hash does not match the reinstall payload.")
        else:
            try:
                health = json.loads(body.decode("utf-8"))
            except (UnicodeDecodeError, json.JSONDecodeError) as error:
                raise RestoreError("HTTPS health response is not valid JSON.") from error
            if (
                not isinstance(health, dict)
                or health.get("service") != "player-assistant-broker"
                or health.get("schema_version") != 7
                or health.get("status") != "ok"
            ):
                raise RestoreError("HTTPS health response does not confirm the expected healthy broker schema.")
        results[path] = len(body)
    return results


def run_remote_installer(session: DreamHostSession, command: str, timeout: int = 600) -> dict[str, Any]:
    status, stdout, stderr = session.execute(command, timeout=timeout)
    if status != 0:
        raise RestoreError(f"Remote installer failed (exit {status}): {stderr.strip() or stdout.strip()}")
    try:
        return parse_installer_result(stdout)
    except ValueError as error:
        raise RestoreError(
            "Remote installer completion is ambiguous; inspect status and do not rerun installation blindly."
        ) from error


def install(artifacts: Artifacts) -> dict[str, Any]:
    run_id = make_run_id()
    run_directory = validate_remote_run_directory(REMOTE_RUN_ROOT + "/" + run_id)
    with DreamHostSession() as session:
        target = session.preflight()
        unresolved = session.unresolved_transactions()
        if unresolved:
            raise RestoreError(f"An unresolved installer transaction already exists: {unresolved}")
        session.create_run_directory(run_directory)
        paths = session.upload_installer(artifacts, run_directory, include_payload=True)
        result = run_remote_installer(session, build_install_command(paths["payload"], paths["installer"]))
        transaction_id = validate_transaction_id(str(result.get("transaction_id", "")))
        if result.get("status") != "installed_pending_https_verification":
            raise RestoreError("Remote installation did not enter the expected pending-verification state.")
        try:
            https = verify_public_https()
        except Exception:
            rollback = build_transaction_command("rollback", transaction_id, paths["installer"])
            rollback_result = run_remote_installer(session, rollback)
            validate_transaction_action_result("rollback", transaction_id, rollback_result)
            raise
        finalized = run_remote_installer(
            session,
            build_transaction_command("finalize", transaction_id, paths["installer"]),
        )
        validate_transaction_action_result("finalize", transaction_id, finalized)
        session.cleanup_run_directory(run_directory)
        return {
            "status": "finalized",
            "transaction_id": transaction_id,
            "payload_sha256": artifacts.payload_sha256,
            "target": target,
            "https_verified": https,
        }


def transaction_action(artifacts: Artifacts, action: str, transaction_id: str) -> dict[str, Any]:
    transaction_id = validate_transaction_id(transaction_id)
    run_id = make_run_id()
    run_directory = validate_remote_run_directory(REMOTE_RUN_ROOT + "/" + run_id)
    with DreamHostSession() as session:
        session.preflight()
        session.create_run_directory(run_directory)
        paths = session.upload_installer(artifacts, run_directory, include_payload=False)
        result = run_remote_installer(
            session,
            build_transaction_command(action, transaction_id, paths["installer"]),
        )
        validate_transaction_action_result(action, transaction_id, result)
        session.cleanup_run_directory(run_directory)
        return result


def main(argv: list[str] | None = None) -> int:
    arguments = build_parser().parse_args(argv)
    try:
        artifacts = discover_artifacts()
        if arguments.action == "install":
            require_install_confirmation(arguments.confirm_production_reinstall)
            result = install(artifacts)
        elif arguments.action in {"finalize", "rollback"}:
            if arguments.transaction_id is None:
                raise ValueError("--transaction-id is required for finalize or rollback.")
            result = transaction_action(artifacts, arguments.action, arguments.transaction_id)
        else:
            with DreamHostSession() as session:
                target = session.preflight()
                unresolved = session.unresolved_transactions()
            result = {
                "status": "preflight_ok" if arguments.action == "preflight" else "status",
                "target": target,
                "payload_sha256": artifacts.payload_sha256,
                "unresolved_transactions": unresolved,
            }
        print(json.dumps(result, indent=2, sort_keys=True))
        return 0
    except (OSError, ValueError, RestoreError) as error:
        print(f"DreamHost restore rejected: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
