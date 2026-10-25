#!/usr/bin/env python3
"""Offline held-out ranking comparison. Never connects to SQL or changes the app.

Input schema and interpretation are documented in docs/recommendations-runtime/evaluation.md.
Every method ranks the SAME supplied eligible candidate pool. This measures scoring,
not generator recall, data verification or actual production latency.
"""
from __future__ import annotations

import argparse
import csv
import itertools
import json
import math
import statistics
import sys
import time
from pathlib import Path

LEVELS = ("Low", "Medium", "High")
DEFAULT_Q = ((0.0, 0.1, 0.25), (0.1, 0.45, 0.7), (0.25, 0.7, 1.0))
DEFAULT_VERSION = "cakecorner-sugeno-2input-v1"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def number(value, label, minimum=0.0, maximum=1.0):
    require(isinstance(value, (int, float)) and not isinstance(value, bool), label + ": numeric value required")
    value = float(value)
    require(math.isfinite(value) and minimum <= value <= maximum, label + ": out of range")
    return value


def membership(value):
    z = number(value, "fit")
    return {"Low": max(0.0, 1 - 2 * z), "Medium": 1 - abs(2 * z - 1), "High": max(0.0, 2 * z - 1)}


def load_method(path=None):
    if path is None:
        return DEFAULT_VERSION, {(a, b): DEFAULT_Q[i][j] for i, a in enumerate(LEVELS) for j, b in enumerate(LEVELS)}
    value = json.loads(Path(path).read_text(encoding="utf-8-sig"))
    # C# default serializers and web-style files are both accepted; other shapes fail.
    version = value.get("Version", value.get("version"))
    rules = value.get("Rules", value.get("rules"))
    require(isinstance(version, str) and version.strip(), "Method version is required")
    require(isinstance(rules, list) and len(rules) == 9, "Method must have nine two-input rules")
    result, ids = {}, set()
    for rule in rules:
        key = (rule.get("Taste", rule.get("taste")), rule.get("Sweetness", rule.get("sweetness")))
        rule_id = rule.get("Id", rule.get("id"))
        require(rule_id not in ids and isinstance(rule_id, str) and rule_id, "Duplicate/invalid rule ID")
        ids.add(rule_id)
        require(key not in result, "Duplicate rule antecedents")
        result[key] = number(rule.get("Consequent", rule.get("consequent")), "consequent")
    require(set(result) == set(itertools.product(LEVELS, repeat=2)), "Incomplete rule coverage")
    for i, a in enumerate(LEVELS):
        for j, b in enumerate(LEVELS):
            if i < 2:
                require(result[(a, b)] <= result[(LEVELS[i + 1], b)], "Non-monotone method")
            if j < 2:
                require(result[(a, b)] <= result[(a, LEVELS[j + 1])], "Non-monotone method")
    return version, result


def sugeno(taste, sweetness, rules):
    t, s = membership(taste), membership(sweetness)
    activation = [(t[a] * s[b], q) for (a, b), q in rules.items()]
    denominator = sum(w for w, _ in activation)
    require(math.isfinite(denominator) and denominator > 1e-12, "Zero rule activation")
    return sum(w * q for w, q in activation) / denominator


def score(candidate, active, method, rules):
    values = [candidate["tasteFit"] if name == "taste" else candidate["sweetnessFit"] for name in active]
    if method == "content":
        return candidate["tasteFit"] if "taste" in active else None
    if not values:
        return None
    if method == "linear" or len(values) == 1:
        return sum(values) / len(values)
    return sugeno(candidate["tasteFit"], candidate["sweetnessFit"], rules)


def rank(candidates, active, method, rules):
    candidates = [c for c in candidates if c["eligible"]]
    return sorted(candidates, key=lambda c: (-(score(c, active, method, rules) or 0.0), c["key"]))


def dcg(relevance):
    return sum((2 ** r - 1) / math.log2(position + 2) for position, r in enumerate(relevance))


def per_request_metrics(ranked, relevance, cutoff, relevant_threshold):
    selected = ranked[:cutoff]
    gains = [relevance[c["key"]] for c in selected]
    ideal = sorted(relevance.values(), reverse=True)[:cutoff]
    idcg = dcg(ideal)
    hits = sum(r >= relevant_threshold for r in gains)
    all_relevant = sum(r >= relevant_threshold for r in relevance.values())
    # Fixed K denominator also penalizes a list shorter than the requested length.
    return {
        "ndcg": dcg(gains) / idcg if idcg > 0 else 0.0,
        "precision": hits / cutoff,
        "recall_in_pool": hits / all_relevant if all_relevant else 0.0,
        "returned": len(selected),
    }


def read_pools(path):
    pools, seen = [], set()
    with Path(path).open(encoding="utf-8-sig") as source:
        for lineno, line in enumerate(source, 1):
            if not line.strip():
                continue
            row = json.loads(line)
            request_id = row.get("requestId")
            require(isinstance(request_id, str) and request_id and request_id not in seen, f"Line {lineno}: duplicate/invalid requestId")
            seen.add(request_id)
            require(row.get("split") in {"train", "validation", "test"}, request_id + ": explicit split required")
            active = row.get("activeFeatures")
            require(isinstance(active, list) and len(active) == len(set(active)) and set(active) <= {"taste", "sweetness"}, request_id + ": invalid activeFeatures")
            require(isinstance(row.get("candidates"), list), request_id + ": candidates list required")
            keys = set()
            for candidate in row["candidates"]:
                key = candidate.get("key")
                require(isinstance(key, str) and key and key.isascii() and key not in keys, request_id + ": duplicate/invalid ASCII candidate key")
                keys.add(key)
                require(type(candidate.get("eligible")) is bool, request_id + ": eligible must be boolean")
                constraint = candidate.get("constraintsSatisfied")
                require(constraint is None or type(constraint) is bool, request_id + ": invalid independent constraint check")
                for name, field in [("taste", "tasteFit"), ("sweetness", "sweetnessFit")]:
                    value = candidate.get(field)
                    if value is not None:
                        number(value, request_id + ": " + field)
                    if candidate["eligible"] and name in active:
                        require(value is not None, request_id + ": missing active candidate feature")
                candidate.setdefault("tasteFit", None)
                candidate.setdefault("sweetnessFit", None)
            pools.append(row)
    require(pools, "No request pools found")
    return pools


def read_judgments(path, pools):
    known = {row["requestId"]: {c["key"] for c in row["candidates"]} for row in pools}
    judgments = {}
    with Path(path).open(encoding="utf-8-sig", newline="") as source:
        reader = csv.DictReader(source)
        require(reader.fieldnames is not None and {"request_id", "candidate_key", "relevance"} <= set(reader.fieldnames), "Judgment CSV headers are invalid")
        for row in reader:
            request_id, key = row["request_id"], row["candidate_key"]
            require(request_id in known and key in known[request_id], "Judgment references unknown request/candidate")
            identity = (request_id, key)
            require(identity not in judgments, "Duplicate judgment; aggregate multiple raters explicitly before evaluation")
            try:
                value = float(row["relevance"])
            except (TypeError, ValueError) as error:
                raise ValueError("Invalid relevance") from error
            judgments[identity] = number(value, "relevance", 0, 3)
    return judgments


def evaluate(pools, judgments, version, rules, cutoff=5, split="test", threshold=2.0):
    selected_pools = [row for row in pools if row["split"] == split]
    require(selected_pools, "No pools in selected split")
    require(type(cutoff) is int and 1 <= cutoff <= 100, "K must be an integer 1..100")
    number(threshold, "relevant threshold", 0, 3)
    for row in selected_pools:
        missing = [c["key"] for c in row["candidates"] if c["eligible"] and (row["requestId"], c["key"]) not in judgments]
        require(not missing, row["requestId"] + ": every eligible candidate requires a held-out judgment; missing " + ",".join(missing[:3]))
    eligible_keys = {c["key"] for row in selected_pools for c in row["candidates"] if c["eligible"]}
    output = {
        "methodVersion": version,
        "split": split,
        "requests": len(selected_pools),
        "k": cutoff,
        "relevanceScale": [0, 3],
        "relevantThreshold": threshold,
        "eligibleCandidateKeys": len(eligible_keys),
        "notes": [
            "All methods use the same supplied eligible pool and active request criteria.",
            "Eligible-candidate coverage is not whole-catalog or generator coverage.",
            "Offline ranking time is Python scoring time, not MVC/SQL production latency.",
            "Missing independent constraint checks are reported as unknown, never as zero violations.",
            "Zero ideal DCG is assigned NDCG=0 and retained in the request mean.",
        ],
        "methods": {},
    }
    for method in ("content", "linear", "sugeno"):
        records, covered, times = [], set(), []
        known_checks = violations = unknown_checks = 0
        for row in selected_pools:
            relevance = {c["key"]: judgments[(row["requestId"], c["key"])] for c in row["candidates"] if c["eligible"]}
            started = time.perf_counter_ns()
            ranked = rank(row["candidates"], row["activeFeatures"], method, rules)
            times.append((time.perf_counter_ns() - started) / 1_000_000)
            metrics = per_request_metrics(ranked, relevance, cutoff, threshold)
            records.append((row, metrics))
            for candidate in ranked[:cutoff]:
                covered.add(candidate["key"])
                if candidate.get("constraintsSatisfied") is None:
                    unknown_checks += 1
                else:
                    known_checks += 1
                    violations += not candidate["constraintsSatisfied"]
        def aggregate(items):
            return {field: statistics.mean(record[field] for record in items) for field in ("ndcg", "precision", "recall_in_pool", "returned")}
        value = aggregate([r for _, r in records])
        value.update({
            "requestResultCoverage": sum(r["returned"] > 0 for _, r in records) / len(records),
            "eligibleCandidateCoverage": len(covered) / len(eligible_keys) if eligible_keys else 0.0,
            "independentConstraintChecks": known_checks,
            "constraintChecksUnknown": unknown_checks,
            "constraintViolations": violations,
            "constraintViolationRate": violations / known_checks if known_checks else None,
            "offlineRankingMeanMs": statistics.mean(times),
            "offlineRankingMaxMs": max(times),
            "groups": {},
        })
        for group in sorted({str(r.get("contextGroup", "unspecified")) for r, _ in records}):
            group_items = [m for r, m in records if str(r.get("contextGroup", "unspecified")) == group]
            value["groups"][group] = {"requests": len(group_items), **aggregate(group_items)}
        output["methods"][method] = value
    return output


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pools", required=True, help="JSONL of frozen eligible candidate pools")
    parser.add_argument("--judgments", required=True, help="CSV: request_id,candidate_key,relevance")
    parser.add_argument("--method", help="Optional nine-rule RecommendationMethod JSON; otherwise use fixed runtime defaults")
    parser.add_argument("--split", choices=["train", "validation", "test"], default="test")
    parser.add_argument("--k", type=int, default=5)
    parser.add_argument("--relevant-threshold", type=float, default=2.0)
    parser.add_argument("--output", help="Write report to this JSON file instead of stdout")
    args = parser.parse_args(argv)
    pools = read_pools(args.pools)
    judgments = read_judgments(args.judgments, pools)
    version, rules = load_method(args.method)
    result = evaluate(pools, judgments, version, rules, args.k, args.split, args.relevant_threshold)
    if any(row.get("sourceKind", "").startswith("synthetic") for row in pools if row["split"] == args.split):
        result["notes"].append("Selected split includes synthetic contract examples; this is not empirical recommendation-quality evidence.")
    encoded = json.dumps(result, ensure_ascii=False, indent=2, allow_nan=False) + "\n"
    if args.output:
        Path(args.output).write_text(encoded, encoding="utf-8")
    else:
        sys.stdout.write(encoded)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (ValueError, KeyError, TypeError, OSError) as error:
        print("FAIL:", error, file=sys.stderr)
        raise SystemExit(1)
