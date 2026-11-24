"""Exercise the three real modules over HTTP. Requires the local demo identities."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import json
import threading
import time
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from uuid import uuid4


class Library:
    def __init__(self, base):
        self.base = base.rstrip("/")

    def request(self, method, path, actor=None, body=None, expected=(200,)):
        headers = {"Accept": "application/json"}
        if actor:
            headers["Authorization"] = f"Bearer demo-{actor}"
        if body is not None:
            headers["Content-Type"] = "application/json"
        request = Request(self.base + path, data=json.dumps(body).encode() if body is not None else None,
                          method=method, headers=headers)
        try:
            response = urlopen(request, timeout=8)
        except HTTPError as error:
            response = error
        with response:
            raw = response.read().decode()
            if response.status not in expected:
                raise AssertionError(f"{method} {path}: expected {expected}, got {response.status}: {raw}")
            data = json.loads(raw) if raw else None
            return response.status, data

    def queue(self, copy_id, hold_ids):
        deadline = time.monotonic() + 15
        path = f"/api/pickup-desk/queue?copyId={copy_id}"
        while time.monotonic() < deadline:
            _, data = self.request("GET", path, "staff")
            if [row["holdId"] for row in data["items"]] == hold_ids:
                return data["items"]
            time.sleep(.1)
        raise AssertionError(f"Pickup queue for {copy_id} did not converge to {hold_ids}: {data}")


def borrow_and_return(library):
    _, item = library.request("POST", "/api/catalog/items", "staff",
                              {"title": "Cordless drill", "description": "Includes battery and charger."}, (201,))
    _, copy = library.request("POST", "/api/lending/copies", "staff",
                              {"catalogItemId": item["id"], "barcode": f"DRILL-{uuid4().hex[:12]}"}, (201,))
    copy_id = copy["copyId"]
    start = threading.Barrier(2)

    def compete(actor):
        start.wait(timeout=5)
        status, result = library.request("POST", "/api/lending/holds", actor, {"copyId": copy_id}, (201, 409))
        return status, actor, result

    with ThreadPoolExecutor(max_workers=2) as pool:
        results = sorted(pool.map(compete, ["borrower-a", "borrower-b"]))
    if [r[0] for r in results] != [201, 409]:
        raise AssertionError(f"Expected exactly one winning hold: {results}")
    (_, winner, hold), (_, loser, conflict) = results
    assert conflict["code"] == "copy-unavailable"
    hold_id = hold["holdId"]
    path = f"/api/lending/holds/{hold_id}"
    library.request("GET", path, expected=(401,))
    library.request("GET", path, loser, expected=(403,))
    library.request("POST", path + "/cancel", loser, expected=(403,))
    _, owned = library.request("GET", path, winner)
    assert owned["state"] == "held"
    assert "borrowerId" not in library.queue(copy_id, [hold_id])[0]
    library.request("GET", "/api/pickup-desk/queue", winner, expected=(403,))
    desk = f"/api/pickup-desk/queue/{hold_id}"
    library.request("POST", desk + "/claim", "staff", expected=(204,))
    library.request("POST", desk + "/ready", "staff", expected=(204,))
    _, collected = library.request("POST", path + "/collect", "staff")
    assert collected["state"] == "collected"
    library.queue(copy_id, [])
    loan = f"/api/lending/loans/{hold_id}"
    library.request("POST", loan + "/renew", winner)
    library.request("POST", loan + "/return", "staff")
    library.request("POST", loan + "/return", "staff")
    _, next_hold = library.request("POST", "/api/lending/holds", loser, {"copyId": copy_id}, (201,))
    library.queue(copy_id, [next_hold["holdId"]])
    library.request("POST", f"/api/lending/holds/{next_hold['holdId']}/cancel", loser)
    library.queue(copy_id, [])
    print(f"PASS copy {copy_id}: {winner} reserved, {loser} got 409, staff prepared one pickup.")
    print(f"PASS hold {hold_id}: collected, renewed and returned; a second hold was cancelled.")
    return copy_id, hold_id


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", default="http://127.0.0.1:5284")
    borrow_and_return(Library(parser.parse_args().base))
