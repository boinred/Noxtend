#!/usr/bin/env python3
import os
from pathlib import Path
import shutil
import signal
import subprocess
import tempfile
import time


def check(args, *, forward_fails=False, unhealthy=False, listeners=(), stubborn=False, delayed=False, missing_tool=False, identity=None, lookup_error=False, release_error=False, startup_delay=0):
    with tempfile.TemporaryDirectory(prefix="noxtend-local-up-") as directory:
        root = Path(directory)
        deploy = root / "deploy"
        (deploy / "k8s").mkdir(parents=True)
        shutil.copyfile(Path(__file__).with_name("local-up.sh"), deploy / "local-up.sh")
        (deploy / "k8s/secrets.yaml").touch()
        bin_path = root / "bin"
        bin_path.mkdir()
        fixtures = []
        pid = None
        try:
            for index, _ in enumerate(listeners):
                marker = root / f"listener-{index}"
                handler = "signal.SIG_IGN" if stubborn else f"lambda *_: (time.sleep({.15 if delayed else 0}), pathlib.Path({str(marker)!r}).unlink(), exit(0))"
                code = f"import signal,time,pathlib; time.sleep({startup_delay}); signal.signal(signal.SIGTERM, {handler}); pathlib.Path({str(marker)!r}).touch(); time.sleep(60)"
                fixture = subprocess.Popen([shutil.which("python3"), "-c", code])
                fixtures.append(fixture)
            deadline = time.monotonic() + 5
            for index, fixture in enumerate(fixtures):
                while not (root / f"listener-{index}").exists():
                    assert fixture.poll() is None, "listener fixture exited before readiness"
                    assert time.monotonic() < deadline, "listener fixture readiness timed out"
                    time.sleep(.01)
            rows = [f"{os.getuid()} {bin_path}/kubectl {command}" for command in listeners]
            if identity == "owner":
                rows = [row.replace(str(os.getuid()), str(os.getuid() + 1), 1) for row in rows]
            if identity == "executable":
                rows = [row.replace(str(bin_path / "kubectl"), "/other/kubectl") for row in rows]
            for fixture, row in zip(fixtures, rows):
                (root / str(fixture.pid)).write_text(row)
            commands = {
                "docker": 'echo docker >> "$TEST_RUNTIME/actions"\nexit 0\n',
                "lsof": '\n'.join(f'[ -f "$TEST_RUNTIME/listener-{i}" ] && echo {f.pid}' for i, f in enumerate(fixtures)) + "\nexit 0\n",
                "ps": 'cat "$TEST_RUNTIME/$3"\n',
                "sleep": '/bin/sleep .01\n',
                "kubectl": '''echo kubectl >> "$TEST_RUNTIME/actions"
    case "$*" in
      "config current-context") echo docker-desktop; exit 0 ;;
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
            if release_error:
                command = bin_path / "lsof"
                command.write_text('#!/usr/bin/env bash\nif [ ! -f "$TEST_RUNTIME/listener-0" ]; then echo "inspection failed" >&2; exit 2; fi\n' + commands["lsof"])
            if lookup_error:
                (bin_path / "lsof").write_text('#!/usr/bin/env bash\necho "inspection failed" >&2\nexit 2\n')
            for name in ("bash", "id", "cat", "seq", "mktemp", "nohup", "dirname"):
                (bin_path / name).symlink_to(shutil.which(name))
            if missing_tool:
                (bin_path / "lsof").unlink()
            env = {
                **os.environ,
                "PATH": str(bin_path),
                "TMPDIR": str(root),
                "TEST_RUNTIME": str(root),
                "TEST_FORWARD_FAILS": str(int(forward_fails)),
                "TEST_UNHEALTHY": str(int(unhealthy)),
            }
            result = subprocess.run(
                [str(bin_path / "bash"), str(deploy / "local-up.sh"), "--skip-build", *args],
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
            rejected = missing_tool or lookup_error or release_error or identity is not None or stubborn or any(command not in (
                "-n noxtend port-forward svc/api 18080:8080",
                "--context docker-desktop -n noxtend port-forward svc/api 18080:8080",
                "-n noxtend port-forward svc/api 18080:8080 --address 127.0.0.1",
                "--context docker-desktop -n noxtend port-forward svc/api 18080:8080 --address 127.0.0.1",
            ) for command in listeners)
            assert alive == (bool(args) and not forward_fails and not unhealthy and not rejected), output
            if rejected:
                assert result.returncode != 0, output
                assert pid is None, output
                actions = (root / "actions").read_text() if (root / "actions").exists() else ""
                assert actions in ("", "kubectl\n"), actions
                for fixture in fixtures:
                    assert (fixture.poll() is not None) == release_error, output
                return
            for fixture in fixtures:
                assert fixture.poll() is not None, output
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
            if pid is None and (root / "pid").exists():
                pid = int((root / "pid").read_text())
            for fixture in fixtures:
                fixture.terminate()
                try:
                    fixture.wait(timeout=.5)
                except subprocess.TimeoutExpired:
                    fixture.kill()
                    fixture.wait()
            if pid is not None:
                try:
                    os.kill(pid, signal.SIGTERM)
                except ProcessLookupError:
                    pass


if __name__ == "__main__":
    known = "-n noxtend port-forward svc/api 18080:8080 --address 127.0.0.1"
    explicit = "--context docker-desktop " + known
    check([], listeners=(known,))
    check([], listeners=(known,), startup_delay=2)
    check([], listeners=(known.removesuffix(" --address 127.0.0.1"),))
    check([], listeners=(explicit.removesuffix(" --address 127.0.0.1"),))
    check(["-d"], listeners=(explicit,), delayed=True)
    for unknown in ("other-program", explicit.replace("docker-desktop", "production"), known.replace("noxtend", "other"), known.replace("svc/api", "svc/other"), known.replace("18080:8080", "18080:9090")):
        check([], listeners=(unknown,))
    check([], listeners=(known, "other-program"))
    check([], listeners=(known, known))
    check([], listeners=(known,), stubborn=True)
    check([], missing_tool=True)
    check([], lookup_error=True)
    check([], listeners=(known,), release_error=True)
    check([], listeners=(known,), identity="owner")
    check([], listeners=(known,), identity="executable")
    check(["-d"])
    check(["--detach"])
    check([])
    check([], forward_fails=True)
    check(["-d"], unhealthy=True)
    print("PASS: 23 fake-only cases — forwarding replacement, protected conflicts, TERM timeout/delay, inspection failure, missing tool, detach/SIGHUP, cleanup")
