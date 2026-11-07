"""Cross-validated study of linear reaction-time profiles against BeatLeader replay data."""

import concurrent.futures
import json
import math
import statistics
import struct
import sys
import time
import urllib.error
import urllib.request
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
from scipy import stats
from sklearn.model_selection import KFold, RepeatedKFold

UA = {"User-Agent": "DynamicJD-research/1.0"}
API = "https://api.beatleader.xyz"
TYPE_BITS = {1: "Accuracy", 2: "Tech", 4: "Balanced", 8: "Speed", 16: "Fitbeat", 32: "Linear", 64: "BombReset"}
TYPE_COEFFICIENTS = {
    1: (0.320, -0.160, 0.120),
    2: (0.050, -0.010, 0.0),
    4: (0.0, 0.0, 0.0),
    8: (-0.056, -0.010, 0.0),
    16: (0.018, 0.0, 0.0),
    32: (-0.056, -0.010, 0.0),
    64: (-0.008, 0.0, 0.0),
}
STRENGTH_GRID = np.round(np.arange(0.0, 1.0001, 0.05), 2)
SPEED_MODIFIERS = {"FS", "SF", "SS", "FN"}
ROOT = Path(__file__).resolve().parent
DATA_PATH = ROOT / "fit_study_data.json"
RESULTS_PATH = ROOT / "fit_study_results.json"


def get_bytes(url, headers=None, timeout=25, attempts=3):
    request = urllib.request.Request(url, headers={**UA, **(headers or {})})
    error = None
    for attempt in range(attempts):
        try:
            with urllib.request.urlopen(request, timeout=timeout) as response:
                return response.read()
        except urllib.error.HTTPError as ex:
            error = ex
            if ex.code in (400, 404):
                raise
            time.sleep(0.6 * (attempt + 1))
        except Exception as ex:
            error = ex
            time.sleep(0.6 * (attempt + 1))
    raise error


def get_json(url):
    return json.loads(get_bytes(url).decode("utf-8"))


def get_range(url, limit=16384):
    request = urllib.request.Request(url, headers={**UA, "Range": f"bytes=0-{limit - 1}"})
    error = None
    for attempt in range(3):
        try:
            with urllib.request.urlopen(request, timeout=25) as response:
                return response.read(limit)
        except urllib.error.HTTPError as ex:
            error = ex
            if ex.code in (400, 404):
                raise
            time.sleep(0.6 * (attempt + 1))
        except Exception as ex:
            error = ex
            time.sleep(0.6 * (attempt + 1))
    raise error


def parse_bsor_header(data):
    offset = 0

    def read_i32():
        nonlocal offset
        value = struct.unpack_from("<i", data, offset)[0]
        offset += 4
        return value

    def read_string():
        nonlocal offset
        length = read_i32()
        if length < 0 or length > 1024 * 1024:
            raise ValueError("invalid string length")
        value = data[offset:offset + length].decode("utf-8", "replace")
        offset += length
        return value

    if read_i32() != 0x442D3D69:
        raise ValueError("not a BSOR replay")
    if data[offset] != 1:
        raise ValueError("unsupported BSOR version")
    offset += 1
    if data[offset] != 0:
        raise ValueError("unexpected BSOR compression flag")
    offset += 1

    read_string()
    read_string()
    read_string()
    read_string()
    read_string()
    read_string()
    read_string()
    read_string()
    read_string()
    hash_value = read_string()
    read_string()
    read_string()
    read_string()
    offset += 4
    read_string()
    read_string()
    read_string()
    jump_distance = struct.unpack_from("<f", data, offset)[0]
    return jump_distance, hash_value


def original_jd(bpm, njs, beat_offset):
    one_beat = 60.0 / bpm
    half_jump = 4.0
    while njs * one_beat * half_jump > 17.999:
        half_jump /= 2.0
    half_jump = max(0.25, half_jump + beat_offset)
    return njs * one_beat * half_jump * 2.0


def reaction_time(jd, njs):
    return jd * 500.0 / (2.0 * njs)


def type_factor(mask, njs, strength=1.0):
    z = (njs - 20.0) / 10.0
    factors = []
    for bit, (a, b, c) in TYPE_COEFFICIENTS.items():
        if mask & bit:
            factors.append(min(1.85, max(0.55, 1.0 + a + b * z + c * z * z)))
    raw = sum(factors) / len(factors) if factors else 1.0
    return 1.0 + (raw - 1.0) * strength


def player_page(target_rank):
    page = max(1, (int(target_rank) - 1) // 5 + 1)
    url = f"{API}/v2/players?page={page}&count=5&sortBy=pp&mapsType=all"
    data = get_json(url).get("data") or []
    return [p for p in data if p.get("id") and not p.get("banned") and not p.get("inactive")][:3]


def collect_player(player, count=100):
    pid = player["id"]
    name = player.get("name") or pid
    url = f"{API}/player/{pid}/scores?page=1&count={min(100, count)}&sortBy=top&order=desc"
    scores = get_json(url).get("data") or []
    jobs = []
    for score in scores:
        leaderboard = score.get("leaderboard") or {}
        difficulty = leaderboard.get("difficulty") or {}
        song = leaderboard.get("song") or {}
        replay = score.get("replay")
        mask = int(difficulty.get("type") or 0)
        njs = difficulty.get("njs")
        bpm = song.get("bpm")
        if not replay or not njs or not bpm or not mask:
            continue
        jobs.append((score, difficulty, song, replay))

    def work(job):
        score, difficulty, song, replay = job
        try:
            raw = get_range(replay)
            jd, replay_hash = parse_bsor_header(raw)
            if jd <= 0:
                return None, "invalid_jd", None
            njs = float(difficulty["njs"])
            bpm = float(song["bpm"])
            offset = float(difficulty.get("noteJumpStartBeatOffset") or 0.0)
            authored = original_jd(bpm, njs, offset)
            if authored <= 0:
                return None, "invalid_authored_jd", None
            rt = reaction_time(jd, njs)
            authored_rt = reaction_time(authored, njs)
            modded = abs(jd - authored) > 1.5 or abs(rt - authored_rt) > 25.0
            if not modded:
                return None, None, None
            if rt < 80.0 or rt > 600.0:
                return None, "rt_outlier", None
            score_hash = (song.get("hash") or "").lower()
            if replay_hash and score_hash and replay_hash.lower() != score_hash:
                return None, "hash_mismatch", None
            return {
                "njs": njs,
                "rt": rt,
                "jd": jd,
                "authored_jd": authored,
                "mask": int(difficulty["type"]),
                "timestamp": int(score.get("timeset") or 0),
                "accuracy": float(score.get("accuracy") or 0.0),
                "pp": float(score.get("pp") or 0.0),
                "modifiers": score.get("modifiers") or "",
            }, None, None
        except Exception as ex:
            return None, "replay_error", str(ex)

    points = []
    errors = Counter()
    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        for point, error, _ in pool.map(work, jobs):
            if point:
                points.append(point)
            elif error:
                errors[error] += 1
    return {
        "id": pid,
        "name": name,
        "rank": player.get("rank"),
        "pp": player.get("pp"),
        "scores": len(scores),
        "replay_candidates": len(jobs),
        "points": sorted(points, key=lambda p: (p["njs"], p["timestamp"])),
        "errors": dict(errors),
    }


def build_dataset(refresh=False, count=100):
    if DATA_PATH.exists() and not refresh:
        return json.loads(DATA_PATH.read_text())

    players = []
    seen = set()
    for target in (1, 800, 4000, 16000):
        for player in player_page(target):
            if player["id"] in seen:
                continue
            seen.add(player["id"])
            players.append(player)
    user_id = "76561199131267169"
    if user_id not in seen:
        players.append({"id": user_id, "name": "PixlPainter", "rank": None, "pp": None})

    collected = []
    for index, player in enumerate(players, 1):
        print(f"[{index}/{len(players)}] collecting {player.get('name') or player['id']}", flush=True)
        collected.append(collect_player(player, count))
        print(f"    points={len(collected[-1]['points'])} errors={collected[-1]['errors']}", flush=True)

    model = {
        "collected_at": datetime.now(timezone.utc).isoformat(),
        "score_count_per_player": count,
        "players": collected,
    }
    DATA_PATH.write_text(json.dumps(model, indent=2))
    return model


def as_arrays(player):
    points = player["points"]
    x = np.array([p["njs"] for p in points], dtype=float)
    y = np.array([p["rt"] for p in points], dtype=float)
    mask = np.array([p["mask"] for p in points], dtype=int)
    timestamp = np.array([p["timestamp"] for p in points], dtype=float)
    return x, y, mask, timestamp


def design(x, degree):
    dx = x - 20.0
    return np.column_stack([np.ones_like(dx)] + [dx ** degree for degree in range(1, degree + 1)])


def fit_ols(x, y, degree=1, weights=None):
    matrix = design(x, degree)
    if weights is not None:
        root = np.sqrt(weights)
        matrix = matrix * root[:, None]
        y = y * root
    coefficients, _, _, _ = np.linalg.lstsq(matrix, y, rcond=None)
    return coefficients


def predict_ols(coefficients, x):
    return design(x, len(coefficients) - 1) @ coefficients


def fit_theil(x, y):
    slope, intercept, _, _ = stats.theilslopes(y, x - 20.0)
    return np.array([intercept, slope], dtype=float)


def predict_theil(coefficients, x):
    return coefficients[0] + coefficients[1] * (x - 20.0)


def recency_weights(timestamps, half_life_days=365.0):
    valid = timestamps[timestamps > 0]
    if len(valid) == 0:
        return np.ones(len(timestamps))
    now = max(valid.max(), timestamps.max())
    age_days = np.maximum(0.0, (now - timestamps) / 86400.0)
    return np.power(0.5, age_days / half_life_days)


def factor_vector(mask, x, strength=1.0):
    return np.array([type_factor(int(m), float(n), strength) for m, n in zip(mask, x)])


def fit_strength_model(x, y, mask, inner_folds=3):
    if len(x) < inner_folds * 4:
        return 0.0, fit_ols(x, y, 1)
    splitter = KFold(n_splits=inner_folds, shuffle=True, random_state=17)
    best_strength = 0.0
    best_error = float("inf")
    for strength in STRENGTH_GRID:
        factors = factor_vector(mask, x, float(strength))
        errors = []
        for train_idx, test_idx in splitter.split(x):
            base = y[train_idx] / factors[train_idx]
            coefficients = fit_ols(x[train_idx], base, 1)
            prediction = predict_ols(coefficients, x[test_idx]) * factors[test_idx]
            errors.append(np.abs(prediction - y[test_idx]))
        error = float(np.mean(np.concatenate(errors)))
        if error < best_error:
            best_error = error
            best_strength = float(strength)
    factors = factor_vector(mask, x, best_strength)
    coefficients = fit_ols(x, y / factors, 1)
    return best_strength, coefficients


def fit_per_type(x, y, mask, degree=1, minimum=12):
    coefficients = {}
    for value in np.unique(mask):
        selected = mask == value
        if selected.sum() >= minimum:
            coefficients[int(value)] = fit_ols(x[selected], y[selected], degree)
    return coefficients


def predict_per_type(models, global_coefficients, x, mask, degree=1):
    prediction = np.empty_like(x)
    for value in np.unique(mask):
        selected = mask == value
        coefficients = models.get(int(value), global_coefficients)
        prediction[selected] = predict_ols(coefficients, x[selected])
    return prediction


def fit_predict(model, train, test):
    xtr, ytr, mtr, ttr = train
    xte, yte, mte, tte = test
    if model == "constant":
        return np.full_like(yte, np.mean(ytr))
    if model == "linear":
        return predict_ols(fit_ols(xtr, ytr, 1), xte)
    if model == "quadratic":
        return predict_ols(fit_ols(xtr, ytr, 2), xte)
    if model == "theil":
        return predict_theil(fit_theil(xtr, ytr), xte)
    if model == "current_clone":
        line = fit_theil(xtr, ytr)
        return predict_theil(line, xte) * factor_vector(mte, xte, 1.0)
    if model == "type_linear":
        ftr = factor_vector(mtr, xtr, 1.0)
        fte = factor_vector(mte, xte, 1.0)
        return predict_ols(fit_ols(xtr, ytr / ftr, 1), xte) * fte
    if model == "type_quadratic":
        ftr = factor_vector(mtr, xtr, 1.0)
        fte = factor_vector(mte, xte, 1.0)
        return predict_ols(fit_ols(xtr, ytr / ftr, 2), xte) * fte
    if model == "type_tuned":
        strength, coefficients = fit_strength_model(xtr, ytr, mtr)
        return predict_ols(coefficients, xte) * factor_vector(mte, xte, strength)
    if model == "per_type_linear":
        return predict_per_type(fit_per_type(xtr, ytr, mtr, 1),
                                fit_ols(xtr, ytr, 1), xte, mte, 1)
    if model == "per_type_quadratic":
        return predict_per_type(fit_per_type(xtr, ytr, mtr, 2, 20),
                                fit_ols(xtr, ytr, 2), xte, mte, 2)
    if model == "linear_recency":
        weights = recency_weights(ttr)
        return predict_ols(fit_ols(xtr, ytr, 1, weights), xte)
    if model == "current_clone_recency":
        line = fit_theil(xtr, ytr)
        return predict_theil(line, xte) * factor_vector(mte, xte, 1.0)
    raise ValueError(model)


def choose_and_predict(train, test):
    candidates = ["constant", "linear", "quadratic", "theil", "type_tuned"]
    if len(train[0]) < 15:
        return fit_predict("constant", train, test), "constant"
    splitter = KFold(n_splits=3, shuffle=True, random_state=31)
    best_model = "constant"
    best_error = float("inf")
    for candidate in candidates:
        errors = []
        for inner_train, inner_test in splitter.split(train[0]):
            train_part = tuple(values[inner_train] for values in train)
            test_part = tuple(values[inner_test] for values in train)
            prediction = fit_predict(candidate, train_part, test_part)
            errors.append(np.abs(prediction - test_part[1]))
        error = float(np.mean(np.concatenate(errors)))
        if error < best_error:
            best_error = error
            best_model = candidate
    return fit_predict(best_model, train, test), best_model


def metrics(y, prediction):
    error = prediction - y
    mae = float(np.mean(np.abs(error)))
    rmse = float(math.sqrt(np.mean(error ** 2)))
    ss_res = float(np.sum(error ** 2))
    ss_tot = float(np.sum((y - np.mean(y)) ** 2))
    r2 = 1.0 - ss_res / ss_tot if ss_tot > 0 else 0.0
    return {"mae": mae, "rmse": rmse, "r2": r2}


def cross_validate(x, y, mask, timestamp, repeats=5):
    models = ["constant", "linear", "quadratic", "theil", "current_clone",
              "type_linear", "type_quadratic", "type_tuned", "per_type_linear",
              "per_type_quadratic", "linear_recency", "auto"]
    splitter = RepeatedKFold(n_splits=5, n_repeats=repeats, random_state=23)
    actual = []
    predictions = {model: [] for model in models}
    choices = Counter()
    for train_idx, test_idx in splitter.split(x):
        train = (x[train_idx], y[train_idx], mask[train_idx], timestamp[train_idx])
        test = (x[test_idx], y[test_idx], mask[test_idx], timestamp[test_idx])
        actual.append(y[test_idx])
        for model in models:
            if model == "auto":
                prediction, choice = choose_and_predict(train, test)
                predictions[model].append(prediction)
                choices[choice] += 1
            else:
                predictions[model].append(fit_predict(model, train, test))
    y_all = np.concatenate(actual)
    return ({model: metrics(y_all, np.concatenate(values)) for model, values in predictions.items()},
            dict(choices))


def temporal_holdout(x, y, mask, timestamp):
    models = ["constant", "linear", "quadratic", "current_clone", "type_linear",
              "type_quadratic", "type_tuned", "linear_recency", "auto"]
    order = np.argsort(timestamp, kind="stable")
    cut = max(10, int(len(x) * 0.8))
    train_idx = order[:cut]
    test_idx = order[cut:]
    if len(test_idx) < 5:
        return {}
    train = (x[train_idx], y[train_idx], mask[train_idx], timestamp[train_idx])
    test = (x[test_idx], y[test_idx], mask[test_idx], timestamp[test_idx])
    results = {}
    for model in models:
        if model == "auto":
            prediction, _ = choose_and_predict(train, test)
        else:
            prediction = fit_predict(model, train, test)
        results[model] = metrics(test[1], prediction)
    return results


def diagnostics(x, y, mask):
    coefficients = fit_ols(x, y, 1)
    prediction = predict_ols(coefficients, x)
    error = y - prediction
    n = len(x)
    sxx = float(np.sum((x - np.mean(x)) ** 2))
    mse = float(np.sum(error ** 2) / max(1, n - 2))
    slope_se = math.sqrt(mse / sxx) if sxx > 0 else float("inf")
    t_value = stats.t.ppf(0.975, max(1, n - 2))
    slope = float(coefficients[1])
    strength, strength_coefficients = fit_strength_model(x, y, mask)
    type_prediction = predict_ols(strength_coefficients, x) * factor_vector(mask, x, strength)
    base_prediction = predict_ols(strength_coefficients, x)
    return {
        "n": n,
        "njs_min": float(np.min(x)),
        "njs_max": float(np.max(x)),
        "njs_iqr": float(np.percentile(x, 75) - np.percentile(x, 25)),
        "rt20": float(coefficients[0]),
        "slope": slope,
        "slope_ci95": [slope - t_value * slope_se, slope + t_value * slope_se],
        "slope_se": slope_se,
        "linear_r2": metrics(y, prediction)["r2"],
        "quadratic_r2": metrics(y, predict_ols(fit_ols(x, y, 2), x))["r2"],
        "theil_slope": float(fit_theil(x, y)[1]),
        "tuned_type_strength": strength,
        "type_model_mae": float(np.mean(np.abs(type_prediction - y))),
        "base_linear_mae": float(np.mean(np.abs(base_prediction - y))),
        "type_counts": {TYPE_BITS[bit]: int(np.sum(mask & bit)) for bit in TYPE_BITS if np.any(mask & bit)},
    }


def filtered_points(player, include_speed):
    if include_speed:
        return list(player["points"])
    kept = []
    for point in player["points"]:
        modifiers = {part.strip() for part in (point.get("modifiers") or "").split(",") if part.strip()}
        if modifiers & SPEED_MODIFIERS:
            continue
        kept.append(point)
    return kept


def analyze(dataset, include_speed=False):
    players = []
    excluded_speed = 0
    speed_effect = []
    for source in dataset["players"]:
        points = filtered_points(source, include_speed)
        excluded_speed += len(source["points"]) - len(points)
        if not include_speed and len(source["points"]) >= 30 and len(points) >= 30:
            raw = dict(source)
            raw["points"] = list(source["points"])
            cleaned = dict(source)
            cleaned["points"] = points
            raw_x, raw_y, raw_mask, _ = as_arrays(raw)
            clean_x, clean_y, clean_mask, _ = as_arrays(cleaned)
            raw_diag = diagnostics(raw_x, raw_y, raw_mask)
            clean_diag = diagnostics(clean_x, clean_y, clean_mask)
            speed_effect.append({
                "id": source["id"],
                "name": source["name"],
                "rank": source["rank"],
                "all_points": len(source["points"]),
                "clean_points": len(points),
                "all_slope": raw_diag["slope"],
                "clean_slope": clean_diag["slope"],
                "slope_change": clean_diag["slope"] - raw_diag["slope"],
                "all_rt20": raw_diag["rt20"],
                "clean_rt20": clean_diag["rt20"],
            })
        if len(points) < 30:
            continue
        player = dict(source)
        player["points"] = points
        x, y, mask, timestamp = as_arrays(player)
        player_result = {
            "id": player["id"],
            "name": player["name"],
            "rank": player["rank"],
            "scores": player["scores"],
            "replay_candidates": player["replay_candidates"],
            "errors": player["errors"],
            "points": len(player["points"]),
            "diagnostics": diagnostics(x, y, mask),
        }
        player_result["cv"], player_result["auto_choices"] = cross_validate(x, y, mask, timestamp)
        player_result["temporal"] = temporal_holdout(x, y, mask, timestamp)
        players.append(player_result)

    model_names = list(players[0]["cv"].keys()) if players else []
    aggregate = {}
    for model in model_names:
        cv_mae = [p["cv"][model]["mae"] for p in players if model in p["cv"]]
        cv_rmse = [p["cv"][model]["rmse"] for p in players if model in p["cv"]]
        cv_r2 = [p["cv"][model]["r2"] for p in players if model in p["cv"]]
        temporal_mae = [p["temporal"][model]["mae"] for p in players if model in p["temporal"]]
        aggregate[model] = {
            "median_mae": float(np.median(cv_mae)) if cv_mae else None,
            "mean_mae": float(np.mean(cv_mae)) if cv_mae else None,
            "median_rmse": float(np.median(cv_rmse)) if cv_rmse else None,
            "median_r2": float(np.median(cv_r2)) if cv_r2 else None,
            "temporal_median_mae": float(np.median(temporal_mae)) if temporal_mae else None,
            "temporal_mean_mae": float(np.mean(temporal_mae)) if temporal_mae else None,
        }

    wins = Counter()
    for player in players:
        best = min(player["cv"].items(), key=lambda item: item[1]["mae"])
        wins[best[0]] += 1

    pairwise = {}
    for left, right in [("linear", "constant"), ("quadratic", "linear"), ("type_linear", "linear"),
                        ("type_quadratic", "linear"), ("type_tuned", "linear"),
                        ("current_clone", "linear"), ("per_type_linear", "linear"),
                        ("linear_recency", "linear"), ("auto", "linear"), ("auto", "constant")]:
        improvements = []
        for player in players:
            a = player["cv"][left]["mae"]
            b = player["cv"][right]["mae"]
            improvements.append((b - a) / b * 100.0)
        pairwise[f"{left}_vs_{right}"] = {
            "mean_improvement_pct": float(np.mean(improvements)),
            "median_improvement_pct": float(np.median(improvements)),
            "players_improved": int(sum(value > 0 for value in improvements)),
            "players_improved_gt5pct": int(sum(value > 5 for value in improvements)),
            "players": len(improvements),
        }

    slopes = [p["diagnostics"]["slope"] for p in players]
    strengths = [p["diagnostics"]["tuned_type_strength"] for p in players]
    auto_choices = Counter()
    for player in players:
        auto_choices.update(player["auto_choices"])
    return {
        "collected_at": dataset["collected_at"],
        "score_count_per_player": dataset["score_count_per_player"],
        "speed_modifier_filtered_points": excluded_speed,
        "speed_filter_effect": speed_effect,
        "players": players,
        "aggregate": aggregate,
        "wins": dict(wins),
        "pairwise": pairwise,
        "auto_choices": dict(auto_choices),
        "slope_summary": {
            "median": float(np.median(slopes)),
            "min": float(np.min(slopes)),
            "max": float(np.max(slopes)),
            "negative": int(sum(value < 0 for value in slopes)),
            "positive": int(sum(value > 0 for value in slopes)),
            "ci_includes_zero": int(sum(p["diagnostics"]["slope_ci95"][0] <= 0 <= p["diagnostics"]["slope_ci95"][1]
                                         for p in players)),
        },
        "strength_summary": {
            "median": float(np.median(strengths)),
            "zero": int(sum(value == 0 for value in strengths)),
            "one": int(sum(value == 1 for value in strengths)),
            "values": strengths,
        },
    }


def print_summary(results):
    print("\n## Sample")
    print(f"Players: {len(results['players'])}")
    print(f"Collected: {results['collected_at']}")
    print(f"Score requests per player: {results['score_count_per_player']}")
    print(f"Speed-modifier points excluded: {results['speed_modifier_filtered_points']}")
    if results.get("speed_filter_effect"):
        changes = [abs(item["slope_change"]) for item in results["speed_filter_effect"]]
        examples = sorted(results["speed_filter_effect"], key=lambda item: abs(item["slope_change"]), reverse=True)[:5]
        print(f"Median absolute slope change after excluding speed modifiers: {np.median(changes):.2f} ms/NJS")
        for item in examples:
            print(f"  {item['name']}: {item['all_slope']:.2f} -> {item['clean_slope']:.2f} ms/NJS")
    for player in results["players"]:
        d = player["diagnostics"]
        print(f"- {player['name']} (rank {player['rank']}): n={player['points']}, "
              f"NJS {d['njs_min']:.1f}-{d['njs_max']:.1f}, slope {d['slope']:.2f} "
              f"[{d['slope_ci95'][0]:.2f}, {d['slope_ci95'][1]:.2f}], "
              f"R2 {d['linear_r2']:.3f}/{d['quadratic_r2']:.3f}, "
              f"type strength {d['tuned_type_strength']:.2f}")

    print("\n## Cross-validated RT error")
    print("| Model | Mean MAE ms | Median MAE ms | Mean JD@20 | RMSE ms | Median R2 | Temporal mean MAE ms |")
    print("|---|---:|---:|---:|---:|---:|---:|")
    for name, values in sorted(results["aggregate"].items(), key=lambda item: item[1]["mean_mae"]):
        temporal = values["temporal_mean_mae"]
        temporal_text = f"{temporal:.2f}" if temporal is not None else "-"
        print(f"| {name} | {values['mean_mae']:.2f} | {values['median_mae']:.2f} | "
              f"{values['mean_mae'] * 0.08:.2f} | {values['median_rmse']:.2f} | "
              f"{values['median_r2']:.3f} | {temporal_text} |")

    print("\n## Pairwise vs linear")
    for name, values in results["pairwise"].items():
        print(f"- {name}: mean {values['mean_improvement_pct']:.1f}%, "
              f"median {values['median_improvement_pct']:.1f}%, "
              f"{values['players_improved']}/{values['players']} improved, "
              f"{values['players_improved_gt5pct']} improved >5%")
    print("\nWins:", results["wins"])
    print("Auto choices:", results["auto_choices"])
    print("Slope summary:", results["slope_summary"])
    print("Tuned type strength:", results["strength_summary"])


def main():
    refresh = "--refresh" in sys.argv
    include_speed = "--include-speed" in sys.argv
    dataset = build_dataset(refresh=refresh)
    results = analyze(dataset, include_speed=include_speed)
    results["includes_speed_modifiers"] = include_speed
    results["method"] = {
        "sampling": "top 100 scores per player, rank bands near 1/800/4000/16000 plus local player",
        "validation": "5-fold CV repeated 5 times; temporal holdout uses the most recent 20% of plays",
        "speed_modifiers_excluded": [] if include_speed else sorted(SPEED_MODIFIERS),
        "models": list(results["aggregate"].keys()),
    }
    RESULTS_PATH.write_text(json.dumps(results, indent=2))
    print_summary(results)
    print(f"\nSaved {RESULTS_PATH}")


if __name__ == "__main__":
    main()
