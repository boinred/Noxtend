#!/usr/bin/env python3
import os
from pathlib import Path
import shutil
import signal
import subprocess
import tempfile
import time


def check(args, *, forward_fails=False, unhealthy=False):
    with tempfile.TemporaryDirectory(prefix="noxtend-local-up-") as directory:
        root = Path(directory)
        deploy = root / "deploy"
        (deploy / "k8s").mkdir(parents=True)
        shutil.copyfile(Path(__file__).with_name("local-up.sh"), deploy / "local-up.sh")
        (deploy / "k8s/secrets.yaml").touch()
        bin_path = root / "bin"
        bin_path.mkdir()
        commands = {
            "docker": "exit 0\n",
            "kubectl": '''case "$*" in
  *"port-forward svc/api"*)
    echo $$ > "$TEST_RUNTIME/pid"
    if [ "$TEST_FORWARD_FAILS" = 1 ]; then
      /bin/sleep 0.3
      echo 'mock: port 18080 already in use' >&2
      exit 1
    fi
    echo 'Forwarding from 127.0.0.1:18080 -> 8080'
    exec /bin/sleep 60
    ;;
esac
''',
            "curl": '''/bin/sleep 0.1
if [ "$TEST_UNHEALTHY" = 1 ]; then
  echo '{"data":{"db":"failed","redis":"ok","blob":"ok"},"error":null}'
else
  echo '{"data":{"db":"ok","redis":"ok","imageDecoder":"ok","blob":"ok"},"error":null}'
fi
''',
        }
        for name, body in commands.items():
            command = bin_path / name
            command.write_text("#!/usr/bin/env bash\n" + body)
            command.chmod(0o755)
        env = {
            **os.environ,
            "PATH": str(bin_path) + os.pathsep + os.environ["PATH"],
            "TMPDIR": str(root),
            "TEST_RUNTIME": str(root),
            "TEST_FORWARD_FAILS": str(int(forward_fails)),
            "TEST_UNHEALTHY": str(int(unhealthy)),
        }
        pid = None
        try:
            result = subprocess.run(
                ["bash", str(deploy / "local-up.sh"), "--skip-build", *args],
                env=env, capture_output=True, text=True, timeout=10,
            )
            output = result.stdout + result.stderr
            if (root / "pid").exists():
                pid = int((root / "pid").read_text())
            time.sleep(0.1)
            try:
                if pid is not None:
                    os.kill(pid, 0)
                alive = pid is not None
            except ProcessLookupError:
                alive = False
            assert alive == (bool(args) and not forward_fails and not unhealthy), output
            if forward_fails or unhealthy:
                assert result.returncode != 0, output
                assert "Backend API is up" not in output, output
                if forward_fails:
                    assert "already in use" in output, output
            else:
                assert result.returncode == 0, output
                assert pid is not None, output
                if args:
                    assert str(pid) in output and "kill " in output, output
                    assert list(root.glob("noxtend-port-forward.*")), output
                    os.kill(pid, signal.SIGHUP)
                    time.sleep(0.1)
                    os.kill(pid, 0)
        finally:
            if pid is not None:
                try:
                    os.kill(pid, signal.SIGTERM)
                except ProcessLookupError:
                    pass


if __name__ == "__main__":
    check(["-d"])
    check(["--detach"])
    check([])
    check([], forward_fails=True)
    check(["-d"], unhealthy=True)
    print("PASS: detach aliases, process lifetime, port conflict, unhealthy cleanup")
