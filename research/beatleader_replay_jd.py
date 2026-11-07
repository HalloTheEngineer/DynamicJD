"""Collect effective JD preferences from public BeatLeader replays.

Usage: python3 beatleader_replay_jd.py [player_id]
Requires only the Python standard library.
"""
import concurrent.futures
import json
import statistics
import struct
import sys
import time
import urllib.request
from collections import defaultdict

UA = {"User-Agent": "DynamicJD-research/1.0"}
TYPE_BITS = {1: "Accuracy", 2: "Tech", 4: "Balanced", 8: "Speed", 16: "Fitbeat", 32: "Linear", 64: "BombReset"}


def get(url, timeout=25):
    for attempt in range(3):
        try:
            return urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=timeout).read()
        except Exception:
            if attempt == 2:
                raise
            time.sleep(1)


def get_json(url):
    return json.loads(get(url))


def parse_bsor_header(data):
    offset = 0
    magic = struct.unpack_from("<i", data, offset)[0]
    offset += 4
    version = data[offset]
    offset += 1
    if magic != 0x442D3D69 or version != 1 or data[offset] != 0:
        raise ValueError("not a BSOR v1 replay")
    offset += 1

    def read_string():
        nonlocal offset
        length = struct.unpack_from("<i", data, offset)[0]
        offset += 4
        value = data[offset:offset + length].decode("utf-8", "replace")
        offset += length
        return value

    for _ in range(13):
        read_string()
    offset += 4                 # score
    read_string()               # mode
    read_string()               # environment
    read_string()               # modifiers
    return struct.unpack_from("<f", data, offset)[0]


def range_replay(url):
    request = urllib.request.Request(url, headers={**UA, "Range": "bytes=0-8191"})
    with urllib.request.urlopen(request, timeout=25) as response:
        return response.read()


def original_jd(bpm, njs, beat_offset):
    one_beat = 60.0 / bpm
    half = 4.0
    while njs * one_beat * half > 17.999:
        half /= 2.0
    half = max(0.25, half + beat_offset)
    return njs * one_beat * half * 2.0


def reaction_time(jd, njs):
    return jd * 500.0 / (2.0 * njs)


def types_from_mask(mask):
    return [name for bit, name in TYPE_BITS.items() if mask & bit]


def collect(player_id, count=30):
    scores = get_json(
        f"https://api.beatleader.xyz/player/{player_id}/scores?page=1&count={count}&sortBy=top&order=desc"
    )
    jobs = []
    for score in scores.get("data", []):
        leaderboard = score.get("leaderboard") or {}
        difficulty = leaderboard.get("difficulty") or {}
        song = leaderboard.get("song") or {}
        if not score.get("replay") or not difficulty.get("njs") or not song.get("bpm"):
            continue
        if not types_from_mask(difficulty.get("type") or 0):
            continue
        jobs.append((difficulty, song, score["replay"]))

    def work(job):
        difficulty, song, url = job
        try:
            jd = parse_bsor_header(range_replay(url))
            if jd <= 0:
                return None
            njs = difficulty["njs"]
            authored = original_jd(song["bpm"], njs, difficulty.get("noteJumpStartBeatOffset") or 0)
            return {
                "types": types_from_mask(difficulty["type"]),
                "njs": njs,
                "rt": reaction_time(jd, njs),
                "delta": jd - authored,
            }
        except Exception:
            return None

    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        return [row for row in pool.map(work, jobs) if row]


def main():
    player_id = sys.argv[1] if len(sys.argv) > 1 else "1922350521131465"
    rows = [row for row in collect(player_id) if abs(row["delta"]) > 1.5]
    by_type = defaultdict(list)
    for row in rows:
        for name in row["types"]:
            by_type[name].append(row["rt"])
    for name, values in sorted(by_type.items()):
        if len(values) >= 3:
            print(f"{name:10} n={len(values):3} medianRT={statistics.median(values):6.1f} ms")


if __name__ == "__main__":
    main()
