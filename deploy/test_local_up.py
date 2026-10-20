#!/usr/bin/env python3
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import tempfile
import time


def check(args, *, forward_fails=False, unhealthy=False, listeners=(), stubborn=False, delayed=False, missing_tool=False, identity=None, lookup_error=False, release_error=False, startup_delay=0, build=False, source_revision="1" * 40, source_dirty=False, image_id="a" * 64, checkout_dirty=False, metadata_missing=False, rollout_fails=False, image_mismatch=False, pod_error=None, build_fails=False, apply_fails=False, first_deployment=False):
    with tempfile.TemporaryDirectory(prefix="noxtend-local-up-") as directory:
        root = Path(directory)
        deploy = root / "deploy"
        (deploy / "k8s").mkdir(parents=True)
        shutil.copyfile(Path(__file__).with_name("local-up.sh"), deploy / "local-up.sh")
        helper = Path(__file__).with_name("local-deployment.py")
        shutil.copyfile(helper, deploy / helper.name)
        shutil.copyfile(Path(__file__).with_name("k8s") / "api.yaml", deploy / "k8s/api.yaml")
        (deploy / "k8s/secrets.yaml").touch()
        record_path = deploy / ".local/deployments/latest.json"
        record_path.parent.mkdir(parents=True)
        previous_record = '{"previousSuccess":true}\n'
        if not first_deployment:
            record_path.write_text(previous_record)
            (record_path.parent / "previous.json").write_text(previous_record)
        else:
            record_path.parent.rmdir()
            record_path.parent.parent.rmdir()
        revision = "2" * 40 if build else source_revision
        dirty = checkout_dirty if build else source_dirty
        expected_image = f"noxtend-api:{revision[:40]}-{'dirty' if dirty else 'clean'}-{image_id}"
        owner = lambda kind, uid: [{"kind": kind, "uid": uid, "controller": True}]
        deployment = {"metadata": {"uid": "deployment-uid"}, "spec": {"template": {"spec": {"containers": [{"name": "api", "image": expected_image}]}}}}
        replicasets = {"items": [{"metadata": {"uid": "rs-uid", "ownerReferences": owner("Deployment", "deployment-uid")}}]}
        pod = {"metadata": {"name": "api-pod", "uid": "pod-uid", "ownerReferences": owner("ReplicaSet", "rs-uid")}, "spec": {"containers": [{"name": "api", "image": expected_image}]}, "status": {"phase": "Running", "containerStatuses": [{"name": "api", "image": expected_image, "imageID": "containerd://sha256:" + "b" * 64, "ready": True}]}}
        if image_mismatch:
            deployment["spec"]["template"]["spec"]["containers"][0]["image"] = "noxtend-api:other"
        if pod_error == "owner":
            pod["metadata"]["ownerReferences"] = owner("ReplicaSet", "unrelated-rs")
        elif pod_error == "replicaset-owner":
            replicasets["items"][0]["metadata"]["ownerReferences"] = owner("Deployment", "unrelated-deployment")
        elif pod_error == "image":
            pod["spec"]["containers"][0]["image"] = "noxtend-api:other"
        elif pod_error == "status-image":
            pod["status"]["containerStatuses"][0]["image"] = "noxtend-api:other"
        elif pod_error == "image-id":
            pod["status"]["containerStatuses"][0]["imageID"] = ""
        elif pod_error == "normalized-image":
            pod["status"]["containerStatuses"][0]["image"] = "docker.io/library/" + expected_image
        elif pod_error == "not-ready":
            pod["status"]["containerStatuses"][0]["ready"] = False
        pods = {"items": [pod, pod] if pod_error == "multiple" else [pod]}
        if pod_error == "extra-unowned":
            unrelated = json.loads(json.dumps(pod))
            unrelated["metadata"]["ownerReferences"] = owner("ReplicaSet", "unrelated-rs")
            pods["items"].append(unrelated)
        for name, value in (("deployment", deployment), ("replicasets", replicasets), ("pods", pods)):
            (root / f"{name}.json").write_text(json.dumps(value))
        changed_pods = json.loads(json.dumps(pods))
        changed_pods["items"][0]["metadata"]["uid"] = "replacement-pod-uid"
        (root / "pods-after.json").write_text(json.dumps(changed_pods))
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
                "docker": '''echo "docker $*" >> "$TEST_RUNTIME/actions"
    case "$*" in
      "build "*) [ "$TEST_BUILD_FAILS" = 0 ] ;;
      "image inspect "*)
        if [ "$TEST_METADATA_MISSING" = 1 ]; then echo '<no value> <no value> sha256:unknown'; else echo "$TEST_REVISION $TEST_DIRTY sha256:$TEST_IMAGE_ID"; fi ;;
    esac
    ''',
                "git": '''echo "git $*" >> "$TEST_RUNTIME/actions"
    case "$*" in
      *"rev-parse HEAD") echo 2222222222222222222222222222222222222222 ;;
      *"status --porcelain"*) [ "$TEST_CHECKOUT_DIRTY" = 0 ] || echo ' M apps/backend/source.cs' ;;
    esac
    exit 0
    ''',
                "lsof": '\n'.join(f'[ -f "$TEST_RUNTIME/listener-{i}" ] && echo {f.pid}' for i, f in enumerate(fixtures)) + "\nexit 0\n",
                "ps": 'cat "$TEST_RUNTIME/$3"\n',
                "sleep": '/bin/sleep .01\n',
                "kubectl": '''echo "kubectl $*" >> "$TEST_RUNTIME/actions"
    case "$*" in
      "config current-context") echo docker-desktop; exit 0 ;;
      *"get deployment api -o json") cat "$TEST_RUNTIME/deployment.json" ;;
      *"get replicasets -l app=api -o json") cat "$TEST_RUNTIME/replicasets.json" ;;
      *"get pods -l app=api -o json") cat "$TEST_RUNTIME/pods.json" ;;
      "apply -f -"|*" apply -f -") cat > "$TEST_RUNTIME/applied.yaml"; [ "$TEST_APPLY_FAILS" = 0 ] ;;
      *"rollout status"*) [ "$TEST_ROLLOUT_FAILS" = 0 ] ;;
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
    if [ "$TEST_POD_CHANGED" = 1 ]; then cat "$TEST_RUNTIME/pods-after.json" > "$TEST_RUNTIME/pods.json"; fi
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
            for name in ("bash", "id", "cat", "seq", "mktemp", "nohup", "dirname", "python3"):
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
                "TEST_REVISION": revision,
                "TEST_DIRTY": str(dirty).lower(),
                "TEST_IMAGE_ID": image_id,
                "TEST_METADATA_MISSING": str(int(metadata_missing)),
                "TEST_CHECKOUT_DIRTY": str(int(checkout_dirty)),
                "TEST_ROLLOUT_FAILS": str(int(rollout_fails)),
                "TEST_BUILD_FAILS": str(int(build_fails)),
                "TEST_APPLY_FAILS": str(int(apply_fails)),
                "TEST_POD_CHANGED": str(int(pod_error == "changed")),
            }
            result = subprocess.run(
                [str(bin_path / "bash"), str(deploy / "local-up.sh"), *([] if build else ["--skip-build"]), *args],
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
            provenance_failed = metadata_missing or image_mismatch or pod_error not in (None, "normalized-image") or rollout_fails or build_fails or apply_fails
            assert alive == (bool(args) and not forward_fails and not unhealthy and not rejected and not provenance_failed), output
            actions = (root / "actions").read_text() if (root / "actions").exists() else ""
            if rejected or provenance_failed or forward_fails or unhealthy:
                assert record_path.read_text() == previous_record, output
                assert sorted(path.name for path in record_path.parent.glob("*.json")) == ["latest.json", "previous.json"], output
            if metadata_missing:
                assert result.returncode != 0 and "rebuild" in output.lower(), output
                assert " apply " not in actions and "rollout" not in actions, actions
                assert pid is None, output
                for fixture in fixtures:
                    assert fixture.poll() is None, output
                return
            if provenance_failed:
                assert result.returncode != 0 and "Backend API is up" not in output, output
                return
            if rejected:
                assert result.returncode != 0, output
                assert pid is None, output
                assert not any(action.startswith(("docker build", "docker tag", "docker save")) or " apply " in action for action in actions.splitlines()), actions
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
                record = json.loads(record_path.read_text())
                assert record["revision"] == revision and record["dirty"] == dirty, record
                assert record["imageTag"] == expected_image, record
                assert record["imageID"] == "sha256:" + image_id, record
                assert record["podImageID"] == "containerd://sha256:" + "b" * 64, record
                assert record["context"] == "docker-desktop" and record["namespace"] == "noxtend", record
                assert record["podUID"] == "pod-uid" and record["recordedAt"].endswith("Z"), record
                assert set(record) == {"recordedAt", "context", "namespace", "revision", "dirty", "imageTag", "imageID", "podName", "podUID", "podImageID", "deploymentUID"}, record
                histories = [path for path in record_path.parent.glob("*.json") if path.name not in ("latest.json", "previous.json")]
                assert len(histories) == 1 and json.loads(histories[0].read_text()) == record, histories
                if not first_deployment:
                    assert (record_path.parent / "previous.json").read_text() == previous_record
                applied = (root / "applied.yaml").read_text()
                assert f"image: {expected_image}" in applied and "image: noxtend-api:local" not in applied, applied
                assert f"docker tag sha256:{image_id} {expected_image}" in actions, actions
                assert f"docker save {expected_image}" in actions, actions
                if build:
                    assert f"org.opencontainers.image.revision={revision}" in actions, actions
                    assert f"io.noxtend.source.dirty={str(dirty).lower()}" in actions, actions
                else:
                    assert "git " not in actions and "docker build" not in actions, actions
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
    check([], build=True)
    check([], build=True, checkout_dirty=True)
    check([], source_dirty=True, checkout_dirty=False)
    check([], source_revision="3" * 40, checkout_dirty=True)
    check([], image_id="c" * 64)
    check([], metadata_missing=True, listeners=(known,))
    check([], rollout_fails=True)
    check([], image_mismatch=True)
    for error in ("owner", "replicaset-owner", "image", "status-image", "image-id", "not-ready", "multiple"):
        check([], pod_error=error)
    check([], build=True, build_fails=True)
    check([], apply_fails=True)
    check([], pod_error="changed")
    check([], pod_error="normalized-image")
    check([], first_deployment=True)
    check([], pod_error="extra-unowned")
    check([], source_revision="f" * 64)
    print("PASS: 45 fake-only cases — original 23 forwarding guards plus 22 image provenance, applied Pod identity, history and failure preservation cases")
