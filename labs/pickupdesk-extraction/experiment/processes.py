"""Launch only the pinned lab binaries and resources registered with this run."""
import json
import os
from pathlib import Path
import secrets
import shutil
import socket
import subprocess
import time
from urllib.error import URLError
from urllib.request import Request, urlopen
import uuid

from database import ROOT

LAB = Path(__file__).resolve().parents[1]
MONOLITH = ROOT / "src/Api/EquipmentLibrary.Api.csproj"
PUBLISHER = LAB / "Publisher/EquipmentLibrary.Lab.Publisher.csproj"
PICKUP = LAB / "PickupService/EquipmentLibrary.Lab.PickupService.csproj"
BROKER_IMAGE = "nats:2.15.3-alpine@sha256:558394a23dd3c36f2d3a64b51865fd09908010e80a542cea5e75c2af543ed62a"


def free_port():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def eventually(check, description, timeout=35):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if check():
            return
        time.sleep(0.05)
    raise TimeoutError(description)


def request(base, path, *, method="GET", token="demo-staff", data=None):
    body = None if data is None else json.dumps(data).encode()
    headers = {"Authorization": "Bearer " + token, "Content-Type": "application/json"}
    with urlopen(Request(base + path, data=body, method=method, headers=headers), timeout=8) as response:
        text = response.read()
        if response.headers.get_content_type() == "application/json" and text:
            return json.loads(text)
        return text


def reachable(url):
    try:
        request(url, "/health")
        return True
    except URLError:
        return False


def stop(process):
    if process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=6)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=3)


def launch(project, url, env, cleanup, scratch):
    binary = project.parent / "bin/Release/net11.0" / (project.stem + ".dll")
    log = scratch / (project.stem + "-" + uuid.uuid4().hex[:6] + ".log")
    process_env = {**os.environ, **env, "ASPNETCORE_ENVIRONMENT": "Development", "ASPNETCORE_URLS": url,
                   "DOTNET_USE_POLLING_FILE_WATCHER": "1", "DOTNET_hostBuilder__reloadConfigOnChange": "false"}
    with log.open("w") as output:
        process = subprocess.Popen(["dotnet", str(binary)], cwd=project.parent, env=process_env,
                                   stdout=output, stderr=subprocess.STDOUT)
    cleanup.callback(stop, process)

    def started():
        if process.poll() is not None:
            raise RuntimeError(f"{project.stem} exited during startup:\n" + log.read_text()[-2500:])
        return reachable(url)

    eventually(started, f"{project.stem} failed to become healthy")
    return process


def broker(cleanup, scratch):
    port = free_port()
    password = secrets.token_hex(16)
    native = os.getenv("NATS_SERVER") or shutil.which("nats-server")
    log = scratch / "broker.log"
    with log.open("w") as output:
        if native:
            actual = subprocess.check_output([native, "-v"], text=True).strip()
            if actual != "nats-server: v2.15.3":
                raise RuntimeError("The lab pins nats-server v2.15.3; unset NATS_SERVER and remove nats-server from PATH to use Docker.")
            process = subprocess.Popen([native, "-js", "-sd", str(scratch / "jetstream"), "-a", "127.0.0.1",
                                        "-p", str(port), "--user", "lab", "--pass", password], stdout=output, stderr=output)
        else:
            name = "equipment-lab-nats-" + uuid.uuid4().hex[:12]
            process = subprocess.Popen(["docker", "run", "--rm", "--name", name, "-p", f"127.0.0.1:{port}:4222",
                                        BROKER_IMAGE, "-js", "--user", "lab", "--pass", password], stdout=output, stderr=output)
            cleanup.callback(subprocess.run, ["docker", "rm", "-f", name], stdout=subprocess.DEVNULL,
                             stderr=subprocess.DEVNULL, timeout=15)
    cleanup.callback(stop, process)

    def ready():
        if process.poll() is not None:
            raise RuntimeError("Broker failed to start: " + log.read_text()[-1000:])
        try:
            with socket.create_connection(("127.0.0.1", port), timeout=0.3) as sock:
                return sock.recv(5) == b"INFO "
        except OSError:
            return False

    eventually(ready, "JetStream broker failed to become ready", timeout=90)
    return f"nats://lab:{password}@127.0.0.1:{port}"
