#!/usr/bin/env python3
"""Reference implementation of the ToggleForge rollout algorithm v1.

This script is an oracle independent from the C# code: it computes the expected
bucket of each test vector using only the Python standard library and writes them
to rollout-v1.json. The C# tests assert that their implementation agrees.

Specification: docs/rollout-algorithm.md. Never edit the JSON by hand.

Usage:
    python3 tools/rollout-vectors/generate.py          # rewrite the vectors file
    python3 tools/rollout-vectors/generate.py --check  # fail if the file is stale
"""

import argparse
import hashlib
import json
import sys
from pathlib import Path

ALGORITHM_TAG = "toggleforge.rollout.v1"
BUCKET_COUNT = 10_000
LENGTH_PREFIX_SIZE = 4
HASH_PREFIX_SIZE = 8

REPO_ROOT = Path(__file__).resolve().parents[2]
VECTORS_PATH = REPO_ROOT / "tests" / "ToggleForge.Evaluation.Tests" / "Vectors" / "rollout-v1.json"

MAX_KEY = "k" * 64
MAX_PLAYER_ID = "p" * 256


def bucket(project_key: str, environment_key: str, flag_key: str, seed: str, player_id: str) -> int:
    fields = (ALGORITHM_TAG, project_key, environment_key, flag_key, seed, player_id)
    digest = hashlib.sha256(encode_canonically(fields)).digest()
    return int.from_bytes(digest[:HASH_PREFIX_SIZE], "big") % BUCKET_COUNT


def encode_canonically(fields: tuple[str, ...]) -> bytes:
    """Length-prefixes every field so that ("ab", "c") and ("a", "bc") never collide."""
    encoded = bytearray()
    for field in fields:
        data = field.encode("utf-8")
        encoded += len(data).to_bytes(LENGTH_PREFIX_SIZE, "big")
        encoded += data
    return bytes(encoded)


def vector_inputs() -> list[tuple[str, str, str, str, str]]:
    base = ("studio-game", "prod", "new-shop", "seed-1")
    sequential_players = [(*base, f"player-{n}") for n in range(1, 11)]
    return [
        *sequential_players,
        ("studio-game", "staging", "new-shop", "seed-1", "player-1"),
        ("studio-game", "prod", "new-shop", "seed-2", "player-1"),
        ("studio-game", "prod", "battle.pass_v2", "seed-1", "player-1"),
        ("other-game", "prod", "new-shop", "seed-1", "player-1"),
        # Same concatenation, different field boundaries.
        ("ab", "c", "flag", "seed", "player"),
        ("a", "bc", "flag", "seed", "player"),
        # Unicode is hashed as UTF-8 bytes, without normalisation.
        ("studio-game", "prod", "new-shop", "seed-1", "Jogador Ação"),
        ("studio-game", "prod", "new-shop", "seed-1", "Jogador Ação"),
        ("studio-game", "prod", "new-shop", "seed-1", "プレイヤー"),
        ("studio-game", "prod", "new-shop", "seed-1", "gamer 🎮"),
        ("studio-game", "prod", "new-shop", "seed-1", "  padded  "),
        ("studio-game", "prod", "new-shop", "seed-1", "550e8400-e29b-41d4-a716-446655440000"),
        ("studio-game", "prod", "new-shop", "seed-1", "PLAYER-1"),
        # Boundaries of field length.
        ("a", "b", "c", "d", "e"),
        (MAX_KEY, MAX_KEY, MAX_KEY, "seed-1", MAX_PLAYER_ID),
        ("studio-game", "prod", "new-shop", "s" * 300, "player-1"),
    ]


def build_document() -> dict:
    vectors = [
        {
            "projectKey": project_key,
            "environmentKey": environment_key,
            "flagKey": flag_key,
            "seed": seed,
            "playerId": player_id,
            "bucket": bucket(project_key, environment_key, flag_key, seed, player_id),
        }
        for project_key, environment_key, flag_key, seed, player_id in vector_inputs()
    ]
    return {"algorithm": ALGORITHM_TAG, "bucketCount": BUCKET_COUNT, "vectors": vectors}


def render(document: dict) -> str:
    return json.dumps(document, ensure_ascii=False, indent=2) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="fail if the committed vectors are stale")
    args = parser.parse_args()

    expected = render(build_document())
    if args.check:
        actual = VECTORS_PATH.read_text(encoding="utf-8") if VECTORS_PATH.exists() else ""
        if actual != expected:
            print(f"{VECTORS_PATH.relative_to(REPO_ROOT)} is stale; run tools/rollout-vectors/generate.py", file=sys.stderr)
            return 1
        print("rollout vectors are up to date")
        return 0

    VECTORS_PATH.write_text(expected, encoding="utf-8")
    print(f"wrote {len(vector_inputs())} vectors to {VECTORS_PATH.relative_to(REPO_ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
