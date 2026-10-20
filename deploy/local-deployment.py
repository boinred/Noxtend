#!/usr/bin/env python3
import datetime
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


def running_api(context, namespace, image):
    def get(*args):
        result = subprocess.run(
            ["kubectl", "--context", context, "-n", namespace, "get", *args, "-o", "json"],
            check=True, capture_output=True, text=True,
        )
        return json.loads(result.stdout)

    def owned_by(resource, kind, uid):
        return any(owner.get("kind") == kind and owner.get("uid") == uid and owner.get("controller") is True
                   for owner in resource["metadata"].get("ownerReferences", []))

    def api_container(containers):
        matches = [container for container in containers if container.get("name") == "api"]
        if len(matches) != 1:
            raise ValueError("cannot identify the api container")
        return matches[0]

    deployment = get("deployment", "api")
    deployment_uid = deployment["metadata"]["uid"]
    if api_container(deployment["spec"]["template"]["spec"]["containers"])["image"] != image:
        raise ValueError("Deployment api image does not match the expected tag")
    replicasets = get("replicasets", "-l", "app=api")["items"]
    owners = {rs["metadata"]["uid"] for rs in replicasets if owned_by(rs, "Deployment", deployment_uid)}
    pods = [pod for pod in get("pods", "-l", "app=api")["items"]
            if not pod["metadata"].get("deletionTimestamp")]
    if len(pods) != 1 or not any(owned_by(pods[0], "ReplicaSet", uid) for uid in owners):
        raise ValueError("expected one active Pod owned by Deployment api")
    pod = pods[0]
    status = api_container(pod["status"]["containerStatuses"])
    # Runtime-normalized local image names
    if (api_container(pod["spec"]["containers"])["image"] != image
            or status["image"] not in (image, "docker.io/library/" + image)
            or not status.get("ready") or not status.get("imageID")):
        raise ValueError("running api Pod image, readiness or imageID could not be verified")
    return {"deploymentUID": deployment_uid, "podName": pod["metadata"]["name"],
            "podUID": pod["metadata"]["uid"], "podImageID": status["imageID"]}


def main():
    mode, *args = sys.argv[1:]
    if mode == "render":
        path, image = args
        manifest = Path(path).read_text()
        source = "          image: noxtend-api:local\n"
        if manifest.count(source) != 1:
            raise ValueError("cannot identify the local api image in the manifest")
        print(manifest.replace(source, f"          image: {image}\n"), end="")
    elif mode == "verify":
        context, namespace, image = args
        print(json.dumps(running_api(context, namespace, image)))
    elif mode == "record":
        path, context, namespace, revision, dirty, image, image_id, previous_pod = args
        pod = running_api(context, namespace, image)
        if pod != json.loads(previous_pod):
            raise ValueError("api Pod changed during health verification; retry deployment")
        recorded_at = datetime.datetime.now(datetime.timezone.utc)
        record = {"recordedAt": recorded_at.isoformat().replace("+00:00", "Z"),
                  "context": context, "namespace": namespace, "revision": revision,
                  "dirty": dirty == "true", "imageTag": image, "imageID": image_id, **pod}
        destination = Path(path)
        destination.parent.mkdir(parents=True, exist_ok=True)
        # Atomic replacement after rollout, health and Pod verification
        temporary = None
        try:
            with tempfile.NamedTemporaryFile(mode="w", dir=destination.parent, delete=False) as stream:
                temporary = Path(stream.name)
                json.dump(record, stream, indent=2)
                stream.write("\n")
            history = destination.with_name(f"{recorded_at.strftime('%Y%m%dT%H%M%S.%fZ')}-{image_id.removeprefix('sha256:')}.json")
            os.link(temporary, history)
            temporary.replace(destination)
        finally:
            if temporary is not None:
                temporary.unlink(missing_ok=True)
    else:
        raise ValueError("unknown local deployment operation")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, KeyError, OSError, subprocess.CalledProcessError) as error:
        print(f"local deployment metadata failed: {error}", file=sys.stderr)
        sys.exit(1)
